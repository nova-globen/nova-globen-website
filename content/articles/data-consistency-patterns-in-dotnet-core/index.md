---
title: "Data Consistency Patterns in Distributed Systems and .NET Core"
date: 2025-10-06
slug: data-consistency-patterns-in-dotnet-core
description: "In distributed systems, maintaining data consistency across multiple data stores or services is a fundamental challenge. The dual write problem occurs when you need to update two separate systems atomically but lack a distributed transaction mechanism. If one write succeeds and t"
tags: [data, microservices]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Comparison of Patterns

| Pattern | Complexity | Consistency | Performance | Use Case |
|---------|-----------|-------------|-------------|----------|
| **Transactional Outbox** | Medium | Strong | Good | General-purpose, reliable event publishing |
| **CDC** | Low | Strong | Excellent | Existing systems, minimal code changes |
| **Event Sourcing** | High | Strong | Excellent | Audit requirements, temporal queries |
| **Saga Pattern** | High | Eventual | Good | Complex distributed workflows |

## Best Practices and Decision Tree

**For Transactional Outbox:**
- Use background services with proper error handling and retry logic
- Implement idempotency in message consumers
- Monitor outbox table size and clean up processed messages
- Consider partitioning outbox table for high-volume scenarios

**For CDC:**
- Regularly monitor change tracking overhead on the database
- Configure appropriate retention periods
- Handle schema changes carefully to avoid breaking CDC
- Test CDC processor recovery after failures

**For All Patterns:**
- Implement comprehensive logging and monitoring
- Use distributed tracing to track operations across services
- Design for idempotency at every level
- Plan for failure scenarios and compensating actions
- Consider using Azure Monitor and Application Insights for observability

## Azure-Specific Considerations

When implementing these patterns on Azure:
- Use **Azure SQL Database** with built-in CDC support
- Leverage **Azure Service Bus** for reliable message delivery with features like dead-letter queues and scheduled messages
- Implement **Azure Functions** as lightweight CDC processors for serverless scenarios
- Use **Azure Cosmos DB** change feed as an alternative to CDC for NoSQL scenarios
- Enable **Application Insights** for end-to-end transaction tracking
- Consider **Azure Durable Functions** for implementing saga patterns with built-in state management

## The Anti-Pattern

```csharp
// ANTI-PATTERN: Dual write (not atomic)
public async Task CreateOrderAsync(Order order)
{
    await _database.SaveAsync(order); // Write 1
    await _serviceBus.PublishAsync(new OrderCreated(order.Id)); // Write 2
    
    // If the publish fails after the database save succeeds,
    // we have inconsistency - the order exists but no event was published!
}
```

This approach has several critical issues:
- No atomicity between database write and message publication
- Partial failures leave the system in an inconsistent state
- Manual compensation logic is complex and error-prone
- Difficult to recover from failures

## Solution 1: Transactional Outbox Pattern

The Transactional Outbox pattern ensures atomicity by storing events in the same database transaction as the business data, then publishing them asynchronously. You can read more about [distributed transactions in .NET core and Azure in here](https://nova-globen.se/blog/distributed-transactions-dotnet-core-azure/).

### Implementation with EF Core 9 and Azure Service Bus

```csharp
// Outbox message entity
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; }
    public string Payload { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}

// DbContext with outbox
public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    
    public AppDbContext(DbContextOptions<AppDbContext> options) 
        : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(m => new { m.ProcessedAt, m.CreatedAt })
            .HasFilter("[ProcessedAt] IS NULL");
    }
}

// Service with transactional outbox
public class OrderService
{
    private readonly AppDbContext _context;
    private readonly ILogger<OrderService> _logger;
    
    public OrderService(AppDbContext context, ILogger<OrderService> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    public async Task<Guid> CreateOrderAsync(CreateOrderRequest request)
    {
        // Use ExecuteInTransactionAsync for .NET 9
        await _context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
            async () =>
            {
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerId = request.CustomerId,
                    TotalAmount = request.Items.Sum(i => i.Quantity * i.Price),
                    Status = OrderStatus.Created,
                    CreatedAt = DateTime.UtcNow
                };
                
                _context.Orders.Add(order);
                
                // Store event in outbox within the same transaction
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = nameof(OrderCreatedEvent),
                    Payload = JsonSerializer.Serialize(new OrderCreatedEvent
                    {
                        OrderId = order.Id,
                        CustomerId = order.CustomerId,
                        CreatedAt = order.CreatedAt
                    }),
                    CreatedAt = DateTime.UtcNow
                };
                
                _context.OutboxMessages.Add(outboxMessage);
                
                await _context.SaveChangesAsync();
                
                _logger.LogInformation(
                    "Order {OrderId} created with outbox message {MessageId}",
                    order.Id, outboxMessage.Id);
                
                return order.Id;
            },
            verifySucceeded: null);
        
        return await Task.FromResult(Guid.Empty); // This will be set by the transaction
    }
}

// Outbox processor with Azure Service Bus
public class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ServiceBusSender _serviceBusSender;
    private readonly ILogger<OutboxProcessor> _logger;
    private const int BatchSize = 100;
    private const int MaxRetries = 5;
    
    public OutboxProcessor(
        IServiceProvider serviceProvider,
        ServiceBusClient serviceBusClient,
        ILogger<OutboxProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _serviceBusSender = serviceBusClient.CreateSender("order-events");
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox processor started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
    
    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // Fetch unprocessed messages
        var messages = await context.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        
        if (!messages.Any())
            return;
        
        _logger.LogInformation("Processing {Count} outbox messages", messages.Count);
        
        foreach (var message in messages)
        {
            try
            {
                // Publish to Azure Service Bus
                var serviceBusMessage = new ServiceBusMessage(message.Payload)
                {
                    MessageId = message.Id.ToString(),
                    Subject = message.EventType,
                    ContentType = "application/json"
                };
                
                await _serviceBusSender.SendMessageAsync(serviceBusMessage, cancellationToken);
                
                // Mark as processed
                message.ProcessedAt = DateTime.UtcNow;
                
                _logger.LogInformation(
                    "Outbox message {MessageId} published successfully",
                    message.Id);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;
                
                _logger.LogWarning(ex,
                    "Failed to process outbox message {MessageId}. Retry count: {RetryCount}",
                    message.Id, message.RetryCount);
            }
        }
        
        await context.SaveChangesAsync(cancellationToken);
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox processor stopping");
        await _serviceBusSender.CloseAsync();
        await base.StopAsync(cancellationToken);
    }
}

// Register services in Program.cs
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton(sp =>
    new ServiceBusClient(builder.Configuration.GetConnectionString("ServiceBus")));

builder.Services.AddHostedService<OutboxProcessor>();
```

## Solution 2: Change Data Capture (CDC)

Change Data Capture automatically tracks changes in your database and publishes them as events, eliminating the need for application-level dual writes.

### Implementation with SQL Server CDC and Azure

```csharp
// Enable CDC on SQL Server
// Execute these SQL commands on your Azure SQL Database:
/*
ALTER DATABASE YourDatabase SET CHANGE_TRACKING = ON  
    (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);

ALTER TABLE Orders ENABLE CHANGE_TRACKING  
    WITH (TRACK_COLUMNS_UPDATED = ON);
*/

// Change tracking model
public class OrderChange
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Operation { get; set; } // I, U, D
    public long ChangeVersion { get; set; }
}

// CDC Processor with .NET 9
public class CdcProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ServiceBusSender _serviceBusSender;
    private readonly ILogger<CdcProcessor> _logger;
    private long _lastSyncVersion;
    
    public CdcProcessor(
        IServiceProvider serviceProvider,
        ServiceBusClient serviceBusClient,
        ILogger<CdcProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _serviceBusSender = serviceBusClient.CreateSender("order-events");
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CDC processor started");
        
        // Initialize last sync version
        _lastSyncVersion = await GetCurrentChangeVersionAsync();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessChangesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CDC changes");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
    
    private async Task ProcessChangesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // Query change tracking using FormattableString for safety
        var currentVersion = await GetCurrentChangeVersionAsync();
        
        var changes = await context.Database
            .SqlQuery<OrderChange>(
                $@"SELECT o.OrderId, o.CustomerId, 
                      CT.SYS_CHANGE_OPERATION as Operation,
                      CT.SYS_CHANGE_VERSION as ChangeVersion
                   FROM Orders o
                   RIGHT OUTER JOIN CHANGETABLE(CHANGES Orders, {_lastSyncVersion}) AS CT
                      ON o.OrderId = CT.OrderId
                   WHERE CT.SYS_CHANGE_VERSION <= {currentVersion}
                   ORDER BY CT.SYS_CHANGE_VERSION")
            .ToListAsync(cancellationToken);
        
        if (!changes.Any())
            return;
        
        _logger.LogInformation("Processing {Count} changes", changes.Count);
        
        foreach (var change in changes)
        {
            try
            {
                var eventMessage = change.Operation switch
                {
                    "I" => new ServiceBusMessage(JsonSerializer.Serialize(
                        new OrderCreatedEvent
                        {
                            OrderId = change.OrderId,
                            CustomerId = change.CustomerId
                        }))
                    {
                        Subject = nameof(OrderCreatedEvent)
                    },
                    
                    "U" => new ServiceBusMessage(JsonSerializer.Serialize(
                        new OrderUpdatedEvent
                        {
                            OrderId = change.OrderId
                        }))
                    {
                        Subject = nameof(OrderUpdatedEvent)
                    },
                    
                    "D" => new ServiceBusMessage(JsonSerializer.Serialize(
                        new OrderDeletedEvent
                        {
                            OrderId = change.OrderId
                        }))
                    {
                        Subject = nameof(OrderDeletedEvent)
                    },
                    
                    _ => throw new InvalidOperationException(
                        $"Unknown operation: {change.Operation}")
                };
                
                eventMessage.MessageId = Guid.NewGuid().ToString();
                eventMessage.ContentType = "application/json";
                
                await _serviceBusSender.SendMessageAsync(eventMessage, cancellationToken);
                
                _logger.LogInformation(
                    "Published {EventType} for order {OrderId}",
                    eventMessage.Subject, change.OrderId);
                
                // Update last sync version
                _lastSyncVersion = Math.Max(_lastSyncVersion, change.ChangeVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish event for order {OrderId}",
                    change.OrderId);
            }
        }
    }
    
    private async Task<long> GetCurrentChangeVersionAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var version = await context.Database
            .SqlQuery<long>($"SELECT CHANGE_TRACKING_CURRENT_VERSION()")
            .FirstOrDefaultAsync();
        
        return version;
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CDC processor stopping");
        await _serviceBusSender.CloseAsync();
        await base.StopAsync(cancellationToken);
    }
}
```

## Solution 3: Event Sourcing

Event Sourcing naturally solves the dual write problem by treating events as the single source of truth. See the [dedicated Event Sourcing article for a complete implementation](https://nova-globen.se/blog/event-sourcing-dotnet-core-azure/).

## Solution 4: Saga Pattern for Distributed Transactions

For complex workflows spanning multiple services, the Saga pattern coordinates distributed transactions through compensating actions.

```csharp
// Saga state machine using MassTransit
public class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    public State OrderCreated { get; private set; }
    public State PaymentProcessing { get; private set; }
    public State InventoryReserving { get; private set; }
    public State OrderCompleted { get; private set; }
    public State OrderFailed { get; private set; }
    
    public Event<OrderSubmitted> OrderSubmitted { get; private set; }
    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; }
    public Event<PaymentFailed> PaymentFailed { get; private set; }
    public Event<InventoryReserved> InventoryReserved { get; private set; }
    public Event<InventoryReservationFailed> InventoryReservationFailed { get; private set; }
    
    public OrderSaga()
    {
        InstanceState(x => x.CurrentState);
        
        Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentSucceeded, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReserved, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => InventoryReservationFailed, x => x.CorrelateById(m => m.Message.OrderId));
        
        Initially(
            When(OrderSubmitted)
                .Then(context =>
                {
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.CustomerId = context.Message.CustomerId;
                })
                .TransitionTo(OrderCreated)
                .PublishAsync(context => context.Init<ProcessPayment>(new
                {
                    context.Message.OrderId,
                    context.Message.Amount
                })));
        
        During(OrderCreated,
            When(PaymentSucceeded)
                .TransitionTo(PaymentProcessing)
                .PublishAsync(context => context.Init<ReserveInventory>(new
                {
                    context.Message.OrderId,
                    context.Saga.CustomerId
                })),
            
            When(PaymentFailed)
                .TransitionTo(OrderFailed)
                .ThenAsync(async context =>
                {
                    // Compensate: Cancel order
                    await context.PublishAsync(new OrderCancelled
                    {
                        OrderId = context.Message.OrderId,
                        Reason = "Payment failed"
                    });
                }));
        
        During(PaymentProcessing,
            When(InventoryReserved)
                .TransitionTo(OrderCompleted)
                .Finalize(),
            
            When(InventoryReservationFailed)
                .TransitionTo(OrderFailed)
                .ThenAsync(async context =>
                {
                    // Compensate: Refund payment
                    await context.PublishAsync(new RefundPayment
                    {
                        OrderId = context.Message.OrderId
                    });
                })
                .ThenAsync(async context =>
                {
                    // Cancel order
                    await context.PublishAsync(new OrderCancelled
                    {
                        OrderId = context.Message.OrderId,
                        Reason = "Inventory unavailable"
                    });
                }));
    }
}

public class OrderSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
}

// Configure in Program.cs
builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderSaga, OrderSagaState>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.UseSqlServer();
        });
    
    x.UsingAzureServiceBus((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("ServiceBus"));
        cfg.ConfigureEndpoints(context);
    });
});
```
