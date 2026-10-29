---
title: "Materialized view patterns, trade-offs, and when to use each on SQL Server/Azure SQL and .NET"
date: 2025-09-29
slug: materialized-view-pattern-pagination-and-reporting
description: "Materialized read models can make pagination much faster, but they help indirectly. They reduce the per-page work (joins/aggregates/shaping), so your keyset/seek query touches fewer pages and uses a tight covering index. The seek/cursor pattern still does the heavy li"
tags: [data]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# What do we mean by “materialized view” on Azure SQL?

SQL Server/Azure SQL doesn’t have Oracle-style materialized views; the closest native feature is an **Indexed View** (schema-bound view with a clustered index). In practice, teams use three flavors:

# Summary and key points

* **Materialized read models (indexed views or custom projection tables)** absolutely help large-scale pagination by **removing runtime work** and enabling **perfect indexes** for your cursor pattern.
* Use **custom read models + outbox** for maximum control and minimal read latency; use **indexed views** when you want SQL Server to maintain a specific aggregation/join.
* Keep **seek/cursor pagination**—it’s the core scalability lever. Materialization just makes each page cheaper.
* Add **Redis page/window caching** for super-hot lists, and move faceted text search to **Azure AI Search/Elasticsearch** if needed.

## 1. Indexed View (native)

* **What it is:** A `VIEW ... WITH SCHEMABINDING` + **clustered index** that stores view rows physically.
* **When it shines:** Expensive joins/aggregations that are stable and heavily reused.
* **Impact on pagination:** You paginate **over the view** with a covering index aligned to your sort key, so the engine skips the big base-table joins at runtime.
* **Costs/constraints:** Write penalty (maintained on every insert/update), schema rules (determinism, no `*`, etc.).

## 2. Materialized Read Model Table (custom)

* **What it is:** A **denormalized table** that you keep in sync (CQRS projection). Often called a “projection,” “read model,” or “summary table.”
* **Sync options:**

  * **Streaming** via outbox + background dispatcher (near-real-time, strong control).
  * **CDC/Change Tracking** + ETL job (near-real-time or batch).
* **Impact on pagination:** You tailor the table to the exact API shape, add a **clustered index on `(SortKey, Id)`** and include only the columns needed by the list page → extremely fast keyset pagination.
* **Costs:** Extra storage + write/update path. You must design rebuild/backfill procedures.

## 3. Precomputed Page Cache (ephemeral)

* **What it is:** Cache **page windows** (e.g., first 50, next 50 cursors) in Redis keyed by filter + sort + cursor.
* **Impact:** Removes repeat read costs for hot feeds and “first page” traffic.
* **Costs:** Cache invalidation; combinatorial explosion for many filter combos (use selectively).

# Do they improve pagination performance?

**Yes, by shrinking the query work per page**:

* No/less joining at runtime.
* Narrow, page-friendly rows (no wide payload).
* Perfectly **aligned indexes** for your sort/filter.
* Fewer logical reads, lower CPU, and better P95/P99.

But remember: **seek/cursor pagination** is still required for huge data. Materialization won’t fix OFFSET/FETCH’s deep-page slowness.

# Design playbook (10M+ rows)

## 1) Choose your read model

* **If your list endpoint needs multiple joins, computed fields, or rollups:**
  Use a **custom read model table** or **indexed view**.
* **If writes are heavy and latency tolerance is low (OLTP):**
  Prefer a **read model table** updated asynchronously (outbox/CQRS). Indexed views add write latency.

## 2) Shape for pagination

* Store exactly what the endpoint needs (no N+1 lookups).
* **Clustered index:** `(SortKey, Id)` in the same direction you present (often `DESC` for newest-first).
* **Covering index:** If the clustered key differs (for example, you cluster by `Id` for other reasons), add a **nonclustered** index on `(SortKey, Id) INCLUDE (<DTO columns>)`.

## 3) Keep it fresh

* **Outbox + projector (recommended):**

  * On each domain write, append to `Outbox`.
  * A background worker consumes and **upserts** the read model.
  * Great observability and backpressure; deterministic.
* **CDC/Change Tracking:**

  * Poll changes; apply upserts.
  * Simpler to add later; a bit more laggy.

## 4) Paginate with a cursor (seek)

* Stable order (e.g., `CreatedAtUtc DESC, Id DESC`).
* Seek predicate using last item’s `(SortKey, Id)` from a **signed `pageToken`**.
* **Projection to DTO** in the query (don’t materialize entities).

## 5) Validate filters & align indexes

* Whitelist filter fields.
* If you commonly filter by `Status`, build `(Status, SortKey, Id)` index and **INCLUDE** the display columns.

# Alternatives & complements

### A) **Indexed View** vs **Read Model Table**

* **Indexed View**: Zero custom sync code; SQL Server maintains it. Great for **deterministic aggregates**. But it **taxes writes** and is harder to evolve.
* **Read Model Table**: Maximum control, **cheapest reads**, and you can store denormalized JSON or precomputed projections. Needs a projector (worker) and backfill logic.

### B) **Search engine for faceted filtering**

* For free-text + many facets, push to **Azure AI Search/Elasticsearch**.

  * Use **search cursors** (continuation tokens) and rely on their inverted indexes.
  * Keep SQL for exact transactional lists; use search for discovery/exploration.

### C) **Partitioning & storage options**

* **Range partitioning** on date can keep working sets small (monthly tables or partition function).
* **Columnstore** is excellent for analytics scans, but **not ideal** for cursor pagination of OLTP feeds; prefer rowstore + narrow covering indexes.

### D) **Cosmos DB**

* If your data is already in Cosmos, create a **projection container** tailored to the list shape; use **SDK continuation tokens** and `ORDER BY createdAt, id` with partition-aligned queries.

# When *not* to use materialization

* If the base list is already a single table with a perfect covering index and no computed fields, adding a read model won’t move the needle much—the **seek query is already optimal**.
* If write throughput is extreme and the projection would add unacceptable write amplification, prefer **on-the-fly** with careful indexing or consider **eventual consistency** projections for read paths that can tolerate lag.

# Practical .NET implementation sketch

**Outbox + projector (EF Core 9)**

1. In your command handler, write domain changes **and** append `OutboxEvent` in the same transaction.
2. A hosted service (or Function/Worker) polls unsent events and `UPSERT`s the **ReadModel_Items** table.
3. The list endpoint queries **ReadModel_Items** with the **seek** pattern and emits a signed `nextPageToken`.

**SQL for read model**

```sql
CREATE TABLE ReadModel_Items
(
  Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  CreatedAtUtc DATETIME2(3) NOT NULL,
  Status TINYINT NOT NULL,
  Name NVARCHAR(200) NOT NULL,
  -- ...other columns the DTO needs only
  -- optional: a JSON column for flexible metadata
);

-- Fast seek for newest-first
CREATE UNIQUE CLUSTERED INDEX CIX_ReadModel_Items_Created_Id
ON ReadModel_Items (CreatedAtUtc DESC, Id DESC);

-- Common filter: by Status
CREATE INDEX IX_ReadModel_Items_Status_Created_Id
ON ReadModel_Items (Status, CreatedAtUtc DESC, Id DESC)
INCLUDE (Name /*, other DTO columns */);
```

**Endpoint (pseudo)**

```csharp
// Decode pageToken -> (lastCreatedAtUtc, lastId)
var q = db.ReadModelItems.AsNoTracking();

q = q.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);

if (cursor != null)
    q = q.Where(x => x.CreatedAtUtc < cursor.CreatedAtUtc
                  || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id < cursor.Id));

if (statusFilter is not null)
    q = q.Where(x => x.Status == statusFilter);

var items = await q
    .Select(x => new ItemDto(x.Id, x.Name, x.Status, x.CreatedAtUtc))
    .Take(pageSize)
    .ToListAsync(ct);

// Emit nextPageToken from last item’s keys (signed)
```
