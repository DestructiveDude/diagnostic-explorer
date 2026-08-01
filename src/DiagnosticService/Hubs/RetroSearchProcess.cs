using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Diagnostic.Service.Transport;

namespace Diagnostic.Service.Hubs;

public sealed class RetroSearchProcess : IDisposable
{
    private readonly CancellationTokenSource _cancelToken = new();
    private readonly IWebHubClient _client;
    private readonly RetroManager _retroManager;
    private readonly Stopwatch _watch = new();

    public RetroSearchProcess(RetroManager retroManager, string clientId, IWebHubClient client, RetroQuery query)
    {
        _retroManager = retroManager ?? throw new ArgumentNullException(nameof(retroManager));
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public RetroQuery Query { get; }
    public string ClientId { get; }

    public TimeSpan SearchTime => _watch.Elapsed;

    public void Dispose()
    {
        _cancelToken.Dispose();
    }

    public event EventHandler? Finished;

    [SuppressMessage(
        "Reliability",
        "S6966:Await CancelAsync instead",
        Justification = "The public cancellation entry point is synchronous and callers require cancellation callbacks to finish before it returns."
    )]
    public void Cancel()
    {
        try
        {
            _cancelToken.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrently completed search has already released its cancellation source.
        }
    }

    public void Start()
    {
        _watch.Restart();
        var channel = Channel.CreateBounded<RetroSearchResult>(
            new BoundedChannelOptions(200)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );

        Task.Run(() => ExecuteQuery(channel, _cancelToken.Token));
        Task.Run(() => SendResults(channel, _cancelToken.Token));
    }

    private async Task SendResults(Channel<RetroSearchResult> channel, CancellationToken cancel)
    {
        try
        {
            await foreach (var result in channel.Reader.ReadAllAsync(cancel))
            {
                cancel.ThrowIfCancellationRequested();
                await _client.ProcessSearchResults(result);
            }

            await _client.ProcessSearchEnd(Query.SearchId);
        }
        catch (OperationCanceledException)
        {
            Trace.TraceInformation("RetroSearchProcess.SendResults cancelled");
        }
        catch (Exception ex)
        {
            // Cancel the query producer so ExecuteQuery exits promptly when delivery fails.
            await _cancelToken.CancelAsync();
            Trace.TraceError(ex.ToString());
            await _client.ProcessSearchError(Query.SearchId, ex.Message, ex.ToString());
        }
        finally
        {
            Finished?.Invoke(this, EventArgs.Empty);
            _watch.Stop();
        }
    }

    private async Task ExecuteQuery(Channel<RetroSearchResult> channel, CancellationToken cancel)
    {
        try
        {
            cancel.ThrowIfCancellationRequested();

            var results = _retroManager.GetRetroLog(Query, cancel);
            await foreach (var messages in results)
            {
                cancel.ThrowIfCancellationRequested();

                // No debug Info: the old `cancelled: {IsCancellationRequested}` was always false here
                // (we ThrowIfCancellationRequested above) and was only ever console.logged client-side.
                RetroSearchResult result = new() { SearchId = Query.SearchId, Results = messages };
                await channel.Writer.WriteAsync(result, cancel);
            }

            channel.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            channel.Writer.TryComplete();
            Trace.TraceInformation("RetroSearchProcess.ExecuteQuery cancelled");
        }
        catch (Exception ex)
        {
            channel.Writer.TryComplete(ex);
        }
    }
}
