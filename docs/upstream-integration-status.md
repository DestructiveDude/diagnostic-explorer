# Upstream integration status

This fork ports features from `cell001nz/diagnostic-explorer` onto its own
implementation. Upstream baseline: `f8dbb59`. The original phase numbering is
historical; use the implemented behavior below when resuming work.

## Already on main before this pass

- Logging core and MEL/NLog/Serilog adapters, with log4net retained in the core assembly.
- Property/getter model and fluent type configuration.
- MessagePack and SignalR client results on the agent channel.
- Realtime log stream with bounded payloads, framed replay and routing-change recovery.
- Drilldown protocol, fenced collection paths and deduplicated operator actions.
- First UI port: drilldowns, breadcrumbs, collection inspection, JSON/expanded previews,
  contextual actions, projected event views and event detail (PR #164).

## This completion pass

The implementation plan is [upstream integration completion](superpowers/plans/2026-09-08-upstream-integration-completion.md).
It closes legacy event retention, shared routing ownership and logging registration
helpers. It also activates configured object-registration callbacks, which the
first configuration port stored without invoking.
The remote host passes its service provider to each request's root collection,
so configured DI services can participate in diagnostics, drilldowns and actions.

The [logging executable](../samples/Logging/README.md) exercises all four adapters
without importing four copies of the upstream WinForms harness. The existing
`src/WidgetSample` remains the graphical diagnostics demonstration.

## UI completion pass

Completed in the `phase6-ui-completion` branch. Keep our `diagnostics-web`
application, Realtime/Retro switch and Trace Scope views.

| Status | Completed behavior |
| --- | --- |
| Complete | Per-process retained event stores honour stream/sequence identity and negotiated retention; sink views bind to reconciled destination projections. |
| Complete | Fixed configured sinks render when empty and routes reconcile without discarding surviving view state. |
| Complete | Visible event-bearing drilldowns own process subscriptions and retain their originating process after main selection changes. |
| Complete | Bag, group and property operations use contextual operation sets and complete fenced paths while preserving their originating process and refresh behavior. |
| Complete | Preview and JSON buttons provide live, read-only structured or formatted JSON overlays with five-second refresh, keyboard and pointer lifetime handling, and retained originating context. |

The source comparison points are our `Model/RealtimeModel.ts`, `Model/EventSinkModel.ts`,
`Model/PropGroup.ts`, `realtime-category/` and `drill-down-dialog/`, against upstream
`diag-web/src/app/diagnostics/model/ProcessEventStore.ts`, `ProcessModel.ts`,
`category-view/` and `property-hover/`.

Drilldown loading, manual refresh, stale-response suppression, errors, truncation,
breadcrumbs and full collection paths already work. Reuse them. Our Trace Scope
tab is valuable fork functionality and should survive the next UI pass.

## Deployment boundary

The project versions are `4.0.0`; this document does not establish a package release
or an EMS upgrade. Deploy the compatible DiagnosticService before new agents.
EMS must switch its realtime log4net configuration from `DiagnosticAppender` to
`RoutingDiagnosticAppender` and supply routing. There is deliberately no bridge
from the legacy appender to the new realtime stream.

Self-hosted web assets and the WiX Windows installer remain separate deployment
decisions. They are not prerequisites for the fork's Docker-hosted service.
