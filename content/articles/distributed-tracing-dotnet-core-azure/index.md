---
title: "Distributed Tracing in .NET Core and Azure"
date: 2025-10-06
slug: distributed-tracing-dotnet-core-azure
description: "In distributed systems where a single user request flows through multiple microservices, databases, and external APIs, understanding system behavior becomes exponentially more complex. Traditional logging provides isolated snapshots from individual components, but fails to reveal"
tags: [reliability, microservices, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

This article explores distributed tracing in .NET applications, covering built-in support through the Activity API, integration with OpenTelemetry for vendor-neutral instrumentation, and deployment to Azure with Application Insights for production monitoring.

## What is Distributed Tracing?

Distributed tracing is a diagnostic technique that tracks requests as they traverse different components of a distributed system, including microservices, databases, message queues, and external APIs.

**Key benefits:**
- **Pinpoint failures:** Identify exactly which service or component caused an error
- **Measure latency:** Understand time spent in each service and operation
- **Visualize dependencies:** Map how services interact and depend on each other
- **Identify bottlenecks:** Find slow queries, API calls, or processing steps

**Example request flow:**
1. Load balancer receives HTTP request
2. API Gateway routes to Order Service
3. Order Service calls Customer Service via HTTP
4. Order Service queries PostgreSQL database
5. Order Service publishes event to Azure Service Bus
6. Payment Service processes payment via external API
7. Response returns to client

Distributed tracing captures this entire journey as a single **trace** composed of multiple **spans** (individual operations), allowing you to see timing, errors, and context at each step.

## Core Concepts

**Trace:** The complete journey of a request through your system, identified by a unique trace ID.

**Span:** A single operation within a trace (e.g., HTTP request, database query, message processing). Each span has:
- Unique span ID
- Parent span ID (forming a tree structure)
- Start time and duration
- Operation name and attributes
- Status (success, error)

**Context Propagation:** Passing trace and span IDs between services, typically via HTTP headers (W3C Trace Context standard).

**Tags/Attributes:** Key-value pairs attached to spans for filtering and analysis (e.g., `http.method=POST`, `db.statement=SELECT * FROM orders`).

## Built-in Support in .NET

.NET has native distributed tracing support through the `System.Diagnostics` namespace, centered around the `Activity` class.

### Activity API

The Activity API represents operations in your application and automatically handles trace context propagation:

```csharp
using System.Diagnostics;

public class OrderService
{
    // Create an ActivitySource for your service
    private static readonly ActivitySource ActivitySource = 
        new("CompanyName.OrderService", "1.0.0");
    
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        // Start a new activity (span)
        using var activity = ActivitySource.StartActivity(
            "CreateOrder",
            ActivityKind.Server);
        
        // Add tags for filtering and analysis
        activity?.SetTag("order.customerId", request.CustomerId);
        activity?.SetTag("order.itemCount", request.Items.Count);
        activity?.SetTag("order.totalAmount", request.TotalAmount);
        
        try
        {
            // Business logic
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                Items = request.Items,
                TotalAmount = request.TotalAmount,
                CreatedAt = DateTime.UtcNow
            };
            
            await _repository.SaveAsync(order);
            
            // Add result information
            activity?.SetTag("order.id", order.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            
            return order;
        }
        catch (Exception ex)
        {
            // Record error information
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
    
    public async Task<Customer> GetCustomerAsync(Guid customerId)
    {
        // Create a child span for external HTTP call
        using var activity = ActivitySource.StartActivity(
            "GetCustomer",
            ActivityKind.Client);
        
        activity?.SetTag("customer.id", customerId);
        
        // HTTP client automatically propagates trace context
        var response = await _httpClient.GetAsync($"/api/customers/{customerId}");
        
        activity?.SetTag("http.status_code", (int)response.StatusCode);
        
        return await response.Content.ReadFromJsonAsync<Customer>();
    }
}
```

### Automatic Context Propagation

.NET automatically propagates trace context using W3C Trace Context headers:

```
traceparent: 00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01
tracestate: congo=t61rcWkgMzE
```

**Key components:**
- `00`: Version
- `0af7651916cd43dd8448eb211c80319c`: Trace ID (128-bit)
- `b7ad6b7169203331`: Parent span ID (64-bit)
- `01`: Trace flags (sampled)

### ActivitySource Registration

Register your ActivitySource to enable tracing:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register ActivitySource - must match the name used in your code
builder.Services.AddSingleton(new ActivitySource("CompanyName.OrderService"));

// Built-in instrumentation for ASP.NET Core
builder.Services.AddHttpClient();
```

## OpenTelemetry in .NET

OpenTelemetry is the CNCF standard for observability, providing vendor-neutral instrumentation that works with multiple backends.

### Installing OpenTelemetry

```bash
# Core packages
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http

# Database instrumentation
dotnet add package OpenTelemetry.Instrumentation.EntityFrameworkCore
dotnet add package OpenTelemetry.Instrumentation.SqlClient

# Exporters
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol  # OTLP (Jaeger, etc.)
dotnet add package Azure.Monitor.OpenTelemetry.Exporter  # Application Insights
```

### Basic Configuration

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "order-service",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        // Add instrumentation for ASP.NET Core
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = context => 
                !context.Request.Path.StartsWithSegments("/health");
        })
        
        // Add instrumentation for HttpClient
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
            options.FilterHttpRequestMessage = request =>
                !request.RequestUri?.PathAndQuery.Contains("/health") ?? true;
        })
        
        // Add instrumentation for Entity Framework Core
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.SetDbStatementForStoredProcedure = true;
        })
        
        // Add instrumentation for SQL Client
        .AddSqlClientInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.RecordException = true;
        })
        
        // Register your custom ActivitySource
        .AddSource("CompanyName.*")
        
        // Export to OTLP endpoint (Jaeger)
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                builder.Configuration["OpenTelemetry:Endpoint"] ?? 
                "http://localhost:4317");
        }));

var app = builder.Build();
```

### Advanced Configuration with Multiple Exporters

Export traces to multiple backends simultaneously:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("order-service")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "ecommerce",
            ["service.team"] = "platform"
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            
            // Enrich spans with additional context
            options.Enrich = (activity, eventName, rawObject) =>
            {
                if (eventName == "OnStartActivity" && rawObject is HttpRequest request)
                {
                    activity.SetTag("http.user_agent", request.Headers.UserAgent.ToString());
                    activity.SetTag("http.client_ip", request.HttpContext.Connection.RemoteIpAddress?.ToString());
                }
            };
        })
        
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSqlClientInstrumentation()
        .AddSource("CompanyName.*")
        
        // Export to Jaeger for development
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://jaeger:4317");
        })
        
        // Export to Azure Monitor for production
        .AddAzureMonitorTraceExporter(options =>
        {
            options.ConnectionString = 
                builder.Configuration["ApplicationInsights:ConnectionString"];
        })
        
        // Set sampling - only trace 10% of requests
        .SetSampler(new TraceIdRatioBasedSampler(0.1)));
```

### Custom Instrumentation

Create detailed spans for business operations:

```csharp
public class OrderProcessingService
{
    private readonly ActivitySource _activitySource;
    private readonly IOrderRepository _repository;
    private readonly IPaymentService _paymentService;
    private readonly IInventoryService _inventoryService;
    
    public OrderProcessingService(
        ActivitySource activitySource,
        IOrderRepository repository,
        IPaymentService paymentService,
        IInventoryService inventoryService)
    {
        _activitySource = activitySource;
        _repository = repository;
        _paymentService = paymentService;
        _inventoryService = inventoryService;
    }
    
    public async Task<Order> ProcessOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(
            "ProcessOrder",
            ActivityKind.Internal);
        
        activity?.SetTag("order.customerId", request.CustomerId);
        activity?.SetTag("order.itemCount", request.Items.Count);
        activity?.SetTag("order.totalAmount", request.TotalAmount);
        
        try
        {
            // Create order
            var order = await CreateOrderInternalAsync(request, cancellationToken);
            activity?.AddEvent(new ActivityEvent("OrderCreated"));
            
            // Reserve inventory
            using (var inventoryActivity = _activitySource.StartActivity(
                "ReserveInventory",
                ActivityKind.Client))
            {
                await _inventoryService.ReserveAsync(
                    order.Items.Select(i => new InventoryReservation
                    {
                        ProductId = i.ProductId,
                        Quantity = i.Quantity
                    }),
                    cancellationToken);
                
                inventoryActivity?.AddEvent(new ActivityEvent("InventoryReserved"));
            }
            
            // Process payment
            using (var paymentActivity = _activitySource.StartActivity(
                "ProcessPayment",
                ActivityKind.Client))
            {
                paymentActivity?.SetTag("payment.amount", request.TotalAmount);
                paymentActivity?.SetTag("payment.currency", "USD");
                
                var paymentResult = await _paymentService.ProcessPaymentAsync(
                    new PaymentRequest
                    {
                        OrderId = order.Id,
                        Amount = request.TotalAmount,
                        PaymentMethod = request.PaymentMethod
                    },
                    cancellationToken);
                
                paymentActivity?.SetTag("payment.transactionId", paymentResult.TransactionId);
                paymentActivity?.AddEvent(new ActivityEvent("PaymentProcessed"));
            }
            
            // Update order status
            order.Status = OrderStatus.Confirmed;
            await _repository.UpdateAsync(order, cancellationToken);
            
            activity?.SetTag("order.id", order.Id);
            activity?.SetTag("order.status", order.Status.ToString());
            activity?.SetStatus(ActivityStatusCode.Ok);
            
            return order;
        }
        catch (InsufficientInventoryException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Insufficient inventory");
            activity?.RecordException(ex);
            activity?.AddEvent(new ActivityEvent("OrderFailed", 
                tags: new ActivityTagsCollection
                {
                    ["error.type"] = "InsufficientInventory"
                }));
            throw;
        }
        catch (PaymentFailedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Payment failed");
            activity?.RecordException(ex);
            activity?.AddEvent(new ActivityEvent("OrderFailed",
                tags: new ActivityTagsCollection
                {
                    ["error.type"] = "PaymentFailed"
                }));
            
            // Compensate: release inventory
            await _inventoryService.ReleaseAsync(order.Id, cancellationToken);
            
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

### Adding Baggage for Cross-Service Context

Baggage allows you to pass custom data across service boundaries:

```csharp
public class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Add baggage that will propagate to downstream services
        Baggage.SetBaggage("correlation.id", context.TraceIdentifier);
        Baggage.SetBaggage("user.id", context.User.FindFirst("sub")?.Value ?? "anonymous");
        Baggage.SetBaggage("tenant.id", context.Request.Headers["X-Tenant-ID"].ToString());
        
        await _next(context);
    }
}

// Downstream service can access baggage
public class CustomerService
{
    public async Task<Customer> GetCustomerAsync(Guid id)
    {
        using var activity = ActivitySource.StartActivity("GetCustomer");
        
        // Access baggage from upstream service
        var userId = Baggage.GetBaggage("user.id");
        var tenantId = Baggage.GetBaggage("tenant.id");
        
        activity?.SetTag("user.id", userId);
        activity?.SetTag("tenant.id", tenantId);
        
        // Business logic
    }
}
```

## Application Insights Integration

Azure Application Insights provides managed distributed tracing with powerful query and visualization capabilities.

### Configuration

```csharp
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    options.EnableAdaptiveSampling = true;
    options.EnableQuickPulseMetricStream = true;
});

// Or using OpenTelemetry exporter
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddAzureMonitorTraceExporter(options =>
        {
            options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        }));
```

### Application Map

Application Insights automatically generates a visual map of service dependencies based on distributed traces, showing:
- Service topology
- Call volumes
- Success rates
- Average response times
- Failed dependency calls

### Querying Traces with KQL

Use Kusto Query Language to analyze traces:

```kusto
// Find slow requests
requests
| where timestamp > ago(1h)
| where duration > 1000  // Over 1 second
| project timestamp, name, duration, resultCode, operation_Id
| order by duration desc

// Analyze dependencies by service
dependencies
| where timestamp > ago(24h)
| summarize 
    Count = count(),
    AvgDuration = avg(duration),
    P95Duration = percentile(duration, 95),
    FailureRate = 100.0 * countif(success == false) / count()
    by target, type
| order by Count desc

// Find requests with failed dependencies
let failedDeps = dependencies
| where timestamp > ago(1h) and success == false
| distinct operation_Id;
requests
| where timestamp > ago(1h)
| where operation_Id in (failedDeps)
| project timestamp, name, operation_Id, duration, resultCode
```

## Visualizing Traces with Jaeger

Jaeger is an open-source distributed tracing platform ideal for development and self-hosted production.

### Running Jaeger Locally

```bash
# Run Jaeger all-in-one with Docker
docker run -d --name jaeger \
  -e COLLECTOR_OTLP_ENABLED=true \
  -p 4317:4317 \
  -p 4318:4318 \
  -p 16686:16686 \
  jaegertracing/all-in-one:latest

# Access Jaeger UI at http://localhost:16686
```

### Configure OpenTelemetry to Export to Jaeger

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("CompanyName.*")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
            options.Protocol = OtlpExportProtocol.Grpc;
        }));
```

### Environment Variable Configuration

```bash
# Alternative: Configure via environment variables
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=order-service
export OTEL_RESOURCE_ATTRIBUTES=service.namespace=ecommerce,deployment.environment=development
```

## Sampling Strategies

Tracing every request can be expensive. Use sampling to balance observability with performance:

### Always On Sampler (Development)

```csharp
.SetSampler(new AlwaysOnSampler())
```

### Ratio-Based Sampler (Production)

```csharp
// Sample 10% of traces
.SetSampler(new TraceIdRatioBasedSampler(0.1))
```

### Parent-Based Sampler

```csharp
// Sample based on parent decision, fallback to 10%
.SetSampler(new ParentBasedSampler(
    new TraceIdRatioBasedSampler(0.1)))
```

### Custom Sampler

```csharp
public class SmartSampler : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        // Always sample errors
        if (samplingParameters.Tags.Any(t => 
            t.Key == "error" && (bool)t.Value))
        {
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }
        
        // Always sample slow requests
        var duration = samplingParameters.Tags.FirstOrDefault(t => 
            t.Key == "duration").Value as TimeSpan?;
        if (duration > TimeSpan.FromSeconds(1))
        {
            return new SamplingResult(SamplingDecision.RecordAndSample);
        }
        
        // Sample 5% of normal requests
        return new TraceIdRatioBasedSampler(0.05)
            .ShouldSample(samplingParameters);
    }
}

.SetSampler(new SmartSampler())
```

## Best Practices

**Instrumentation:**
- Use meaningful span names that describe the operation
- Add relevant tags for filtering (customer ID, transaction ID, etc.)
- Record exceptions with full context
- Use ActivityKind appropriately (Server, Client, Internal, Producer, Consumer)

**Performance:**
- Implement sampling in production to control costs
- Filter health check endpoints from tracing
- Avoid logging sensitive data in spans (PII, credentials, etc.)
- Use async/await to prevent blocking

**Context Propagation:**
- Ensure HTTP clients propagate trace context automatically
- Manually propagate context for message queues and non-HTTP protocols
- Use baggage sparingly (it's transmitted with every request)

**Organization:**
- Use consistent service naming across your organization
- Group related services with service.namespace attribute
- Tag spans with deployment.environment for filtering
- Include service version for deployment tracking

**Azure-Specific:**
- Use Application Insights for production monitoring
- Enable adaptive sampling to control costs
- Configure custom metrics and alerts based on traces
- Use Application Map to visualize dependencies
- Leverage Log Analytics for advanced querying

**Troubleshooting:**
- Always trace error paths with detailed context
- Add custom events for important business milestones
- Include compensation/rollback operations in traces
- Monitor trace export failures and sampling decisions

By implementing comprehensive distributed tracing, teams gain deep visibility into system behavior, enabling faster debugging, performance optimization, and confident deployment of complex distributed applications.
