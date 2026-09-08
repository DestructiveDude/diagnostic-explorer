# Upstream integration completion

> Execution: subagent-driven-development. User approved completing backend omissions and then assessing broader UI work on 2026-09-08.

**Goal:** Finish the deferred retention and logging configuration integration without replacing this fork's hardened implementation.

**Architecture:** Preserve EventSinkRepo's snapshot/live-stream synchronization and injected clock. LogEventStore owns an ordered set of active router contributions plus explicitly configured base routing; lifecycle teardown retracts each contribution. Preserve the existing wire contracts.

**Tech stack:** C#, net10.0/net48, xUnit v3, AwesomeAssertions, NSubstitute; Angular/PrimeNG frontend with Jest.

**Spec:** This document records the concrete execution decisions for the user's approved recovery report at E:/Documents/Obsidian Vault/Claude/State of Play/2026-09-08-080328-diagnostic-recovery.md.

## Global constraints

- Worktree: .claude/worktrees/phase6-protocol, branch phase6-integration-completion, base eeeb1f5.
- Port from upstream/main (f8dbb59), but retain our tuple sink keys, injected TimeProvider, bounded channels, message limits, atomic replay/live handoff, namespace shape, and in-core log4net appenders.
- No dependency upgrades, no self-hosting or Windows installer, no release/deploy/EMS mutation. Existing sample framework versions and package version 4.0.0 remain authoritative.
- Use xUnit v3 with AwesomeAssertions Should(); regression checks must fail before the fix. Prefer existing tests over parallel suites. Both target frameworks must build.
- Preserve existing public constructors; avoid ambiguous null overloads. Read all callers before changing a shared function.
- Keep one completed branch/PR; no pushes until full checks and local review finish.

### Task 1: Finish retention, routing ownership and logging helpers

**Files:** src/DiagnosticExplorer/Events/EventSink.cs, EventSinkRepo.cs; src/DiagnosticExplorer/DiagnosticManager.cs; src/DiagnosticExplorer/Logging/LogEventStore.cs, EventSinkRouter.cs; src/DiagnosticExplorer/Log4Net/RoutingDiagnosticAppender.cs and LoggingRepositoryExtensions.cs (new); provider/target/sink and registration extensions in src/DiagnosticExplorer.Extensions.Logging, src/DiagnosticExplorer.NLog, src/DiagnosticExplorer.Serilog; existing corresponding tests under tests/DiagnosticExplorer.UnitTests.

**Interfaces and behavior:**

1. EventSinkRepo.ConfigureEventRetention(EventRetentionOptions) clones and validates before replacing settings. Existing constructors stay valid. Count and age retention apply on publish, GetEvents, CreateSinkStream, and explicit configuration; quiet sinks must not replay expired entries. Keep MaxMessages as compatibility/default constant, use injected TimeProvider, and avoid timers/finalizers/weak-reference registries. Imported timestamps may be out of order: old entries behind a fresh head must not survive snapshot retention. Preserve public Events queue behavior as far as possible and document any lazy-purge ceiling. DiagnosticManager.UseConfiguration validates both retention sets before updating published state and applies legacy retention to EventSinkRepo.Default.
2. EventSinkRouter registers its validated routing contribution with its LogEventStore and implements idempotent disposal. Registration ordering plus local route order produces deterministic contiguous global Order values. The explicit Configure/ConfigureRouting snapshot is the base contribution. Empty contributions do not select a match mode. Nonempty contributions must agree on MatchMode; reject conflicts clearly BEFORE mutating routing or terminating subscriptions. Removal retains other routers and base routes. Registration/removal/reconfiguration supersede subscribers using the existing recovery mechanism. Avoid invoking host callbacks under locks.
3. MEL provider disposal, Serilog sink disposal, NLog target close/reinitialization and log4net appender close/reactivation release the matching router registration. A failed replacement leaves the old working registration intact. Disabled/disposed adapters must not silently re-register on a later write. Trace every direct router creator, including the NLog fallback path.
4. Add upstream-shaped CurrentConfiguration convenience overloads to MEL/NLog/Serilog registration and the in-core log4net LoggingRepositoryExtensions. Read exact upstream signatures with git show. Preserve all existing explicit-options/custom-store entry points. Use our namespace and core log4net assembly, never add the upstream log4net package. Remove now-false comments that configuration does not exist. Registration APIs must register actual working adapters, not merely return unconnected objects.
5. Invoke configured RegisterObjects callbacks on every GetRegisteredObjects collection outside the legacy registration lock. Preserve the no-argument method and add an IServiceProvider overload; combine current configured roots with legacy weak roots without permanently registering callback output. Callback exceptions remain visible. Add tests for a changing collection and real service resolution.

The log4net helper is additive: never import upstream's ResetConfiguration call or erase the host's appenders/levels. Repeated registration replaces only this helper's own appender after successful validation.

**Validation steps:**

- [ ] Run baseline core tests, then add minimal regression tests and demonstrate RED for retention and routing-loss defects.
- [ ] Implement behavior and verify targeted tests with fake time; cover count/age, out-of-order imported events, reconfiguration snapshots, and fluent configuration wiring.
- [ ] Verify two live routers retain both destinations, stable ordering, disposal preserves the survivor, conflicting modes leave state/subscribers intact, configured base routing survives contributors, and real adapter lifecycle routes retract on teardown/replacement.
- [ ] Exercise each convenience helper through its framework to a real isolated LogEventStore where possible; restore static configuration in the existing serialized test collection.
- [ ] Run CSharpier on changed C# files and targeted tests, then full Release build/test suite. Write exact commands/results and concerns to the task report. Commit explicit paths; no push.

### Task 2: Samples, current guides, and UI assessment

**Files:** samples/ logging framework demonstrations; README.md; docs/agent-configuration-guide.md; docs/upstream-integration-status.md. Existing production frontend files are read for assessment, not redesigned in this task.

- [ ] Supply runnable examples for MEL, NLog, Serilog and in-core log4net using our 4.0.0 projects and central package management. Reuse upstream samples where they fit; omit upstream-only publishing/self-host assumptions. Compile and run their event-to-store path.
- [ ] Document fluent configuration, retention, logging registration, shared-routing mode/ordering/disposal contract, service-before-agent rollout and EMS appender switch. Clearly distinguish completed implementation from unreleased/unverified deployment.
- [ ] Compare our current drilldown/event UI against upstream in the real source: retained event window, empty configured destinations, projected views, nested actions, JSON/expanded previews, error/truncation displays. Record actual gaps and a concrete next UI scope without claiming visual QA occurred.
- [ ] Run full .NET gate and current frontend typecheck/lint/Jest gates; use production build if the local CLI can run it. Do not upgrade Node or Angular as an incidental fix.
- [ ] Run one independent whole-branch and composition review, remedy blocking findings, then prepare the branch for review. Never merge or publish packages without existing authorization.

### Task 3: Carry configured registration services through the existing remote host

Inspection found that a core IServiceProvider overload alone would not make RegisterService work when the service polls a real agent. Complete that boundary, without static service-provider storage or a new hosting subsystem.

**Files:** src/DiagnosticExplorer.Hosting/DiagnosticHostingExtensions.cs, DiagnosticHostingService.cs, RegistrationHandler.cs, HubServerAdapter.cs; existing host/real-connection tests. Consume Task 1's GetRegisteredObjects(IServiceProvider) overload.

- [ ] Pass the host's IServiceProvider through hosted service, registration handler and per-connection adapter instances. Preserve existing public constructor signatures with forwarding overloads; add parameters to internal constructors where appropriate.
- [ ] Each adapter diagnostics/drilldown/set/execute path obtains the current registered roots with that instance's provider, then calls the existing root-taking DiagnosticManager overload. Preserve request serialization/deduplication, full object paths and error handling.
- [ ] Add the upstream-shaped ConfigureDiagnosticExplorer(IServiceCollection, IConfiguration, Action<IDiagConfigurator>, Action<HttpConnectionOptions> optional) convenience entry point using our existing remote hosting path. Keep configuration application at startup; no deferred-configuration framework and no SelfHost. Validate arguments before mutations.
- [ ] Demonstrate a registered DI singleton reaches a real adapter request, changes dynamically, and can be acted on using the same root lookup. Use the existing real SignalR test harness if the request path changes; no empty mock substituting the wire boundary. Confirm the legacy manual-registration path still works.
- [ ] Build both target frameworks, run the relevant host/wire tests, commit owned paths, and record scope limits: Hosts/SelfHost and configurable SystemEnvironment are separate existing unported hosting features, not silently implemented by this overload.
