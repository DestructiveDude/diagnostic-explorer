# Security Recommendations

These items were surfaced by a Semgrep Pro scan of the `diagnostic-explorer`
repository (rule packs: `p/csharp`, `p/javascript`, `p/typescript`,
`p/security-audit`, `p/secrets`, `p/default`).

The scan raised three issues. One — an unconditional `UseDeveloperExceptionPage()`
in `src/DiagnosticService/Program.cs` — has already been fixed (the developer
exception page is now guarded by `IWebHostEnvironment.IsDevelopment()`, with a
generic `UseExceptionHandler` for non-development environments).

The two items below are **documented but not yet actioned**. They are recorded
here so they can be triaged and scheduled rather than silently dropped.

---

## 1. Missing Subresource Integrity (SRI) on a CDN-hosted script

**File:** `Docker/Utility/test-signalr-connection.html:5`
**Semgrep rule:** `html.security.audit.missing-integrity.missing-integrity`
**Severity:** Low — this is a diagnostic/test utility, not user-facing
production code.

### Current code

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/6.0.1/signalr.min.js"></script>
```

### Risk

The page executes whatever bytes cdnjs returns, with full trust. If the CDN is
compromised — or the fetch is intercepted — arbitrary JavaScript runs in the
page's context. Subresource Integrity closes the gap: the browser hashes the
downloaded file and refuses to execute it if the hash does not match the value
declared in the markup.

### Recommended solution (preferred): self-host the script

Because this is a test utility under `Docker/Utility/`, the cleanest fix is to
remove the third-party runtime dependency entirely. Copy `signalr.min.js`
alongside the HTML file (or reference the copy already bundled with the SPA)
and load it with a relative path:

```html
<script src="./signalr.min.js"></script>
```

No external trust dependency, no SRI hash to maintain.

### Alternative solution: add an integrity attribute

If the CDN reference is kept, pin it with SRI:

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/6.0.1/signalr.min.js"
        integrity="sha512-<hash>"
        crossorigin="anonymous"></script>
```

Notes:

- `crossorigin="anonymous"` is **mandatory**. Without it the browser will not
  expose a cross-origin response to the integrity check and the script fails
  to load.
- Obtain the hash from the cdnjs library page (it offers a "copy SRI" button),
  from <https://www.srihash.org/>, or compute it locally:
  `openssl dgst -sha384 -binary signalr.min.js | openssl base64 -A`.
- The URL already pins an exact version (`6.0.1`), which SRI requires — a
  floating version is incompatible with a fixed hash. The trade-off: bumping
  the version means regenerating the hash, or the script silently stops
  loading.

---

## 2. Permissive CORS policy combined with credentials

**File:** `src/DiagnosticService/Program.cs:59`
**Severity:** Medium if DiagnosticService is reachable from untrusted browsers;
lower if the service is strictly internal. **Confirm the deployment topology
before changing this.**

### Current code

```csharp
app.UseCors(x => x.SetIsOriginAllowed(x => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
```

### Risk

`SetIsOriginAllowed(_ => true)` accepts any `Origin` header. Combined with
`AllowCredentials()`, ASP.NET Core cannot send `Access-Control-Allow-Origin: *`
(browsers forbid `*` with credentials), so it instead **reflects the caller's
origin** and adds `Access-Control-Allow-Credentials: true`.

The effect: any website a user visits can issue credentialed requests to
DiagnosticService — carrying cookies / SignalR authentication — and read the
responses. This defeats the same-origin policy and enables cross-site data
exfiltration.

### Related: a dead, conflicting CORS configuration

`Program.cs:24-31` registers a named policy `"CorsPolicy"` (using
`AllowAnyOrigin()`, no credentials), but `UseCors("CorsPolicy")` is never
called. That named policy is dead configuration and should be removed or
actually used, so the file has a single source of truth.

### Recommended solution: allow-list explicit origins

If credentialed cross-origin access is genuinely required (likely, for the SPA
dev-proxy scenario), replace the wildcard with an explicit allow-list driven by
configuration:

```csharp
app.UseCors(x => x
    .WithOrigins(settings.AllowedCorsOrigins)   // string[] from DiagServiceSettings
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials());
```

`WithOrigins(...)` + `AllowCredentials()` is the safe pairing — an origin is
reflected only when it matches the configured list. Add an
`AllowedCorsOrigins` property to `DiagServiceSettings` so each deployment names
its real SPA origin(s).

### Alternatives

- **Drop `AllowCredentials()`** — if no credentialed cross-origin calls are
  needed, removing it makes `AllowAnyOrigin()` merely permissive rather than
  dangerous. SignalR with authentication usually does need credentials, so this
  is often not viable.
- **Scope the permissive policy to development only** — DiagnosticService
  serves its own SPA via `UseSpa`, so in production the SPA is same-origin and
  needs no CORS. CORS is likely only required for the `UseSpaProxy` dev
  scenario. The permissive policy could be applied under
  `if (app.Environment.IsDevelopment())` and omitted entirely in production.

### Caveat

`AllowCredentials()` was added deliberately. Tightening CORS **will** break the
SPA or dev proxy if the real origins are not listed correctly. Confirm with the
service owner: is DiagnosticService internal-only or internet-facing, and where
is the SPA served from in each environment?

### Related minor item

`Program.cs:42` sets `EnableDetailedErrors = true` on the `WebHub` SignalR hub,
which leaks hub exception text to clients. Consider disabling this outside
development.
