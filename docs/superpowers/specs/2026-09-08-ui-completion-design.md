# UI integration completion

Continue the four First/Next items in `docs/upstream-integration-status.md` against upstream `f8dbb59`. Preserve the fork's application and existing interaction patterns.

## Retained events and destinations

RealtimeModel owns one ProcessEventStore per known/viewed process, not a static global cache. Store each accepted stream/sequence once, in descending sequence order. Use the initialization's positive finite count/age retention, with 5,000 events and five minutes as defensive defaults. Prune on arrival/initialization and the existing one-second maintenance tick; inject the store's time reader for deterministic checks. Ignore wrong-stream live frames and events before initialization. Initialization remains an authoritative snapshot, including framed replay delivered afterward; replace retained contents and routing even for the same stream ID. This preserves the fork's restart/routing recovery contract.

Use existing route matching and destination key helpers. Maintain destination projections containing references to retained records, rather than independent capped sink buffers. Fixed category/name destinations exist when empty; derived destinations exist while retained events resolve to them. Reconcile routes, sinks and categories without discarding property bags or surviving sink filters/expansion. Drop stale selected events and Trace Scope when their record no longer exists. Remove stores when processes are removed or absent from an authoritative process list. Retain stores across ordinary selection changes.

## Subscription ownership

WebHub currently exposes exclusive Subscribe(processId). Keep it working for old browsers and add SetSubscriptions(string[] processIds), backed by RealtimeManager, to reconcile this connection's desired set. Validate all targets before changing memberships; an empty set releases all. Do not tear down/re-add unchanged memberships. Preserve disconnect rollback, replay-before-live ordering and existing agent lifecycle. Observe and surface failures; invalid desired sets must not quietly replace a valid feed.

The browser sends the union of the selected process and process IDs held by open event-bearing drilldowns. Serialize/reconcile updates so rapid selection, close and reconnect cannot let an earlier request become final state. Each dialog releases its own ownership on close, navigation to a view without events, or removal; multiple dialogs share a process subscription. Incoming stream frames update the owned process store, independent of main selection; property diagnostics still only update the selected process. Reconnect reestablishes the complete current set. Dialog event views bind to their request's process store. Removed processes cannot be recreated by stale frames. Reuse the existing connection instead of opening a connection per dialog.

## Operations

Copy Category.operationSet into PropGroup. The existing operation dialog accepts any target with operationSet and getPropertyPath(), covering SubCat, PropGroup and PropModel. Use that full path for ExecuteOperation, preserving contextual process ID, objectPaths, blank group segments and collection identity fences. Render accessible group/property operation buttons only where operationSet is present. Preserve operation completion refresh and existing errors.

## Constraints and validation

- Preserve Realtime/Retro, Trace Scope, routing semantics, full fenced paths, loading/errors, stale-response guards and manual refresh.
- No new dependencies, incidental Node/Angular upgrades, emoji, new preview subsystem, Hosts/SelfHost work, release or deployment.
- Tests use existing Jest; C# uses existing xUnit v3, AwesomeAssertions Should() and NSubstitute conventions.
- Full frontend TypeScript, Angular templates, lint and Jest gate; backend build/format/tests for subscription changes. Production build uses an already available supported Node if present, otherwise report the known local version limit and use CI's real build.
- Review stateful subscription composition, including replay, ordering, ownership, disconnect and partial failure. Open one completed PR and request review under the committed risk policy.
