---
title: "Service Mesh in .NET core"
date: 2025-10-06
slug: service-mesh-dotnet-core-azure
description: "A service mesh is a dedicated infrastructure layer that handles service-to-service communication in microservices architectures. It provides observability, traffic management, security, and resilience without requiring changes to application code. Service meshes use sidecar proxi"
tags: [microservices]
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Key Benefits

- **Traffic Management**: Load balancing, routing, and traffic splitting for canary deployments
- **Security**: Mutual TLS (mTLS) encryption, authentication, and authorization between services
- **Observability**: Distributed tracing, metrics, and logging for service interactions
- **Resilience**: Automatic retries, circuit breaking, and timeout policies

## Popular Service Mesh Options

### Istio
**Full-featured, Kubernetes-native** service mesh offering comprehensive traffic management, security policies, and observability. Best for complex enterprise environments requiring fine-grained control.

### Linkerd
**Lightweight and simpler** alternative focused on ease of use and minimal resource overhead. Ideal for teams wanting service mesh benefits without operational complexity.

### Dapr (Distributed Application Runtime)
**Multi-runtime, cloud-agnostic** framework that works beyond Kubernetes across VMs, edge, and cloud environments. Provides building blocks for microservices including service invocation, pub/sub, state management, and more.

## .NET Aspire: Cloud-Native Orchestration

[.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) is Microsoft's opinionated stack for building observable, production-ready cloud-native applications with .NET 9. While not a traditional service mesh, Aspire provides service discovery, orchestration, and observability features that complement or serve as alternatives to service meshes in certain scenarios.

**Key Aspire Features:**
- Automatic service discovery and health checks
- Built-in OpenTelemetry integration for distributed tracing
- Local development orchestration with production-ready patterns
- Azure integration for seamless cloud deployment

```csharp
// .NET Aspire App Host (Program.cs)
var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");
var messaging = builder.AddRabbitMQ("messaging");

var customerService = builder.AddProject<Projects.CustomerService>("customer-service")
    .WithReference(cache);

var orderService = builder.AddProject<Projects.OrderService>("order-service")
    .WithReference(cache)
    .WithReference(messaging)
    .WithReference(customerService); // Automatic service discovery

builder.Build().Run();
```

## Dapr with .NET 9 and C# 13

Dapr integrates seamlessly with .NET 9, leveraging modern C# features and Azure services.

### Service Invocation and Pub/Sub

```csharp
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add Dapr services
builder.Services.AddControllers().AddDapr();
builder.Services.AddDaprClient();

var app = builder.Build();

app.MapControllers();
app.MapSubscribeHandler(); // Dapr pub/sub subscriptions

app.Run();

[ApiController]
[Route("api/[controller]")]
public class OrderController(DaprClient daprClient, IOrderService orderService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        // Save order using primary constructor parameter
        var order = await orderService.CreateAsync(request);
        
        // Publish event via Dapr pub/sub to Azure Service Bus
        await daprClient.PublishEventAsync(
            "servicebus-pubsub",
            "order-events",
            new OrderCreatedEvent 
            { 
                OrderId = order.Id,
                CustomerId = request.CustomerId,
                CreatedAt = DateTime.UtcNow
            });
            
        // Invoke customer service with resilience (automatic retries)
        var customer = await daprClient.InvokeMethodAsync<Customer>(
            HttpMethod.Get,
            "customer-service",
            $"api/customers/{request.CustomerId}");
            
        return Ok(new { order, customer });
    }
    
    // Dapr pub/sub subscription
    [Topic("servicebus-pubsub", "payment-events")]
    [HttpPost("payment-processed")]
    public async Task<IActionResult> HandlePaymentProcessed(PaymentProcessedEvent @event)
    {
        await orderService.UpdatePaymentStatusAsync(@event.OrderId, @event.Status);
        return Ok();
    }
}

// Event records using C# 13 features
public record OrderCreatedEvent
{
    required public string OrderId { get; init; }
    required public string CustomerId { get; init; }
    required public DateTime CreatedAt { get; init; }
}

public record PaymentProcessedEvent(string OrderId, string Status);
```

### State Management with Azure Cosmos DB

```csharp
public class OrderService(DaprClient daprClient)
{
    private const string StateStoreName = "cosmosdb-state";
    
    public async Task<Order> CreateAsync(CreateOrderRequest request)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = request.CustomerId,
            Items = request.Items,
            Status = OrderStatus.Pending
        };
        
        // Save to Cosmos DB via Dapr state management
        await daprClient.SaveStateAsync(StateStoreName, order.Id, order);
        
        return order;
    }
    
    public async Task<Order?> GetAsync(string orderId)
    {
        return await daprClient.GetStateAsync<Order>(StateStoreName, orderId);
    }
}
```

### Azure Integration with Dapr Components

**Dapr Component for Azure Service Bus (components/servicebus-pubsub.yaml):**

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: servicebus-pubsub
spec:
  type: pubsub.azure.servicebus.topics
  version: v1
  metadata:
  - name: connectionString
    secretKeyRef:
      name: azure-servicebus-connection
  - name: consumerID
    value: "order-service"
```

**Dapr Component for Azure Cosmos DB (components/cosmosdb-state.yaml):**

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: cosmosdb-state
spec:
  type: state.azure.cosmosdb
  version: v1
  metadata:
  - name: url
    value: "https://your-account.documents.azure.com:443/"
  - name: masterKey
    secretKeyRef:
      name: cosmosdb-key
  - name: database
    value: "orders-db"
  - name: collection
    value: "orders"
```

## Choosing the Right Approach

- **Use .NET Aspire** for cloud-native .NET applications requiring orchestration, observability, and Azure integration with minimal setup
- **Use Dapr** for polyglot microservices, multi-cloud scenarios, or when you need portable building blocks across different hosting environments
- **Use Traditional Service Meshes (Istio/Linkerd)** for Kubernetes-native applications requiring advanced traffic management and security policies at the infrastructure level

For comprehensive microservices patterns and best practices, visit [nova-globen.se](https://nova-globen.se).

## Additional Resources

- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Dapr Documentation](https://docs.dapr.io/)
- [Azure Service Mesh Options](https://learn.microsoft.com/azure/aks/servicemesh-about)
- [Microservices Architecture Patterns at nova-globen.se](https://nova-globen.se)


*Ready to modernize your microservices architecture? Explore more patterns and tutorials at [nova-globen.se](https://nova-globen.se/blog/).*
