---
title: "CQRS with Separate Read/Write Models in .NET Core"
date: 2025-10-06
slug: cqrs-with-separate-read-write-models-in-dotnet-core
description: "By maintaining distinct models for reading and writing data, CQRS enables independent optimization of each side based on their specific requirements. The write model focuses on business logic and data consistency, while the read model is optimized for query performance through de"
tags: [architecture, data]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Understanding CQRS
Command Query Responsibility Segregation (CQRS) is an architectural pattern that separates read operations (queries) from write operations (commands). 

## Benefits and Trade-offs

**Advantages:**
- **Independent Scaling:** Scale read and write workloads separately based on demand
- **Optimized Performance:** Denormalized read models eliminate complex joins
- **Flexible Querying:** Create multiple read models tailored to different query patterns
- **Technology Diversity:** Use different databases for reads (e.g., Elasticsearch) and writes (e.g., PostgreSQL)

**Challenges:**
- **Eventual Consistency:** Read model updates lag behind write operations
- **Increased Complexity:** Maintaining separate models and synchronization logic
- **Data Duplication:** Same data exists in multiple forms across models
- **Debugging Difficulty:** Tracking issues across distributed components

**When to Use CQRS:**
- High-traffic systems with significantly different read and write patterns
- Applications requiring multiple specialized views of the same data
- Systems where read performance is critical (reporting, analytics)
- Domains with complex business logic that benefits from separation of concerns

**When to Avoid CQRS:**
- Simple CRUD applications with balanced read/write operations
- Small-scale systems where the added complexity isn't justified
- Teams unfamiliar with eventual consistency patterns
- Projects with tight deadlines and limited resources

## Write Model (Command Side)

The write model enforces business rules and maintains transactional consistency:

```csharp
public class OrderWriteModel
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public List<OrderLine> Lines { get; set; }
    
    // Encapsulates business logic
    public void AddLine(Guid productId, int quantity, decimal price)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");
            
        Lines.Add(new OrderLine(productId, quantity, price));
    }
}
```

## Read Model (Query Side)

The read model is denormalized for optimal query performance, containing pre-computed and aggregated data:

```csharp
public class OrderReadModel
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public List<OrderItemReadModel> Items { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Denormalized fields eliminate joins at query time
    public string ShippingAddress { get; set; }
    public string PaymentMethod { get; set; }
    public DateTime? ShippedAt { get; set; }
}

public class OrderItemReadModel
{
    public string ProductName { get; set; }
    public string ProductImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
```

## Command Handler

Command handlers process write operations and update the write model:

```csharp
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly OrderWriteDbContext _writeDb;
    private readonly IEventPublisher _eventPublisher;
    
    public CreateOrderCommandHandler(
        OrderWriteDbContext writeDb,
        IEventPublisher eventPublisher)
    {
        _writeDb = writeDb;
        _eventPublisher = eventPublisher;
    }
    
    public async Task<Guid> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = new OrderWriteModel
        {
            Id = Guid.NewGuid(),
            CustomerId = command.CustomerId,
            Lines = new List<OrderLine>()
        };
        
        foreach (var item in command.Items)
        {
            order.AddLine(item.ProductId, item.Quantity, item.Price);
        }
        
        _writeDb.Orders.Add(order);
        await _writeDb.SaveChangesAsync(cancellationToken);
        
        // Publish integration event to synchronize read model
        await _eventPublisher.PublishAsync(
            new OrderCreatedEvent(order.Id, order.CustomerId));
            
        return order.Id;
    }
}
```

## Event Handler

Event handlers listen for changes and update the read model asynchronously:

```csharp
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly OrderReadDbContext _readDb;
    private readonly ICustomerServiceClient _customerClient;
    private readonly IProductServiceClient _productClient;
    private readonly IOrderServiceClient _orderServiceClient;
    
    public OrderCreatedEventHandler(
        OrderReadDbContext readDb,
        ICustomerServiceClient customerClient,
        IProductServiceClient productClient,
        IOrderServiceClient orderServiceClient)
    {
        _readDb = readDb;
        _customerClient = customerClient;
        _productClient = productClient;
        _orderServiceClient = orderServiceClient;
    }
    
    public async Task Handle(OrderCreatedEvent @event)
    {
        // Fetch data from various sources for denormalization
        var customer = await _customerClient.GetCustomerAsync(@event.CustomerId);
        var order = await _orderServiceClient.GetOrderAsync(@event.OrderId);
        
        var productIds = order.Lines.Select(l => l.ProductId).ToList();
        var products = await _productClient.GetProductsAsync(productIds);
        
        // Build denormalized read model with all necessary data
        var readModel = new OrderReadModel
        {
            Id = @event.OrderId,
            CustomerName = customer.Name,
            CustomerEmail = customer.Email,
            Items = order.Lines.Select(line => new OrderItemReadModel
            {
                ProductName = products.First(p => p.Id == line.ProductId).Name,
                ProductImageUrl = products.First(p => p.Id == line.ProductId).ImageUrl,
                Quantity = line.Quantity,
                UnitPrice = line.Price,
                LineTotal = line.Quantity * line.Price
            }).ToList(),
            TotalAmount = order.Lines.Sum(l => l.Quantity * l.Price),
            Status = "Created",
            CreatedAt = DateTime.UtcNow
        };
        
        _readDb.Orders.Add(readModel);
        await _readDb.SaveChangesAsync();
    }
}
```

## Query Handler

Query handlers retrieve data from the optimized read model:

```csharp
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderReadModel>
{
    private readonly OrderReadDbContext _readDb;
    
    public GetOrderQueryHandler(OrderReadDbContext readDb)
    {
        _readDb = readDb;
    }
    
    public async Task<OrderReadModel> Handle(
        GetOrderQuery query,
        CancellationToken cancellationToken)
    {
        // Execute fast query on denormalized data with no joins required
        return await _readDb.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken);
    }
}
```

## Database Contexts

Separate database contexts allow independent optimization strategies for reads and writes:

### Write Database Context

```csharp
public class OrderWriteDbContext : DbContext
{
    public DbSet<OrderWriteModel> Orders { get; set; }
    
    public OrderWriteDbContext(DbContextOptions<OrderWriteDbContext> options)
        : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Minimal indexes optimized for write operations
        modelBuilder.Entity<OrderWriteModel>()
            .HasIndex(o => o.CustomerId);
    }
}
```

### Read Database Context

```csharp
public class OrderReadDbContext : DbContext
{
    public DbSet<OrderReadModel> Orders { get; set; }
    
    public OrderReadDbContext(DbContextOptions<OrderReadDbContext> options)
        : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Multiple indexes optimized for various query patterns
        modelBuilder.Entity<OrderReadModel>()
            .HasIndex(o => o.CustomerEmail);
        
        modelBuilder.Entity<OrderReadModel>()
            .HasIndex(o => o.CreatedAt);
            
        modelBuilder.Entity<OrderReadModel>()
            .HasIndex(o => o.Status);
        
        // Mark as read-only to prevent accidental migrations
        modelBuilder.Entity<OrderReadModel>()
            .ToTable("OrderReadModels", t => t.ExcludeFromMigrations());
    }
}
```
