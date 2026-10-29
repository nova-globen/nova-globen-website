---
title: "Pagination for huge amount of data and best practiced in .NET core"
date: 2025-09-28
slug: pagination-huge-data-keyset-seek-sort-keys-page-tokens-index-filter-friendly-queries-dotnet-core
description: "Rock-solid pagination story for 100M+ rows on .NET 9 / EF Core 9 with Azure SQL / SQL Server (plus notes for Cosmos DB). Patterns that hold up at scale: keyset/seek pagination, stable sort keys, page tokens, and index- & filter-friendly queries."
tags: [data, apis]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# 1) Why keyset/seek (a.k.a. cursor) beats OFFSET/FETCH

How to implement pagination and filtering by stable ordering, keyset/seek pagination for large data; exposing pageToken or continuationToken and validating inputs. I strongly suggest reading about [how materialized read models can make pagination much faster](https://nova-globen.se/blog/materialized-view-pattern-pagination-and-reporting/) also.

* **OFFSET/FETCH (page number + size)** scans/counts then skips N rows → cost grows with page number (very bad at 10M+), can read thousands of rows just to throw them away.
* **Keyset/seek** uses a **cursor (last seen key)**: `WHERE (SortKey, TieBreakerId) > (@lastKey, @lastId)` with `ORDER BY SortKey, TieBreakerId` and `TOP (@pageSize)`.

  * Complexity is ~O(pageSize) per page, **independent of page depth**.
  * Naturally stable even as rows are inserted/deleted between requests.

**Rule of thumb:** For anything beyond a few thousand rows or any list with “infinite scroll,” use **keyset/seek + tokens**.

# 2) Choose a deterministic ordering (and tie-breaker)

Pick an order that:

* Has a **supporting index**.
* Is **monotonic** for the scenario (e.g., `CreatedAt`).
* Has a **unique tie-breaker** (usually `Id`).

Examples:

* `ORDER BY CreatedAtUtc DESC, Id DESC`
* `ORDER BY (SomeScore DESC, Id DESC)` for ranking feeds.

If your “sort by” isn’t unique, add `Id` as the second column in `ORDER BY` and in the index.

# 3) The seek predicate (SQL pattern)

**Descending example** (newest first):

```sql
-- Inputs: @lastCreatedAtUtc, @lastId, @take
SELECT TOP (@take) <columns>
FROM dbo.Items WITH (READPAST)
WHERE
  (@lastCreatedAtUtc IS NULL AND @lastId IS NULL)
  OR (CreatedAtUtc < @lastCreatedAtUtc)
  OR (CreatedAtUtc = @lastCreatedAtUtc AND Id < @lastId)
ORDER BY CreatedAtUtc DESC, Id DESC;
```

**Ascending example** (oldest first, just flip operators):

```sql
WHERE
  (@lastCreatedAtUtc IS NULL AND @lastId IS NULL)
  OR (CreatedAtUtc > @lastCreatedAtUtc)
  OR (CreatedAtUtc = @lastCreatedAtUtc AND Id > @lastId)
ORDER BY CreatedAtUtc ASC, Id ASC;
```

**Covering index (critical):**

```sql
-- If you filter by CreatedAtUtc and order by (CreatedAtUtc, Id):
CREATE INDEX IX_Items_Created_Id
ON dbo.Items (CreatedAtUtc DESC, Id DESC)
INCLUDE (ColA, ColB, ColC); -- whatever the page needs to render
```

* For heavy filters, put the **filter columns first** in a composite index (see §6).
* Use `READ COMMITTED SNAPSHOT` at the DB level to reduce blocking; `READPAST` can help skip locked rows for “feed” scenarios.

# 4) Page tokens (a continuation cursor)

Expose a **`pageToken` (a.k.a. continuationToken)** instead of `pageNumber`.

**What to store in the token**

* The **sort field(s)** of the **last item** returned (e.g., `CreatedAtUtc`, `Id`).
* The **direction** (asc/desc) if you support both.
* Any **active filters** (optional: you can roundtrip them to detect tampering/mismatch).

**Serialize securely**

* Example: `{ "k": "2025-10-01T16:22:12Z", "i":"8d1a…", "dir":"desc" }`
* Encode with **base64url** and **sign (HMAC)** to prevent tampering; or encrypt if you embed filter criteria.
* If a token is missing/invalid → start from the **beginning**.

**Backward/previous page**

* Option 1: return a `prevToken` computed from the **first** item of the page and reverse the operators.
* Option 2: have the client keep a **stack of prior tokens** (simpler server code).

# 5) .NET 9 / EF Core 9 patterns

## 5.1 Controller/endpoint contract

```csharp
public sealed record ListRequest(
    int? PageSize = null,
    string? PageToken = null,
    string? Sort = "createdAt:desc",
    string? Filter = null // e.g., "status=Active;category=Books"
);

public sealed record ListResponse<T>(
    IReadOnlyList<T> Items,
    string? NextPageToken
);
```

## 5.2 Decode/encode tokens

```csharp
public sealed record Cursor(DateTime CreatedAtUtc, Guid Id, string Dir);

static Cursor? DecodeToken(string? token, byte[] hmacKey)
{
    if (string.IsNullOrWhiteSpace(token)) return null;
    // token = base64url(payload) + "." + base64url(hmac)
    // Verify HMAC, then deserialize JSON payload to Cursor
    // (omitted for brevity; use System.Text.Json + your HMAC helper)
    return payload;
}

static string EncodeToken(Cursor c, byte[] hmacKey)
{
    // Serialize c -> JSON -> base64url, append HMAC
    return token;
}
```

## 5.3 Query with seek in EF Core

```csharp
public async Task<ListResponse<ItemDto>> GetPageAsync(ListRequest req, CancellationToken ct)
{
    var pageSize = Math.Clamp(req.PageSize ?? 50, 1, 500); // validate

    var cursor = DecodeToken(req.PageToken, HmacKey);
    var descending = (req.Sort ?? "createdAt:desc").EndsWith(":desc", StringComparison.OrdinalIgnoreCase);

    IQueryable<Item> q = db.Items.AsNoTracking();

    // Apply validated filters (see §6)
    q = ApplyFilters(q, req.Filter);

    // Order
    q = descending
        ? q.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        : q.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id);

    // Seek
    if (cursor is not null)
    {
        if (descending)
            q = q.Where(x => x.CreatedAtUtc < cursor.CreatedAtUtc
                          || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id < cursor.Id));
        else
            q = q.Where(x => x.CreatedAtUtc > cursor.CreatedAtUtc
                          || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id > cursor.Id));
    }

    // Project to DTO (avoid materializing entire entity graphs)
    var items = await q
        .Select(x => new ItemDto(x.Id, x.Name, x.Status, x.CreatedAtUtc))
        .Take(pageSize)
        .ToListAsync(ct);

    var nextToken = items.Count == pageSize
        ? EncodeToken(
            new Cursor(
                items[^1].CreatedAtUtc,
                items[^1].Id,
                descending ? "desc" : "asc"),
            HmacKey)
        : null;

    return new ListResponse<ItemDto>(items, nextToken);
}
```

## Notes

* Use **`AsNoTracking`** for read-heavy endpoints.
* Always **project** (`Select` → DTO) to keep reads lean and index-friendly.
* Page **forward-only** is simplest; add reverse seek if UX needs “previous”.

# 6) Filtering at scale (10M+ rows)

## Validation rules

* **Whitelist** allowed fields & operators. Parse into a safe AST, don’t ever concatenate SQL.
* Allow only **index-aligned** filters when possible. Examples:

  * `CreatedAtUtc BETWEEN …`
  * `Status IN (…)`
  * `CategoryId = …`
* Reject filters that break the index strategy (like wildcards on leading columns) or route them to a **search service** (Elasticsearch/Azure AI Search) instead.

## Index design

* If you filter on `Status` and sort by `CreatedAtUtc, Id`, consider:

  ```sql
  CREATE INDEX IX_Items_Status_Created_Id
  ON dbo.Items (Status, CreatedAtUtc DESC, Id DESC)
  INCLUDE (Name, CategoryId /* columns needed for DTO */);
  ```
* If you filter by a **date range** only, `IX_Items_Created_Id` may be enough.
* Use **filtered indexes** for very selective predicates (e.g., `WHERE Status = 'Active'`).

## Parameter sniffing & plan stability

* Keep parameters **parameterized**.
* Use **query store** and **OPTIMIZE FOR** or recompile hints sparingly if you see regressions.
* For highly variable filters, consider splitting endpoints (common vs rare filters) to get stable plans.

# 7) Counts and “total results”

* Exact `COUNT(*)` over 10M with filters can be **expensive**.
* Options:
  * Return **no total**, only whether there’s a **`nextToken`**.
  * Return a **best-effort estimate** from precomputed stats.
  * Compute count **asynchronously** and cache per filter for dashboards.
  * Or offer a separate endpoint for **just the count** (the client can opt in).

# 8) Edge cases & correctness

* **Inserted/deleted rows between pages:** keyset handles this gracefully; you won’t see duplicates with a stable sort + tie-breaker.
* **Ties on the sort key:** always include a unique secondary key (`Id`) in `ORDER BY` and **token**.
* **Nulls in sort key:** normalize (e.g., treat `NULL` as min/max) and be consistent in the predicate.
* **Time precision:** store UTC `datetime2(3|7)`; write tokens with the exact precision you query on.
* **Security:** sign tokens; don’t let clients invent cursors that skip checks.

---

# 9) Cosmos DB note (if you use it)

* The **SDK already returns a continuation token** — use it directly in your response as `nextPageToken`, and pass it back to the SDK on the next call.
* Still keep **stable ordering** (`ORDER BY c.createdAt, c.id`) for deterministic UX.
* For mixed filters, ensure the **partition key** is part of your query, or fan-out cost can spike.

# 10) Performance checklist (10M+)

* ✅ **Keyset/seek** with `(SortKey, Id)` and a **covering index**.
* ✅ **Projection to DTO** in the query (no entity graphs).
* ✅ **Page tokens** (base64url + HMAC).
* ✅ **Whitelist filters** and align **composite indexes**.
* ✅ `AsNoTracking`, small **page size caps** (50–500), protect against DoS.
* ✅ Consider **READ COMMITTED SNAPSHOT** and `READPAST` for feed-like lists.
* ✅ Monitor: logical reads, CPU, spills (tempdb), and page time (P95/P99).
