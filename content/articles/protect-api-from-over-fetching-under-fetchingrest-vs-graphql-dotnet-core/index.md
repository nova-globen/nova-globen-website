---
title: "Protect APIs from over-fetching/under-fetching - REST vs GraphQL in .NET core"
date: 2025-10-01
slug: protect-api-from-over-fetching-under-fetchingrest-vs-graphql-dotnet-core
description: "How do you protect APIs from over-fetching/under-fetching? Why for REST: narrow DTOs, includes, and projections and for broader needs consider GraphQL?"
tags: [apis]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# Best Practices Summary

## For *REST* in .NET:
  * Use **projection to DTOs** (`Select` instead of `.Include()` when possible).
  * Define **endpoint-specific contracts** to prevent over-fetch.
  * Use `AsNoTracking` and **async LINQ** queries for read APIs.

## For *GraphQL* in .NET:

  * Use **DataLoader pattern** to batch N+1 queries.
  * Limit **query depth & complexity** for protection.
  * Cache persisted queries when possible.

## Over-fetching

* The client receives **more data than it needs**.
* Example: `GET /users/1` returns the entire user entity including addresses, settings, history, but the UI only needs `name` and `email`.
* Downsides: unnecessary CPU, bandwidth, and serialization cost.

## Under-fetching

* The client receives **too little data**, requiring **multiple round-trips**.
* Example: `GET /orders/1` returns order header only. The UI then must call `/orders/1/items` and `/orders/1/payments`.
* Downsides: chatty APIs, high latency.

---

## 2. REST vs GraphQL

### 2.1) REST APIs

* **Pros:**

  * Simple, mature, widely supported in .NET (`Minimal APIs`, `ASP.NET Core MVC`).
  * Strong HTTP semantics (GET/PUT/POST/DELETE, caching, status codes).
  * Easy to secure with middleware and API gateways.
  * Straightforward to implement with EF Core projections.

* **Cons:**

  * Fixed shape: risk of over/under-fetching.
  * For complex UI, clients may need multiple calls.

## Narrow-down DTOs, Includes, and Projections in REST

* Narrowed-down response models designed for **specific endpoints**, preventing accidental over-fetch.
* Example: `UserProfileDto` with only the fields needed for a profile screen.


### **Includes**
* In EF Core, `.Include()` lets you load related entities in a **single query**.
* Use carefully: it prevents under-fetching but may over-fetch if you pull deep graphs unnecessarily.

### **Projections**
* Selecting **only the needed fields** directly from the DB into DTOs.
* Example:

```csharp
  var users = db.Users
      .Select(u => new UserDto(u.Id, u.Name, u.Email))
      .ToListAsync();
```
* This is the most **efficient pattern** for REST endpoints—avoids both over-fetching and under-fetching.

### 2.2) GraphQL

* **Pros:**

  * Client defines **exactly what fields** it wants (solves over/under-fetching elegantly).
  * Single endpoint can serve multiple client needs.
  * Great for frontend-heavy apps (React/Angular/Blazor with many nested queries).

* **Cons:**

  * **Processing cost:** server parses & validates dynamic queries → heavier than REST.
  * **Memory:** query execution may hydrate large graphs before resolving → higher pressure than REST projections.
  * **Complexity:** harder caching, monitoring, and securing (need query depth limits, persisted queries, etc.).
  * In .NET (HotChocolate, GraphQL.NET): performance is **good**, but raw REST with EF Core projection is still **faster** when payload size doesn’t matter.

## 3. Which is Better in .NET Core

> Assuming payload size is irrelevant

* **Processing time:**
  REST with EF Core projection is faster because the SQL is pre-shaped and predictable. GraphQL incurs query parsing and field resolution overhead.

* **Memory usage:**
  REST wins again; GraphQL’s resolver pipeline often materializes intermediate objects.

* **Raw speed (throughput):**
  REST (properly designed DTO + projection) usually achieves **higher RPS** (requests per second). GraphQL trades some speed for flexibility.

* **When GraphQL shines:**

  * Complex UI with many optional fields.
  * B2B integrations where clients vary widely in what they need.
  * Cases where reducing network calls is more important than raw speed.

* **When REST is better:**

  * Internal services and backends where contracts are stable.
  * High-throughput scenarios (10M+ rows, heavy pagination).
  * APIs where performance predictability is critical.

# ✅ My recommendation
> 10M+ data, high performance, payload size doesn’t matter

* Stick with **REST + EF Core projections + keyset pagination**.
* Add GraphQL **only** if your consumers demand flexible queries across complex object graphs.
