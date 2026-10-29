---
title: "Modeling: contracts, DTOs, entities, value objects, and the best practices in .NET core"
date: 2025-09-27
slug: modeling-contracts-dtos-entities-value-objects-best-practices-dotnet-core
description: "A practical, opinionated guide to contracts vs DTOs vs entities, how to model them with records, when to map (and how), plus a comparison of Mappers, and finally whether T4 codegen is worth it today."
tags: [architecture, apis]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# Modeling: contracts, DTOs, entities, value objects

## Contracts (API surface)

* **Purpose:** What your API promises to clients (request/response models).
* **Shape:** Prefer *immutable* **`record`** types with `init`-only props; put them in a **Contracts** assembly that’s separate from your domain and EF Core models.
* **Versioning:** Additive changes; never break existing clients. Use explicit JSON names if you need long-term wire stability.

## DTOs (internal transfer)

* **Purpose:** Data carriers between layers (e.g., query projections to API).
* **Shape:** Also good candidates for `record` types; keep them **read-only** where possible.
* **Tip:** For hot paths, DTOs can be flat/projection-specific to avoid over-fetching.

## Entities (domain)

* **Purpose:** Behavioral + invariant-enforcing types that **own identity**.
* **Shape:** **Classes** with methods and private setters; avoid leaking them outside the domain.
* **Concurrency:** Include `RowVersion`/ETag for optimistic concurrency.

## Value Objects

* **Purpose:** Identity-less concepts (Money, DateRange).
* **Shape:** **`record struct`** or `record class` with validation in the ctor/factory; make them truly immutable.

## Why `record`s?
They give you value-based equality, terse init, and work great for contracts/DTOs. Use classes for entities to emphasize identity and behavior.

# When to map (and when not): What I recommend (quick decision tree)

1. **Is the endpoint hot (perf-critical)?**

   * Yes → **Projection or generator (Mapperly/Mapster-gen)**, or hand map.
   * No → AutoMapper/Mapster runtime is fine.

2. **Do you want compile-time safety?**

   * Yes → **Mapperly / Mapster-gen**.

3. **Keep dependencies tiny / own the stack?**

   * Yes → **StoneKit Transverse Mapper** or hand map. ([NuGet][1])

4. **Do you need advanced conventions & eco-system?**

   * Yes → **AutoMapper**.

## Map (separate types) when:

* You expose public APIs (never return EF entities directly).
* You need tailored read models (projections for list/detail endpoints).
* You cross bounded contexts (translate language).

## Don’t map (reuse type) when:

* Internal-only microservice endpoint and the domain object *is* the contract (rare).
* Simple pass-through with no coupling concerns.
* Performance-critical pipelines where mapping cost dominates and you can shape the query to the exact contract (e.g., `Select(new Contract(...))`).

# Mapping strategies: what to pick where

| Case                              | Best practice                                                                                                                           |
| --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| **Hot path reads (10k+/sec)**     | **Hand mapping or code-gen** (projection in LINQ, or a generator like Mapperly/Mapster). Avoid runtime reflection.                      |
| **Most CRUD endpoints**           | **Mapster or AutoMapper** with profile/conventions; keep config near the feature (“vertical slice”).                                    |
| **Strict compile-time safety**    | **Mapperly (source generator)** or Mapster’s source-gen mode so changes fail at build time.                                             |
| **Tiny, single-purpose services** | Hand mapping (it’s trivial and most readable).                                                                                          |
| **Greenfield .NET 8+**            | Consider **StoneKit.TransverseMapper** if you want ultra-minimal runtime overhead and you control the library (see below). ([NuGet][1]) |

---

# Library comparison (concise)

## AutoMapper

* **Pros:** Mature, huge ecosystem, convention-based, profiles, value converters, projection support.
* **Cons:** Historically reflection/emission at runtime (good but not the fastest vs hand/codegen); config indirection can hide breaking changes; recent licensing/commercial model changes sparked “alternatives” discussions. ([Code Maze][2])

## Mapster

* **Pros:** Faster than classic AutoMapper in many cases; can **generate code** (no runtime cost); flexible.
* **Cons:** Two modes (runtime vs codegen) to learn; you must opt into source‐gen to get the real perf wins. ([Code Maze][2])

## Mapperly (source generator)

* **Pros:** Pure **Roslyn source generator**—outputs plain mapping code at build time; excellent perf and compile-time safety (missing members are errors).
* **Cons:** Smaller ecosystem than AutoMapper; explicit config via attributes. (Community pieces discuss moves from AutoMapper to Mapperly.) ([ABP.IO][3])

## TinyMapper / AgileMapper / others

* **Pros:** Lightweight and fast (TinyMapper), flexible (AgileMapper).
* **Cons:** Smaller communities; fewer features than AutoMapper/Mapster. Benchmarks repos highlight tradeoffs. ([GitHub][4])

## StoneKit Transverse Mapper

* **What it is:** A tiny, **.NET 8+** mapping library aiming to be *tiniest and fastest*, inspired by AutoMapper & TinyMapper.
* **Status & perf:** NuGet/package notes and repo claim better performance than AutoMapper on tested cases; positioned for minimal overhead. ([NuGet][1])

## Bottom line

* **Max safety + speed:** *Mapperly/Mapster (source-gen mode)*.
* **Ease + ecosystem:** *AutoMapper*.
* **Minimalist + perf-focused:** StoneKit Transverse Mapper or hand-written mappings.

# Practical patterns & snippets

## Contracts and mapping shape

```csharp
// Contracts (API)
public sealed record CreateOrderRequest(string CustomerId, IReadOnlyList<OrderLineDto> Lines);
public sealed record OrderLineDto(string Sku, int Qty, decimal UnitPrice);

public sealed record OrderResponse(string Id, string Status, decimal Total);

// Domain
public sealed class Order : Entity<OrderId> // identity, invariants
{
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyCollection<OrderLine> Lines => _lines;
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.Total);
    // behavior methods...
}
```

**Projection without a mapper (fast path):**

```csharp
// EF Core query straight to contract (no extra allocation/mapping)
var response = await db.Orders
    .Where(o => o.Id == id)
    .Select(o => new OrderResponse(o.Id, o.Status, o.Lines.Sum(l => l.Price * l.Qty)))
    .SingleAsync();
```

## AutoMapper (conventional)

```csharp
public class OrdersProfile : Profile
{
    public OrdersProfile()
    {
        CreateMap<Order, OrderResponse>()
            .ForMember(d => d.Total, ex => ex.MapFrom(s => s.Lines.Sum(l => l.Price * l.Qty)));
        CreateMap<CreateOrderRequest, Order>()
            .ForCtorParam("id", opt => opt.MapFrom(_ => OrderId.New()));
    }
}
```

**Pros:** terse; **Cons:** runtime mapping (unless using projection). Good default for non-hot paths. ([Code Maze][2])

## Mapster (with source-gen)

```csharp
[Mapper]
public partial class OrdersMapper
{
    public partial OrderResponse ToResponse(Order src);
    public partial Order ToEntity(CreateOrderRequest src);
}
// Build generates strongly-typed mapping methods (fast).
```

**Pros:** generated code, compile-time checks; **Cons:** attribute learning curve. ([Code Maze][2])

## Mapperly (pure source-gen)

```csharp
[Mapper]
public partial class OrdersMapper
{
    public partial OrderResponse Map(Order order);
    public partial Order Map(CreateOrderRequest req);
}
```

**Pros:** no runtime reflection, missing members => build errors. ([ABP.IO][3])

## StoneKit Transverse Mapper (lean runtime)

The library’s goal is a very small & quick mapper for .NET 8+ with benchmarks showing wins vs AutoMapper on specific scenarios. Use where you want a tiny dependency and full control. ([NuGet][1])

```csharp
Transverse.Bind<Person, PersonDto>(config =>
{
	config.Ignore(x => x.Id);
	config.Ignore(x => x.Email);
	config.Bind(source => source.LastName, target => target.Surname);
	config.Bind(target => source.Emails, typeof(List<string>));
});

var person = new Person
{
	Id = Guid.NewGuid(),
	FirstName = "John",
	LastName = "Doe",
	Emails = new List<string>{"a@b.com", "c@d.com"}
};

var personDto = Transverse.Map<PersonDto>(person);
```

# Azure-stack considerations

* **DTO size & paging:** Prefer projection to contracts (`Select` → DTO) to reduce payload & DB traffic—especially for **Azure SQL** and **Cosmos DB RU** costs.
* **Minimal allocations:** Generated mappings reduce GC pressure in **high-QPS** Azure App Service/ACA workloads.
* **AOT (NativeAOT, .NET 9):** Source-generated mappers (Mapperly/Mapster gen) play nicer with trimming/AOT than heavy reflection.

---

# Code generation: **T4** vs **Roslyn source generators**

**T4 (Text Template Transformation Toolkit)**

* **How it works:** Design/build-time templates that spit out `.cs` files.
* **Pros:** Simple, works offline, you fully control the generated code.
* **Cons:** Older tooling; friction with SDK-style projects & CI; not incremental; weaker IDE feedback.

**Roslyn source generators (modern)**

* **How they work:** Run at compile time inside the compiler; emit C# that participates in IntelliSense and **incremental builds**.
* **Pros:** First-class with SDK projects, great IDE experience, no separate T4 pipeline, better for trimming/AOT.
* **Cons:** You need a generator or a library that provides one.

**Are T4 templates “faster”?**
At runtime, **both** approaches compile to plain C# → IL, so **runtime performance is comparable** if they produce the same mappings (near hand-written). The big wins are:

* **Build ergonomics & safety:** Roslyn generators catch missing members at build time and integrate better with modern .NET.
* **Maintenance:** Generators (Mapperly/Mapster) remove template upkeep.
  TL;DR: **Prefer Roslyn source generators** today unless you have a special pipeline that already uses T4 heavily.

# Pitfalls & best practices (checklist)

* **Never** expose EF entities over the wire.
* **One mapping per feature** (keep config near handlers/endpoints).
* **Projection first:** When hitting a DB, project directly to the contract/DTO to avoid materializing full graphs.
* **Immutable contracts:** `record` + `init` to prevent accidental mutation.
* **Map only what you need:** Avoid “God DTOs”.
* **Tests:** Unit test complex mappings (custom resolvers/converters).
* **Perf guardrails:** Benchmark a representative mapping; if >5–10% of endpoint time, switch to projection or generator.
* **AOT/trimming:** Prefer generator mappers in NativeAOT scenarios.

## References & further reading

* **StoneKit.TransverseMapper** NuGet & repo (tiny, perf-oriented mapper for .NET 8+). ([NuGet][1])
* **Mapster vs AutoMapper** overviews & perf notes. ([Code Maze][2])
* **Alternatives & source-gen trend (Mapperly)**—community discussion and migration rationale. ([ABP.IO][3])
* **Benchmarks & landscape of .NET mappers** (TinyMapper, AgileMapper, etc.). ([GitHub][4])
* **AutoMapper performance context** (historical, shows it’s “fast enough” for many cases). ([Stack Overflow][5])

[1]: https://www.nuget.org/packages/StoneKit.TransverseMapper/ "NuGet Gallery | StoneKit.TransverseMapper 2024.1.1.391401"
[2]: https://code-maze.com/automapper-vs-mapster-dotnet/ "AutoMapper vs Mapster in .NET - Code Maze"
[3]: https://abp.io/community/articles/best-free-alternatives-to-automapper-in-net-l9f5ii8s "Best Free Alternatives to AutoMapper in .NET - abp.io"
[4]: https://github.com/mjebrahimi/DotNet-Mappers-Benchmark ".NET Object Mappers Benchmark - GitHub"
[5]: https://stackoverflow.com/questions/38619574/automapper-performance "c# - Automapper performance - Stack Overflow"
