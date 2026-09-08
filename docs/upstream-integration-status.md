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

## Next UI task

This comparison is based on source behavior, not visual or interactive testing.
Keep our `diagnostics-web` application, Realtime/Retro switch and Trace Scope views.

| Priority | Current behavior | Required follow-on |
| --- | --- | --- |
| First | `RealtimeModel.logStreamEvents` holds only the selected process's latest 500 events. Per-sink buffers duplicate them. Negotiated count/age retention is ignored. | A retained store per process, using stream ID and sequence identity, negotiated retention, and destination indexes. Bind sink views to those projections. |
| First | Categories/sinks appear only when an actual event routes to them. | Reconcile fixed configured destinations at initialization, including ordinary empty states, and remove stale projections when routes change. |
| Next | An open drilldown loses its event source when another process is selected. | Bind to the drilldown's process store and explicitly own subscriptions for visible views. A process store alone does not establish an active subscription. |
| Next | Property operation sets are carried but have no button; category operation sets are dropped by `PropGroup`. | Surface group/property operations through the existing contextual request path and retain collection identity fences. |
| Later | Preview buttons fetch one-shot plain text or JSON. | Consider live structured previews, readable JSON and refresh while visible after the event/operation work. |

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
