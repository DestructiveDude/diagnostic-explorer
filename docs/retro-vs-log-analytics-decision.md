# Retro store on Azure: Log Analytics vs CosmosDB vs hosted Mongo

**Status:** Decision memo / discussion input
**Date:** 2026-06-07
**Audience:** Cameron (maintainer) + FixPortal fork
**Question:** As part of the Azure integration, should the Retro store be dropped in favour of Log Analytics, kept and ported to CosmosDB, or kept on Azure-hosted Mongo?

---

## TL;DR

Retro is **historical, windowed, filtered log search over a flat log record**. That is precisely the workload Azure Log Analytics is built for. After reading the code, the Log Analytics direction is sound, and **CosmosDB is the weakest of the three options** for this specific workload.

Only **two** things actually need confirming before committing to Log Analytics:

1. **Message text must round-trip untruncated** into Log Analytics (the trace-scope tree is parsed client-side from the message text).
2. **Confirm whether interactive delete is a real workflow** or just a substitute for the record retention Mongo never had (in which case Log Analytics retention/TTL replaces it outright).

Nail those two and the move is correct.

---

## What Retro actually is (from the code)

This matters because the decision hinges on it, and the name "Retro" suggests more than the implementation does.

**Ingest path** — a log4net appender (`DiagnosticExplorer/Log4Net/DiagnosticRetroAppender.cs`) converts each `LoggingEvent` to a `DiagnosticMsg`, ships it over SignalR (`DiagnosticHub.LogEvents`), through `RetroManager`'s buffered write channel, into MongoDB.

**Store** — `DiagnosticService/Common/MongoRetroLogger.cs`: database `Diagnostics`, collection `Log`, document type `RetroMsg`, single descending index on `Date`. Writes are `InsertManyAsync`; reads are date-range + level + machine/user/process/message regex, sorted `Date` descending, batched 250.

**Record shape** — flat log row:

```
Level, Date, Machine, Process, User, Category, Message   (+ RecordId, MsgId)
```

There is **no property tree, no snapshot, no live state** in the stored record. The trace-scope hierarchy is plain text embedded inside the `Message`/`Detail` string.

**Query model** — `RetroModel.search()` builds a query from a date, a start hour, and a span (`hours`, default 12), plus optional filters, capped at `maxRecords` (default 5000). It is a **one-shot search of a past time window**. Results stream back in chunks, then the search completes. There is no auto-refresh and no live tail.

**Conclusion:** Retro = "search a window of historical log records by time and a few fields." This is a textbook Log Analytics / KQL workload.

---

## The three concerns people raise — checked against the code

### 1. "We'd lose real-time / live tail"

**Void — it never existed.** `RetroModel.search()` is one-shot over a past window (`RetroModel.ts:112`). The streaming is chunked delivery of a *finite* result set, not a live feed. Because every search targets the past, Log Analytics' ingestion latency (typically minutes) is irrelevant to how Retro is actually used.

### 2. "We'd lose the trace-scope tree"

**Survives Log Analytics, with one caveat.** The trace-scope tree is parsed and rendered **entirely client-side** from the message text (`ScopeNode.hasTraceScope`, `TraceScopeComponent.parse`, format `[ss.mmm] [ss.mmm] BEGIN Label`). The store holds only text. So as long as the full message text round-trips intact, the existing UI renders the tree unchanged regardless of the backing store.

- **Caveat:** verify Log Analytics does not truncate the message field for large traces. Confirm the per-field/per-row size limits against the largest real messages.
- **Potential upside:** the hand-rolled timing tree is a manual version of what Application Insights' end-to-end transaction view does natively (Activity/spans, `operation_Id` correlation). If the app emits proper spans, the Azure path can *improve* on Retro here, not merely match it — and add cross-service correlation Retro never had.

### 3. "We'd lose the delete capability"

**This is the one genuine functional gap.** `RetroModel.delete()` bulk-deletes the currently displayed results by `msgId`, behind a confirm prompt (`RetroModel.ts:130`). Log Analytics is append-only/immutable — it offers retention policies and an async/restricted purge API, not interactive selective delete.

- **Key question:** is manual delete a workflow users rely on, or a crutch because the Mongo store had no TTL? Deleting noisy log rows by hand strongly suggests the latter. If so, Log Analytics' native per-table retention/TTL replaces it and is strictly better. If users genuinely curate the log set by hand, that workflow does not survive a move to Log Analytics.

---

## Why CosmosDB is the weakest option here

The CosmosDB port is attractive because Cosmos has a Mongo API, so the existing `MongoRetroLogger` is nearly drop-in. But for *this* workload:

1. **Logs are a write firehose, and Cosmos bills writes hardest.** Sustained high-ingest writes against provisioned/autoscaled RU/s is the textbook expensive Cosmos anti-pattern. Log Analytics' per-GB ingestion (with Basic/Auxiliary tiers for high-volume logs) is materially cheaper for this shape.
2. **Mongo-on-Cosmos gains almost nothing Azure-native.** No KQL, no native retention/TTL tiering, no Application Insights join, no workbook/alert integration. You'd be paying more to keep running essentially the same thing, just hosted on Azure.
3. **You still operate a store.** Partitioning, throughput, and cost stay your problem. Log Analytics is fully managed.

CosmosDB would be the right call for a *low-latency, app-owned, read/write document model* — which is not what Retro is.

---

## Recommendation

Ranked for the Retro/log-persistence half specifically:

| Rank | Option | When it's right |
|------|--------|-----------------|
| 1 | **Log Analytics** | Default. Fits the workload, managed, cheapest for a log firehose, native Azure (KQL, retention, alerts, App Insights). |
| 2 | **Azure-hosted Mongo / Atlas** | Only if interactive delete proves load-bearing *and* you want zero code change. |
| 3 | **CosmosDB** | Last. Most expensive for write-heavy logs; least Azure-native payoff. |

**Important scope note:** this concerns only the **historical log store**. The real-time diagnostic property-tree explorer (`GetDiagnostics`, live in-memory) is a *separate* subsystem and is unaffected by this decision — it stays regardless.

## Two open questions to close before committing

1. **Message-text fidelity** — does Log Analytics preserve full message text (incl. the largest trace-scope payloads) without truncation? Test against real data.
2. **Delete intent** — is `RetroModel.delete()` a workflow users depend on, or a stand-in for retention Mongo lacked? If the latter, Log Analytics retention/TTL replaces it cleanly.

---

## Evidence (file references)

- Ingest: `DiagnosticExplorer/Log4Net/DiagnosticRetroAppender.cs:41`
- Hub entry: `DiagnosticService/Hubs/DiagnosticHub.cs:76` (`LogEvents`)
- Write path / buffering: `DiagnosticService/Hubs/RetroManager.cs:116` (`RunLoop`), `:136` (`TryLog`)
- Store + index + query: `DiagnosticService/Common/MongoRetroLogger.cs:104` (collection), `:111` (Date index), `:169` (`GetMessages`), `:211` (`WriteMessages`), `:126` (`Delete`)
- Record shape: `DiagnosticService/Transport/RetroMsg.cs:12`, `DiagnosticExplorer/DiagnosticMsg.cs:8`
- Query shape: `DiagnosticService/Transport/RetroQuery.cs:6`
- One-shot search: `diagnostics-web/src/app/Model/RetroModel.ts:112` (`search`), `:203` (`onSearchComplete`)
- Interactive delete: `diagnostics-web/src/app/Model/RetroModel.ts:130` (`delete`), `:151` (`canDelete`)
- Client-side trace-scope parse: `diagnostics-web/src/app/retro-display/retro-display.component.ts:32` (`hasTraceScope`), `diagnostics-web/src/app/trace-scope/trace-scope.component.ts:17` (`parse`)
