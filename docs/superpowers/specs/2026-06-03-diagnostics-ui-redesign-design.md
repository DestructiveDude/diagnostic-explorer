# Diagnostic Explorer — UI Redesign Design

**Date:** 2026-06-03
**Project:** FixPortal `diagnostic-explorer` fork — `diagnostics-web` (Angular 21 + PrimeNG)
**Status:** Design approved (brainstorming); ready for implementation planning.

## 1. Overview & goals

Re-platform of the `diagnostics-web` UI off Angular Material (broken by the
Angular 13→21 MDC migration) onto **PrimeNG**, finished and polished as **our own
product** — "the diagnostics we know and love": a fast, dense, dark diagnostics
console for local use.

Crucial reframing from this session: the assumption that Cameron's
`diag-azure-app` was a finished reference UI to copy was **false** — when built
and run (preview container, port 8090) it proved to be an early, unfinished
PrimeNG scaffold (bare process-table sidebar, WIP markers, half-built Azure/SaaS
shell). Our fork is functionally *ahead* (runs locally on real data, has Retro,
tested). So this design is **not** a copy of his app; it is our own layout/visual
design, freed from matching anything. See
`memory/cameron-primeng-rewrite-reference.md` (corrected).

**Non-goals / out of scope (this spec):**
- Azure auth / SaaS shell (members, sites, login) — deliberately deferred.
- Repointing the Docker SPA build to the new bundle — separate follow-up.
- Backend / SignalR / Mongo changes — UI only (existing data layer unchanged).

## 2. Framework & styling decisions

- **PrimeNG (locked).** Rationale: "back to Material" is not a rollback — the old
  Material is gone (MDC rewrite is what broke us), so it would mean re-theming
  from scratch and re-fighting Material's density constraints. PrimeNG suits a
  dense data tool (rich tables/panels/trees) and we already have a working
  PrimeNG baseline on the `primeng-migration` branch.
- **Theme:** PrimeNG **Aura** preset, dark mode (`darkModeSelector: .app-dark`),
  via `providePrimeNG(...)` (already wired in `app.module.ts`).
- **Tailwind:** keep the existing **Tailwind v3** config (custom grid/size
  utilities the templates rely on). No Tailwind 4 / tailwindcss-primeui migration.
- **Custom palette** layered on top as CSS custom properties (section 6) — Aura
  provides component chrome; our tokens define surfaces, accent, value/severity
  colours.

## 3. Information architecture — two modes, one shell

The app is a single fixed full-viewport shell with **no page-level scroll**;
regions scroll internally only when their own content overflows. Two modes share
the shell, switched by a **segmented toggle** at the top of the left rail:

| Region | **Realtime** mode | **Retro** mode |
|---|---|---|
| Left rail | Process list (filter + "online only") | Search form (query fields + buttons) |
| Main content | Category property/event panels | Flat results table |
| Right strip | Category navigation list (severity dots) | *(none — Retro has no categories)* |
| Docked detail | Tabbed: Detail / Trace Scope | Tabbed: Detail / Trace Scope |

## 4. Layout (chosen direction "A")

Top to bottom:

- **Top bar** — app title (left), active-process context `machine / user / process`
  (centre, lime when online), status `Received HH:mm:ss` / `Search N of M · Ts`
  (right).
- **Body** — horizontal split: **left rail** | **main**.
  - Left rail: fixed-ish width (~210px Realtime, ~236px Retro), **resizable**.
  - Main (Realtime): horizontal arrangement of **content** (fills) + **category
    strip** (right, ~150px). Content is itself split vertically: **panels (fills)**
    over a **docked detail panel** (bottom).
  - Main (Retro): **results table (fills)** over the docked **detail panel**; no
    right strip.

**Resize points (all PrimeNG-native, no custom drag code):**
1. Sidebar width — `p-splitter`.
2. Content ⇆ detail vertical split — nested `p-splitter`.
3. Process table & results table **column widths** — `p-table [resizableColumns]`.
4. Category strip width — `p-splitter` (consistency; minor).

Persist user-adjusted sizes across reloads (localStorage) — nice-to-have.

## 5. Visual reference

Companion mockups (saved under `.superpowers/brainstorm/587253-1780497863/content/`):
`mockup-a-pal2-v5.html` (Realtime, final look + resize points),
`mockup-retro-v3.html` (Retro + focus), `mockup-trace-scope.html` (Trace Scope tree).

## 6. Palette — "Muted slate, punchy severity" (locked)

Define as CSS custom properties on the dark root. Hex values:

```
/* surfaces & chrome */
--bg:        #0f141b   /* app background            */
--topbar:    #141b24   /* top bar / detail bg       */
--surface:   #1a212b   /* panel headers, inputs     */
--border:    #2a3340   /* borders, gutters          */
--head:      #e2e8f0   /* heading / primary text    */
--label:     #94a3b8   /* secondary / labels        */

/* accents */
--accent:    #6366f1   /* indigo: active toggle, focus ring, primary buttons */
--active:    #818cf8   /* active category indicator */
--value:     #86efac   /* property values (lime)    */
--value-set: #818cf8   /* settable property names (blue) */
--online:    #86efac   /* online process rows       */
--resize:    #2dd4bf   /* teal: resize grips        */

/* severity (per level band) — row tint / left slice / bold text */
error / severe   bg #2a1416  slice #ff5252  text #ff6b6b
warn             bg #2a2110  slice #ffa31a  text #ffc24d
notice           bg #1d1430  slice #b15cff  text #d8a2ff
info/debug/trace/verbose  bg #0f2418  slice #2ee06a  text #5cf08a
critical/alert/fatal/emergency  → reuse the error band (default; see §15)

/* severity dots (category nav) */
quiet #475569   ok #34d399   warn #fbbf24   error #f87171
```

Focus ring (inputs, on focus): `border-color: --accent; box-shadow: 0 0 0 3px rgba(99,102,241,.32)`.

## 7. Event-row treatment (Realtime event tables & Retro results)

Calm row + punchy severity. Per row of severity *S*:
- **Row background:** the muted `bg` tint for *S* (message stays readable).
- **Left slice:** 4px solid `slice` colour on the first cell **+** a soft inset
  glow `inset 7px 0 9px -6px <slice>`.
- **Metadata cells** (Realtime: Id, Date, Level — Retro: Date, Level): **bold**,
  in the `text` severity colour.
- **Other metadata** (Retro: Machine, User, Process): secondary (`--label`/`--txt`).
- **Message** (last cell): **white, ~12.5px, semibold (600)** — primary readable
  content, deliberately *not* full-bold to avoid a heavy wall across long streams.
- Selected row: indigo outline (`2px #6366f1`, inset) + slight brightness.
- Drag-select supported (mousedown + mouseover-while-pressed) — existing model
  behaviour (`handleMouseOver` checks buttons).

Level→band mapping uses the existing `Level` enum (Verbose 10000 … Emergency
120000) and `levelName` pipe; CSS classes `event-level-<name>` (lowercase).

## 8. Category navigation (Realtime, right strip)

Replace the cramped vertical "tabs-right" with a **readable vertical list**:
- One row per category: **severity dot** + name; active row highlighted with an
  `--active` right border + bolder text; list scrolls if many categories.
- Severity dot reflects the category's worst recent level (existing
  `CategoryModel.labelClass` / `worstSev` logic): quiet/ok/warn/error.
- Scales to many categories and keeps names readable (the old rotated/cramped
  tabs were the sore point).

## 9. Detail panel + Trace Scope tree (restore)

Docked at the bottom of the main area, **tabbed**:
- **Detail** — raw event detail/message, monospace, read-only, scrollable.
- **Trace Scope** — the **collapsible node tree** (restored).

**Trace Scope is a first-class requirement.** It was invaluable and degraded to a
bare `+/-` button with crude `ml-10` indent (`collapsible-region.component`),
then got orphaned in the Material→PrimeNG pass (temporarily replaced by a flat
textarea). The parsing already exists: `ScopeNode.parseTraceScope()` builds the
tree from indented `[nn.nnn] [nn.nnn] BEGIN … / END …` blocks; `EventModel`
still calls it. Work:
1. **Investigate & fix** why `collapsible-region` stopped rendering the tree
   (Angular 13→21 churn + the migration swap).
2. Rebuild the renderer as a proper tree:
   - chevron (▸/▾) per node, **indent guides** (left border per level),
   - per-scope **start-offset** (dim) + **duration badge**; **red badge for
     large/slow durations** so expensive scopes pop,
   - **leaf lines** (e.g. SQL, counts) under their scope, values in `--value`,
   - **"View raw text"** disclosure to see the original wall,
   - native expand/collapse (or PrimeNG tree) — each node independently
     collapsible; remember `expanded` per node (existing `ScopeNode.expanded`).

## 10. Retro view specifics

- **Search form** (left rail), fields from `RetroQuery` / existing `retro-nav`:
  Max Records (select 1k/5k/10k/20k), Min Level (All…Emergency select),
  Machine / Process / User (text), Message-contains (text), Date (date picker),
  Time (hour select) + Window/Hours (duration select). Buttons: **Reset**,
  **Delete** (disabled unless deletable), **Search** (toggles to **Cancel** while
  running). Determinate **progress bar** during a search.
- **Query text inputs** get the indigo **focus highlight** (section 6).
- **Results table** (main): columns Date · Level · Machine · User · Process ·
  Message, with the section-7 severity treatment; sticky header; results count +
  in-results filter (existing `event-filter`). Click a row → detail panel.

## 11. Component mapping

Existing Angular components (keep structure; this is refinement of the
`primeng-migration` work, not a rewrite):

| Component | Role | PrimeNG widgets |
|---|---|---|
| `app.component` | shell: top bar, mode toggle, splitters | `p-splitter`, `p-button`/`p-selectButton` |
| `realtime-nav` | process list (filter + online) | `p-table` (resizable cols), `p-iconfield`+`pInputText`, `p-checkbox` |
| `realtime-display` | category strip + content + detail | `p-splitter`, category list, `p-tabs` (detail) |
| `realtime-category` | bag/group/property panels | `p-panel`, `p-fieldset` |
| `realtime-events` | event sink table | `p-table` / styled table (severity rows) |
| `event-filter` | in-results level filter | `p-checkbox`, `pInputText` |
| `retro-nav` | search form | `p-select`, `pInputText`, `p-datepicker`, `p-button`, `p-progressbar` |
| `retro-display` | results table + detail | `p-table` (resizable cols), `p-tabs` |
| `collapsible-region` | Trace Scope tree | tree (native details or `p-tree`) |
| detail panel | Detail / Trace Scope tabs | `p-tabs` |

Category navigation: a styled list (could be `p-listbox` or custom) replacing the
`tabs-right` p-tabs.

## 12. States

- No process selected → centred hint "Select a process from the list to view
  diagnostics." (already added).
- Process selected, no frame yet → "Waiting for diagnostics…".
- Empty event sink / empty results → "No events".
- Retro searching → progress bar + Search→Cancel.

## 13. Quality gates (must stay green)

Jest tests, ESLint + SonarJS, production build (within budgets — restore the
initial-bundle budget to 2mb once Material is removed), CodeQL clean. Material
(`@angular/material`) fully removed at the end; `@angular/cdk` kept only for
`Clipboard`.

## 14. Implementation notes

- Builds on the existing `primeng-migration` branch (components already converted
  Material→PrimeNG). This design **adjusts**: layout A + splitters, the palette
  tokens, category-nav list (replacing tabs-right), event-row treatment, the
  tabbed detail panel with the **restored Trace Scope tree**, Retro form/results
  polish, indigo focus, resizable columns.
- Keep our data layer untouched (models, `diag-hub` SignalR to local `/web-hub`,
  Retro Mongo search). Anonymous/local; no auth.

## 15. Open questions (non-blocking; defaults chosen)

- Critical/Alert/Fatal/Emergency severity band — default to the `error` band
  unless a distinct darker-red is wanted.
- Persist resize sizes in localStorage — default yes (nice-to-have, can defer).
- Category strip resizable — default yes (consistency).
