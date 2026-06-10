using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading.Channels;
using Diagnostic.Service.Common;
using Diagnostic.Service.Transport;
using DiagnosticExplorer;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Props;
using log4net;
using Microsoft.Extensions.Options;

namespace Diagnostic.Service.Hubs;

public class RetroManager : IHostedService
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(RetroManager));
    private IRetroLogger? _logger;
    private Channel<IList<DiagnosticMsg>>? _writeChannel;
    private Task? _loggingTask;
    private Subject<IList<DiagnosticMsg>>? _ownedLogSubject;
    private ISubject<IList<DiagnosticMsg>>? _logSubject;
    private IDisposable? _logSubscription;
    private long _writeQueueSize;
    private readonly ConcurrentDictionary<string, RetroSearchProcess> _searches = new();
    public EventSink RetroEvents { get; } = EventSinkRepo.Default.GetSink("Retro Events", "Retro");

    // _logger and _writeChannel are only populated once StartAsync has run (and nulled out by
    // StopAsync). Every consumer below runs while the hosted service is started, so accessing them
    // before/after that is a programming error — surface it as a clear InvalidOperationException
    // rather than a bare NullReferenceException.
    private IRetroLogger Logger =>
        _logger ?? throw new InvalidOperationException("RetroManager has not been started");

    private Channel<IList<DiagnosticMsg>> WriteChannel =>
        _writeChannel ?? throw new InvalidOperationException("RetroManager has not been started");


    private readonly IHostApplicationLifetime _lifetime;

    public RetroManager(IHostApplicationLifetime lifetime, IOptions<DiagServiceSettings> config)
    {
        Options = config.Value;
        // Lifecycle is now driven by the host (registered via AddHostedService); no ctor
        // self-wiring. _lifetime is kept so StartAsync can tie the drain loop to ApplicationStopping.
        _lifetime = lifetime;
    }


    public Task StartAsync(CancellationToken cancel)
    {
        DiagnosticManager.Register(this, "Retro Manager", "Retro");

        _writeQueueSize = 0;

        // 10k batches is a real backlog cap (1_000_000 batches x up-to-50 msgs was effectively
        // unbounded, so DropWrite never engaged and memory was uncapped during a logger outage).
        _writeChannel = Channel.CreateBounded<IList<DiagnosticMsg>>(new BoundedChannelOptions(10_000)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });


        _ownedLogSubject = new Subject<IList<DiagnosticMsg>>();
        _logSubject = Subject.Synchronize(_ownedLogSubject);

        // Keep the subscription so StopAsync can dispose it (was discarded → leaked across restarts).
        _logSubscription = _logSubject.SelectMany(list => list)
            .Buffer(TimeSpan.FromSeconds(1), 50)
            .Where(evts => evts.Count != 0)
            .Subscribe(evts => {
                if (_writeChannel.Writer.TryWrite(evts))
                {
                    Interlocked.Add(ref _writeQueueSize, evts.Count);
                }
            });


        _logger = Options.CreateRetroLogger();

        // Do NOT pass ApplicationStopping here: ApplicationStopping fires before StopAsync is
        // invoked, so the drain loop would exit early and drop queued messages. Instead, let
        // the loop run until the channel writer completes (signalled in StopAsync), with the
        // 5-second Task.WhenAny in StopAsync as the hard-cap.
        _loggingTask = Task.Run(() => RunLoop(CancellationToken.None));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancel)
    {
        // Stop accepting input, then let the drain loop flush what's already queued before the
        // host tears the process down. Previously StopAsync returned immediately without awaiting
        // the drain or disposing the Rx subscription, so queued messages were lost on recycle.
        _logSubject?.OnCompleted();
        _logSubject = null;
        _logSubscription?.Dispose();
        _logSubscription = null;
        _ownedLogSubject?.Dispose();
        _ownedLogSubject = null;

        _writeChannel?.Writer.Complete();

        if (_loggingTask != null)
        {
            await Task.WhenAny(_loggingTask, Task.Delay(TimeSpan.FromSeconds(5)));
        }

        (_logger as IDisposable)?.Dispose();
    }

    private async Task RunLoop(CancellationToken cancel)
    {
        await foreach (var messages in WriteChannel.Reader.ReadAllAsync(cancel))
        {
            await TryLog(messages, cancel);
        }
    }


    public long WriteQueueSize => _writeQueueSize;
    public int ItemsInQueue => WriteChannel.Reader.CanCount ? WriteChannel.Reader.Count : -1;

    [ExtendedProperty]
    public DiagServiceSettings Options { get; set; }

    [RateProperty(ExposeTotal = false, ExposeRate = true)]
    public RateCounter EventsQueued { get; set; } = new(3);

    [RateProperty(ExposeTotal = false, ExposeRate = true)]
    public RateCounter EventsWritten { get; set; } = new(3);


    private async Task TryLog(IList<DiagnosticMsg> messages, CancellationToken cancel)
    {
        try
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    await Logger.WriteMessages(messages, cancel);
                    EventsWritten.Register(messages.Count);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Error(ex);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancel);
                }
            }
        }
        finally
        {
            // Decrement once whether the batch was written or abandoned after the retries: the count
            // was incremented on enqueue, so a permanently-failing logger would otherwise leak
            // WriteQueueSize upward forever (it never recovers after a sustained outage). (A8)
            Interlocked.Add(ref _writeQueueSize, -1 * messages.Count);
        }
    }

    public IAsyncEnumerable<RetroMsg[]> GetRetroLog(RetroQuery query, CancellationToken cancel)
    {
        return Logger.GetMessages(query, cancel);
    }


    public void LogEvents(IList<DiagnosticMsg> messages)
    {
        ISubject<IList<DiagnosticMsg>>? logSubject = _logSubject;

        if (logSubject != null)
        {
            logSubject.OnNext(messages);
            // Do NOT increment _writeQueueSize here: the Rx subscription increments it when the
            // batch is actually written to the channel (and skips it on DropWrite). Counting it
            // here too double-counted the backlog and never reversed on a dropped write.
            EventsQueued.Register(messages.Count);
        }
    }

    public Task StartRetroSearch(RetroQuery query, string connectionId, IWebHubClient client)
    {
        if (_searches.TryRemove(connectionId, out RetroSearchProcess? existingSearch))
        {
            existingSearch.Cancel();
        }

        RetroEvents.Info($"Retro search starting for connection {connectionId}",
            JsonSerializer.SerializeToElement(query).ToString());

        RetroSearchProcess search = new(this, connectionId, client, query);
        _searches.TryAdd(connectionId, search);
        search.Finished += HandleSearchFinished;
        search.Start();
        return Task.CompletedTask;
    }


    /// <summary>
    /// Whether the active backend supports interactive per-record delete. Surfaced to the web
    /// client (via WebHub.RetroSupportsDelete) so the UI can hide the delete affordance for
    /// append-only backends such as Log Analytics.
    /// </summary>
    public bool SupportsDelete => Logger.SupportsDelete;

    public Task<long> RetroDelete(string[] idList)
    {
        RetroEvents.Info($"Retro delete starting {idList.Length} messages");

        return Logger.Delete(idList);
    }

    private void HandleSearchFinished(object? sender, EventArgs e)
    {
        RetroSearchProcess search = (RetroSearchProcess) sender!;
        // Remove the finished search so completed searches don't linger in the registry until the
        // connection starts another or disconnects. Pair-remove so a newer search that already
        // replaced this entry is left intact. (A3)
        _searches.TryRemove(new KeyValuePair<string, RetroSearchProcess>(search.ClientId, search));
        RetroEvents.Info($"Retro search complete for connection {search.ClientId} in {search.SearchTime.TotalSeconds:N2}s",
            JsonSerializer.SerializeToElement(search.Query).ToString());
        search.Dispose();
    }

    public Task CancelConnectionSearch(string connectionId)
    {
        // Called on client disconnect: cancel and drop any in-flight retro search for the connection
        // so a disconnected client's search isn't left running or lingering in the registry. (A3)
        if (_searches.TryRemove(connectionId, out RetroSearchProcess? running))
        {
            running.Cancel();
        }

        return Task.CompletedTask;
    }

    public Task CancelRetroSearch(int searchId, string connectionId)
    {
        if (_searches.TryGetValue(connectionId, out RetroSearchProcess? running)
            && running.Query.SearchId == searchId)
        {
            running.Cancel();
            _searches.TryRemove(connectionId, out _);
        }

        return Task.CompletedTask;
    }


}