# AI Findings Ledger

Durable record of un-dismissable static-analysis findings (GitHub Code Quality / CodeQL / Copilot AI
Findings) for this repo. Substitutes for the missing dismiss UI — a finding present here with status
`dismissed` or `fixed` does not need re-investigation when it resurfaces.

Scan source: GitHub Code Quality (CodeQL default setup, Code Quality mode — no REST API; web UI only).
Last triage: 2026-06-10 (batch23).

| Finding | Status | Reason | Rationale | First seen |
|---|---|---|---|---|
| `DiagnosticExplorer/Props/PropertyGetter.cs:309` — invalid-format-string | dismissed | false positive | Dynamic format string intentionally omits unused indices on some branches; extra args to `string.Format` are silently ignored — no `FormatException` possible | 2026-06-10 |
| `DiagnosticExplorer.Hosting/HubServerAdapter.cs:81` — empty-catch-block | dismissed | by-design | `ObjectDisposedException` on `CTS.Cancel()` in teardown is correct to swallow; comment explains disposal ordering | 2026-06-10 |
| `DiagnosticService/Common/LZString.cs:609` — redundant-ToString | fixed | — | Removed `.ToString()` on char; `string + char` works without it | 2026-06-10 |
| `DiagnosticService/Common/LZString.cs:619` — redundant-ToString | fixed | — | Same | 2026-06-10 |
| `DiagnosticService/Common/LZString.cs:392` — useless-assignment | fixed | — | Removed dead `context_enlargeIn` reassignment at end of outer loop; variable never read after loop closes | 2026-06-10 |
| `DiagnosticService/Common/LZString.cs:481` — useless-assignment | fixed | — | Removed dead `next = bits` assignment in `switch`; `next` variable also removed from declaration | 2026-06-10 |
| `DiagnosticService/Common/LZString.cs:16` — inefficient-ContainsKey | fixed | — | Refactored to `TryGetValue` + local dict reference; eliminates double-lookup | 2026-06-10 |
| `DiagnosticService/Common/LZString.cs:601` — inefficient-ContainsKey | fixed | — | Refactored to `TryGetValue`; also removed redundant `.ToString()` on `entry[0]` | 2026-06-10 |
| `DiagnosticExplorer/Util/ProtobufUtil.cs:13` — missing-Dispose | fixed | — | Added `using var` to `MemoryStream` | 2026-06-10 |
| `DiagnosticService/Program.cs:19` — path-combine-drops-args | dismissed | false positive | Both args are relative string literals — neither can be absolute | 2026-06-10 |
| `DiagnosticService/Program.cs:215` — path-combine-drops-args | dismissed | false positive | `Path.IsPathRooted` guard ensures `path` is relative before entering the `Combine` branch | 2026-06-10 |
| `DiagnosticService/Common/MongoRetroLogger.cs:138` — implicit-foreach-filter | dismissed | false positive | `TryParse` out-param pattern; equivalent LINQ chain is more complex and less readable | 2026-06-10 |
| `DiagnosticService/Hubs/RealtimeManager.cs:337` — implicit-foreach-filter | dismissed | by-design | State-mutation loop; `.Where().ForEach()` is not idiomatic for mutation | 2026-06-10 |
| `WidgetSample/Form1.cs:436` — gc-collect | dismissed | by-design | Sample code; intentional GC force to demonstrate gadget removal from diagnostics display | 2026-06-10 |
| `WidgetSample/Form1.cs:446` — gc-collect | dismissed | by-design | Same | 2026-06-10 |
| `DiagnosticExplorer.Hosting/DiagnosticClientHandler.cs:15` — missing-Dispose (Subject) | dismissed | won't fix | Rx `Subject<T>` holds no unmanaged resources; app-lifetime subject, disposal not required | 2026-06-10 |
| `DiagnosticExplorer.Hosting/DiagnosticClientHandler.cs:16` — missing-Dispose (Subject) | dismissed | won't fix | Same | 2026-06-10 |
| `DiagnosticService/Hubs/RealtimeManager.cs:32` — missing-Dispose (Subject) | dismissed | won't fix | Service-lifetime singleton Subject; no unmanaged resources | 2026-06-10 |
| `DiagnosticService/Hubs/RealtimeManager.cs:33` — missing-Dispose (Subject) | dismissed | won't fix | Same | 2026-06-10 |
| `DiagnosticExplorer.Hosting/TraceScope.cs:123` — missing-Dispose (Timer) | dismissed | false positive | `newTimer` assigned to tracked field `_autoTraceTimer` via `Interlocked.Exchange`; `oldTimer` disposed inline; class manages lifetime | 2026-06-10 |
| `WidgetSample/Form1.cs:384,389,394,404,406` — write-to-static-field | dismissed | by-design | Sample code; single-threaded WinForms UI event handlers | 2026-06-10 |
| `DiagnosticService/ClientHandlers/DiagnosticSubscription.cs:33` — write-to-static-field | dismissed | false positive | Uses `Interlocked.Increment` — thread-safe by design; CodeQL does not see through Interlocked | 2026-06-10 |
| `DiagnosticService/Transport/Operation.cs` — IndexOf-without-guard | fixed | — | Added `int i = IndexOf('(')` guard; `Name = i >= 0 ? sig[..i] : sig` | 2026-06-10 |
| `WidgetSample/Form1.cs:666,671` — encoding-artifact (SCOPE TASK strings) | fixed | — | Replaced U+FFFD garbled characters with `###$%` ASCII marker pattern | 2026-06-10 |
| `tests/DiagnosticService.UnitTests/LogAnalyticsRetroLoggerTests.cs:164` — Delete test missing message assertion | fixed | — | Added `.WithMessage("*not supported*")` to `ThrowAsync<NotSupportedException>` | 2026-06-10 |
| Generic catch — all `catch (Exception)` in diagnostic/logging infrastructure (~105 instances) | dismissed | by-design | Appenders must not throw (log4net contract); diagnostic walkers must degrade gracefully; `Dispose()` methods must not throw. Every catch logs or degrades to an error string. Files: DiagnosticHostingService, WebApiUtil, EventSink, DiagnosticManager, AppenderProxy, AsyncProcessor, SmtpAppender, DateGetter, CollectionGetter, ExtendedPropertyGetter, RateGetter, RateCounter, PropertyGetter, TraceScope, HubServerAdapter, RegistrationHandler, DiagnosticSubscription, WebClientHandler, MongoRetroLogger, RealtimeManager, RetroSearchProcess, LoggingExtensions, Form1 | 2026-06-10 |
