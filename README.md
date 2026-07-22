# DiagnosticExplorer

DiagnosticExplorer is a .NET diagnostic instrumentation toolkit and an
accompanying web-based viewer service. Application code emits property
bags, operations, and events via the `DiagnosticExplorer` library;
those get pushed over SignalR to a central hosting service which fans
them out to a browser-based dashboard for live inspection across a
fleet of running processes.

The project originated as Cameron Elliot's open-source diagnostic
toolset around 2010 (LGPL v3+) and has been carried forward under
Centerprise's EMS trading platform as the diagnostic backbone for the
TOMI engine and its surrounding services.

## Repository layout

```
DiagnosticExplorer/          netstandard2.0 core library
                             - PropertyBag, TraceScope, OperationSet,
                               protobuf transport types, log4net forwarding
DiagnosticExplorer.Hosting/  net8.0 / net6.0 / net48 hosting integration
                             - AddDiagnosticExplorer DI extension,
                               DiagnosticHostingService, RegistrationHandler
DiagnosticService/           Standalone web service (Docker payload)
                             - ASP.NET Core + SignalR hubs + SPA host
diagnostics-web/             Angular 21 SPA (the dashboard UI)
Docker/                      Dockerfile and compose YAMLs for the service
WidgetSample/                WinForms demo of the library
ConsoleApp/                  Smaller CLI demo
```

## Using the library

Add the package reference:

```xml
<PackageReference Include="DiagnosticExplorer.Hosting" Version="3.2.1" />
```

Wire into a `Host.CreateDefaultBuilder` pipeline:

```csharp
services.AddDiagnosticExplorer(context.Configuration);
```

For SignalR connections that need custom configuration (e.g. an Azure
AD bearer token), pass an `Action<HttpConnectionOptions>`:

```csharp
services.AddDiagnosticExplorer(
    context.Configuration,
    options => options.AccessTokenProvider = GetCurrentAccessToken);
```

Static start (for non-DI hosts):

```csharp
DiagnosticHostingService.Start(
    "http://diagnostics:2803/diagnostics",
    options => options.AccessTokenProvider = GetCurrentAccessToken);
```

Required configuration:

```json
{
  "DiagnosticExplorer": {
    "Uri": "http://diagnostics:2803/diagnostics",
    "Enabled": true
  }
}
```

The `Uri` may be a comma-or-semicolon-separated list of hub URLs if you
want a single application to report to multiple diagnostic servers.

### Tracing scopes

```csharp
using (var scope = new TraceScope(Log.Info))
{
    TraceScope.Trace("Loaded {0} records", count);
    // ... work ...
    TraceScope.Trace("Completed in {0}ms", elapsed);
}
```

`TraceScope` flows through `AsyncLocal`, so nested async calls share
the same scope automatically.

## Running the service

Two compose files under `Docker/`:

```bash
# Build the image locally and bring up service + MongoDB:
docker compose -f Docker/compose-and-create-image.yaml up -d --build

# Or pull the published image from ghcr.io:
docker compose -f Docker/compose-with-existing-image.yaml up -d
```

Then open `http://localhost:2803/` for the dashboard.

Environment-variable overrides documented inline at the top of each
compose file. Most-useful:

| Variable | Default | Purpose |
|---|---|---|
| `DIAGEXPLORER_HOST_PORT` | `2803` | Host-side port mapping if 2803 is in use locally |
| `MONGO_USERNAME` / `MONGO_PASSWORD` | `admin` / `password123` | Mongo root credentials |
| `DIAGEXPLORER_IMAGE_NAME` | `ghcr.io/cell001nz/diagnostic-explorer` | Repoint at a fork's GHCR namespace |
| `DIAGEXPLORER_IMAGE_TAG` | `latest` | Pin to a specific GHCR tag (e.g. `3.1.38`) |

The service listens on port `2803` inside the container. Settings
default to a Mongo backend at `mongodb:27017` (the sidecar in compose);
override `DiagServiceSettings__RetroConnection` to point at a
different store.

## Building from source

```bash
# Library + service + samples:
dotnet build DiagnosticExplorer.slnx -c Release

# Angular dashboard (Node 20.19+ / 22 for Angular 21):
cd diagnostics-web
npm ci
npm run build
```

`diagnostics-web/.npmrc` pins `legacy-peer-deps=true`, so `npm ci` resolves the
dependency graph without an explicit flag (the Angular build tooling's peer
range for Tailwind lags the installed Tailwind 3).

### .NET unit tests

The core library and service are covered by xUnit v3 suites under `tests/`
(NSubstitute + AwesomeAssertions). Both are part of `DiagnosticExplorer.slnx`
and run on every push/PR via GitHub Actions.

Coverage spans the public surface — `PropertyBag`/`Property`/`Category`,
`ProtobufUtil` wire round-tripping, the JSON converters, `AttributeUtil`,
`WeakReferenceHash`, `EventSink`/`EventSinkRepo`, and the `TraceScope` tracing
hierarchy — and two internal helpers, `ScopeStack` and `TypeUtil`. The library
grants the test project access to its internals via an `InternalsVisibleTo`
entry in `DiagnosticExplorer.csproj`; the generated attribute ships in the
assembly but only names the test project, so it exposes nothing else.

```bash
dotnet test DiagnosticExplorer.slnx -c Release
```

Both test projects target `net10.0` and run cross-platform. In Visual Studio,
open `DiagnosticExplorer.slnx`, build the solution, then use **Test Explorer >
Run All** to run both suites.

### Frontend tests and mutation analysis

The dashboard is tested with **Jest** (`jest-preset-angular`); Karma has been
removed.

```bash
cd diagnostics-web

# Unit tests with coverage:
npm test

# Mutation analysis (StrykerJS over the Jest suite):
npm run test:mutation
```

Stryker writes its report to `diagnostics-web/reports/mutation/`.
`scripts/summarize-stryker.ps1` condenses `mutation.json` into a compact
JSON/Markdown summary, which the `mutation-web` GitHub Actions workflow posts to
the run's job summary. The `publish-docker-image` workflow runs `npm ci`,
`npm test`, and `npm run build` as a frontend gate before building the image.

The shipped libraries have `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`,
so a Release build produces the NuGet packages under each project's
`bin/Release/`.

Vulnerability check:

```bash
dotnet list DiagnosticExplorer.slnx package --vulnerable --include-transitive
```

(Should report no vulnerable packages as of `3.2.1`.)

## Container image

Published to `ghcr.io/cell001nz/diagnostic-explorer` by the GitHub
Actions workflow at `.github/workflows/publish-docker-image.yml`.

Triggers:
- `push` to `main` → tags `latest`, `main`, `sha-<short>`
- `push` of a `v*.*.*` git tag → tags the matching semver + `major.minor`
- `workflow_dispatch` → manual publish from any ref
- `pull_request` against `main` → build-validate only, no push

The image is `linux/amd64`. The GHCR package's visibility inherits
the source repo on first publish (public repo → public package,
private repo → private package). Override at any time from the
package's settings page on GitHub.

## Releases

Current release: **3.2.1** — a patch over `3.2.0` (CodeQL triage + a dogfood
pass: the Retro `Date`-index fix, operation-exception unwrap, and UI nits;
defaults unchanged). `3.2.0` remains the headline feature release.

Versions are tagged `v{semver}` (e.g. `v3.2.1`); pushing the tag
triggers a container-image publish to GHCR.

## License

LGPL v3 or later — see `LICENSE` and the file headers in
`DiagnosticExplorer/`.
