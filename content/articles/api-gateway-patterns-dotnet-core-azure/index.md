---
title: "API Gateway Patterns in .NET Core and Azure"
date: 2025-10-06
slug: api-gateway-patterns-dotnet-core-azure
description: "Different clients (web, mobile, IoT) often require different data shapes and aggregation levels. The BFF pattern addresses this by creating specialized gateways tailored to each client type, optimizing the data format and payload size for specific use cases."
tags: [apis, microservices, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

API gateways serve as the entry point for client applications in distributed architectures, handling request routing, composition, and protocol translation. As systems grow more complex with multiple client types and backend services, choosing the right gateway pattern becomes crucial for maintaining performance, scalability, and developer productivity.

This article explores two powerful API gateway patterns in .NET: the Backend for Frontend (BFF) pattern, which creates client-specific gateways, and GraphQL, which offers flexible, query-driven data fetching. Both patterns address the challenge of efficiently serving diverse clients while maintaining clean architecture and optimal performance.

## Backend for Frontend (BFF) Pattern

### Web BFF Implementation

The web BFF returns detailed, enriched data suitable for desktop browsers with higher bandwidth and processing power:

```csharp
[ApiController]
[Route("api/web/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderServiceClient _orderClient;
    private readonly ICustomerServiceClient _customerClient;
    private readonly IInventoryServiceClient _inventoryClient;
    
    [HttpGet("{id}")]
    public async Task<WebOrderDto> GetOrder(Guid id)
    {
        // Execute parallel calls to improve response time
        var orderTask = _orderClient.GetOrderAsync(id);
        var customerTask = _customerClient.GetCustomerAsync(id);
        var inventoryTask = _inventoryClient.GetInventoryStatusAsync(id);
        
        await Task.WhenAll(orderTask, customerTask, inventoryTask);
        
        return new WebOrderDto
        {
            Order = orderTask.Result,
            Customer = customerTask.Result,
            InventoryStatus = inventoryTask.Result,
            // Include additional rich data for enhanced web UI experience
            RecommendedProducts = await GetRecommendationsAsync(id)
        };
    }
}
```

### Mobile BFF Implementation

The mobile BFF provides optimized, lightweight responses to minimize bandwidth consumption and improve performance on mobile networks:

```csharp
[ApiController]
[Route("api/mobile/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderServiceClient _orderClient;
    
    [HttpGet("{id}")]
    public async Task<MobileOrderDto> GetOrder(Guid id)
    {
        var order = await _orderClient.GetOrderAsync(id);
        
        // Return only essential data to reduce payload size
        return new MobileOrderDto
        {
            Id = order.Id,
            Status = order.Status,
            Total = order.TotalAmount
        };
    }
}
```

## GraphQL as API Gateway

GraphQL provides a flexible alternative to traditional REST-based BFF implementations, allowing clients to request exactly the data they need in a single query.

### Setting Up GraphQL with HotChocolate

First, install the required package:

```bash
dotnet add package HotChocolate.AspNetCore
```

### Defining Query Types

```csharp
public class Query
{
    public async Task<Order> GetOrder(
        [ID] Guid id,
        [Service] IOrderRepository repository)
    {
        return await repository.GetByIdAsync(id);
    }
    
    public async Task<Customer> GetCustomer(
        [ID] Guid id,
        [Service] ICustomerRepository repository)
    {
        return await repository.GetByIdAsync(id);
    }
}
```

### Extending Types for Nested Queries

Type extensions enable nested data fetching, allowing clients to retrieve related entities in a single request:

```csharp
[ExtendObjectType(typeof(Order))]
public class OrderExtensions
{
    public async Task<Customer> GetCustomer(
        [Parent] Order order,
        [Service] ICustomerServiceClient client)
    {
        return await client.GetCustomerAsync(order.CustomerId);
    }
    
    public async Task<List<Product>> GetProducts(
        [Parent] Order order,
        [Service] IProductServiceClient client)
    {
        var productIds = order.OrderLines.Select(l => l.ProductId);
        return await client.GetProductsAsync(productIds);
    }
}
```

### Configuring GraphQL Server

Configure the GraphQL server in `Program.cs` with essential features like data loaders, filtering, and sorting:

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddTypeExtension<OrderExtensions>()
    .AddDataLoader<CustomerByIdDataLoader>()
    .AddFiltering()
    .AddSorting()
    .AddProjections();
```

### Implementing DataLoader to Prevent N+1 Queries

DataLoaders batch and cache requests to prevent the common N+1 query problem in GraphQL:

```csharp
public class CustomerByIdDataLoader : BatchDataLoader<Guid, Customer>
{
    private readonly ICustomerServiceClient _client;
    
    public CustomerByIdDataLoader(
        ICustomerServiceClient client,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options = null)
        : base(batchScheduler, options)
    {
        _client = client;
    }
    
    protected override async Task<IReadOnlyDictionary<Guid, Customer>> 
        LoadBatchAsync(
            IReadOnlyList<Guid> keys, 
            CancellationToken cancellationToken)
    {
        var customers = await _client.GetCustomersByIdsAsync(keys);
        return customers.ToDictionary(c => c.Id);
    }
}
```

## Benefits and Considerations

**BFF Pattern Advantages:**
- Client-specific optimization reduces over-fetching and under-fetching
- Independent evolution of client and backend APIs
- Simplified client-side logic

**GraphQL Advantages:**
- Single endpoint for all data requirements
- Strongly typed schema with built-in documentation
- Efficient data fetching with precise field selection
- Reduced number of API requests

**Trade-offs:**
- BFF requires maintaining multiple gateway implementations
- GraphQL introduces complexity in query optimization and security
- Both patterns require careful monitoring to prevent performance issues
