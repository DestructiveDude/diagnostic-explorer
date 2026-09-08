# Logging integration examples

This one executable demonstrates Microsoft.Extensions.Logging, NLog, Serilog and
the in-core log4net appender. Each method is an independent startup example using
the fluent routing configuration. It uses project references and the repository's
central package versions; no published upstream packages or local web server are
required.

Run the four adapters and check that each delivers one event:

```powershell
dotnet run --project samples/Logging/Logging.csproj -c Release -f net10.0
```

On Windows, check the compatibility target:

```powershell
dotnet run --project samples/Logging/Logging.csproj -c Release -f net48
```

Optionally report the retained events to an existing DiagnosticService and wait
for you to inspect the process in its browser UI:

```powershell
dotnet run --project samples/Logging/Logging.csproj -c Release -f net10.0 -- http://localhost:2803/diagnostics
```

The route is declared once and remains as base routing after the adapters close,
so those retained events remain addressable. In a real application keep the
logger factory/logger/repository alive for the application's lifetime, and shut
it down with the host.

This is a compact adaptation of the four upstream logging demonstrations. The
existing `src/WidgetSample` remains the WinForms diagnostics demonstration; this
sample focuses on logging registration and delivery.
