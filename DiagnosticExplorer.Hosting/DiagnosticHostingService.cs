using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer.Log4Net;
using log4net;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;


namespace DiagnosticExplorer;

public class DiagnosticHostingService
#if NET5_0_OR_GREATER
    : IHostedService
#endif
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(DiagnosticHostingService));

    // Accessed via Interlocked/Volatile; only ever published after StartHosting succeeds so a
    // failed init can't leave a non-null, half-initialized instance behind.
    private static DiagnosticHostingService _instance;
    private DiagnosticOptions _options;

    private RegistrationHandler[] _registrationHandlers;

    private Action<HttpConnectionOptions> _configureHttp;

    private DiagnosticHostingService(DiagnosticOptions options, Action<HttpConnectionOptions> configureHttp = null)
    {
        _options = options;
        _configureHttp = configureHttp;
    }

#if NET5_0_OR_GREATER

    public DiagnosticHostingService(IOptions<DiagnosticOptions> options, Action<HttpConnectionOptions> configureHttp = null)
        : this(options.Value, configureHttp)
    {
        Debug.WriteLine($"DiagnosticHostingService constructed {_options.Enabled} Uri [{_options.Uri}");
    }


    public Task StartAsync(CancellationToken cancel)
    {
        Debug.WriteLine($"DiagnosticHostingService starting {_options.Enabled} Uri [{_options.Uri}");
        if (_options.Enabled)
            TryStart(this);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancel)
    {
        Interlocked.CompareExchange(ref _instance, null, this);
        return StopHosting();
    }

#endif

    // Claim the singleton slot atomically, then start. Publish stays only if hosting actually
    // starts: on failure we roll the slot back to null so a later Start can retry and LogEvent
    // never sees a half-initialized instance.
    private static void TryStart(DiagnosticHostingService candidate)
    {
        if (Interlocked.CompareExchange(ref _instance, candidate, null) != null)
            throw new InvalidOperationException("An instance of DiagnosticHostingService is already running. Only one instance can run at a time.");

        if (!candidate.StartHosting())
            Interlocked.CompareExchange(ref _instance, null, candidate);
    }

    private bool StartHosting()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.Uri))
            {
                _log.Warn("DiagnosticHostingService not started: no Uri configured");
                return false;
            }

            DiagnosticRetroAppender.SetLoggingAction(LogEvent);
            SystemStatus.Register();

            Registration registration = new() {
                ProcessId = Process.GetCurrentProcess().Id,
                InstanceId = Guid.NewGuid().ToString("N"),
                UserDomain = Environment.UserDomainName,
                UserName = Environment.UserName,
                MachineName = Environment.MachineName,
                ProcessName = ResolveProcessName()
            };

            RegistrationHandler[] handlers = Regex.Split(_options.Uri, @"\s|;|,")
                .Select(hubUrl => hubUrl.Trim())
                .Where(hubUrl => !string.IsNullOrWhiteSpace(hubUrl))
                .Select(hubUrl => new RegistrationHandler(hubUrl, registration, _options.ApiKey))
                .ToArray();

            foreach (RegistrationHandler handler in handlers)
                handler.Start(_configureHttp);

            // Publish only after the full build + start succeeds.
            _registrationHandlers = handlers;
            return true;
        }
        catch (Exception ex)
        {
            // Diagnostics setup must not crash the host, but the failure must be visible (logged,
            // not swallowed to Debug) and must not leave a half-initialized instance published.
            _log.Error("DiagnosticHostingService failed to start", ex);
            DiagnosticRetroAppender.SetLoggingAction(null);
            return false;
        }
    }

    // The entry assembly name is a stabler identifier than the OS process
    // name for .NET apps: `dotnet MyApp.dll` reports "dotnet" as the
    // process, but the entry assembly is still "MyApp". Falls back to the
    // process name when there is no managed entry assembly (rare -- mostly
    // unmanaged hosts).
    private static string ResolveProcessName()
    {
        string entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrEmpty(entryAssemblyName))
            return entryAssemblyName;

        return Process.GetCurrentProcess().ProcessName.Replace(".vshost", "");
    }

    public async Task StopHosting()
    {
        try
        {
            DiagnosticRetroAppender.SetLoggingAction(null);

            // Null-guard: StartHosting may have failed (or Stop been called without a successful
            // Start), leaving _registrationHandlers null.
            RegistrationHandler[] handlers = _registrationHandlers;
            _registrationHandlers = null;
            if (handlers != null)
                await Task.WhenAll(handlers.Select(handler => handler.Stop()).ToArray());
        }
        catch (Exception ex)
        {
            _log.Error(ex);
        }
    }


    public static void Start(string url, Action<HttpConnectionOptions> configureHttp = null)
    {
        DiagnosticOptions options = new(url);
        TryStart(new DiagnosticHostingService(options, configureHttp));
    }

    public static async Task Stop()
    {
        DiagnosticHostingService instance = Interlocked.Exchange(ref _instance, null);
        if (instance != null)
            await instance.StopHosting();
    }


    public static void LogEvent(DiagnosticMsg evt)
    {
        DiagnosticHostingService instance = Volatile.Read(ref _instance);
        if (instance != null)
            foreach (RegistrationHandler handler in instance._registrationHandlers ?? Array.Empty<RegistrationHandler>())
                handler.LogEvent(evt);
    }
}