---
title: "Event Sourcing in .NET Core"
date: 2025-10-06
slug: event-sourcing-dotnet-core-azure
description: "Event Sourcing is an architectural pattern where instead of storing only the current state of an entity, you store the complete sequence of events that led to that state. The current state is derived by replaying these events from the beginning. This approach fundamentally change"
tags: [architecture, data]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Benefits of Event Sourcing

**Complete Audit Trail:** Every change to the system is captured as an immutable event, providing a comprehensive history of what happened, when, and why.

**Time Travel Capabilities:** You can reconstruct the state of any entity at any point in time by replaying events up to that moment, invaluable for debugging and compliance.

**Event Replay for Debugging:** Reproduce issues by replaying the exact sequence of events that caused a problem, making debugging significantly easier.

**Multiple Read Models:** Generate different read models (projections) from the same event stream, optimizing queries for different use cases without duplicating write logic.

## Implementation with Marten

[Marten](https://github.com/JasperFx/marten) is a .NET library that provides event sourcing and document database capabilities on top of PostgreSQL. Below is a complete implementation of event sourcing for an order management system.

### Installing Marten

```bash
dotnet add package Marten
```

### Defining the Aggregate Root

The aggregate root applies events to build its state:

```csharp
public class Order
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public List<OrderLine> Lines { get; private set; } = new();
    public decimal TotalAmount { get; private set; }
    
    // Event application methods
    public void Apply(OrderCreated @event)
    {
        Id = @event.OrderId;
        CustomerId = @event.CustomerId;
        Status = OrderStatus.Created;
    }
    
    public void Apply(OrderLineAdded @event)
    {
        Lines.Add(new OrderLine
        {
            ProductId = @event.ProductId,
            Quantity = @event.Quantity,
            Price = @event.Price
        });
        TotalAmount += @event.Quantity * @event.Price;
    }
    
    public void Apply(OrderConfirmed @event)
    {
        Status = OrderStatus.Confirmed;
    }
    
    public void Apply(OrderCancelled @event)
    {
        Status = OrderStatus.Cancelled;
    }
}
```

### Defining Events

Events are immutable records that represent state changes:

```csharp
public record OrderCreated(Guid OrderId, Guid CustomerId, DateTime CreatedAt);
public record OrderLineAdded(Guid ProductId, int Quantity, decimal Price);
public record OrderConfirmed(DateTime ConfirmedAt);
public record OrderCancelled(string Reason, DateTime CancelledAt);
```

### Configuring Marten

Set up Marten with event sourcing and projection support:

```csharp
builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("Marten"));
    
    // Register event types
    options.Events.AddEventType<OrderCreated>();
    options.Events.AddEventType<OrderLineAdded>();
    options.Events.AddEventType<OrderConfirmed>();
    options.Events.AddEventType<OrderCancelled>();
    
    // Configure projections for read models
    options.Projections.Add<OrderProjection>(ProjectionLifecycle.Inline);
});
```

### Writing Events with Command Handlers

Command handlers append events to the event stream:

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly IDocumentSession _session;
    
    public CreateOrderHandler(IDocumentSession session)
    {
        _session = session;
    }
    
    public async Task<Guid> Handle(
        CreateOrderCommand request, 
        CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        
        // Start a new event stream for the order
        _session.Events.StartStream<Order>(
            orderId,
            new OrderCreated(orderId, request.CustomerId, DateTime.UtcNow)
        );
        
        // Append additional events to the stream
        foreach (var item in request.Items)
        {
            _session.Events.Append(
                orderId,
                new OrderLineAdded(item.ProductId, item.Quantity, item.Price)
            );
        }
        
        await _session.SaveChangesAsync(cancellationToken);
        return orderId;
    }
}
```

### Reading State with Query Handlers

Query handlers rebuild aggregates by replaying events:

```csharp
public class GetOrderHandler : IRequestHandler<GetOrderQuery, Order>
{
    private readonly IDocumentSession _session;
    
    public GetOrderHandler(IDocumentSession session)
    {
        _session = session;
    }
    
    public async Task<Order> Handle(
        GetOrderQuery request,
        CancellationToken cancellationToken)
    {
        // Marten automatically rebuilds the aggregate from its event stream
        var order = await _session.Events
            .AggregateStreamAsync<Order>(request.OrderId, token: cancellationToken);
            
        return order;
    }
}
```

### Creating Projections for Read Models

Projections transform events into optimized read models:

```csharp
public class OrderProjection : EventProjection
{
    // Create initial read model from first event
    public OrderReadModel Create(OrderCreated @event)
    {
        return new OrderReadModel
        {
            Id = @event.OrderId,
            CustomerId = @event.CustomerId,
            Status = "Created",
            CreatedAt = @event.CreatedAt
        };
    }
    
    // Update read model as new events occur
    public void Apply(OrderLineAdded @event, OrderReadModel model)
    {
        model.ItemCount++;
        model.TotalAmount += @event.Quantity * @event.Price;
    }
    
    public void Apply(OrderConfirmed @event, OrderReadModel model)
    {
        model.Status = "Confirmed";
        model.ConfirmedAt = @event.ConfirmedAt;
    }
}
```

### Time Travel Queries

One of the most powerful features of event sourcing is the ability to query historical state:

```csharp
public async Task<Order> GetOrderAtPointInTime(Guid orderId, DateTime timestamp)
{
    // Replay events only up to the specified timestamp
    return await _session.Events
        .AggregateStreamAsync<Order>(
            orderId,
            timestamp: timestamp);
}
```

## Key Considerations

**When to Use Event Sourcing:**
- Systems requiring complete audit trails (financial, healthcare, compliance)
- Applications needing temporal queries and historical analysis
- Domains with complex business logic where understanding the "why" matters
- Systems that benefit from multiple read models

**Challenges:**
- Increased complexity compared to traditional CRUD
- Event schema evolution requires careful planning
- Higher storage requirements due to event retention
- Learning curve for developers unfamiliar with the pattern

**Best Practices:**
- Keep events immutable and focused on business facts
- Version your events to handle schema changes
- Use projections for query optimization
- Implement snapshots for aggregates with long event histories
- Consider eventual consistency implications
