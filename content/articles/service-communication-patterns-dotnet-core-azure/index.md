---
title: "Service Communication Patterns in .NET Core and Azure"
date: 2025-10-02
slug: service-communication-patterns-dotnet-core-azure
description: "Modern distributed applications require robust communication patterns to ensure reliability, scalability, and maintainability. This guide explores proven patterns and anti-patterns for service communication in .NET and Azure, helping you build resilient microservices architecture"
tags: [microservices]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Asynchronous Messaging with Azure Service Bus

Azure Service Bus provides enterprise-grade messaging infrastructure with advanced features for reliable message delivery, ordering guarantees, and complex routing scenarios.

### Service Bus vs Azure Queue Storage

**Azure Service Bus** offers enterprise messaging capabilities including:
- Topics and subscriptions for pub/sub patterns
- Message sessions for ordered processing
- Transaction support across operations
- Dead-letter queues for failed messages
- Messages up to 100MB (premium tier)
- Advanced routing with filters and actions

**Azure Queue Storage** provides:
- Simple FIFO queue operations
- Lower cost for basic scenarios
- Messages up to 64KB
- Best for simple point-to-point messaging

### When to Choose Service Bus

Use Azure Service Bus when you need:
- Publish-subscribe patterns with multiple subscribers
- Guaranteed message ordering with sessions
- Transactional message processing
- Message size beyond 64KB
- Advanced routing and filtering
- Integration with hybrid or on-premises systems

### Implementation with .NET 9

.NET 9 introduces improved performance and simplified APIs for working with Azure Service Bus:

```csharp
// Producer using .NET 9 with improved performance
public class OrderCreatedPublisher
{
    private readonly ServiceBusSender _sender;
    
    public OrderCreatedPublisher(ServiceBusClient client)
    {
        _sender = client.CreateSender("order-events");
    }
    
    public async Task PublishOrderCreatedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var message = new ServiceBusMessage(JsonSerializer.Serialize(order))
        {
            MessageId = order.OrderId.ToString(),
            Subject = "OrderCreated",
            ContentType = "application/json",
            // .NET 9: Better support for distributed tracing
            ApplicationProperties = 
            {
                ["CorrelationId"] = Activity.Current?.Id ?? Guid.NewGuid().ToString(),
                ["OrderDate"] = order.CreatedAt.ToString("O")
            }
        };
        
        await _sender.SendMessageAsync(message, cancellationToken);
    }
}

// Consumer using BackgroundService
public class InventoryService : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<InventoryService> _logger;
    private readonly IInventoryRepository _repository;
    
    public InventoryService(
        ServiceBusClient client, 
        ILogger<InventoryService> logger,
        IInventoryRepository repository)
    {
        _processor = client.CreateProcessor("order-events", "inventory-subscription");
        _processor.ProcessMessageAsync += ProcessMessageHandler;
        _processor.ProcessErrorAsync += ErrorHandler;
        _logger = logger;
        _repository = repository;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _processor.StartProcessingAsync(stoppingToken);
        
        // .NET 9: Improved cancellation handling
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _processor.StopProcessingAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
    
    private async Task ProcessMessageHandler(ProcessMessageEventArgs args)
    {
        try
        {
            var order = JsonSerializer.Deserialize<Order>(args.Message.Body);
            
            // Reserve inventory with idempotency check
            await _repository.ReserveInventoryAsync(order!, args.CancellationToken);
            
            // Complete the message
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order message {MessageId}", args.Message.MessageId);
            
            // Dead-letter the message if processing fails repeatedly
            if (args.Message.DeliveryCount > 3)
            {
                await args.DeadLetterMessageAsync(args.Message, 
                    "Processing failed after retries", 
                    ex.Message, 
                    args.CancellationToken);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            }
        }
    }
    
    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
```

### Dependency Injection Setup for .NET 9

```csharp
// Program.cs with .NET 9 improvements
var builder = WebApplication.CreateBuilder(args);

// Azure Service Bus with managed identity
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddServiceBusClient(builder.Configuration.GetSection("ServiceBus"))
        .WithCredential(new DefaultAzureCredential());
});

// Register background services
builder.Services.AddHostedService<InventoryService>();
builder.Services.AddSingleton<OrderCreatedPublisher>();

// Add health checks for Service Bus
builder.Services.AddHealthChecks()
    .AddAzureServiceBusQueue(
        builder.Configuration["ServiceBus:ConnectionString"]!,
        "order-events");
```

## Pub/Sub vs Producer/Consumer Patterns

Understanding when to use each pattern is crucial for effective system design.

### Publish-Subscribe Pattern

In pub/sub, events are broadcast to multiple independent subscribers who each receive a copy of every message. This pattern excels at:
- Event notification across multiple services
- Decoupling event producers from consumers
- Allowing new subscribers without changing publishers
- Broadcasting state changes to interested parties

**Implementation Options:**
- **Azure Service Bus Topics**: Durable messaging with advanced filtering
- **Azure Event Grid**: Lightweight, serverless event routing

### Producer-Consumer Pattern

Producer-consumer implements point-to-point communication where each message is processed by exactly one consumer. Ideal for:
- Work queue distribution
- Load balancing across workers
- Task processing with guaranteed completion
- Rate limiting and backpressure handling

**Implementation Options:**
- **Azure Service Bus Queues**: Enterprise features with transactions
- **Azure Queue Storage**: Simple, cost-effective queuing
- **RabbitMQ**: Self-hosted option with rich features

### Azure Event Grid Example

```csharp
public class EventGridPublisher
{
    private readonly EventGridPublisherClient _client;
    private readonly ILogger<EventGridPublisher> _logger;
    
    public EventGridPublisher(EventGridPublisherClient client, ILogger<EventGridPublisher> logger)
    {
        _client = client;
        _logger = logger;
    }
    
    public async Task PublishOrderEventAsync(OrderCreated orderEvent, CancellationToken cancellationToken = default)
    {
        var cloudEvent = new CloudEvent(
            source: "OrderService",
            type: "Order.Created",
            jsonSerializableData: orderEvent)
        {
            Id = Guid.NewGuid().ToString(),
            Time = DateTimeOffset.UtcNow,
            // .NET 9: Enhanced structured data support
            ExtensionAttributes = 
            {
                ["correlationId"] = orderEvent.CorrelationId,
                ["orderAmount"] = orderEvent.TotalAmount
            }
        };
        
        try
        {
            await _client.SendEventAsync(cloudEvent, cancellationToken);
            _logger.LogInformation("Published order event {OrderId}", orderEvent.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish order event {OrderId}", orderEvent.OrderId);
            throw;
        }
    }
}
```

## Azure Event Grid vs Azure Service Bus

### When to Use Event Grid

Event Grid excels in event-driven architectures requiring:
- **Serverless integration**: Trigger Azure Functions, Logic Apps
- **React to Azure service events**: Storage changes, resource updates
- **Real-time notifications**: Push updates to web clients
- **High throughput**: Millions of events per second
- **Low latency**: Sub-second delivery
- **Pay per event**: Cost-effective for sporadic events

**Best for**: State change notifications, IoT telemetry, serverless workflows, webhook delivery

### When to Use Service Bus

Service Bus is designed for enterprise messaging requiring:
- **Message sessions**: Ordered processing of related messages
- **Transactions**: ACID guarantees across operations
- **Dead-letter queues**: Handle poison messages
- **Scheduled delivery**: Defer message processing
- **Duplicate detection**: Automatic deduplication
- **Large messages**: Up to 100MB payloads

**Best for**: Order processing, financial transactions, workflow orchestration, hybrid cloud integration

## Commands and Queries with CQRS

Command Query Responsibility Segregation separates write operations (commands) from read operations (queries), enabling independent optimization and scaling.

### MediatR in .NET 9

MediatR provides in-process mediator pattern implementation with .NET 9 performance improvements:

```csharp
// Command with validation
public record CreateOrderCommand(Guid CustomerId, List<OrderLineDto> Items) 
    : IRequest<Result<Guid>>;

// Fluent validation
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new OrderLineValidator());
    }
}

// Command Handler with event publishing
public class CreateOrderCommandHandler 
    : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _repository;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    
    public async Task<Result<Guid>> Handle(
        CreateOrderCommand request, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Create aggregate
            var order = Order.Create(request.CustomerId, request.Items);
            await _repository.AddAsync(order, cancellationToken);
            
            // Publish domain event
            await _publisher.Publish(
                new OrderCreatedEvent(order.Id, order.CustomerId, order.TotalAmount), 
                cancellationToken);
                
            _logger.LogInformation("Order {OrderId} created for customer {CustomerId}", 
                order.Id, order.CustomerId);
                
            return Result<Guid>.Success(order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order for customer {CustomerId}", request.CustomerId);
            return Result<Guid>.Failure("Failed to create order");
        }
    }
}

// Query with projection
public record GetOrderQuery(Guid OrderId) : IRequest<Result<OrderDto>>;

// Query Handler using read model
public class GetOrderQueryHandler 
    : IRequestHandler<GetOrderQuery, Result<OrderDto>>
{
    private readonly IOrderReadRepository _repository;
    private readonly IMemoryCache _cache;
    
    public async Task<Result<OrderDto>> Handle(
        GetOrderQuery request, 
        CancellationToken cancellationToken)
    {
        // .NET 9: Improved caching with GetOrCreateAsync
        var order = await _cache.GetOrCreateAsync(
            $"order:{request.OrderId}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await _repository.GetByIdAsync(request.OrderId, cancellationToken);
            });
            
        return order is not null 
            ? Result<OrderDto>.Success(order) 
            : Result<OrderDto>.Failure("Order not found");
    }
}
```

### Pipeline Behaviors for Cross-Cutting Concerns

```csharp
// Validation behavior
public class ValidationBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any()) return await next();
        
        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(result => result.Errors)
            .Where(f => f != null)
            .ToList();
            
        if (failures.Count != 0)
            throw new ValidationException(failures);
            
        return await next();
    }
}

// Logging behavior with .NET 9 structured logging
public class LoggingBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        
        _logger.LogInformation("Handling {RequestName}", requestName);
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var response = await next();
            stopwatch.Stop();
            
            _logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", 
                requestName, stopwatch.ElapsedMilliseconds);
                
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error handling {RequestName} after {ElapsedMs}ms", 
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
```

## Common Anti-Patterns and Solutions

### SignalR and CQRS Anti-Pattern

Using SignalR hubs for synchronous command-response patterns defeats the purpose of CQRS and creates a fragile, tightly-coupled system.

**Anti-Pattern: Synchronous Command Over WebSocket**

```csharp
// BAD: Treating SignalR as synchronous RPC
public class OrderHub : Hub
{
    private readonly IOrderService _orderService;
    
    // This is NOT true CQRS - it's synchronous RPC over WebSockets
    public async Task<OrderResult> SubmitOrder(CreateOrderRequest request)
    {
        // Caller waits for complete processing
        return await _orderService.ProcessOrderAsync(request);
    }
}
```

**Problems with this approach:**
- Couples UI latency directly to backend processing time
- WebSocket connection can timeout or drop during processing
- No retry mechanism if connection fails
- Cannot scale command processing independently
- Creates artificial synchronous behavior in an async system

### Recommended Pattern: Async Command Processing with SignalR Notifications

Implement true asynchronous command handling with status updates pushed via SignalR:

```csharp
// GOOD: Async command submission with correlation
public class OrderHub : Hub
{
    private readonly ServiceBusSender _commandSender;
    private readonly ILogger<OrderHub> _logger;
    
    public async Task<CommandAccepted> SubmitOrder(CreateOrderRequest request)
    {
        var correlationId = Guid.NewGuid().ToString();
        
        // Add user to correlation group for status updates
        await Groups.AddToGroupAsync(Context.ConnectionId, correlationId);
        
        // Enqueue command to Service Bus
        var command = new CreateOrderCommand(request.CustomerId, request.Items)
        {
            CorrelationId = correlationId,
            UserId = Context.User?.Identity?.Name
        };
        
        var message = new ServiceBusMessage(JsonSerializer.Serialize(command))
        {
            MessageId = correlationId,
            Subject = "CreateOrder",
            ApplicationProperties = { ["UserId"] = command.UserId }
        };
        
        await _commandSender.SendMessageAsync(message);
        
        _logger.LogInformation("Order command queued with correlation {CorrelationId}", correlationId);
        
        // Return 202 Accepted immediately
        return new CommandAccepted(correlationId, "Order is being processed");
    }
}

// Command handler processes asynchronously
public class CreateOrderCommandHandler : IConsumer<CreateOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly IHubContext<OrderHub> _hubContext;
    private readonly IPublisher _publisher;
    
    public async Task Consume(ConsumeContext<CreateOrderCommand> context)
    {
        var command = context.Message;
        
        try
        {
            // Send processing notification
            await _hubContext.Clients.Group(command.CorrelationId)
                .SendAsync("OrderStatusUpdate", new { Status = "Processing", command.CorrelationId });
            
            // Process business logic
            var order = Order.Create(command.CustomerId, command.Items);
            await _repository.AddAsync(order);
            
            // Publish domain events to update read models
            await _publisher.Publish(new OrderCreatedEvent(order.Id, command.CorrelationId));
            
            // Send completion notification
            await _hubContext.Clients.Group(command.CorrelationId)
                .SendAsync("OrderCompleted", new { order.Id, command.CorrelationId });
        }
        catch (Exception ex)
        {
            // Send error notification
            await _hubContext.Clients.Group(command.CorrelationId)
                .SendAsync("OrderFailed", new { Error = ex.Message, command.CorrelationId });
        }
    }
}

// Read model updater for queries
public class OrderProjection : IEventHandler<OrderCreatedEvent>
{
    private readonly IOrderReadRepository _readRepository;
    
    public async Task Handle(OrderCreatedEvent evt, CancellationToken cancellationToken)
    {
        // Update denormalized read model for fast queries
        await _readRepository.UpsertAsync(evt.OrderId, evt.ToDto(), cancellationToken);
    }
}
```

### Complete Async Flow Architecture

1. **Client → SignalR Hub**: Submit command, receive correlation ID immediately
2. **Hub**: Validate, enqueue to message broker, return 202 Accepted
3. **Command Handler**: Process business logic, emit domain events, update write model
4. **Event Handlers**: Update read models and projections
5. **Notifications**: Push progress via SignalR using correlation ID
6. **Client**: Query read model endpoint for latest state

### Key Principles

**CQRS Does Not Require Async**: CQRS fundamentally means separating command and query models. You can implement commands synchronously in a monolith and still follow CQRS. Asynchrony and message queues are architectural choices for scalability and decoupling, not strict CQRS requirements.

**Eventual Consistency is Common**: When using async commands with CQRS, read models are typically eventually consistent. This is acceptable for most business scenarios and enables independent scaling.

### Implementation Best Practices

**Transport Layer**: Use Rebus or MassTransit with Azure Service Bus for command transport. Reserve SignalR exclusively for pushing notifications to clients, never for inter-service communication.

**Outbox Pattern**: Implement transactional outbox to ensure commands and events are persisted atomically with domain changes, guaranteeing at-least-once delivery without duplicates.

**Idempotency**: Make all command handlers idempotent using command IDs or correlation IDs to safely handle retries and duplicate messages.

**Correlation**: Thread correlation IDs through logs, messages, and SignalR groups to trace operations across distributed components.

**Security**: Never trust hub payloads. Revalidate all commands in handlers. Use per-user or per-correlation groups in SignalR to prevent unauthorized access to status updates.

**User Experience**: Return fast with 202 Accepted, display processing status, push updates via SignalR, and query the read model endpoint for authoritative data.

### Chatty Services Anti-Pattern

Chatty services make excessive synchronous calls between services, creating network overhead, increased latency, and tight coupling.

**Example of Anti-Pattern:**

```csharp
// BAD: Multiple sequential synchronous calls
public async Task<OrderSummary> GetOrderSummaryAsync(Guid orderId)
{
    // Each call waits for the previous to complete
    var order = await _orderClient.GetOrderAsync(orderId);
    var customer = await _customerClient.GetCustomerAsync(order.CustomerId);
    var payment = await _paymentClient.GetPaymentAsync(orderId);
    var shipping = await _shippingClient.GetShippingAsync(orderId);
    var inventory = await _inventoryClient.GetInventoryStatusAsync(orderId);
    
    // High latency: sum of all service calls plus network overhead
    return new OrderSummary(order, customer, payment, shipping, inventory);
}
```

**Problems:**
- Total latency is sum of all service calls
- Single point of failure if any service is unavailable
- Tight coupling between services
- Poor scalability under load
- Difficult to version and evolve independently

### Solution: Materialized Views and CQRS

Implement event-driven read models that aggregate data asynchronously:

```csharp
// GOOD: Query pre-aggregated materialized view
public class OrderSummaryQueryHandler : IRequestHandler<GetOrderSummaryQuery, OrderSummaryDto>
{
    private readonly IOrderSummaryReadRepository _repository;
    private readonly IMemoryCache _cache;
    
    public async Task<OrderSummaryDto> Handle(
        GetOrderSummaryQuery request, 
        CancellationToken cancellationToken)
    {
        // Query optimized read model - single database call
        var summary = await _cache.GetOrCreateAsync(
            $"order-summary:{request.OrderId}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await _repository.GetOrderSummaryAsync(request.OrderId, cancellationToken);
            });
            
        return summary;
    }
}

// Update materialized view via domain events
public class OrderSummaryProjection : 
    IEventHandler<OrderCreatedEvent>,
    IEventHandler<PaymentCompletedEvent>,
    IEventHandler<ShippingUpdatedEvent>
{
    private readonly IOrderSummaryReadRepository _repository;
    
    public async Task Handle(OrderCreatedEvent evt, CancellationToken cancellationToken)
    {
        var summary = new OrderSummaryDto
        {
            OrderId = evt.OrderId,
            CustomerId = evt.CustomerId,
            TotalAmount = evt.TotalAmount,
            Status = "Created",
            CreatedAt = evt.CreatedAt
        };
        
        await _repository.UpsertAsync(summary, cancellationToken);
    }
    
    public async Task Handle(PaymentCompletedEvent evt, CancellationToken cancellationToken)
    {
        await _repository.UpdatePaymentStatusAsync(
            evt.OrderId, 
            "Paid", 
            evt.PaymentMethod, 
            cancellationToken);
    }
    
    public async Task Handle(ShippingUpdatedEvent evt, CancellationToken cancellationToken)
    {
        await _repository.UpdateShippingStatusAsync(
            evt.OrderId, 
            evt.Status, 
            evt.TrackingNumber, 
            cancellationToken);
    }
}
```

### Alternative: Backend for Frontend (BFF) Pattern

For scenarios where real-time consistency is required, implement a BFF that intelligently aggregates:

```csharp
// BFF with parallel calls and circuit breaker
public class OrderBffService
{
    private readonly IOrderClient _orderClient;
    private readonly ICustomerClient _customerClient;
    private readonly IPaymentClient _paymentClient;
    private readonly ILogger<OrderBffService> _logger;
    
    public async Task<OrderSummary> GetOrderSummaryAsync(
        Guid orderId, 
        CancellationToken cancellationToken)
    {
        // Execute calls in parallel to reduce total latency
        var orderTask = _orderClient.GetOrderAsync(orderId, cancellationToken);
        
        // Wait for order to get customer ID
        var order = await orderTask;
        
        // Now parallel calls for remaining data
        var customerTask = _customerClient.GetCustomerAsync(order.CustomerId, cancellationToken);
        var paymentTask = _paymentClient.GetPaymentAsync(orderId, cancellationToken);
        var shippingTask = _shippingClient.GetShippingAsync(orderId, cancellationToken);
        
        await Task.WhenAll(customerTask, paymentTask, shippingTask);
        
        return new OrderSummary(
            order, 
            customerTask.Result, 
            paymentTask.Result, 
            shippingTask.Result);
    }
}
```

### Monitoring Chatty Services

Proactively identify chatty service patterns using Azure Application Insights:

```csharp
// Configure Application Insights with enhanced dependency tracking
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.EnableDependencyTrackingTelemetryModule = true;
    options.EnableRequestTrackingTelemetryModule = true;
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});

// Add custom telemetry for detailed tracking
builder.Services.AddSingleton<ITelemetryInitializer, ServiceCallTelemetryInitializer>();
```

### KQL Queries for Detection

Use Kusto Query Language to identify problematic patterns:

```kusto
// Detect excessive service-to-service calls
dependencies
| where timestamp > ago(1h)
| where type == "HTTP" or type == "Azure Service Bus"
| summarize 
    CallCount = count(), 
    AvgDuration = avg(duration),
    P95Duration = percentile(duration, 95),
    P99Duration = percentile(duration, 99)
    by target, name, operation_Name
| where CallCount > 100
| order by CallCount desc

// Identify sequential call chains (chatty pattern indicator)
requests
| where timestamp > ago(1h)
| join kind=inner (
    dependencies
    | where timestamp > ago(1h)
) on operation_Id
| summarize 
    DependencyCount = dcount(name),
    TotalDuration = sum(duration),
    RequestDuration = max(duration1)
    by operation_Id, operation_Name
| where DependencyCount > 5
| order by DependencyCount desc

// Find slow operations with many dependencies
requests
| where timestamp > ago(24h)
| where duration > 1000 // over 1 second
| join kind=inner (
    dependencies
    | summarize DepCount = count(), DepDuration = sum(duration) by operation_Id
) on operation_Id
| where DepCount > 3
| project 
    timestamp, 
    operation_Name, 
    duration, 
    DepCount, 
    DepDuration,
    ChattyRatio = DepDuration / duration * 100
| order by ChattyRatio desc
```

### Alerting Setup

Create proactive alerts in Azure Monitor:

```csharp
// High dependency call rate alert
{
    "name": "High Service Dependency Calls",
    "description": "Alert when service makes excessive calls to dependencies",
    "severity": 2,
    "evaluationFrequency": "PT5M",
    "windowSize": "PT15M",
    "criteria": {
        "allOf": [{
            "query": "dependencies | where timestamp > ago(15m) | summarize count() by target | where count_ > 500",
            "threshold": 0,
            "operator": "GreaterThan"
        }]
    }
}
```

## Advanced Patterns in .NET 9

### Using Keyed Services for Multi-Tenant Messaging

.NET 9 introduces keyed services for better dependency injection:

```csharp
// Register multiple Service Bus clients for different tenants
builder.Services.AddKeyedSingleton<ServiceBusClient>("tenant-a", (sp, key) =>
    new ServiceBusClient(builder.Configuration["ServiceBus:TenantA:ConnectionString"]));

builder.Services.AddKeyedSingleton<ServiceBusClient>("tenant-b", (sp, key) =>
    new ServiceBusClient(builder.Configuration["ServiceBus:TenantB:ConnectionString"]));

// Use in services
public class MultiTenantOrderPublisher
{
    private readonly IServiceProvider _serviceProvider;
    
    public async Task PublishAsync(string tenantId, Order order)
    {
        var client = _serviceProvider.GetRequiredKeyedService<ServiceBusClient>(tenantId);
        var sender = client.CreateSender("orders");
        await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(order)));
    }
}
```

### TimeProvider for Testable Time-Based Operations

```csharp
// Use TimeProvider abstraction for testable delayed messages
public class ScheduledOrderService
{
    private readonly ServiceBusSender _sender;
    private readonly TimeProvider _timeProvider;
    
    public ScheduledOrderService(ServiceBusSender sender, TimeProvider timeProvider)
    {
        _sender = sender;
        _timeProvider = timeProvider;
    }
    
    public async Task ScheduleOrderProcessingAsync(Order order, TimeSpan delay)
    {
        var message = new ServiceBusMessage(JsonSerializer.Serialize(order))
        {
            ScheduledEnqueueTime = _timeProvider.GetUtcNow().Add(delay)
        };
        
        await _sender.SendMessageAsync(message);
    }
}

// In tests, use FakeTimeProvider
var fakeTime = new FakeTimeProvider();
var service = new ScheduledOrderService(sender, fakeTime);
```

## Conclusion

Building resilient distributed systems requires careful selection of communication patterns. Use asynchronous messaging for decoupling, implement CQRS properly with event-driven read models, avoid chatty service patterns with materialized views, and leverage .NET 9's enhanced performance and features for optimal results.

Monitor your services continuously with Application Insights and KQL queries to detect anti-patterns early. Following these proven patterns will help you build scalable, maintainable microservices architectures on Azure.
