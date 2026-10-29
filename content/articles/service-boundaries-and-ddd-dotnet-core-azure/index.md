---
title: "Service Boundaries and Domain-Driven Design in .NET Core and Azure"
date: 2025-10-02
slug: service-boundaries-and-ddd-dotnet-core-azure
description: "DDD provides the theoretical foundation for defining service boundaries through bounded contexts — explicit boundaries within which a particular domain model is defined and applicable."
tags: [architecture, microservices]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

### What “Explicit Context Mapping” means (in DDD terms)

In DDD, **context mapping** is how you define and document the relationships between **bounded contexts** (separate domain models owned by different teams or services). Making it **explicit** means you don’t let integration “just happen” through ad-hoc calls or shared tables—you **codify** the relationships, the integration style (e.g., Customer-Supplier, Shared Kernel, Open Host Service), and the **translation layer** between models so it’s visible in code and contracts, not tribal knowledge. ([martinfowler.com][1])

### How to do it in .NET (Core) — a minimal, workable recipe
> Carve out bounded contexts

Split into separate projects (or services) with their own models, persistence, and language.

```
/src
  /SalesContext.Api
  /SalesContext.Domain
  /SalesContext.Infrastructure (EF Core, Migrations)
  /BillingContext.Api
  /BillingContext.Domain
  /BillingContext.Infrastructure
  /Contracts (versioned DTOs/events)
```

Document the relationship (e.g., **Sales** is upstream, **Billing** is downstream using **Customer-Supplier** with **Published Language** via events/HTTP). ([ddd-crew on GitHub][2])

> Choose an integration style and make it concrete

### Common explicit patterns:

* **Open Host Service + Published Language** (HTTP/REST or gRPC with explicit contracts)
* **Event Publication** (messages with versioned schemas)
* **Anti-Corruption Layer (ACL)** (an adapter/translator that shields your domain from upstream models)

Pick one per interaction and stick it in code (see next steps). ([InfoQ][3])

### Define **explicit contracts** (don’t leak domain models)

Create a `Contracts` assembly shared only as **schemas** (not domain types).

```csharp
// Contracts/Orders/v1/OrderPlaced.v1.cs
namespace Contracts.Orders.V1;
public record OrderPlacedV1(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt);
```

Version contracts (V1, V2…) and keep them stable.

### Build an **Anti-Corruption Layer** in the downstream context

Put all mapping code in one place so domain stays pure.

```csharp
// BillingContext.Infrastructure/Integration/OrdersAcl.cs
using Contracts.Orders.V1;
using BillingContext.Domain.Billing;

public interface IOrdersAcl {
    InvoiceDraft Translate(OrderPlacedV1 evt);
}

public sealed class OrdersAcl : IOrdersAcl {
    public InvoiceDraft Translate(OrderPlacedV1 evt) =>
        InvoiceDraft.Create(
            invoiceId: Guid.NewGuid(),
            orderId: evt.OrderId,
            customerId: evt.CustomerId,
            amount: Money.Of(evt.TotalAmount, evt.Currency),
            issuedAt: evt.OccurredAt);
}
```

You can hand-roll mappings (fast/explicit), or use a mapper (AutoMapper/Mapster/TransverseMapper) if you prefer. Mapster supports code-gen for **explicit** mapper classes if you want compile-time safety. ([GitHub][4])

### Wire it through messaging or HTTP

Example: event consuming (MassTransit/NServiceBus/ReBus or raw Rabbit/Kafka). Here’s a handler calling the ACL:

```csharp
// BillingContext.Api/Consumers/OrderPlacedConsumer.cs
using Contracts.Orders.V1;
using MassTransit;

public class OrderPlacedConsumer : IConsumer<OrderPlacedV1> {
    private readonly IOrdersAcl _acl;
    private readonly IBillingRepository _repo;

    public OrderPlacedConsumer(IOrdersAcl acl, IBillingRepository repo) {
        _acl = acl; _repo = repo;
    }

    public async Task Consume(ConsumeContext<OrderPlacedV1> context) {
        var draft = _acl.Translate(context.Message);
        await _repo.SaveDraftAsync(draft, context.CancellationToken);
    }
}
```

For Open Host Service: expose a minimal API in **Sales** with DTOs from `Contracts`, consume in **Billing**, then pass through the ACL.

### Keep persistence mapping internal to each context

Use EF Core configurations inside each context; never expose these types across contexts.

```csharp
// BillingContext.Infrastructure/Config/InvoiceConfig.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class InvoiceConfig : IEntityTypeConfiguration<Invoice> {
    public void Configure(EntityTypeBuilder<Invoice> b) {
        b.ToTable("Invoices");
        b.HasKey(i => i.Id);
        b.OwnsOne(i => i.Amount, m => {
            m.Property(p => p.Value).HasColumnName("Amount");
            m.Property(p => p.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        b.Property(i => i.IssuedAt);
        // etc.
    }
}
```

(These are regular EF Core mappings—separate from *context* mapping—but important to keep bounded context persistence clean.) ([Anton Dev Tips][5])

### Test the map (contract tests)

* **Consumer tests** in Billing verify that a recorded `OrderPlacedV1` yields the expected `InvoiceDraft`.
* **Provider tests** in Sales verify it emits `OrderPlacedV1` matching schema.
  This locks the integration map and catches breaking changes early.

### Tiny, end-to-end example (HTTP, Open Host + ACL)

**Sales** exposes an endpoint with a published language:

```csharp
// SalesContext.Api/OrdersController.cs
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase {
    [HttpGet("{id:guid}")]
    public ActionResult<Contracts.Orders.V1.OrderDtoV1> Get(Guid id) {
        var order = _app.GetOrder(id);
        return new Contracts.Orders.V1.OrderDtoV1(
            order.Id, order.CustomerId, order.Total.Value, order.Total.Currency);
    }
}
```

**Billing** calls it and uses the ACL:

```csharp
// BillingContext.Infrastructure/Integration/OrdersClient.cs
public sealed class OrdersClient(HttpClient http) {
    public async Task<OrderDtoV1?> Get(Guid id, CancellationToken ct) =>
        await http.GetFromJsonAsync<OrderDtoV1>($"api/orders/{id}", ct);
}

// BillingContext.Application/UseCases/GenerateInvoice.cs
public sealed class GenerateInvoice {
    private readonly OrdersClient _client;
    private readonly IOrdersAcl _acl;
    private readonly IBillingRepository _repo;

    public async Task Handle(Guid orderId, CancellationToken ct) {
        var dto = await _client.Get(orderId, ct) ?? throw new InvalidOperationException();
        var draft = _acl.Translate(new OrderPlacedV1(dto.OrderId, dto.CustomerId, dto.Total, dto.Currency, DateTimeOffset.UtcNow));
        await _repo.SaveDraftAsync(draft, ct);
    }
}
```

The **map** is explicit: contracts, translation, and the chosen relationship are all visible in code and docs.

### Tips & pitfalls

* **Never** reference another context’s domain types—only contracts.
* Put all cross-context translations in **one ACL layer**, not scattered.
* Version contracts (V1/V2) and **don’t break** old consumers.
* Prefer **domain language** inside each context; translations happen only at the edges.
* Document the chosen patterns (Customer-Supplier, Shared Kernel, etc.) in your repo’s `/architecture/` folder.

### Further reading (solid background material)

* Microsoft Learn: [Anti-corruption Layer pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/anti-corruption-layer)
* DDD Crew’s concise guide to **Context Mapping** patterns (Customer-Supplier, Shared Kernel, etc.). ([GitHub][2])
* Martin Fowler on **Bounded Context** (why mappings exist and what they protect). ([martinfowler.com][1])
* Alberto Brandolini’s **strategic DDD with context mapping** article (how to pick relationships). ([InfoQ][3])
* For explicit, generated mapper classes in .NET, see **Mapster**’s code-gen option. ([GitHub][4])
* EF Core mapping techniques (for your *internal* persistence model). ([Anton Dev Tips][5])

[1]: https://martinfowler.com/bliki/BoundedContext.html "Bounded Context - Martin Fowler"
[2]: https://github.com/ddd-crew/context-mapping "GitHub - ddd-crew/context-mapping"
[3]: https://www.infoq.com/articles/ddd-contextmapping/ "Strategic Domain Driven Design with Context Mapping - InfoQ"
[4]: https://github.com/MapsterMapper/Mapster "GitHub - MapsterMapper/Mapster: A fast, fun and stimulating object to ..."
[5]: https://antondevtips.com/blog/exploring-data-mapping-options-in-ef-core "Exploring Data Mapping Options in EF CORE - Anton Dev Tips"1. 

### Tools for Bounded Context Definition:
   - **Context Mapper**: A DSL for strategic DDD
   - **EventStorming**: Workshop-based approach to identify boundaries
   - **C4 Model diagrams**: Visual documentation of contexts
   - **.NET Aspire**: For local development orchestration of bounded contexts

**Example with .NET Aspire:**
```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var orderService = builder.AddProject<Projects.OrderService>("order-service");
var customerService = builder.AddProject<Projects.CustomerService>("customer-service");
var inventoryService = builder.AddProject<Projects.InventoryService>("inventory-service");

builder.Build().Run();
```

### Analyzing Coupling

**Coupling Analysis Techniques:**

1. **Static Code Analysis** using NDepend or Roslyn Analyzers:

```csharp
// Custom Roslyn Analyzer to detect cross-boundary references
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BoundedContextAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeNode, 
            SyntaxKind.UsingDirective);
    }
    
    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        // Check for references across bounded contexts
    }
}
```

2. **Dependency Graphs** - Use Visual Studio or Rider's architecture tools
3. **ArchUnit.NET** for architectural testing:

```csharp
[Test]
public void OrderService_Should_Not_Reference_CustomerService()
{
    var architecture = new ArchLoader()
        .LoadAssemblies(typeof(OrderService).Assembly)
        .Build();
        
    var rule = Types()
        .That().ResideInNamespace("OrderManagement")
        .Should().NotDependOnAny("CustomerManagement.Domain");
        
    rule.Check(architecture);
}
```

### Analyzing Communication

**Tools and Approaches:**

1. **Application Insights** with dependency tracking:

```csharp
services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = configuration["ApplicationInsights:ConnectionString"];
    options.EnableDependencyTrackingTelemetryModule = true;
});
```

2. **Distributed Tracing** with OpenTelemetry:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddAzureMonitorTraceExporter();
    });
```

3. **Service Mesh** (like Dapr or Linkerd) provides communication analytics out-of-the-box.
