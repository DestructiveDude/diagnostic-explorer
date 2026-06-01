using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DiagnosticExplorer;
using Diagnostics.Service.Common.Transport;

namespace Diagnostics.Service.Common.Hubs;

public class RetroSearchProcess
{
    private IWebHubClient _client;
    public RetroQuery Query { get; }
    private CancellationTokenSource _cancelToken = new();
    private readonly RetroManager _retroManager;
    public event EventHandler? Finished;
    public string ClientId { get; }
    private Stopwatch _watch = new Stopwatch();


    public RetroSearchProcess(RetroManager retroManager, string clientId, IWebHubClient client, RetroQuery query)
    {
        _retroManager = retroManager ?? throw new ArgumentNullException(nameof(retroManager));
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }


    public void Cancel()
    {
        _cancelToken.Cancel();
    }

    public void Start()
    {
        _watch.Restart();
        var channel = Channel.CreateUnbounded<RetroSearchResult>(new UnboundedChannelOptions
        {
            SingleReader = true, 
            SingleWriter = true
        });


        Task.Run(() => ExecuteQuery(channel, _cancelToken.Token));
        Task.Run(() => SendResults(channel, _cancelToken.Token));
    }

    public TimeSpan SearchTime => _watch?.Elapsed ?? TimeSpan.Zero;

    private async Task SendResults(Channel<RetroSearchResult> channel, CancellationToken cancel)
    {
        try
        {
            await foreach (RetroSearchResult result in channel.Reader.ReadAllAsync(cancel))
            {
                cancel.ThrowIfCancellationRequested();
                await _client.ProcessSearchResults(result);
            }
            await _client.ProcessSearchEnd(Query.SearchId);
        }
        catch (OperationCanceledException)
        {
            Trace.WriteLine("RetroSearchProcess.SendResults cancelled");
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
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

            IAsyncEnumerable<RetroMsg[]> results = _retroManager.GetRetroLog(Query, cancel);
            await foreach (RetroMsg[] messages in results.WithCancellation(cancel))
            {
                cancel.ThrowIfCancellationRequested();

                // No debug Info: the old `cancelled: {IsCancellationRequested}` was always false here
                // (we ThrowIfCancellationRequested above) and was only ever console.logged client-side.
                RetroSearchResult result = new() {
                    SearchId = Query.SearchId,
                    Results = messages,
                };
                channel.Writer.TryWrite(result);
            }

            channel.Writer.Complete();
        }
        catch (OperationCanceledException)
        {
            channel.Writer.Complete();
            Trace.WriteLine("RetroSearchProcess.ExecuteQuery cancelled");
        }
        catch (Exception ex)
        {
            channel.Writer.Complete(ex);
        }
    }

}