using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DiagnosticExplorer.Util;
using DiagWebService.Hubs;
using log4net;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;


namespace DiagnosticExplorer;

public class RegistrationHandler
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(RegistrationHandler));

    private string _url;
    private Registration _registration;
    private readonly string _apiKey;

    // _connLock guards the _connection/_hubAdapter pair. They are mutated from three racing
    // contexts — the registration loop (OpenHub/CloseConnection), the SignalR Closed event
    // (HandleClosed) and Stop — so each teardown captures-and-nulls the pair atomically to
    // guarantee a given connection/adapter is disposed exactly once.
    private readonly object _connLock = new();
    private HubServerAdapter _hubAdapter;
    private HubConnection _connection;

    private CancellationTokenSource _stopToken;
    private Task _registrationLoop;
    private Task _loggingTask;
    private Subject<DiagnosticMsg> _logSubject = new();
    private Channel<IList<DiagnosticMsg>> _logChannel;

    private Action<HttpConnectionOptions> _configureHttp;


    public RegistrationHandler(string url, Registration registration, string apiKey = null)
    {
        _url = url;
        _registration = registration;
        _apiKey = apiKey;

        // F8: never send the API key over a cleartext transport. Fail fast at construction rather
        // than silently leaking it on http/ws negotiate + WebSocket requests.
        if (!string.IsNullOrEmpty(_apiKey) && !IsSecureUrl(_url))
            throw new ArgumentException(
                $"An API key is configured but the diagnostic hub URL '{_url}' is not https/wss — the key would be transmitted in cleartext. Use a TLS URL or clear the API key.",
                nameof(url));
    }

    private static bool IsSecureUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase));
    }

    public void Start(Action<HttpConnectionOptions> configureHttp = null)
    {
        _configureHttp = configureHttp;
        _stopToken = new CancellationTokenSource();
        _logChannel = Channel.CreateBounded<IList<DiagnosticMsg>>(
            new BoundedChannelOptions(10_000) {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });

        _logSubject
            .Buffer(TimeSpan.FromSeconds(2), 50)
            .Where(evts => evts.Count != 0)
            .Subscribe(evts => _logChannel?.Writer.TryWrite(evts));

        _registrationLoop = Task.Run(() => RunRegistrationProcess(_stopToken.Token));
        _loggingTask = Task.Run(() => RunLoggingProcess(_stopToken.Token));

        Debug.WriteLine($"Diagnostics RegistrationHandler for {_url} started");
    }

    private async Task RunLoggingProcess(CancellationToken cancel)
    {
        // Loop until the channel writer is completed (Stop() completes it before cancelling the
        // stop token so queued messages are not dropped). ReadAsync(None) throws ChannelClosedException
        // when the writer is complete and no items remain — that is the natural exit signal.
        // The cancel token is still checked while waiting for the hub adapter so we don't block
        // indefinitely during a hub-less shutdown.
        while (true)
        {
            IList<DiagnosticMsg> messages;
            try
            {
                messages = await _logChannel.Reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                break;
            }

            try
            {
                Stopwatch watch1 = Stopwatch.StartNew();
                byte[] data = ProtobufUtil.Compress(messages, 1024);
                watch1.Stop();

                Stopwatch watch2 = Stopwatch.StartNew();
                while (_hubAdapter == null)
                {
                    if (cancel.IsCancellationRequested)
                        return;
                    await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                }

                // Snapshot: _hubAdapter can be nulled by HandleClosed/CloseConnection between
                // the wait above and the send below.
                HubServerAdapter adapter = _hubAdapter;
                if (adapter == null)
                    continue;

                Debug.WriteLine($"RegistrationHandler sending {data.Length} bytes");
                await adapter.LogEvents(data).ConfigureAwait(false);
                watch2.Stop();
                Debug.WriteLine($"RegistrationHandler sent {data.Length} bytes, zip/send took {watch1.ElapsedMilliseconds}ms/{watch2.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to log {messages.Count} messages: {ex.Message}");
            }
        }

        Debug.WriteLine($"RunLoggingProcess HAS NOW STOPPED");
    }

    private async Task RunRegistrationProcess(CancellationToken cancelToken)
    {
        TimeSpan delay = TimeSpan.Zero;

        while (!cancelToken.IsCancellationRequested)
            try
            {
                if (delay != TimeSpan.Zero) await Task.Delay(delay, cancelToken);

                delay = TimeSpan.FromSeconds(5);

                cancelToken.ThrowIfCancellationRequested();

                await OpenHub();

                cancelToken.ThrowIfCancellationRequested();

                RegistrationResponse response = await _hubAdapter.Register(_registration);

                delay = response.RenewTimeSeconds <= 0
                    ? TimeSpan.FromSeconds(20)
                    : TimeSpan.FromSeconds(response.RenewTimeSeconds);
            }
            catch (Exception ex)
            {
                // On a genuine stop, leave the connection intact: Stop() owns teardown (it
                // deregisters while the adapter is still valid, then disposes). Closing here would
                // null _hubAdapter and make Stop's Deregister silently no-op. (M26)
                if (cancelToken.IsCancellationRequested)
                    return;

                //Something went wrong, so kill the connection and try again
                await CloseConnection();

                Debug.WriteLine($"RunRegistrationProcess exception {ex?.Message}");
                string errorMessage = $"DiagnosticHostingService.RegistrationHandler for {_url} encountered an exception";
                _log.Warn(errorMessage, ex);
            }
    }

    private Task CloseConnection()
    {
        return DisposeConnection(TakeConnection());
    }

    private async Task OpenHub()
    {
        if (_hubAdapter != null)
            return;

        Debug.WriteLine("Diagnostic RegistrationHandler constructing connection");
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(_url, options => {
                // Apply the caller's HTTP customisation first (e.g. M23 opt-in integrated auth via
                // UseDefaultCredentials), THEN layer the API key on top so the required credential
                // always wins — a caller configureHttp that incidentally sets AccessTokenProvider
                // can no longer silently drop the key. (M23 / F4)
                _configureHttp?.Invoke(options);

                // H1: when an API key is configured, send it via the access-token mechanism —
                // "Authorization: Bearer <key>" on negotiate and "access_token" on the WS upgrade.
                if (!string.IsNullOrEmpty(_apiKey))
                    options.AccessTokenProvider = () => Task.FromResult(_apiKey);
            })
            .Build();

        connection.Closed += HandleClosed;

        Debug.WriteLine("Diagnostic RegistrationHandler starting connection");
        await connection.StartAsync(_stopToken.Token);

        Debug.WriteLine("Diagnostic RegistrationHandler connection started");
        HubServerAdapter adapter = new HubServerAdapter(connection);

        lock (_connLock)
        {
            _connection = connection;
            _hubAdapter = adapter;
        }
    }

    private async Task HandleClosed(Exception ex)
    {
        Debug.WriteLine($"RegistrationHandler.HandleClosed {ex?.Message}");
        await DisposeConnection(TakeConnection());
    }

    // Atomically detach the current connection/adapter pair. Whichever of CloseConnection /
    // HandleClosed / Stop wins the lock gets the live instances; the losers get nulls and no-op,
    // so a given connection+adapter is torn down exactly once.
    private (HubConnection Connection, HubServerAdapter Adapter) TakeConnection()
    {
        lock (_connLock)
        {
            HubConnection connection = _connection;
            HubServerAdapter adapter = _hubAdapter;
            _connection = null;
            _hubAdapter = null;
            return (connection, adapter);
        }
    }

    private async Task DisposeConnection((HubConnection Connection, HubServerAdapter Adapter) taken)
    {
        try
        {
            taken.Adapter?.Dispose(); // M25: tear the adapter down too, not just the connection.
        }
        catch (Exception ex)
        {
            Trace.WriteLine("RegistrationHandler.DisposeConnection HubServerAdapter.Dispose: " + ex);
        }

        if (taken.Connection == null)
            return;

        // Detach the Closed handler before disposing so DisposeAsync's own Closed callback can't
        // re-enter HandleClosed and race this teardown. (M28)
        taken.Connection.Closed -= HandleClosed;
        try
        {
            await taken.Connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine("RegistrationHandler.DisposeConnection HubConnection.DisposeAsync: " + ex);
        }
    }

    public async Task Stop()
    {
        try
        {
            Task loopTask = _registrationLoop;
            Task logTask = _loggingTask;

            // Complete the subject and channel writer BEFORE cancelling the stop token so
            // RunLoggingProcess can drain buffered messages. Cancelling first caused ReadAllAsync
            // to exit early and drop queued log items.
            Subject<DiagnosticMsg> logSubject = _logSubject;
            _logSubject = null;
            logSubject?.OnCompleted();

            _logChannel?.Writer.Complete();
            _logChannel = null;

            _stopToken?.Cancel();

            _registrationLoop = null;
            _loggingTask = null;
            _stopToken = null;

            // Drain both background tasks before tearing the connection down. (M26)
            if (loopTask != null)
                await loopTask.ConfigureAwait(false);
            if (logTask != null)
                await logTask.ConfigureAwait(false);

            // Deregister while the adapter is still live (the loop no longer closes the connection
            // on cancellation), then dispose the connection + adapter. (M26, M25)
            await Deregister();
            await CloseConnection();
        }
        catch (Exception ex)
        {
            _log.Error(ex);
        }
    }

    private async Task Deregister()
    {
        // Snapshot under the lock: HandleClosed could null _hubAdapter concurrently.
        HubServerAdapter adapter;
        lock (_connLock)
            adapter = _hubAdapter;

        try
        {
            if (adapter != null)
            {
                _log.Info("DiagnosticHostingService Deregistered");
                await adapter.Deregister(_registration);
                Debug.WriteLine("Deregistered successfully");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to deregister {ex}");
            _log.Error(ex);
        }
    }

    public void LogEvent(DiagnosticMsg evt)
    {
        // Snapshot: Stop() completes and nulls _logSubject; a log event arriving during/after
        // shutdown must be a no-op, not an NRE. (M27)
        Subject<DiagnosticMsg> subject = _logSubject;
        subject?.OnNext(evt);
    }
}