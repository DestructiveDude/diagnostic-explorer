using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Transport;
using DiagnosticExplorer;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;
using DiagnosticExplorer.Props;

namespace Diagnostic.Service.Hubs;

public class RealtimeManager : IHostedService
{
    private static readonly StringComparer _ic = StringComparer.InvariantCultureIgnoreCase;

    private static readonly TimeSpan _alertDuration = TimeSpan.FromSeconds(2);
    private readonly object _configLockObj = new();

    private readonly ConcurrentDictionary<string, DiagnosticClientHandler> _diagClients = new();
    private readonly ConcurrentDictionary<string, DiagProcess> _processes = new();

    private readonly ConcurrentDictionary<DiagProcess, DiagnosticSubscription> _subscriptions = new();
    private readonly Subject<DiagProcess> _processChangedSubject = new();
    private readonly Subject<DiagProcess> _processRemovedSubject = new();

    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, WebClientHandler> _webClients = new();
    private IDisposable? _alertLevelSubscription;

    // Lifecycle is driven by the host (registered via AddHostedService); no ctor self-wiring.
    public RealtimeManager(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        ProcessChanged = Subject.Synchronize(_processChangedSubject);
        ProcessRemoved = Subject.Synchronize(_processRemovedSubject);
    }

    public EventSink RealtimEvents { get; } = EventSinkRepo.Default.GetSink("Realtime Events", "Realtime");

    // Synchronized: OnNext is raised from the alert-decay timer thread (ProcessesAlertLevels),
    // from hub-call threads (RegisterAlertLevel), and from inside the config write lock
    // (Register/Deregister/RemoveProcess/TidyProcesses). Subject<T>.OnNext is not safe for
    // concurrent callers, so wrap it in Subject.Synchronize to serialize notifications.
    public ISubject<DiagProcess> ProcessChanged { get; }

    public ISubject<DiagProcess> ProcessRemoved { get; }

    [CollectionProperty(CollectionMode.Categories, Category = "Processes", CategoryProperty = nameof(DiagProcess.Id))]
    public ICollection<DiagProcess> Processes => _processes.Values;

    [CollectionProperty(
        CollectionMode.Categories,
        Category = "Subscriptions",
        CategoryProperty = nameof(DiagnosticSubscription.ProcessId)
    )]
    public ICollection<DiagnosticSubscription> Subscriptions => _subscriptions.Values;

    [RateProperty(Category = "Requests", ExposeTotal = true, ExposeRate = true)]
    public RateCounter ConfigRequests { get; set; } = new(3);

    [RateProperty(Category = "Requests", ExposeTotal = true, ExposeRate = true)]
    public RateCounter DiagnosticRequests { get; set; } = new(3);

    [RateProperty(Category = "Requests", ExposeTotal = true, ExposeRate = true)]
    public RateCounter Registrations { get; set; } = new(3);

    [RateProperty(Category = "Requests", ExposeTotal = true, ExposeRate = true)]
    public RateCounter Deregistrations { get; set; } = new(3);

    [Property(Category = "Processes")]
    public int TotalProcesses => _processes.Count;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _alertLevelSubscription = Observable
            .Timer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
            .Subscribe(_ => ProcessesAlertLevels());

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _alertLevelSubscription, null)?.Dispose();
        foreach (DiagnosticClientHandler client in _diagClients.Values)
        {
            client.Disconnected -= HandleClientDisconnected;
            client.Dispose();
        }

        _diagClients.Clear();
        _processChangedSubject.Dispose();
        _processRemovedSubject.Dispose();
        return Task.CompletedTask;
    }

    private static void Publish(ISubject<DiagProcess> subject, DiagProcess process)
    {
        try
        {
            subject.OnNext(process);
        }
        catch (ObjectDisposedException)
        {
            // Ignore a late hub/timer publication racing host shutdown.
        }
    }

    public void ProcessesAlertLevels()
    {
        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            foreach (var process in Processes)
            {
                var age = utcNow.Subtract(process.AlertLevelDate ?? utcNow);

                if (process.AlertLevel > 0 && age > _alertDuration)
                {
                    process.AlertLevel = 0;
                    process.AlertLevelDate = null;
                    Publish(ProcessChanged, process);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
        }

        try
        {
            TidyProcesses();
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
        }
    }

    public void RegisterAlertLevel(string connectionId, DiagnosticMsg[] messages)
    {
        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var level = messages
            .Where(m => utcNow.Subtract(m.Date) < _alertDuration)
            .Select(m => m.Level)
            .DefaultIfEmpty(0)
            .Max();

        var process = Processes.FindByConnectionId(connectionId);
        if (process != null && process.AlertLevel <= level && level > 0)
        {
            var levelChanged = process.AlertLevel != level;
            process.AlertLevel = level;
            process.AlertLevelDate = utcNow;
            if (levelChanged)
            {
                Publish(ProcessChanged, process);
            }
        }
    }

    public ICollection<DiagProcess> GetProcesses()
    {
        return Processes;
    }

    internal void AddDiagnosticClient(DiagnosticClientHandler client)
    {
        _diagClients.TryAdd(client.ConnectionId, client);
        RealtimEvents.Notice($"Client {client.ConnectionId} added");

        client.Disconnected += HandleClientDisconnected;
        client.Arm();
    }

    private void HandleClientDisconnected(object? sender, EventArgs e)
    {
        var client = (DiagnosticClientHandler)sender!;
        RealtimEvents.Notice($"Client {client.ConnectionId} disconnected");
        _diagClients.TryRemove(client.ConnectionId, out _);
        try
        {
            Deregister(client);
        }
        finally
        {
            client.Disconnected -= HandleClientDisconnected;
            client.Dispose();
        }
    }

    internal DiagnosticClientHandler? GetClientHandler(string connectionId)
    {
        _diagClients.TryGetValue(connectionId, out var client);
        return client;
    }

    private void EnterConfigLock()
    {
        // Plain Monitor with a 10s fail-fast. The former ReaderWriterLockSlim only ever took the
        // write lock — it never read-locked — so it bought nothing over mutual exclusion, yet (being
        // IDisposable) was never disposed. Monitor is recursive on the owning thread, matching the
        // old SupportsRecursion policy. (10s, was 1000s ≈ 16.7 min — see M2.)
        if (!Monitor.TryEnter(_configLockObj, TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException("Failed to obtain config write lock");
        }
    }

    private void ExitConfigLock()
    {
        Monitor.Exit(_configLockObj);
    }

    public void RemoveProcess(string id)
    {
        EnterConfigLock();
        try
        {
            _processes.TryRemove(id, out var item);

            if (item == null)
            {
                throw new InvalidOperationException($"Can't find item '{id}'");
            }

            RemoveSubscription(item);

            if (item.ConnectionId != null)
            {
                GetClientHandler(item.ConnectionId)?.CloseConnection();
            }

            Publish(ProcessRemoved, item);
        }
        finally
        {
            ExitConfigLock();
        }
    }

    public async Task<OperationResponse> SetProperty(SetPropertyRequest request)
    {
        try
        {
            var p = GetProcess(request.Id);
            if (p == null)
            {
                return OperationResponse.Error($"Process {request.Id} not found");
            }

            var client = GetSubscription(p)?.DiagnosticClient;
            if (client == null)
            {
                return OperationResponse.Error($"Process {request.Id} is not connected");
            }

            return await client.SetProperty(request.Path, request.Value);
        }
        catch (Exception ex)
        {
            return OperationResponse.Error(ex.Message);
        }
    }

    public async Task<OperationResponse> ExecuteOperation(ExecuteOperationRequest request)
    {
        try
        {
            var p = GetProcess(request.Id);
            if (p == null)
            {
                return OperationResponse.Error($"Process {request.Id} not found");
            }

            var client = GetSubscription(p)?.DiagnosticClient;
            if (client == null)
            {
                return OperationResponse.Error($"Process {request.Id} is not connected");
            }

            return await client.ExecuteOperation(request.Path, request.Operation, request.Arguments);
        }
        catch (Exception ex)
        {
            return OperationResponse.Error(ex.Message);
        }
    }

    private void SetStatus(DiagProcess group, OnlineState state, string? message)
    {
        group.State = state;
        group.Message = message;
        if (group.State == OnlineState.Online)
        {
            group.LastOnline = _timeProvider.GetUtcNow().UtcDateTime;
        }
    }

    public void Register(Registration registration, string? connectionId = null)
    {
        Registrations.Register(1);
        EnterConfigLock();
        try
        {
            var regMode = connectionId == null ? RegistrationMode.Auto : RegistrationMode.SignalR;

            DiagProcess? process = null;
            if (!string.IsNullOrWhiteSpace(registration.InstanceId))
            {
                process = Processes.FindByInstanceId(registration.InstanceId);
            }

            if (process == null)
            {
                var found = Processes
                    .Where(x =>
                        _ic.Equals(x.MachineName, registration.MachineName)
                        && _ic.Equals(x.ProcessName, registration.ProcessName)
                        && x.ConnectionId == null
                        && x.RegistrationMode == regMode
                        && (string.IsNullOrEmpty(x.UserName) || _ic.Equals(x.UserName, registration.UserName))
                    )
                    .ToArray();

                if (found.Length >= 1)
                {
                    process = found.FirstOrDefault(x => x.State == OnlineState.Offline);
                }
            }

            var previousState = process?.State;

            if (process == null)
            {
                process = new DiagProcess
                {
                    Id = Guid.NewGuid().ToString("N"),
                    MachineName = registration.MachineName,
                    ProcessName = registration.ProcessName,
                };
                _processes.TryAdd(process.Id, process);
            }

            process.UserName = registration.UserName;
            process.ProcessId = registration.ProcessId;
            process.State = OnlineState.Online;
            process.LastOnline = _timeProvider.GetUtcNow().UtcDateTime;
            process.ConnectionId = connectionId;
            process.InstanceId = registration.InstanceId;
            process.RegistrationMode = regMode;

            SetStatus(process, OnlineState.Online, null);

            if (connectionId != null && _diagClients.TryGetValue(connectionId, out var diagClient))
            {
                GetSubscription(process).SetDiagnosticClient(diagClient);
            }

            if (process.State != previousState)
            {
                Publish(ProcessChanged, process);
            }
        }
        finally
        {
            ExitConfigLock();
        }
    }

    /// <summary>
    ///     Remove any entries which are no longer needed
    /// </summary>
    private void TidyProcesses()
    {
        //Mark as offline anything which is 5 seconds late for renewal
        var expiryTime = TimeSpan.FromSeconds(30);
        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var autoOnline = Processes
            .Where(x => x.State == OnlineState.Online && x.RegistrationMode == RegistrationMode.Auto)
            .ToArray();

        foreach (var proc in autoOnline)
        {
            if (utcNow - proc.LastOnline > expiryTime)
            {
                proc.State = OnlineState.Offline;
                proc.Message = "Failed to renew";
                Publish(ProcessChanged, proc);
            }
        }

        //Group all items by process, instance and host
        DiagProcess[][] procs = (
            from x in Processes
            where x.RegistrationMode != RegistrationMode.Manual
            group x by new { x.ProcessName, Host = x.MachineName?.ToLower() } into grp
            select grp.ToArray()
        ).ToArray();

        RegistrationMode[] tidyModes = { RegistrationMode.Auto, RegistrationMode.SignalR };

        //For each group, remove any excess entries which are offline
        foreach (var matching in procs)
        {
            //Find the items which are no longer online
            var toRemove = matching
                .Where(x => tidyModes.Contains(x.RegistrationMode) && x.State != OnlineState.Online)
                .ToArray();

            //If all must be removed, make sure we leave just one
            if (toRemove.Length == matching.Length)
            {
                toRemove = toRemove.Skip(1).ToArray();
            }

            foreach (var proc in toRemove)
            {
                _processes.TryRemove(proc.Id, out _);
                RemoveSubscription(proc);
                Publish(ProcessRemoved, proc);
            }
        }

        var expired = Processes.Where(process => HasExpired(process, utcNow)).ToArray();
        foreach (var proc in expired)
        {
            _processes.TryRemove(proc.Id, out _);
            RemoveSubscription(proc);
            Publish(ProcessRemoved, proc);
        }
    }

    private static bool HasExpired(DiagProcess process, DateTime utcNow)
    {
        if (process.State == OnlineState.Online)
        {
            return false;
        }

        TimeSpan? elapsed = process.LastOnline.HasValue ? utcNow - process.LastOnline : null;

        return elapsed > TimeSpan.FromDays(100);
    }

    public DiagProcess? GetProcess(string id)
    {
        return _processes.TryGetValue(id, out var value) ? value : null;
    }

    public void Deregister(Registration registration)
    {
        Deregister(() => Processes.FindByInstanceId(registration.InstanceId));
    }

    private void Deregister(DiagnosticClientHandler client)
    {
        Deregister(() => Processes.FindByConnectionId(client.ConnectionId));
    }

    private void Deregister(Func<DiagProcess?> getProcess)
    {
        Deregistrations.Register(1);

        EnterConfigLock();
        try
        {
            var process = getProcess();
            if (process != null)
            {
                if (process.ConnectionId != null)
                {
                    _diagClients.TryRemove(process.ConnectionId, out _);
                }

                process.State = OnlineState.Offline;
                process.ConnectionId = null;
                process.Message = "Offline";

                if (_subscriptions.TryGetValue(process, out var subscription))
                {
                    subscription.SetDiagnosticClient(null);
                }

                Publish(ProcessChanged, process);
            }

            TidyProcesses();
        }
        finally
        {
            ExitConfigLock();
        }
    }

    public void AddWebHubClient(string connectionId, IWebHubClient client)
    {
        var handler = new WebClientHandler(connectionId, client);
        _webClients.TryAdd(connectionId, handler);
        handler.Start(this);
    }

    public void RemoveWebHubClient(string connectionId)
    {
        if (_webClients.TryRemove(connectionId, out var client))
        {
            RemoveClientFromSubscriptions(client);
            client?.Stop();
        }
    }

    private void RemoveClientFromSubscriptions(WebClientHandler client)
    {
        foreach (var sub in _subscriptions.Values)
        {
            sub.RemoveWebClient(client);
        }
    }

    // Removing a process from _processes must also drop its subscription, else the
    // DiagnosticSubscription (keyed by the removed DiagProcess) leaks for the process lifetime.
    // SetDiagnosticClient(null) stops its polling loop (same teardown Deregister uses).
    private void RemoveSubscription(DiagProcess process)
    {
        if (_subscriptions.TryRemove(process, out var sub))
        {
            sub.SetDiagnosticClient(null);
        }
    }

    public async Task<bool> SubscribeWebClient(string webConnectionId, string processId)
    {
        if (!_webClients.TryGetValue(webConnectionId, out var webClient))
        {
            return false;
        }

        // Validate the target BEFORE tearing the client off its current subscription: a subscribe to
        // a stale/removed process id must not silently drop the client's existing live feed. (A9)
        if (!_processes.TryGetValue(processId, out var process))
        {
            return false;
        }

        RemoveClientFromSubscriptions(webClient);

        var subscription = GetSubscription(process);
        await subscription.AddWebClient(webClient);

        // Rollback if connection disconnected concurrently
        if (!_webClients.ContainsKey(webConnectionId))
        {
            subscription.RemoveWebClient(webClient);
            return false;
        }

        return true;
    }

    private DiagnosticSubscription GetSubscription(DiagProcess process)
    {
        return _subscriptions.GetOrAdd(process, key => new DiagnosticSubscription(key, _timeProvider));
    }
}
