---
title: "Performance Optimization in .NET and Azure"
date: 2025-10-06
slug: performance-optimization-in-dotnet-core-and-azure
description: "Performance optimization is critical for building scalable, responsive applications. In distributed .NET systems, optimization strategies span multiple layers: from HTTP response caching and in-memory caching to distributed caching and database query optimization. With .NET 9 and"
tags: [performance, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

This article explores proven caching strategies, database optimization techniques, and modern approaches to building high-performance .NET applications in cloud environments.

## Best Practices and Decision Tree

**Caching:**
- Use HybridCache for most scenarios in .NET 9
- Implement cache invalidation strategies from the start
- Use distributed caching (Redis) for session state and multi-instance scenarios
- Consider output caching for API endpoints

**Database:**
- Always use AsNoTracking() for read-only queries
- Project to DTOs to minimize data transfer
- Use compiled queries for frequently executed queries
- Leverage read replicas for read-heavy workloads
- Use ExecuteUpdate/ExecuteDelete for bulk operations

**Azure-Specific:**
- Enable connection pooling for Azure SQL
- Use Azure Redis Cache for distributed caching
- Monitor with Application Insights
- Consider Azure SQL elastic pools for variable workloads
- Use Azure Front Door for global caching and CDN

**General:**
- Profile before optimizing - measure, don't guess
- Use async/await consistently throughout the stack
- Implement proper error handling and retry policies
- Monitor cache hit rates and query performance
- Document performance SLAs and regularly test against them

## Caching Strategies

Effective caching reduces latency, decreases database load, and improves overall application throughput. Different caching strategies serve different purposes in your application architecture.

### 1. Response Caching

Response caching stores entire HTTP responses on the server or client, ideal for static or semi-static content:

```csharp
[ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "id" })]
[HttpGet("{id}")]
public async Task<IActionResult> GetProduct(Guid id)
{
    var product = await _productService.GetByIdAsync(id);
    return Ok(product);
}

// Configure response caching middleware in Program.cs
builder.Services.AddResponseCaching();

var app = builder.Build();

app.UseResponseCaching();
app.UseHttpCacheHeaders(); // Optional: for more control
```

**Best for:** Public APIs, product catalogs, static content

### 2. Distributed Cache with Azure Redis

Distributed caching with Redis provides a shared cache across multiple application instances, essential for scalable cloud applications:

```csharp
public class CachedProductService : IProductService
{
    private readonly IProductService _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedProductService> _logger;
    
    public CachedProductService(
        IProductService inner,
        IDistributedCache cache,
        ILogger<CachedProductService> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }
    
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"product:{id}";
        
        // Attempt to retrieve from cache
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogDebug("Cache hit for product {ProductId}", id);
            return JsonSerializer.Deserialize<Product>(cached);
        }
        
        // Cache miss - fetch from source
        _logger.LogDebug("Cache miss for product {ProductId}", id);
        var product = await _inner.GetByIdAsync(id, ct);
        
        if (product == null)
            return null;
        
        // Store in cache with expiration policies
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };
        
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(product),
            options,
            ct);
            
        return product;
    }
    
    public async Task InvalidateCacheAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"product:{id}";
        await _cache.RemoveAsync(cacheKey, ct);
        _logger.LogInformation("Invalidated cache for product {ProductId}", id);
    }
}

// Configure Azure Redis Cache in Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "ProductCache:";
});

// Decorator pattern for caching
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.Decorate<IProductService, CachedProductService>();
```

**Best for:** Multi-instance applications, session state, distributed systems

### 3. HybridCache (.NET 9)

HybridCache is the newest caching solution in .NET 9, combining local in-memory caching with distributed caching for optimal performance:

```csharp
public class ProductService
{
    private readonly HybridCache _cache;
    private readonly IProductRepository _repository;
    private readonly ILogger<ProductService> _logger;
    
    public ProductService(
        HybridCache cache,
        IProductRepository repository,
        ILogger<ProductService> logger)
    {
        _cache = cache;
        _repository = repository;
        _logger = logger;
    }
    
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(
            $"product:{id}",
            async cancel => await _repository.GetByIdAsync(id, cancel),
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10), // L2 (distributed) expiration
                LocalCacheExpiration = TimeSpan.FromMinutes(1) // L1 (in-memory) expiration
            },
            tags: ["products"],
            ct);
    }
    
    public async Task<List<Product>> GetByCategoryAsync(
        string category, 
        CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(
            $"products:category:{category}",
            async cancel => await _repository.GetByCategoryAsync(category, cancel),
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: ["products", $"category:{category}"],
            ct);
    }
    
    public async Task InvalidateProductAsync(Guid id, CancellationToken ct = default)
    {
        await _cache.RemoveAsync($"product:{id}", ct);
        _logger.LogInformation("Invalidated cache for product {ProductId}", id);
    }
    
    // Tag-based invalidation
    public async Task InvalidateAllProductsAsync(CancellationToken ct = default)
    {
        await _cache.RemoveByTagAsync("products", ct);
        _logger.LogInformation("Invalidated all product caches");
    }
}

// Configure HybridCache in Program.cs
builder.Services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 1024 * 1024; // 1MB max per entry
    options.MaximumKeyLength = 512;
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});

// Optional: Configure Redis as L2 cache for HybridCache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "HybridCache:";
});
```

**Benefits of HybridCache:**
- Two-tier caching (L1: in-memory, L2: distributed) for best performance
- Built-in stampede protection prevents cache avalanche
- Tag-based invalidation for complex cache management
- Simpler API compared to IDistributedCache
- Optimized serialization with support for custom serializers

**Best for:** High-traffic APIs, modern cloud-native applications, scenarios requiring both speed and consistency

## Database Optimization

Database performance is often the bottleneck in distributed systems. Proper optimization strategies can dramatically improve application responsiveness.

### 1. Read Replicas with Azure SQL

Separate read and write workloads using Azure SQL Database read replicas:

```csharp
// Configure separate contexts for reads and writes
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<Order> Orders { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Product> Products { get; set; }
}

public class ReadOnlyDbContext : AppDbContext
{
    public ReadOnlyDbContext(DbContextOptions<ReadOnlyDbContext> options)
        : base(options)
    {
        // Disable change tracking for read-only operations
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        Database.SetCommandTimeout(30);
    }
}

// Register both contexts in Program.cs
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Primary");
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(60);
    });
});

builder.Services.AddDbContext<ReadOnlyDbContext>(options =>
{
    var readReplicaConnectionString = builder.Configuration
        .GetConnectionString("ReadReplica");
    options.UseSqlServer(readReplicaConnectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
        sqlOptions.CommandTimeout(30);
    });
});

// Use appropriate context based on operation
public class OrderService
{
    private readonly AppDbContext _writeContext;
    private readonly ReadOnlyDbContext _readContext;
    
    public OrderService(AppDbContext writeContext, ReadOnlyDbContext readContext)
    {
        _writeContext = writeContext;
        _readContext = readContext;
    }
    
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            CreatedAt = DateTime.UtcNow
        };
        
        _writeContext.Orders.Add(order);
        await _writeContext.SaveChangesAsync();
        
        return order;
    }
    
    public async Task<List<OrderDto>> GetOrdersAsync(Guid customerId)
    {
        // Use read replica for queries
        return await _readContext.Orders
            .Where(o => o.CustomerId == customerId)
            .Select(o => new OrderDto
            {
                Id = o.Id,
                TotalAmount = o.TotalAmount,
                Status = o.Status
            })
            .ToListAsync();
    }
}
```

### 2. Query Optimization Techniques

Optimize EF Core queries to minimize data transfer and improve performance:

```csharp
public class OrderQueryService
{
    private readonly ReadOnlyDbContext _context;
    
    public OrderQueryService(ReadOnlyDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<OrderDto>> GetPendingOrdersAsync(CancellationToken ct = default)
    {
        return await _context.Orders
            .AsNoTracking() // Disable change tracking for read-only queries
            .Where(o => o.Status == OrderStatus.Pending)
            .Select(o => new OrderDto // Project to DTO to reduce data transfer
            {
                Id = o.Id,
                CustomerName = o.Customer.Name,
                CustomerEmail = o.Customer.Email,
                TotalAmount = o.TotalAmount,
                ItemCount = o.OrderLines.Count
            })
            .AsSplitQuery() // Prevent cartesian explosion with related data
            .ToListAsync(ct);
    }
    
    public async Task<OrderDetailsDto> GetOrderDetailsAsync(
        Guid orderId, 
        CancellationToken ct = default)
    {
        // Use explicit loading for conditional related data
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        
        if (order == null)
            return null;
        
        // Load related data only if needed
        var orderLines = await _context.Entry(order)
            .Collection(o => o.OrderLines)
            .Query()
            .Include(ol => ol.Product)
            .ToListAsync(ct);
        
        return new OrderDetailsDto
        {
            Order = order,
            Lines = orderLines
        };
    }
    
    // Use indexes effectively
    public async Task<List<Order>> SearchOrdersAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        // Ensure index exists: CREATE INDEX IX_Orders_CreatedAt ON Orders(CreatedAt)
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= startDate && o.CreatedAt < endDate)
            .OrderByDescending(o => o.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
    }
}
```

### 3. Compiled Queries

Compiled queries reduce query compilation overhead for frequently executed queries:

```csharp
public class OrderRepository
{
    private readonly AppDbContext _context;
    
    // Define compiled queries as static readonly fields
    private static readonly Func<AppDbContext, Guid, Task<Order?>> _getOrderById =
        EF.CompileAsyncQuery((AppDbContext context, Guid id) =>
            context.Orders
                .Include(o => o.OrderLines)
                .ThenInclude(ol => ol.Product)
                .FirstOrDefault(o => o.Id == id));
    
    private static readonly Func<AppDbContext, Guid, IAsyncEnumerable<Order>> _getOrdersByCustomer =
        EF.CompileAsyncQuery((AppDbContext context, Guid customerId) =>
            context.Orders
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedAt));
    
    private static readonly Func<AppDbContext, OrderStatus, DateTime, Task<int>> _getOrderCount =
        EF.CompileAsyncQuery((AppDbContext context, OrderStatus status, DateTime since) =>
            context.Orders
                .Where(o => o.Status == status && o.CreatedAt >= since)
                .Count());
    
    public OrderRepository(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _getOrderById(_context, id);
    }
    
    public async Task<List<Order>> GetByCustomerAsync(
        Guid customerId, 
        CancellationToken ct = default)
    {
        return await _getOrdersByCustomer(_context, customerId)
            .ToListAsync(ct);
    }
    
    public async Task<int> GetCountAsync(
        OrderStatus status,
        DateTime since,
        CancellationToken ct = default)
    {
        return await _getOrderCount(_context, status, since);
    }
}
```

### 4. Batch Operations

Minimize round trips to the database by batching operations:

```csharp
public class BulkOrderService
{
    private readonly AppDbContext _context;
    private readonly ILogger<BulkOrderService> _logger;
    
    public BulkOrderService(AppDbContext context, ILogger<BulkOrderService> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    // Standard EF Core batching
    public async Task CreateOrdersAsync(
        List<Order> orders, 
        CancellationToken ct = default)
    {
        // EF Core 9 automatically batches these inserts
        _context.Orders.AddRange(orders);
        await _context.SaveChangesAsync(ct);
        
        _logger.LogInformation("Created {Count} orders", orders.Count);
    }
    
    // Bulk update with ExecuteUpdate (EF Core 7+)
    public async Task<int> UpdateOrderStatusAsync(
        List<Guid> orderIds,
        OrderStatus newStatus,
        CancellationToken ct = default)
    {
        var affected = await _context.Orders
            .Where(o => orderIds.Contains(o.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.Status, newStatus),
                ct);
        
        _logger.LogInformation(
            "Updated status for {Count} orders to {Status}",
            affected, newStatus);
        
        return affected;
    }
    
    // Bulk delete with ExecuteDelete (EF Core 7+)
    public async Task<int> DeleteOldOrdersAsync(
        DateTime beforeDate,
        CancellationToken ct = default)
    {
        var deleted = await _context.Orders
            .Where(o => o.CreatedAt < beforeDate && o.Status == OrderStatus.Completed)
            .ExecuteDeleteAsync(ct);
        
        _logger.LogInformation("Deleted {Count} old orders", deleted);
        
        return deleted;
    }
    
    // Use BulkExtensions for very large datasets
    public async Task BulkInsertLargeDatasetAsync(
        List<Order> orders,
        CancellationToken ct = default)
    {
        // Install: dotnet add package EFCore.BulkExtensions
        await _context.BulkInsertAsync(orders, cancellationToken: ct);
        
        _logger.LogInformation("Bulk inserted {Count} orders", orders.Count);
    }
}
```

### 5. Materialized view pattern
> Materialized read models can make pagination much faster, but they help indirectly. They reduce the per-page work (joins/aggregates/shaping), so your keyset/seek query touches fewer pages and uses a tight covering index. The seek/cursor pattern still does the heavy lifting for scalable pagination; materialization makes each seek cheaper.
>
>[Materialized view patterns, trade-offs, and when to use each on SQL Server/Azure SQL and .NET](https://nova-globen.se/blog/materialized-view-pattern-pagination-and-reporting/)

## Additional Performance Optimizations

### Connection Pooling

Ensure proper connection pooling configuration for Azure SQL:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
        sqlOptions.CommandTimeout(30);
        // Connection pooling is enabled by default
        // Adjust via connection string: "...;Max Pool Size=100;Min Pool Size=10;"
    });
});
```

### Async All The Way

Always use async methods throughout your application stack:

```csharp
// Good - async throughout
public async Task<OrderDto> GetOrderAsync(Guid id)
{
    var order = await _repository.GetByIdAsync(id);
    var customer = await _customerService.GetAsync(order.CustomerId);
    return MapToDto(order, customer);
}

// Bad - blocking on async
public OrderDto GetOrder(Guid id)
{
    var order = _repository.GetByIdAsync(id).Result; // Deadlock risk!
    return MapToDto(order);
}
```

Read more about [synchronous vs asynchronous in .NET core and how decide](https://nova-globen.se/blog/synchronous-vs-asynchronous-in-dotnet-core/)

### Output Caching

Output caching is a new middleware in .NET 9+ that caches entire HTTP responses:

```csharp
// Program.cs
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder
        .Expire(TimeSpan.FromMinutes(1))
        .Tag("api"));
    
    options.AddPolicy("products", builder => builder
        .Expire(TimeSpan.FromMinutes(10))
        .Tag("products")
        .SetVaryByQuery("category", "page"));
});

var app = builder.Build();
app.UseOutputCache();

// Controller
[OutputCache(PolicyName = "products")]
[HttpGet]
public async Task<IActionResult> GetProducts(
    [FromQuery] string? category,
    [FromQuery] int page = 1)
{
    var products = await _productService.GetProductsAsync(category, page);
    return Ok(products);
}

// Invalidate cache by tag
app.MapPost("/api/products", async (
    Product product,
    IProductService service,
    IOutputCacheStore cache) =>
{
    await service.CreateAsync(product);
    await cache.EvictByTagAsync("products", default);
    return Results.Created($"/api/products/{product.Id}", product);
});
```

## Monitoring and Profiling

Always measure performance to identify bottlenecks:

```csharp
// Use Application Insights
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    options.EnableAdaptiveSampling = true;
    options.EnableQuickPulseMetricStream = true;
});

// Custom metrics
public class OrderService
{
    private readonly TelemetryClient _telemetry;
    
    public async Task CreateOrderAsync(Order order)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await _repository.AddAsync(order);
            await _repository.SaveChangesAsync();
            
            _telemetry.TrackMetric("OrderCreation.Duration", stopwatch.ElapsedMilliseconds);
            _telemetry.TrackEvent("OrderCreated", new Dictionary<string, string>
            {
                ["OrderId"] = order.Id.ToString(),
                ["Amount"] = order.TotalAmount.ToString()
            });
        }
        catch (Exception ex)
        {
            _telemetry.TrackException(ex);
            throw;
        }
    }
}
```
