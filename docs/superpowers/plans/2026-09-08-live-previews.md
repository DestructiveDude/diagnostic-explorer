# Live Property Previews Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Complete the deferred live structured and readable JSON preview scope.

**Architecture:** One property-preview component owns a CDK connected overlay and its
visible request lifetime. RealtimeCategoryComponent supplies originating context
and routes clicks through its existing drilldown path. Structured previews are read-only.

**Tech Stack:** Existing Angular 22, CDK, PrimeNG, TypeScript and Jest.

**Spec:** docs/superpowers/specs/2026-09-08-live-previews-design.md

## Global Constraints

- Preserve Realtime/Retro, Trace Scope, existing operations and drilldown protocols.
- No dependency/toolchain upgrade, backend change, hosting work, release or deployment.
- No emoji in source, docs or PR text. Reuse existing design tokens and Jest setup.
- Timer behavior must have deterministic tests; no static reads of now in new logic.
- Work only on phase6-live-previews in the preserved phase6-protocol worktree.

### Task 1: Live structured and JSON previews

**Files:**
- Create `diagnostics-web/src/app/property-preview/property-preview.component.ts`, `.html`, `.scss`, `.spec.ts`.
- Modify `diagnostics-web/src/app/realtime-category/realtime-category.component.ts` and `.html` to replace the old tooltip fetching flow; move its preview-button rule from `.scss` into the new component.
- Modify `diagnostics-web/src/app/app.module.ts` for the component and OverlayModule.
- Modify `diagnostics-web/src/styles.scss` for CDK overlay structural CSS and remove obsolete preview tooltip styling.
- Modify `diagnostics-web/src/app/drill-down-dialog/drill-down-dialog.component.spec.ts` for preview integration.
- Modify `diagnostics-web/src/app/Model/RealtimeModel.spec.ts` to control the clock for existing fixed-time log-stream fixtures; preserve explicit advancing expiry tests and production retention.
- Update `docs/upstream-integration-status.md` to mark preview behavior complete after validation.

**Interfaces:**
- Consumes `RealtimeModel.hubService.getDrillDown(request: DrillDownRequest): Promise<DrillDownResponse>`.
- Inputs: `prop: PropModel`, `json: boolean`, `processId: string`, `objectPaths: string[]` (empty by default).
- Produces `inspect = new EventEmitter<void>()`; parent routes to `openDrillDown(prop.getPropertyPath(), prop.name, json)`.
- The tooltip renders read-only `response.diagnostics.propertyBags`, category/group names and properties; it creates no CategoryModel/event state and never retains process events.

- [ ] Read existing category, dialog and model callers, plus upstream `git show f8dbb59:diag-web/src/app/diagnostics/property-hover/property-hover.component.ts` and its template. Read the spec for all lifecycle requirements. Existing upstream lifecycle code is a comparison, not a correctness authority.
- [ ] Add rendered tests using AppModule/TestBed and controlled timers. Start with grouped preview content and a second response on refresh; verify RED before implementation. Extend with keyboard/pointer lifetime and races as the implementation grows. A representative invariant is:

```typescript
button.dispatchEvent(new FocusEvent('focus'));
flushMicrotasks();
fixture.detectChanges();
expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Price');
tick(5000);
flushMicrotasks();
expect(hub.getDrillDown).toHaveBeenCalledTimes(2);
button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));
tick(5000);
expect(hub.getDrillDown).toHaveBeenCalledTimes(2);
```

- [ ] Implement the component with `cdkOverlayOrigin` and `cdkConnectedOverlay`, viewport margin/fallback positioning and a body overlay. Keep the tooltip read-only and scrollable. Inputs bind directly from the enclosing category context; use a copied request captured when opening:

```typescript
const request: DrillDownRequest = {
    ...new DrillDownRequest(), id: this.processId,
    objectPaths: [...this.objectPaths, this.prop.getPropertyPath()],
    jsonHover: this.json, excludeEventViews: true
};
```

  Use independent pointer/focus presence so leaving one does not dismiss the other.
  Escape/click close immediately and clear all refresh/hide timers. Invalidate a
  per-opening identity on close and input-context change; each async completion
  checks its identity before touching data/loading/pending flags. Use no-overlap
  polling at 5000ms. Reopen gets independent request state. Check current path when
  deciding whether an in-place updated property/context still identifies the target.

- [ ] Render grouped diagnostics with Angular interpolation and JSON in `<pre>`.
  Preserve the returned JSON text unchanged: the service already indents it and
  client parsing would round large integers and precise decimals. Empty/absent
  JSON uses the existing empty state. Assert exact numeric text in rendered tests.
  Preserve diagnostics.exceptionMessage, response.errorMessage, transport error,
  empty results and displayedCount/totalCount truncation notices. Hide fences only
  in display (`bag.name.split('\u001f')[0]`), never in requests. Clear stale data on
  errors; allow later polling to recover.
- [ ] Integrate trigger outputs with existing category inspect flow; remove old
  showPreview state/request flattening. Keep separate JSON/Preview opt-ins and labels.
  Include CDK structural overlay CSS using its installed package stylesheet and
  sufficient overlay stacking for dynamic dialogs, using existing design tokens.
- [ ] Cover successful structured and JSON rendering, malformed JSON and escaped
  markup, truncation/empty/errors/recovery, focus+hover deduplication, pointer transit,
  Escape/blur/leave/destroy/input changes, pending refresh exclusion, reopen races,
  no event subscriptions, and original process/fenced nested paths after selection
  changes. Modify the existing dialog preview tests to use the new rendered overlay.
- [ ] Run focused tests during iteration. Run the full frontend suite once before
  committing; record RED/GREEN evidence and exact commands/output in the report.

```powershell
& 'C:/Users/chris/AppData/Local/npm-cache/_npx/09ae5d3560c7b1f2/node_modules/node/bin/node.exe' node_modules/jest/bin/jest.js --runInBand --coverage
```

- [ ] Update the status document, self-review the diff and commit all task-owned
  changes. Do not push. The controller handles browser checks, remaining gates,
  independent task/final review, one completed push and the existing PR review gate.
