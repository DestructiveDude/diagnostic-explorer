# UI Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete retained process events, configured sinks, live drilldowns and group/property operations in the fork's UI.

**Architecture:** RealtimeModel owns per-process retained stores and a desired subscription set on the existing hub connection. Sink and dialog views consume retained records; operation targets share the existing structural path contract.

**Tech Stack:** Angular/TypeScript, RxJS, PrimeNG, Jest, ASP.NET Core SignalR and xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-08-ui-completion-design.md`

## Global Constraints

- Preserve Realtime/Retro, Trace Scope, routing semantics, full fenced paths, loading/errors, stale-response guards and manual refresh.
- No new dependencies, incidental Node/Angular upgrades, emoji, new preview subsystem, Hosts/SelfHost work, release or deployment.
- Tests use existing Jest; C# uses existing xUnit v3, AwesomeAssertions Should() and NSubstitute conventions.
- Read `C:/Users/chris/.agents/notes/web-ui-traps.md` before component edits and `C:/Users/chris/.agents/notes/dotnet-runtime-traps.md` before SignalR test edits.
- One logical shell command per call; no push from workers; commit only your task files. No subagents from workers.

### Task 1: Retained process event stores and fixed destinations

**Files:** Create `diagnostics-web/src/app/Model/ProcessEventStore.ts` and `.spec.ts`. Modify `Model/RealtimeModel.ts`, `Model/RealtimeModel.spec.ts`, `Model/EventSinkModel.ts`, `Model/CategoryModel.ts` under `diagnostics-web/src/app`. Inspect `Model/LogStream.ts`, `Model/EventModel.ts`, `realtime-events/` and all callers before editing.

**Interfaces:** Consumes existing LogStreamInitialization/LogStreamEvent and resolveDestinations/destinationKey/toSystemEvent. Produces `RealtimeModel.getProcessEventStore(id: string): ProcessEventStore` and a store `events: LogStreamEvent[]`, `initialize(initialization): void`, `append(events): void`, `prune(): void`. Store time reader constructor defaults to a supplied Date.now callback; tests pass a mutable clock. Main compatibility `logStreamEvents` may be a getter over selected store. Task 2 will route owned nonselected frames and use the process accessor.

- [ ] Write failing store/model regressions for count/age >500 and expiry without new traffic, duplicate/out-of-order/wrong stream, authoritative same/new-stream resets, fixed empty and derived destinations, routing replacement, process switch/removal, sink-filter preservation and expired selection cleanup. Use fresh controlled timestamps, replacing historical fixture timestamps where new age rules require it.

```ts
let now = Date.parse('2026-09-08T10:00:00Z');
const store = new ProcessEventStore(() => now);
// Existing logEvt/routeTo-style builders must supply matching stream IDs.
// With maxEvents=2, append sequences [3, 1, 2, 3]; expect [3, 2].
// Advance now beyond maxAgeMinutes; prune(); expect events and derived sinks empty.
```

- [ ] Run `npx jest --runInBand src/app/Model/ProcessEventStore.spec.ts src/app/Model/RealtimeModel.spec.ts` and confirm behavioral failures.
- [ ] Implement the store using existing routing helpers; records are unique per stream/sequence, destination projections refer to retained events. Preserve the fork's authoritative initialization, live stream fences and severity scale. Replace realtime sink accumulation with projection reconciliation; retain actual view state for destinations that survive. Prune every store on the existing maintenance tick, and remove absent process stores.
- [ ] Run the targeted tests, TypeScript and Angular template checks; self-review all affected paths and commit `feat(ui): retain per-process log streams and configured sinks`.

### Task 2: Own subscriptions for visible process views

**Files:** Modify `src/DiagnosticService/Hubs/RealtimeManager.cs`, `WebHub.cs`; if lifecycle serialization needs it, `ClientHandlers/WebClientHandler.cs`. Tests: `tests/DiagnosticService.UnitTests/Hubs/RealtimeManagerTests.cs`, `WebHubContractTests.cs` and the existing lifecycle/stream test file that supplies the real subscription fixture. Modify `diagnostics-web/src/app/Model/RealtimeModel.ts`, `.spec.ts`, `drill-down-dialog/drill-down-dialog.component.ts`, `.html`, `.spec.ts`, and hub service only if it simplifies ownership.

**Interfaces:** Consumes Task 1 `getProcessEventStore(id).events`. Produces hub `Task<bool> SetSubscriptions(string[] processIds)` delegating to manager `Task<bool> SetWebClientSubscriptions(string webConnectionId, string[] processIds)`. Retains exclusive `Subscribe` with its established stale-target guarantee. Produces `RealtimeModel.retainProcessEvents(id: string): () => void`; returned release is idempotent. The main selection plus retained dialog owners form desired subscriptions.

- [ ] Add failing tests for A+B concurrent feeds, set [B] releases A only, duplicates/repeated desired sets do not replay/restart B, empty releases all, invalid member leaves current set intact, legacy Subscribe stays exclusive, and disconnect during an awaited attachment leaves no member. Follow real state/stream probes used by existing tests; do not prove absence with sleeps.

```csharp
(await manager.SetWebClientSubscriptions(connectionId, [processA.Id, processB.Id])).Should().BeTrue();
(await manager.SetWebClientSubscriptions(connectionId, [processB.Id])).Should().BeTrue();
(await manager.SetWebClientSubscriptions(connectionId, ["missing"])).Should().BeFalse();
```

- [ ] Add frontend tests covering select A/open A drilldown/select B, both streams delivered, closing final A owner releases only A, two owners share A, navigation without events releases ownership, reconnect resends current union, rapid pending updates finish with latest desired set, rejected updates surface failure, process removal ignores stale frames. Dialog display must use its own store even when B is selected.

```ts
const release = model.retainProcessEvents('A');
await model.selectProcess(processB);
// Assert eventual SetSubscriptions payload contains A and B; release twice is safe.
release();
release();
```

- [ ] Run the focused frontend/backend tests and confirm missing behavior.
- [ ] Implement reconciliation with unchanged memberships preserved, serialized desired updates, validation-before-mutation and disconnect rollback. Keep the existing connection and existing replay chain. Replace obsolete 500-event/selected-process dialog messages with accurate plain-language retained-event copy.
- [ ] Run focused tests, frontend checks, CSharpier check and Release backend build/tests. Commit `feat(ui): keep visible drilldowns subscribed across process changes`.

### Task 3: Contextual group and property operations

**Files:** Modify `diagnostics-web/src/app/Model/PropGroup.ts`, `Model/ExecOperationsModel.ts`, `realtime-category/realtime-category.component.ts`, `.html`; extend the existing rendered operation tests in `drill-down-dialog/drill-down-dialog.component.spec.ts`. Add a focused model spec only if existing tests cannot cover a required behavior cleanly. Update `docs/upstream-integration-status.md` with completed behavior and remaining preview scope.

**Interfaces:** Consume the existing structural `{operationSet: string; getPropertyPath(): string}` contract of SubCat/PropGroup/PropModel and existing action context. No new target hierarchy needed.

- [ ] Add failing parameterized tests for bag/group/property targets: operation selection from contextual sets, target path with empty group and fenced collection bag, stable original process ID after main selection changes, operation completion refresh, conditional accessible buttons and copied group.operationSet on update.

```ts
// ExecOperationsModel's target is structural; existing SubCat calls remain valid.
const target = {operationSet: 'ops', getPropertyPath: () => 'Trading|Orders[2]\u001fabc12345||Price'};
// After execute, assert request.path equals the full target path and objectPaths is unchanged.
```

- [ ] Run focused Jest tests and confirm failures.
- [ ] Carry group.operationSet, use target.getPropertyPath() in the existing execution model, widen the existing dialog method structurally, and add labeled bolt buttons matching the bag control. Preserve existing completion and error handling.
- [ ] Run full frontend typecheck/templates/lint/Jest. Update status docs and commit `feat(ui): expose contextual group and property operations`.

## Final gate

- [ ] Whole-branch spec, quality and composition review; fix findings and verify the changed behavior.
- [ ] Run full frontend and backend gates once on final tree; record production build evidence or precise environment limit.
- [ ] Classify risk from `.claude/review-policy.json`, push completed branch, create one PR, request Gitar immediately, handle review and CI through completion. No merge or release.
