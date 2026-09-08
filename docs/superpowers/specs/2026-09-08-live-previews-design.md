# Live property previews

Continue the remaining upstream UI scope at baseline `f8dbb59`, after PR #167.
The user authorized richer previews and delegated model choice. Terra implements;
Astra reviews. This changes the preview lifecycle within the existing Angular UI.

## Behavior

- Keep separate opt-in Preview and JSON buttons, keyboard focus previews, and
  click/Enter opening the existing drilldown dialog.
- Show structured read-only diagnostics grouped by category, bag and property group.
  Preserve property names/values and display collection bag names without identity fences.
  Actions and nested navigation remain in the existing click-open drilldown.
- Show JSON with indentation using native JSON parsing/formatting; preserve raw text
  when malformed or truncated. Render all remote text through Angular escaping.
- Fetch immediately on opening, then every 5000ms while visible, with no overlapping
  requests for a preview. Hovering the preview keeps it open so it can be read/scrolled.
  Keyboard focus keeps it open independently of pointer movement; Escape dismisses it.
  A short 150ms leave delay allows moving from trigger to overlay.
- Close on trigger click, focus/pointer departure, component destruction, or a change
  to the originating process/path/property/mode. Cancel timers and fence pending results
  on close. Closing/reopening while a request is pending must not display the old result
  or strand the new preview. Changing main selection must not redirect a drilldown preview.
- Requests retain the original process ID, copied objectPaths chain and complete fenced
  property path; always exclude event views. Previews never acquire event subscriptions.
- Preserve loading, agent/transport/diagnostic errors, empty results and collection
  truncation notices. Refresh errors replace stale data and subsequent refresh may recover.
- Fit within desktop and narrow viewports and remain visible over dynamic dialogs.
  Associate the preview with its trigger using aria-describedby and role=tooltip.

## Implementation boundary

Use one property-preview component to own the trigger, CDK connected overlay and
request/timer lifetime. Angular CDK is already installed. Bind originating context
from RealtimeCategoryComponent and emit inspect to its existing openDrillDown flow.
Use a read-only grouped template rather than recursively embedding the action-rich
category component in a tooltip. No JSON tokenizer or new dependency is necessary.

The installed PrimeNG tooltip creates embedded template views without destroying
them on hide and exposes no onHide output. A polling component inside that tooltip
would therefore not get reliable lifetime cleanup. CDK supplies that boundary.

## Global constraints

- Preserve Realtime/Retro, Trace Scope, existing operations and drilldown protocols.
- No dependency/toolchain upgrade, backend change, hosting work, release or deployment.
- No emoji in source, docs or PR text. Reuse existing design tokens and Jest setup.
- Timer behavior must have deterministic tests; no static reads of now in new logic.
- Work only on phase6-live-previews in the preserved phase6-protocol worktree.

## Validation

Rendered component tests cover focus/hover/Escape/click, grouped data and JSON,
errors and recovery, truncation, five-second refresh, no overlapping requests,
close/destroy/context replacement and late-response races. Integration tests verify
original process and fenced paths from a dialog after main selection changes.
Run frontend tests with coverage, TypeScript, lint and production build. Browser
smoke covers live updates, readable structured/JSON data, pointer transition,
keyboard dismissal and overlay geometry at desktop and 480px widths.
