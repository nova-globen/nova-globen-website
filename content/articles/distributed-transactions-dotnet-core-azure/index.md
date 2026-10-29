---
title: "Distributed Transactions in .NET Core and Azure"
date: 2025-10-02
slug: distributed-transactions-dotnet-core-azure
description: "Distributed transactions in .NET Core allow operations across multiple databases or resource managers to be treated as a single atomic unit. This ensures data consistency by committing or rolling back all operations together."
tags: [data, microservices]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## The Saga Pattern

### What is a Saga?
A sequence of local transactions where each transaction updates data within a single service. If a step fails, compensating transactions undo the changes to maintain data consistency across distributed systems.

### Two Types:
1. **Choreography**: Each service produces and listens to events (decentralized)
2. **Orchestration**: A central coordinator directs the saga (centralized)

### Example with Orchestration (using MassTransit)

```csharp
// Saga State (using primary constructors - C# 12+)
public class OrderSagaState : SagaStateMachineInstance
{
    public required Guid CorrelationId { get; set; }
    public required string CurrentState { get; set; }
    public required Guid OrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ShipmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Saga State Machine
public class OrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public State OrderCreated { get; private set; } = null!;
    public State PaymentProcessing { get; private set; } = null!;
    public State PaymentCompleted { get; private set; } = null!;
    public State ShipmentScheduled { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;
    
    public Event<OrderCreatedEvent> OrderCreatedEvent { get; private set; } = null!;
    public Event<PaymentCompletedEvent> PaymentCompletedEvent { get; private set; } = null!;
    public Event<PaymentFailedEvent> PaymentFailedEvent { get; private set; } = null!;
    public Event<ShipmentScheduledEvent> ShipmentScheduledEvent { get; private set; } = null!;
    
    public OrderSagaStateMachine()
    {
        InstanceState(x => x.CurrentState);
        
        Initially(
            When(OrderCreatedEvent)
                .Then(context =>
                {
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.CreatedAt = DateTimeOffset.UtcNow;
                })
                .TransitionTo(PaymentProcessing)
                .PublishAsync(context => context.Init<ProcessPaymentCommand>(new
                {
                    context.Message.OrderId,
                    context.Message.TotalAmount,
                    CorrelationId = context.Saga.CorrelationId
                }))
        );
        
        During(PaymentProcessing,
            When(PaymentCompletedEvent)
                .Then(context =>
                {
                    context.Saga.PaymentId = context.Message.PaymentId;
                })
                .TransitionTo(PaymentCompleted)
                .PublishAsync(context => context.Init<ScheduleShipmentCommand>(new
                {
                    OrderId = context.Saga.OrderId,
                    CorrelationId = context.Saga.CorrelationId
                })),
                
            When(PaymentFailedEvent)
                .TransitionTo(Failed)
                .PublishAsync(context => context.Init<CancelOrderCommand>(new
                {
                    OrderId = context.Saga.OrderId,
                    Reason = "Payment failed",
                    CorrelationId = context.Saga.CorrelationId
                }))
        );
        
        During(PaymentCompleted,
            When(ShipmentScheduledEvent)
                .Then(context =>
                {
                    context.Saga.ShipmentId = context.Message.ShipmentId;
                })
                .TransitionTo(Completed)
                .Finalize()
        );
        
        SetCompletedWhenFinalized();
    }
}

// Registration with Azure Service Bus (in Program.cs)
builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderSagaStateMachine, OrderSagaState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.AddDbContext<DbContext, SagaDbContext>((provider, cfg) =>
            {
                cfg.UseSqlServer(builder.Configuration.GetConnectionString("SagaDb"));
            });
        });

    x.UsingAzureServiceBus((context, cfg) =>
    {
        cfg.Host(builder.Configuration["AzureServiceBus:ConnectionString"]);
        cfg.ConfigureEndpoints(context);
    });
});
```

### Tools for Saga Pattern:
- **MassTransit** (v8.2+): State machine-based sagas with Azure Service Bus
- **Dapr Workflow** (v1.14+): Actor-based workflows with state management
- **Azure Durable Functions**: Serverless orchestration with built-in state persistence
- **NServiceBus** (v9+): Commercial option with advanced saga features
- **Wolverine** (v3+): Modern messaging with saga support

---

## The Outbox Pattern

### What is the Outbox Pattern?
Ensures reliable event publishing by saving events to a database table (the "outbox") in the same transaction as business data, then publishing them asynchronously. This guarantees at-least-once delivery.

### Implementation with EF Core 9

```csharp
// Outbox Message
public sealed record OutboxMessage
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}

// Service with Outbox
public class OrderService(AppDbContext context, ILogger<OrderService> logger)
{
    public async Task CreateOrderAsync(CreateOrderCommand command, CancellationToken ct = default)
    {
        // EF Core 9: Improved transaction handling
        var strategy = context.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            
            try
            {
                // Save business entity
                var order = new Order(command.CustomerId, command.Items);
                context.Orders.Add(order);
                
                // Save event to outbox
                var evt = new OrderCreatedEvent(order.Id, order.CustomerId, order.Items);
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = evt.GetType().AssemblyQualifiedName!,
                    Payload = JsonSerializer.Serialize(evt, JsonSerializerOptions.Default),
                    CreatedAt = DateTimeOffset.UtcNow
                };

                context.OutboxMessages.Add(outboxMessage);
                
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                
                logger.LogInformation("Order {OrderId} created with outbox message {MessageId}", 
                    order.Id, outboxMessage.Id);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                logger.LogError(ex, "Failed to create order");
                throw;
            }
        });
    }
}

// Outbox Processor using IHostedLifecycleService (.NET 8+)
public class OutboxProcessor(
    IServiceProvider serviceProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox processor started");
        
        while (!stoppingToken.IsCancellationRequested && 
               await _timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessOutboxMessagesAsync(stoppingToken);
        }
    }
    
    private async Task ProcessOutboxMessagesAsync(CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        
        // EF Core 9: ExecuteUpdate for better performance
        var messages = await context.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < 5)
            .OrderBy(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
            
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(message.Type, message.Payload, ct);
                message.ProcessedAt = DateTimeOffset.UtcNow;
                
                logger.LogInformation("Published outbox message {MessageId}", message.Id);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;
                logger.LogError(ex, "Failed to publish message {MessageId}, retry {RetryCount}", 
                    message.Id, message.RetryCount);
            }
        }
        
        await context.SaveChangesAsync(ct);
        
        // Clean up old processed messages (older than 7 days)
        await context.OutboxMessages
            .Where(m => m.ProcessedAt != null && 
                        m.ProcessedAt < DateTimeOffset.UtcNow.AddDays(-7))
            .ExecuteDeleteAsync(ct);
    }
    
    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}
```

### Azure Integration

```csharp
// Using Azure Service Bus with Outbox
public class AzureServiceBusPublisher(ServiceBusClient client) : IEventPublisher
{
    public async Task PublishAsync(string eventType, string payload, CancellationToken ct = default)
    {
        var sender = client.CreateSender("events");
        var message = new ServiceBusMessage(payload)
        {
            ContentType = "application/json",
            Subject = eventType,
            MessageId = Guid.NewGuid().ToString()
        };
        
        await sender.SendMessageAsync(message, ct);
    }
}

// Registration
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["AzureServiceBus:ConnectionString"]!;
    return new ServiceBusClient(connectionString);
});
builder.Services.AddSingleton<IEventPublisher, AzureServiceBusPublisher>();
builder.Services.AddHostedService<OutboxProcessor>();
```

### Tools:
- **Entity Framework Core 9**: ExecuteUpdate/Delete, complex types, improved performance
- **MassTransit** (v8.2+): Built-in outbox with automatic publishing
- **Wolverine**: Transactional messaging with PostgreSQL
- **Azure Service Bus**: Reliable message delivery
- **Azure SQL Database**: Outbox storage with geo-replication

---

## Eventual Consistency

### What is Eventual Consistency?
In distributed systems, data across services may be temporarily inconsistent but will eventually converge to a consistent state. This is a trade-off for availability and partition tolerance (CAP theorem).

### Managing with CQRS Read Models

```csharp
// Read model with complex type (EF Core 9)
public sealed record OrderReadModel
{
    public required Guid OrderId { get; init; }
    public required string Status { get; set; }
    public required decimal TotalAmount { get; init; }
    public required string PaymentStatus { get; set; }
    public required Address ShippingAddress { get; init; }
    public required DateTimeOffset LastUpdated { get; set; }
    public TimeSpan? ProcessingTime => 
        Status == "Completed" ? LastUpdated - CreatedAt : null;
    
    private DateTimeOffset CreatedAt { get; init; }
}

// Complex type (EF Core 8+)
public record Address
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public required string PostalCode { get; init; }
}

// Event handlers with primary constructor
public class OrderReadModelUpdater(
    IRepository<OrderReadModel> repository,
    ILogger<OrderReadModelUpdater> logger) :
    IEventHandler<OrderCreatedEvent>,
    IEventHandler<PaymentCompletedEvent>,
    IEventHandler<ShipmentScheduledEvent>
{
    public async Task Handle(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        var model = new OrderReadModel
        {
            OrderId = evt.OrderId,
            Status = "Created",
            TotalAmount = evt.TotalAmount,
            PaymentStatus = "Pending",
            ShippingAddress = new Address
            {
                Street = evt.ShippingAddress.Street,
                City = evt.ShippingAddress.City,
                PostalCode = evt.ShippingAddress.PostalCode
            },
            LastUpdated = DateTimeOffset.UtcNow
        };
        
        await repository.UpsertAsync(model, ct);
        logger.LogInformation("Order read model created for {OrderId}", evt.OrderId);
    }
    
    public async Task Handle(PaymentCompletedEvent evt, CancellationToken ct = default)
    {
        var model = await repository.GetByIdAsync(evt.OrderId, ct);
        
        if (model is null)
        {
            logger.LogWarning("Order {OrderId} not found in read model", evt.OrderId);
            return;
        }
        
        model.PaymentStatus = "Completed";
        model.Status = "Paid";
        model.LastUpdated = DateTimeOffset.UtcNow;
        
        await repository.UpdateAsync(model, ct);
    }
    
    public async Task Handle(ShipmentScheduledEvent evt, CancellationToken ct = default)
    {
        var model = await repository.GetByIdAsync(evt.OrderId, ct);
        
        if (model is null)
        {
            logger.LogWarning("Order {OrderId} not found in read model", evt.OrderId);
            return;
        }
        
        model.Status = "Shipped";
        model.LastUpdated = DateTimeOffset.UtcNow;
        
        await repository.UpdateAsync(model, ct);
    }
}
```

### Azure Cosmos DB for Read Models

```csharp
// Using Cosmos DB for eventual consistency scenarios
public class CosmosOrderReadModelRepository(CosmosClient client) : IRepository<OrderReadModel>
{
    private readonly Container _container = client
        .GetContainer("OrdersDb", "OrderReadModels");
    
    public async Task UpsertAsync(OrderReadModel model, CancellationToken ct = default)
    {
        await _container.UpsertItemAsync(
            model,
            new PartitionKey(model.OrderId.ToString()),
            cancellationToken: ct);
    }
    
    public async Task<OrderReadModel?> GetByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<OrderReadModel>(
                orderId.ToString(),
                new PartitionKey(orderId.ToString()),
                cancellationToken: ct);
            
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
```

---

## Idempotent Handlers

### What is Idempotency?
An operation that produces the same result no matter how many times it's executed. Critical for handling message redeliveries and retries.

> Read more: [A practical, "ship-it" guide to idempotency keys, request hashing, persistence, PUT semantics, and de-dup in handlers/queues on .NET + Azure](https://nova-globen.se/blog/idempotency-in-dotnet-azure/)

### Implementation with Database Tracking

```csharp
// Using record for processed message tracking
public sealed record ProcessedMessage
{
    public required string MessageId { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
    public required string HandlerType { get; init; }
}

// Idempotent event handler
public class IdempotentEventHandler(AppDbContext context, ILogger<IdempotentEventHandler> logger)
{
    public async Task Handle(OrderCreatedEvent evt, string messageId, CancellationToken ct = default)
    {
        // Check if already processed
        var exists = await context.ProcessedMessages
            .AnyAsync(m => m.MessageId == messageId, ct);
            
        if (exists)
        {
            logger.LogInformation("Message {MessageId} already processed, skipping", messageId);
            return;
        }
        
        var strategy = context.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            
            try
            {
                // Process event
                var inventory = await context.Inventory
                    .FirstAsync(i => i.ProductId == evt.ProductId, ct);
                
                inventory.Reserve(evt.Quantity);
                
                // Record message as processed
                context.ProcessedMessages.Add(new ProcessedMessage
                {
                    MessageId = messageId,
                    ProcessedAt = DateTimeOffset.UtcNow,
                    HandlerType = nameof(IdempotentEventHandler)
                });
                
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                
                logger.LogInformation("Message {MessageId} processed successfully", messageId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                logger.LogError(ex, "Failed to process message {MessageId}", messageId);
                throw;
            }
        });
    }
}
```

### Azure Redis Cache for Idempotency

```csharp
// Using Azure Redis for distributed idempotency tracking
public class RedisIdempotencyChecker(
    IConnectionMultiplexer redis,
    ILogger<RedisIdempotencyChecker> logger)
{
    private readonly IDatabase _db = redis.GetDatabase();
    
    public async Task<bool> TryProcessAsync(
        string messageId, 
        Func<Task> handler,
        CancellationToken ct = default)
    {
        var key = $"processed:{messageId}";
        
        // Try to set the key with NX (only if not exists)
        var wasSet = await _db.StringSetAsync(
            key, 
            DateTimeOffset.UtcNow.ToString("O"),
            expiry: TimeSpan.FromDays(7),
            when: When.NotExists);
        
        if (!wasSet)
        {
            logger.LogInformation("Message {MessageId} already processed", messageId);
            return false;
        }
        
        try
        {
            await handler();
            return true;
        }
        catch
        {
            // Remove the key on failure to allow retry
            await _db.KeyDeleteAsync(key);
            throw;
        }
    }
}

// Usage
public class OrderEventHandler(
    RedisIdempotencyChecker idempotencyChecker,
    IOrderService orderService)
{
    public async Task Handle(OrderCreatedEvent evt, string messageId, CancellationToken ct)
    {
        await idempotencyChecker.TryProcessAsync(
            messageId,
            () => orderService.ProcessOrderAsync(evt, ct),
            ct);
    }
}

// Registration with Azure Cache for Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration["Azure:Redis:ConnectionString"]!;
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddSingleton<RedisIdempotencyChecker>();
```

### Natural Idempotency with HTTP

```csharp
// API endpoint with natural idempotency
[ApiController]
[Route("api/[controller]")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    // PUT is naturally idempotent
    [HttpPut("{orderId:guid}")]
    public async Task<IActionResult> CreateOrder(
        Guid orderId,
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var result = await orderService.CreateOrUpdateOrderAsync(orderId, request, ct);
        return result.IsNew ? CreatedAtAction(nameof(GetOrder), new { orderId }, result.Order)
                            : Ok(result.Order);
    }
    
    // Idempotency key header approach
    [HttpPost]
    public async Task<IActionResult> CreateOrderWithIdempotencyKey(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return BadRequest("Idempotency-Key header is required");
        }
        
        var result = await orderService.CreateOrderIdempotentAsync(idempotencyKey, request, ct);
        return result.IsNew ? CreatedAtAction(nameof(GetOrder), new { orderId = result.OrderId }, result.Order)
                            : Ok(result.Order);
    }
    
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken ct)
    {
        var order = await orderService.GetOrderAsync(orderId, ct);
        return order is null ? NotFound() : Ok(order);
    }
}
```

---

## Compensating Actions

### What are Compensating Actions?
Operations that undo the effects of previously completed transactions in a saga. Essential for maintaining consistency when a distributed transaction fails partway through.

> Read more: [Compensating Transaction pattern - Microsoft Azure Architecture](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)

### Implementation Example

```csharp
// Payment service with compensation
public class PaymentService(
    IPaymentGateway paymentGateway,
    ILogger<PaymentService> logger)
{
    public async Task<PaymentResult> ProcessPaymentAsync(
        Guid orderId, 
        decimal amount,
        CancellationToken ct = default)
    {
        logger.LogInformation("Processing payment for order {OrderId}, amount {Amount}", 
            orderId, amount);
        
        var payment = await paymentGateway.ChargeAsync(amount, ct);
        
        return new PaymentResult 
        { 
            Success = true, 
            PaymentId = payment.Id,
            TransactionId = payment.TransactionId
        };
    }
    
    // Compensating action
    public async Task RefundPaymentAsync(
        Guid paymentId,
        string reason,
        CancellationToken ct = default)
    {
        logger.LogWarning("Refunding payment {PaymentId}, reason: {Reason}", 
            paymentId, reason);
        
        await paymentGateway.RefundAsync(paymentId, ct);
    }
}

// Inventory service with compensation
public class InventoryService(
    IRepository<Inventory> repository,
    ILogger<InventoryService> logger)
{
    public async Task ReserveInventoryAsync(
        Guid orderId, 
        IReadOnlyList<OrderLine> items,
        CancellationToken ct = default)
    {
        logger.LogInformation("Reserving inventory for order {OrderId}", orderId);
        
        foreach (var item in items)
        {
            var inventory = await repository.GetAsync(item.ProductId, ct);
            
            if (inventory is null || !inventory.CanReserve(item.Quantity))
            {
                throw new InsufficientInventoryException(item.ProductId, item.Quantity);
            }
            
            inventory.Reserve(orderId, item.Quantity);
        }
        
        await repository.SaveAsync(ct);
    }
    
    // Compensating action
    public async Task ReleaseInventoryAsync(Guid orderId, CancellationToken ct = default)
    {
        logger.LogWarning("Releasing inventory for order {OrderId}", orderId);
        
        var reservations = await repository.GetReservationsByOrderAsync(orderId, ct);
        
        foreach (var reservation in reservations)
        {
            var inventory = await repository.GetAsync(reservation.ProductId, ct);
            inventory?.Release(orderId);
        }
        
        await repository.SaveAsync(ct);
    }
}

// Saga with compensating actions
public class OrderSagaWithCompensation : MassTransitStateMachine<OrderSagaState>
{
    public Event<OrderCreatedEvent> OrderCreated { get; private set; } = null!;
    public Event<PaymentFailedEvent> PaymentFailed { get; private set; } = null!;
    public Event<InventoryReservationFailedEvent> InventoryFailed { get; private set; } = null!;
    
    public OrderSagaWithCompensation()
    {
        // ... other state configurations ...
        
        // Compensation flow when payment fails
        During(PaymentProcessing,
            When(PaymentFailed)
                .Then(ctx => Log.Warning("Payment failed for order {OrderId}", ctx.Saga.OrderId))
                .TransitionTo(Compensating)
                .PublishAsync(ctx => ctx.Init<ReleaseInventoryCommand>(new
                {
                    OrderId = ctx.Saga.OrderId,
                    Reason = "Payment failed"
                }))
                .PublishAsync(ctx => ctx.Init<CancelOrderCommand>(new
                {
                    OrderId = ctx.Saga.OrderId,
                    Reason = "Payment failed"
                }))
                .TransitionTo(Failed)
                .Finalize()
        );
        
        // Compensation flow when inventory reservation fails
        During(InventoryReservation,
            When(InventoryFailed)
                .Then(ctx => Log.Warning("Inventory reservation failed for order {OrderId}", 
                    ctx.Saga.OrderId))
                .TransitionTo(Compensating)
                .If(ctx => ctx.Saga.PaymentId.HasValue,
                    compensate => compensate
                        .PublishAsync(ctx => ctx.Init<RefundPaymentCommand>(new
                        {
                            PaymentId = ctx.Saga.PaymentId!.Value,
                            Reason = "Inventory unavailable"
                        })))
                .PublishAsync(ctx => ctx.Init<CancelOrderCommand>(new
                {
                    OrderId = ctx.Saga.OrderId,
                    Reason = "Inventory unavailable"
                }))
                .TransitionTo(Failed)
                .Finalize()
        );
    }
}
```

### Azure Durable Functions for Compensation

```csharp
[DurableTask(nameof(OrderOrchestrator))]
public class OrderOrchestrator : TaskOrchestrator<OrderResult, OrderRequest>
{
    public override async Task<OrderResult> RunAsync(
        TaskOrchestrationContext context, 
        OrderRequest input)
    {
        var compensations = new Stack<Func<Task>>();
        
        try
        {
            // Step 1: Reserve inventory
            var inventoryReserved = await context.CallActivityAsync<bool>(
                nameof(ReserveInventoryActivity), 
                input.OrderId);
            
            if (!inventoryReserved)
            {
                return OrderResult.Failed("Inventory unavailable");
            }
            
            compensations.Push(() => context.CallActivityAsync(
                nameof(ReleaseInventoryActivity), 
                input.OrderId));
            
            // Step 2: Process payment
            var paymentResult = await context.CallActivityAsync<PaymentResult>(
                nameof(ProcessPaymentActivity), 
                input);
            
            if (!paymentResult.Success)
            {
                await ExecuteCompensationsAsync(context, compensations);
                return OrderResult.Failed("Payment failed");
            }
            
            compensations.Push(() => context.CallActivityAsync(
                nameof(RefundPaymentActivity), 
                paymentResult.PaymentId));
            
            // Step 3: Schedule shipment
            await context.CallActivityAsync(
                nameof(ScheduleShipmentActivity), 
                input.OrderId);
            
            return OrderResult.Success(input.OrderId);
        }
        catch (Exception ex)
        {
            // Execute all compensating actions in reverse order
            await ExecuteCompensationsAsync(context, compensations);
            return OrderResult.Failed(ex.Message);
        }
    }
    
    private static async Task ExecuteCompensationsAsync(
        TaskOrchestrationContext context,
        Stack<Func<Task>> compensations)
    {
        while (compensations.Count > 0)
        {
            var compensation = compensations.Pop();
            try
            {
                await compensation();
            }
            catch (Exception ex)
            {
                // Log but continue with other compensations
                context.SetCustomStatus($"Compensation failed: {ex.Message}");
            }
        }
    }
}
```

---

## Additional Patterns and Best Practices

### Circuit Breaker with Polly

```csharp
// Using Polly v8+ with resilience pipelines
builder.Services.AddHttpClient<IPaymentService, PaymentService>()
    .AddResilienceHandler("payment-resilience", static builder =>
    {
        builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential
            });
    });
```

### Monitoring with Azure Application Insights

```csharp
// Track saga execution
public class OrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public OrderSagaStateMachine(TelemetryClient telemetry)
    {
        Initially(
            When(OrderCreated)
                .Then(context =>
                {
                    telemetry.TrackEvent("OrderSagaStarted", new Dictionary<string, string>
                    {
                        ["OrderId"] = context.Message.OrderId.ToString(),
                        ["CorrelationId"] = context.Saga.CorrelationId.ToString()
                    });
                })
                // ... rest of saga logic
        );
    }
}
```

---

## Summary

These patterns work together to handle distributed transactions in microservices:

- **Saga Pattern**: Coordinates multi-step transactions across services
- **Outbox Pattern**: Ensures reliable event publishing
- **Eventual Consistency**: Accept temporary inconsistency for availability
- **Idempotent Handlers**: Ensure operations can be safely retried
- **Compensating Actions**: Roll back completed steps when failures occur

---

## Advanced Topics

### Event Sourcing with Marten

Event sourcing stores all changes as a sequence of events, providing a complete audit trail and enabling temporal queries.

```csharp
// Event-sourced aggregate
public record OrderCreated(Guid OrderId, string CustomerId, decimal Total);
public record PaymentProcessed(Guid OrderId, Guid PaymentId, decimal Amount);
public record OrderShipped(Guid OrderId, string TrackingNumber);
public record OrderCancelled(Guid OrderId, string Reason);

public class Order
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public string Status { get; private set; } = "Created";
    public Guid? PaymentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    
    // Apply events to rebuild state
    public void Apply(OrderCreated evt)
    {
        Id = evt.OrderId;
        CustomerId = evt.CustomerId;
        Total = evt.Total;
        Status = "Created";
    }
    
    public void Apply(PaymentProcessed evt)
    {
        PaymentId = evt.PaymentId;
        Status = "Paid";
    }
    
    public void Apply(OrderShipped evt)
    {
        TrackingNumber = evt.TrackingNumber;
        Status = "Shipped";
    }
    
    public void Apply(OrderCancelled evt)
    {
        Status = "Cancelled";
    }
}

// Using Marten for event sourcing
public class OrderService(IDocumentSession session)
{
    public async Task<Guid> CreateOrderAsync(
        string customerId, 
        decimal total,
        CancellationToken ct = default)
    {
        var orderId = Guid.NewGuid();
        var evt = new OrderCreated(orderId, customerId, total);
        
        session.Events.StartStream<Order>(orderId, evt);
        await session.SaveChangesAsync(ct);
        
        return orderId;
    }
    
    public async Task ProcessPaymentAsync(
        Guid orderId, 
        Guid paymentId, 
        decimal amount,
        CancellationToken ct = default)
    {
        var evt = new PaymentProcessed(orderId, paymentId, amount);
        session.Events.Append(orderId, evt);
        await session.SaveChangesAsync(ct);
    }
    
    public async Task<Order?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        return await session.Events.AggregateStreamAsync<Order>(orderId, token: ct);
    }
    
    public async Task<Order?> GetOrderAtTimeAsync(
        Guid orderId, 
        DateTimeOffset timestamp,
        CancellationToken ct = default)
    {
        return await session.Events.AggregateStreamAsync<Order>(
            orderId, 
            timestamp: timestamp.UtcDateTime,
            token: ct);
    }
}

// Marten configuration with PostgreSQL
builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("PostgreSQL")!);
    
    // Enable event sourcing
    options.Events.StreamIdentity = StreamIdentity.AsGuid;
    
    // Projections for read models
    options.Projections.Add<OrderProjection>(ProjectionLifecycle.Async);
})
.UseLightweightSessions()
.OptimizeArtifactWorkflow();
```

### Distributed Tracing with OpenTelemetry

```csharp
// Configure OpenTelemetry for distributed tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("MassTransit")
            .AddAzureMonitorTraceExporter(options =>
            {
                options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("MassTransit")
            .AddAzureMonitorMetricExporter(options =>
            {
                options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
            });
    });

// Custom tracing in saga
public class OrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
{
    private static readonly ActivitySource ActivitySource = new("OrderSaga");
    
    public OrderSagaStateMachine()
    {
        Initially(
            When(OrderCreatedEvent)
                .Then(context =>
                {
                    using var activity = ActivitySource.StartActivity("ProcessOrderCreated");
                    activity?.SetTag("order.id", context.Message.OrderId);
                    activity?.SetTag("saga.correlation_id", context.Saga.CorrelationId);
                    
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.CreatedAt = DateTimeOffset.UtcNow;
                })
                .TransitionTo(PaymentProcessing)
                .PublishAsync(context => context.Init<ProcessPaymentCommand>(new
                {
                    context.Message.OrderId,
                    context.Message.TotalAmount,
                    CorrelationId = context.Saga.CorrelationId
                }))
        );
    }
}
```

### Inbox Pattern (Complement to Outbox)

The inbox pattern ensures exactly-once processing of incoming messages by tracking received messages before processing.

```csharp
// Inbox message tracking
public sealed record InboxMessage
{
    public required string MessageId { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Status { get; set; } = "Pending";
}

// Message handler with inbox
public class OrderEventConsumer(
    AppDbContext context,
    IOrderService orderService,
    ILogger<OrderEventConsumer> logger) : IConsumer<OrderCreatedEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedEvent> ctx)
    {
        var messageId = ctx.MessageId?.ToString() ?? Guid.NewGuid().ToString();
        
        // Check inbox for duplicate
        var inboxMessage = await context.InboxMessages
            .FirstOrDefaultAsync(m => m.MessageId == messageId, ctx.CancellationToken);
        
        if (inboxMessage?.ProcessedAt != null)
        {
            logger.LogInformation("Message {MessageId} already processed", messageId);
            return;
        }
        
        var strategy = context.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ctx.CancellationToken);
            
            try
            {
                // Record message in inbox
                if (inboxMessage == null)
                {
                    inboxMessage = new InboxMessage
                    {
                        MessageId = messageId,
                        Source = ctx.SourceAddress?.ToString() ?? "unknown",
                        ReceivedAt = DateTimeOffset.UtcNow
                    };
                    context.InboxMessages.Add(inboxMessage);
                }
                
                // Process the message
                await orderService.HandleOrderCreatedAsync(ctx.Message, ctx.CancellationToken);
                
                // Mark as processed
                inboxMessage.ProcessedAt = DateTimeOffset.UtcNow;
                inboxMessage.Status = "Processed";
                
                await context.SaveChangesAsync(ctx.CancellationToken);
                await transaction.CommitAsync(ctx.CancellationToken);
                
                logger.LogInformation("Message {MessageId} processed successfully", messageId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ctx.CancellationToken);
                logger.LogError(ex, "Failed to process message {MessageId}", messageId);
                
                if (inboxMessage != null)
                {
                    inboxMessage.Status = "Failed";
                    await context.SaveChangesAsync(ctx.CancellationToken);
                }
                
                throw;
            }
        });
    }
}
```

### Two-Phase Commit Alternative: Try-Confirm/Cancel (TCC)

TCC is a lightweight alternative to traditional two-phase commit, suitable for microservices.

```csharp
// TCC transaction coordinator
public interface ITccParticipant
{
    Task<bool> TryAsync(Guid transactionId, CancellationToken ct);
    Task ConfirmAsync(Guid transactionId, CancellationToken ct);
    Task CancelAsync(Guid transactionId, CancellationToken ct);
}

public class PaymentTccParticipant(
    IPaymentGateway gateway,
    IRepository<PaymentReservation> repository) : ITccParticipant
{
    public async Task<bool> TryAsync(Guid transactionId, CancellationToken ct)
    {
        // Reserve funds without actually charging
        var reservation = await gateway.ReserveFundsAsync(transactionId, ct);
        
        if (reservation.Success)
        {
            await repository.AddAsync(new PaymentReservation
            {
                TransactionId = transactionId,
                ReservationId = reservation.Id,
                Status = "Reserved",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            }, ct);
        }
        
        return reservation.Success;
    }
    
    public async Task ConfirmAsync(Guid transactionId, CancellationToken ct)
    {
        var reservation = await repository.GetByTransactionIdAsync(transactionId, ct);
        
        if (reservation != null)
        {
            await gateway.CaptureReservedFundsAsync(reservation.ReservationId, ct);
            reservation.Status = "Confirmed";
            await repository.UpdateAsync(reservation, ct);
        }
    }
    
    public async Task CancelAsync(Guid transactionId, CancellationToken ct)
    {
        var reservation = await repository.GetByTransactionIdAsync(transactionId, ct);
        
        if (reservation != null)
        {
            await gateway.ReleaseReservedFundsAsync(reservation.ReservationId, ct);
            reservation.Status = "Cancelled";
            await repository.UpdateAsync(reservation, ct);
        }
    }
}

// TCC Coordinator
public class TccCoordinator(
    IEnumerable<ITccParticipant> participants,
    ILogger<TccCoordinator> logger)
{
    public async Task<bool> ExecuteAsync(Guid transactionId, CancellationToken ct = default)
    {
        var tryResults = new List<(ITccParticipant Participant, bool Success)>();
        
        // Phase 1: Try
        foreach (var participant in participants)
        {
            try
            {
                var success = await participant.TryAsync(transactionId, ct);
                tryResults.Add((participant, success));
                
                if (!success)
                {
                    logger.LogWarning("Try phase failed for transaction {TransactionId}", transactionId);
                    await CancelAllAsync(transactionId, tryResults.Select(r => r.Participant), ct);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in try phase for transaction {TransactionId}", transactionId);
                await CancelAllAsync(transactionId, tryResults.Select(r => r.Participant), ct);
                return false;
            }
        }
        
        // Phase 2: Confirm
        foreach (var participant in participants)
        {
            try
            {
                await participant.ConfirmAsync(transactionId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in confirm phase for transaction {TransactionId}", transactionId);
                // Continue confirming others - confirmation should be idempotent
            }
        }
        
        return true;
    }
    
    private async Task CancelAllAsync(
        Guid transactionId,
        IEnumerable<ITccParticipant> participants,
        CancellationToken ct)
    {
        foreach (var participant in participants)
        {
            try
            {
                await participant.CancelAsync(transactionId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in cancel phase for transaction {TransactionId}", transactionId);
            }
        }
    }
}
```

### Dead Letter Queue Handling

```csharp
// Dead letter queue processor
public class DeadLetterProcessor(
    ServiceBusClient client,
    ILogger<DeadLetterProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiver = client.CreateReceiver(
            "orders", 
            new ServiceBusReceiverOptions 
            { 
                SubQueue = SubQueue.DeadLetter 
            });
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await receiver.ReceiveMessagesAsync(
                    maxMessages: 10,
                    maxWaitTime: TimeSpan.FromSeconds(5),
                    cancellationToken: stoppingToken);
                
                foreach (var message in messages)
                {
                    logger.LogWarning(
                        "Dead letter message: {MessageId}, Reason: {Reason}, Description: {Description}",
                        message.MessageId,
                        message.DeadLetterReason,
                        message.DeadLetterErrorDescription);
                    
                    // Log to external monitoring system
                    // Optionally attempt reprocessing or manual intervention
                    
                    await receiver.CompleteMessageAsync(message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing dead letter queue");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
```

### Performance Optimization: Parallel Saga Execution

```csharp
// Parallel saga steps where operations are independent
public class OptimizedOrderSagaStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public OptimizedOrderSagaStateMachine()
    {
        During(OrderCreated,
            When(StartProcessing)
                .Then(async context =>
                {
                    // Execute independent operations in parallel
                    var inventoryTask = context.Publish<CheckInventoryCommand>(new
                    {
                        context.Saga.OrderId
                    });
                    
                    var customerTask = context.Publish<ValidateCustomerCommand>(new
                    {
                        context.Saga.CustomerId
                    });
                    
                    var pricingTask = context.Publish<CalculatePricingCommand>(new
                    {
                        context.Saga.OrderId
                    });
                    
                    // Wait for all to complete
                    await Task.WhenAll(inventoryTask, customerTask, pricingTask);
                })
                .TransitionTo(ValidationInProgress)
        );
    }
}
```

---

## Testing Strategies

### Unit Testing Saga State Machines

```csharp
public class OrderSagaStateMachineTests
{
    [Fact]
    public async Task Should_Transition_To_PaymentProcessing_When_Order_Created()
    {
        // Arrange
        var harness = new InMemoryTestHarness();
        var sagaHarness = harness.StateMachineSaga<OrderSagaState, OrderSagaStateMachine>();
        
        await harness.Start();
        
        try
        {
            var orderId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            
            // Act
            await harness.Bus.Publish(new OrderCreatedEvent
            {
                OrderId = orderId,
                CustomerId = "customer-123",
                TotalAmount = 100.00m,
                CorrelationId = correlationId
            });
            
            // Assert
            Assert.True(await sagaHarness.Consumed.Any<OrderCreatedEvent>());
            
            var saga = sagaHarness.Created.ContainsInState(
                correlationId, 
                sagaHarness.StateMachine, 
                sagaHarness.StateMachine.PaymentProcessing);
            
            Assert.NotNull(saga);
            
            Assert.True(await harness.Published.Any<ProcessPaymentCommand>());
        }
        finally
        {
            await harness.Stop();
        }
    }
}
```

### Integration Testing with Testcontainers

```csharp
public class OrderSagaIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();
    
    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();
    
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _rabbitmq.StartAsync();
    }
    
    [Fact]
    public async Task Should_Complete_Full_Order_Saga()
    {
        // Arrange
        var services = new ServiceCollection();
        
        services.AddMassTransit(x =>
        {
            x.AddSagaStateMachine<OrderSagaStateMachine, OrderSagaState>()
                .InMemoryRepository();
            
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(_rabbitmq.GetConnectionString());
                cfg.ConfigureEndpoints(context);
            });
        });
        
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBus>();
        
        await bus.StartAsync();
        
        try
        {
            var orderId = Guid.NewGuid();
            
            // Act
            await bus.Publish(new OrderCreatedEvent
            {
                OrderId = orderId,
                CustomerId = "test-customer",
                TotalAmount = 150.00m
            });
            
            // Wait for saga completion
            await Task.Delay(TimeSpan.FromSeconds(5));
            
            // Assert - verify saga completed successfully
            // This would query your saga repository in a real test
        }
        finally
        {
            await bus.StopAsync();
        }
    }
    
    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitmq.DisposeAsync();
    }
}
```

---

## Recommended Resources

### Official Documentation
- [MassTransit Documentation](https://masstransit.io/)
- [Azure Service Bus Documentation](https://learn.microsoft.com/en-us/azure/service-bus-messaging/)
- [Entity Framework Core 9](https://learn.microsoft.com/en-us/ef/core/)
- [Dapr Workflow](https://docs.dapr.io/developing-applications/building-blocks/workflow/)

### Books
- *Microservices Patterns* by Chris Richardson
- *Building Microservices* by Sam Newman (3rd Edition)
- *Software Architecture: The Hard Parts* by Neal Ford et al.

### Articles & Patterns
- [Saga Pattern - Microsoft](https://learn.microsoft.com/en-us/azure/architecture/reference-architectures/saga/saga)
- [Compensating Transaction Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction)
- [Idempotency in .NET + Azure](https://nova-globen.se/blog/idempotency-in-dotnet-azure/)
