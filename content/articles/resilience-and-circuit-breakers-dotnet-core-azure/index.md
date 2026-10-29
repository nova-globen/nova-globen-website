---
title: "Resilience in .NET: Building Fault-Tolerant Applications - Circuit Breakers"
date: 2025-10-02
slug: resilience-and-circuit-breakers-dotnet-core-azure
description: "Resilience in software architecture refers to an application's ability to recover from failures and continue functioning. In distributed systems and microservices, this means gracefully handling network issues, service outages, transient errors, and slow responses without cascadi"
tags: [reliability]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Resilience and Circuit Breakers

> Read [Implement the Circuit Breaker pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-circuit-breaker-pattern)

> Resilience in software architecture refers to an application's ability to recover from failures and continue functioning. In microservices, this often means gracefully handling network issues, service outages, or slow responses.

> The Circuit Breaker pattern is a resilience strategy that prevents an application from repeatedly trying operations likely to fail. It "breaks" the circuit after a threshold of failures, temporarily halting requests to give the system time to recover. This protects services from overload and improves overall stability.

> Together, resilience techniques like retries and circuit breakers help build robust, fault-tolerant systems that thrive in unpredictable environments.

### Centralized Resilience Policies

**.NET 9 introduces enhanced Microsoft.Extensions.Resilience:**

> Read about it here: [Build resilient HTTP apps: Key development patterns](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience?tabs=dotnet-cli)

**Standard Resilience Handler (.NET 9):**

```csharp
// Install packages
// Microsoft.Extensions.Http.Resilience
// Microsoft.Extensions.Resilience

// Configure resilience pipeline with standard handler
services.AddHttpClient<IOrderServiceClient, OrderServiceClient>(client =>
{
    client.BaseAddress = new Uri("http://order-service");
})
.AddStandardResilienceHandler(options =>
{
    // Configure retry with exponential backoff
    options.Retry = new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(response => 
                response.StatusCode >= HttpStatusCode.InternalServerError ||
                response.StatusCode == HttpStatusCode.RequestTimeout)
    };
    
    // Configure circuit breaker
    options.CircuitBreaker = new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(30),
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(response => 
                response.StatusCode >= HttpStatusCode.InternalServerError)
    };
    
    // Configure timeout
    options.AttemptTimeout = new HttpTimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(3)
    };
    
    options.TotalRequestTimeout = new HttpTimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
});
```

**Custom Resilience Pipeline with .NET 9 Enhancements:**

```csharp
services.AddResiliencePipeline("my-pipeline", builder =>
{
    // Add retry with exponential backoff and jitter
    builder.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        MaxDelay = TimeSpan.FromSeconds(30),
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutException>(),
        OnRetry = args =>
        {
            Console.WriteLine($"Retry attempt {args.AttemptNumber} after {args.RetryDelay}");
            return ValueTask.CompletedTask;
        }
    });
    
    // Add circuit breaker with state change notifications
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(30),
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutException>(),
        OnOpened = args =>
        {
            Console.WriteLine($"Circuit breaker opened at {DateTime.UtcNow}");
            return ValueTask.CompletedTask;
        },
        OnClosed = args =>
        {
            Console.WriteLine($"Circuit breaker closed at {DateTime.UtcNow}");
            return ValueTask.CompletedTask;
        },
        OnHalfOpened = args =>
        {
            Console.WriteLine($"Circuit breaker half-opened at {DateTime.UtcNow}");
            return ValueTask.CompletedTask;
        }
    });
    
    // Add timeout strategy
    builder.AddTimeout(new TimeoutStrategyOptions
    {
        Timeout = TimeSpan.FromSeconds(10),
        OnTimeout = args =>
        {
            Console.WriteLine($"Operation timed out after {args.Timeout}");
            return ValueTask.CompletedTask;
        }
    });
    
    // Add hedging (parallel requests) - useful for read operations
    builder.AddHedging(new HedgingStrategyOptions
    {
        MaxHedgedAttempts = 2,
        Delay = TimeSpan.FromSeconds(1),
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutException>()
    });
});

// Usage with dependency injection
public class OrderService
{
    private readonly ResiliencePipeline _pipeline;
    private readonly HttpClient _httpClient;
    
    public OrderService(
        ResiliencePipelineProvider<string> pipelineProvider,
        HttpClient httpClient)
    {
        _pipeline = pipelineProvider.GetPipeline("my-pipeline");
        _httpClient = httpClient;
    }
    
    public async Task<Order> GetOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _pipeline.ExecuteAsync(async ct =>
        {
            var response = await _httpClient.GetAsync($"/api/orders/{id}", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<Order>(cancellationToken: ct) 
                ?? throw new InvalidOperationException("Failed to deserialize order");
        }, cancellationToken);
    }
}
```

**Polly v8 Integration:**

**Microsoft.Extensions.Resilience** (and **Microsoft.Extensions.Http.Resilience**) are *built on top of Polly v8* and provide first-class dependency injection, options pattern integration, built-in telemetry, and ready-made pipelines (hedging, timeouts, retries, circuit-breaker) for .NET applications.

Microsoft **deprecated the older HttpClient integration packages** (`Microsoft.Extensions.Http.Polly` and `Polly.Extensions.Http`) in favor of the new **Microsoft.Extensions.Http.Resilience** package—but that's an integration change, not Polly being obsolete.

Use **Microsoft.Extensions.Http.Resilience** for HttpClient scenarios and broader **Microsoft.Extensions.Resilience** when you want opinionated, preconfigured pipelines. Use **Polly v8** directly if you need custom strategies or to apply resilience outside those integrations. ([Microsoft for Developers: "Building resilient cloud services with .NET 8"](https://devblogs.microsoft.com/dotnet/building-resilient-cloud-services-with-dotnet-8/))

**Legacy Polly Example (for reference):**

```csharp
// Older Polly approach - consider migrating to Microsoft.Extensions.Resilience
services.AddHttpClient<IOrderServiceClient, OrderServiceClient>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                Log.Warning($"Retry {retryCount} after {timespan}");
            });
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Log.Warning($"Circuit breaker opened for {duration}");
            },
            onReset: () =>
            {
                Log.Information("Circuit breaker reset");
            });
}
```

### Azure Resilience Features

**Azure Service Bus with Built-in Resilience:**

```csharp
// Azure Service Bus client with automatic retries
var clientOptions = new ServiceBusClientOptions
{
    RetryOptions = new ServiceBusRetryOptions
    {
        Mode = ServiceBusRetryMode.Exponential,
        MaxRetries = 3,
        Delay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        TryTimeout = TimeSpan.FromSeconds(10)
    }
};

var client = new ServiceBusClient(connectionString, clientOptions);

// Combine with resilience pipeline for additional protection
services.AddResiliencePipeline("servicebus-pipeline", builder =>
{
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(30)
    });
});
```

**Azure Storage with Resilience:**

```csharp
// Configure blob client with retry policy
var blobClientOptions = new BlobClientOptions
{
    Retry =
    {
        Mode = RetryMode.Exponential,
        MaxRetries = 3,
        Delay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(30),
        NetworkTimeout = TimeSpan.FromSeconds(100)
    }
};

var blobServiceClient = new BlobServiceClient(connectionString, blobClientOptions);
```

**Azure App Configuration for Dynamic Feature Flags:**

```csharp
// Configure Azure App Configuration with resilience
builder.Configuration.AddAzureAppConfiguration(options =>
{
    options.Connect(connectionString)
        .ConfigureRefresh(refresh =>
        {
            refresh.Register("Settings:Sentinel", refreshAll: true)
                .SetCacheExpiration(TimeSpan.FromSeconds(30));
        })
        .UseFeatureFlags(featureFlagOptions =>
        {
            featureFlagOptions.CacheExpirationInterval = TimeSpan.FromSeconds(30);
        });
});

// Add Azure App Configuration middleware
app.UseAzureAppConfiguration();
```

### Graceful Degradation

Graceful degradation is the ability to continue functioning (possibly with reduced capabilities) when parts of the system fail.

**1. Fallback Responses with Multiple Tiers:**

```csharp
public class ProductService
{
    private readonly IProductRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ProductService> _logger;
    private readonly ResiliencePipeline _pipeline;
    
    public ProductService(
        IProductRepository repository,
        IDistributedCache cache,
        IMemoryCache memoryCache,
        ILogger<ProductService> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _repository = repository;
        _cache = cache;
        _memoryCache = memoryCache;
        _logger = logger;
        _pipeline = pipelineProvider.GetPipeline("product-service");
    }
    
    public async Task<Product> GetProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Try memory cache first (fastest)
        if (_memoryCache.TryGetValue($"product:{id}", out Product? cachedProduct))
        {
            return cachedProduct!;
        }
        
        try
        {
            // Try primary data source with resilience
            var product = await _pipeline.ExecuteAsync(
                async ct => await _repository.GetByIdAsync(id, ct),
                cancellationToken);
            
            // Cache successful result
            _memoryCache.Set($"product:{id}", product, TimeSpan.FromMinutes(5));
            await _cache.SetStringAsync(
                $"product:{id}", 
                JsonSerializer.Serialize(product),
                new DistributedCacheEntryOptions 
                { 
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) 
                },
                cancellationToken);
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary data source failed for product {ProductId}, trying distributed cache", id);
            
            try
            {
                // Fallback to distributed cache
                var cached = await _cache.GetStringAsync($"product:{id}", cancellationToken);
                if (cached != null)
                {
                    var product = JsonSerializer.Deserialize<Product>(cached);
                    if (product != null)
                    {
                        // Update memory cache
                        _memoryCache.Set($"product:{id}", product, TimeSpan.FromMinutes(5));
                        return product;
                    }
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogError(cacheEx, "Distributed cache fallback failed for product {ProductId}", id);
            }
            
            // Return degraded response
            _logger.LogWarning("All data sources failed for product {ProductId}, returning degraded response", id);
            return new Product
            {
                Id = id,
                Name = "Product information temporarily unavailable",
                Description = "We're experiencing technical difficulties. Please try again later.",
                IsAvailable = false,
                Price = 0
            };
        }
    }
}
```

**2. Feature Management with Azure App Configuration:**

Read more at [.NET feature management](https://learn.microsoft.com/en-us/azure/azure-app-configuration/feature-management-dotnet-reference)

```csharp
public class OrderService
{
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<OrderService> _logger;
    private readonly IEmailService _emailService;
    private readonly IMessageBus _messageBus;
    
    public async Task<OrderResult> CreateOrderAsync(
        CreateOrderCommand command, 
        CancellationToken cancellationToken = default)
    {
        var order = new Order(command);
        await _repository.SaveAsync(order, cancellationToken);
        
        // Optional feature: Send confirmation email
        if (await _featureManager.IsEnabledAsync("SendOrderConfirmation"))
        {
            try
            {
                await _emailService.SendConfirmationAsync(order, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log but don't fail the order
                _logger.LogWarning(ex, "Failed to send confirmation email for order {OrderId}", order.Id);
            }
        }
        
        // Optional feature: Real-time notifications
        if (await _featureManager.IsEnabledAsync("RealTimeNotifications"))
        {
            try
            {
                await _messageBus.PublishAsync(
                    new OrderCreatedEvent(order.Id), 
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish order event for {OrderId}", order.Id);
            }
        }
        
        return new OrderResult { Success = true, OrderId = order.Id };
    }
}

// Targeted feature rollout with user targeting
public class FeatureConfiguration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddFeatureManagement()
            .AddFeatureFilter<PercentageFilter>()
            .AddFeatureFilter<TimeWindowFilter>()
            .AddFeatureFilter<TargetingFilter>();
    }
}
```

**3. Bulkhead Pattern with .NET 9:**

From [Microsoft Learn - Bulkhead Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/bulkhead):

>The Bulkhead pattern is a type of application design that is tolerant of failure. In a bulkhead architecture, also known as cell-based architecture, elements of an application are isolated into pools so that if one fails, the others will continue to function. It's named after the sectioned partitions (bulkheads) of a ship's hull. If the hull of a ship is compromised, only the damaged section fills with water, which prevents the ship from sinking.
>
>**Use this pattern to:**
>* Isolate resources used to consume a set of backend services, especially if the application can provide some level of functionality even when one of the services is not responding.
>* Isolate critical consumers from standard consumers.
>* Protect the application from cascading failures.
>
>**This pattern may not be suitable when:**
>* Less efficient use of resources may not be acceptable in the project.
>* The added complexity is not necessary

```csharp
// Isolate resources to prevent cascading failures
services.AddResiliencePipeline("critical-operations", builder =>
{
    builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = 100,
        QueueLimit = 50,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });
    
    builder.AddTimeout(TimeSpan.FromSeconds(5));
});

services.AddResiliencePipeline("non-critical-operations", builder =>
{
    builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = 20,
        QueueLimit = 10,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });
    
    builder.AddTimeout(TimeSpan.FromSeconds(30));
});

// Usage example
public class PaymentProcessor
{
    private readonly ResiliencePipeline _criticalPipeline;
    private readonly ResiliencePipeline _nonCriticalPipeline;
    
    public PaymentProcessor(ResiliencePipelineProvider<string> pipelineProvider)
    {
        _criticalPipeline = pipelineProvider.GetPipeline("critical-operations");
        _nonCriticalPipeline = pipelineProvider.GetPipeline("non-critical-operations");
    }
    
    public async Task ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        // Critical: Process the payment
        await _criticalPipeline.ExecuteAsync(
            async ct => await ProcessPaymentCoreAsync(payment, ct),
            cancellationToken);
        
        // Non-critical: Update analytics (don't block on this)
        _ = _nonCriticalPipeline.ExecuteAsync(
            async ct => await UpdateAnalyticsAsync(payment, ct),
            cancellationToken);
    }
}
```

**4. Fallback Pattern with ML Services:**

```csharp
public class RecommendationService
{
    private readonly IMLModelClient _mlClient;
    private readonly IProductRepository _repository;
    private readonly ILogger<RecommendationService> _logger;
    private readonly ResiliencePipeline _pipeline;
    
    public RecommendationService(
        IMLModelClient mlClient,
        IProductRepository repository,
        ILogger<RecommendationService> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _mlClient = mlClient;
        _repository = repository;
        _logger = logger;
        _pipeline = pipelineProvider.GetPipeline("ml-service");
    }
    
    public async Task<List<Product>> GetRecommendationsAsync(
        Guid userId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try ML-based personalized recommendations
            return await _pipeline.ExecuteAsync(
                async ct => await _mlClient.GetPersonalizedRecommendationsAsync(userId, ct),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML service unavailable for user {UserId}, using fallback", userId);
            
            try
            {
                // Fallback to category-based recommendations
                return await GetCategoryBasedRecommendationsAsync(userId, cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Category-based fallback failed for user {UserId}", userId);
                
                // Last resort: popular products
                return await GetPopularProductsAsync(cancellationToken);
            }
        }
    }
    
    private async Task<List<Product>> GetCategoryBasedRecommendationsAsync(
        Guid userId, 
        CancellationToken cancellationToken)
    {
        var userPreferences = await _repository.GetUserPreferencesAsync(userId, cancellationToken);
        return await _repository.GetProductsByCategoriesAsync(
            userPreferences.PreferredCategories, 
            limit: 10, 
            cancellationToken);
    }
    
    private async Task<List<Product>> GetPopularProductsAsync(CancellationToken cancellationToken)
    {
        return await _repository.GetTopSellingProductsAsync(10, cancellationToken);
    }
}
```

### Load Shedding and Rate Limiting

Load shedding is the practice of deliberately dropping requests when the system is overloaded to maintain stability for accepted requests.

***Applications using rate limiting and load shedding should be carefully load tested and reviewed before deploying.***

**Enhanced Rate Limiting in .NET 9:**

```csharp
// Rate limiting middleware in ASP.NET Core 9
// https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0

services.AddRateLimiter(options =>
{
    // Global rate limiter with user-specific limits
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.Identity?.Name ?? 
                     context.Connection.RemoteIpAddress?.ToString() ?? 
                     "anonymous";
        
        // Premium users get higher limits
        var isPremium = context.User.HasClaim("tier", "premium");
        var permitLimit = isPremium ? 200 : 100;
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });
    
    // Sliding window for API endpoints
    options.AddPolicy("api-sliding", context =>
    {
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            });
    });
    
    // Token bucket for burst handling
    options.AddPolicy("token-bucket", context =>
    {
        return RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: context.User.Identity?.Name ?? "anonymous",
            factory: partition => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                TokensPerPeriod = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });
    
    // Concurrency limiter for resource-intensive operations
    options.AddConcurrencyLimiter("concurrent-requests", options =>
    {
        options.PermitLimit = 50;
        options.QueueLimit = 25;
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString();
        }
        
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please try again later." }, 
            token);
    };
});

app.UseRateLimiter();

// Per-endpoint rate limiting with chaining
[EnableRateLimiting("api-sliding")]
[HttpGet("orders")]
public async Task<IActionResult> GetOrders()
{
    // Implementation
    return Ok();
}

// Disable rate limiting for specific endpoints
[DisableRateLimiting]
[HttpGet("health")]
public IActionResult Health()
{
    return Ok();
}
```

**Advanced Load Shedding with Metrics:**

```csharp
services.AddResiliencePipeline("api-handler", builder =>
{
    builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = 100,
        QueueLimit = 50,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });
});

// Advanced load shedding middleware with health metrics
public class AdaptiveLoadSheddingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<AdaptiveLoadSheddingMiddleware> _logger;
    private readonly IMeterFactory _meterFactory;
    private readonly Counter<long> _rejectedRequestsCounter;
    private readonly Counter<long> _acceptedRequestsCounter;
    private int _currentLoad = 0;
    
    public AdaptiveLoadSheddingMiddleware(
        RequestDelegate next, 
        ILogger<AdaptiveLoadSheddingMiddleware> logger,
        IMeterFactory meterFactory)
    {
        _next = next;
        _semaphore = new SemaphoreSlim(100, 100);
        _logger = logger;
        _meterFactory = meterFactory;
        
        var meter = _meterFactory.Create("LoadShedding");
        _rejectedRequestsCounter = meter.CreateCounter<long>("requests.rejected");
        _acceptedRequestsCounter = meter.CreateCounter<long>("requests.accepted");
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var currentLoad = Interlocked.Increment(ref _currentLoad);
        
        try
        {
            // Check if we're at capacity
            if (!await _semaphore.WaitAsync(TimeSpan.Zero))
            {
                _logger.LogWarning("Server overloaded (load: {CurrentLoad}), shedding request", currentLoad);
                _rejectedRequestsCounter.Add(1, new KeyValuePair<string, object?>("reason", "capacity"));
                
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "30";
                await context.Response.WriteAsJsonAsync(new 
                { 
                    error = "Service temporarily unavailable",
                    retryAfter = 30
                });
                return;
            }
            
            _acceptedRequestsCounter.Add(1);
            
            try
            {
                await _next(context);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _currentLoad);
        }
    }
}
```

**Priority-Based Load Shedding with Quality of Service:**

```csharp
public enum RequestPriority
{
    Critical = 0,
    High = 1,
    Medium = 2,
    Low = 3
}

public class PriorityLoadSheddingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PriorityLoadSheddingMiddleware> _logger;
    private int _currentLoad = 0;
    private const int MaxLoad = 100;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var priority = GetRequestPriority(context);
        var currentLoad = Interlocked.Increment(ref _currentLoad);
        var loadPercentage = (double)currentLoad / MaxLoad;
        
        try
        {
            // Shed requests based on load and priority
            if (ShouldShedRequest(loadPercentage, priority))
            {
                _logger.LogWarning(
                    "Shedding {Priority} priority request at {LoadPercentage:P0} load", 
                    priority, 
                    loadPercentage);
                
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = GetRetryAfterSeconds(priority).ToString();
                
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Service busy",
                    priority = priority.ToString(),
                    retryAfter = GetRetryAfterSeconds(priority)
                });
                return;
            }
            
            await _next(context);
        }
        finally
        {
            Interlocked.Decrement(ref _currentLoad);
        }
    }
    
    private bool ShouldShedRequest(double loadPercentage, RequestPriority priority)
    {
        return priority switch
        {
            RequestPriority.Low => loadPercentage > 0.6,
            RequestPriority.Medium => loadPercentage > 0.8,
            RequestPriority.High => loadPercentage > 0.95,
            RequestPriority.Critical => false, // Never shed critical requests
            _ => loadPercentage > 0.8
        };
    }
    
    private RequestPriority GetRequestPriority(HttpContext context)
    {
        // Priority from header
        if (context.Request.Headers.TryGetValue("X-Priority", out var priorityHeader) &&
            Enum.TryParse<RequestPriority>(priorityHeader, out var priority))
        {
            return priority;
        }
        
        // Priority based on endpoint
        if (context.Request.Path.StartsWithSegments("/api/critical"))
            return RequestPriority.Critical;
        if (context.Request.Path.StartsWithSegments("/api/admin"))
            return RequestPriority.High;
        if (context.Request.Path.StartsWithSegments("/api/reports"))
            return RequestPriority.Low;
        
        // Priority based on user tier
        if (context.User.HasClaim("tier", "premium"))
            return RequestPriority.High;
        
        return RequestPriority.Medium;
    }
    
    private int GetRetryAfterSeconds(RequestPriority priority)
    {
        return priority switch
        {
            RequestPriority.Low => 60,
            RequestPriority.Medium => 30,
            RequestPriority.High => 10,
            RequestPriority.Critical => 5,
            _ => 30
        };
    }
}
```

### Health Checks (Liveness and Readiness)

*Read [Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0)*

**What are Health Checks?**
- **Liveness**: Is the application running? (Should it be restarted?)
- **Readiness**: Can the application serve traffic? (Should it receive requests?)
- **Startup**: Has the application finished initializing? (Is it ready to start accepting health checks?)

**Enhanced Health Checks in .NET 9:**

```csharp
// Add health checks with tags and dependencies
services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Application is running"))
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "database",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "db", "sql", "ready" },
        timeout: TimeSpan.FromSeconds(3))
    .AddAzureServiceBusTopic(
        connectionString: builder.Configuration["ServiceBus:ConnectionString"]!,
        topicName: "order-events",
        name: "servicebus",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "messaging", "ready" })
    .AddRedis(
        redisConnectionString: builder.Configuration["Redis:ConnectionString"]!,
        name: "redis-cache",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "cache", "ready" })
    .AddUrlGroup(
        uri: new Uri("http://customer-service/health/ready"),
        name: "customer-service",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "dependencies", "ready" },
        timeout: TimeSpan.FromSeconds(3))
    .AddAzureBlobStorage(
        connectionString: builder.Configuration["Storage:ConnectionString"]!,
        name: "blob-storage",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "storage", "ready" })
    .AddCheck<StartupHealthCheck>(
        name: "startup",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "startup" });

// Configure health check endpoints with improved responses
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("self"),
    AllowCachingResponses = false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    AllowCachingResponses = false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    AllowCachingResponses = false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Detailed health check with custom response
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(x => new
            {
                name = x.Key,
                status = x.Value.Status.ToString(),
                duration = x.Value.Duration.TotalMilliseconds,
                description = x.Value.Description,
                data = x.Value.Data,
                exception = x.Value.Exception?.Message,
                tags = x.Value.Tags
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };
        
        await context.Response.WriteAsJsonAsync(response);
    }
});
```

**Custom Health Checks:**

```csharp
// Startup health check - ensures app is fully initialized
public class StartupHealthCheck : IHealthCheck
{
    private volatile bool _isReady = false;
    
    public void SetReady() => _isReady = true;
    
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_isReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Application startup complete"));
        }
        
        return Task.FromResult(HealthCheckResult.Unhealthy("Application is still starting up"));
    }
}

// Azure Service Bus health check with detailed diagnostics
public class ServiceBusHealthCheck : IHealthCheck
{
    private readonly ServiceBusClient _client;
    private readonly string _queueName;
    private readonly ILogger<ServiceBusHealthCheck> _logger;
    
    public ServiceBusHealthCheck(
        ServiceBusClient client, 
        string queueName,
        ILogger<ServiceBusHealthCheck> logger)
    {
        _client = client;
        _queueName = queueName;
        _logger = logger;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receiver = _client.CreateReceiver(_queueName);
            
            // Try to peek a message (non-destructive check)
            await receiver.PeekMessageAsync(cancellationToken: cancellationToken);
            
            return HealthCheckResult.Healthy(
                "Service Bus is accessible",
                data: new Dictionary<string, object>
                {
                    ["queue"] = _queueName,
                    ["fullyQualifiedNamespace"] = _client.FullyQualifiedNamespace
                });
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceTimeout)
        {
            _logger.LogWarning(ex, "Service Bus health check timed out");
            return HealthCheckResult.Degraded(
                "Service Bus is slow to respond",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["queue"] = _queueName,
                    ["reason"] = ex.Reason.ToString()
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service Bus health check failed");
            return HealthCheckResult.Unhealthy(
                "Service Bus is not accessible",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["queue"] = _queueName
                });
        }
    }
}

// Memory usage health check
public class MemoryHealthCheck : IHealthCheck
{
    private readonly long _threshold;
    
    public MemoryHealthCheck(long thresholdInBytes = 1024L * 1024L * 1024L) // 1 GB default
    {
        _threshold = thresholdInBytes;
    }
    
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var allocated = GC.GetTotalMemory(forceFullCollection: false);
        var data = new Dictionary<string, object>
        {
            ["allocated"] = allocated,
            ["threshold"] = _threshold,
            ["gen0Collections"] = GC.CollectionCount(0),
            ["gen1Collections"] = GC.CollectionCount(1),
            ["gen2Collections"] = GC.CollectionCount(2)
        };
        
        var status = allocated < _threshold 
            ? HealthStatus.Healthy 
            : HealthStatus.Degraded;
        
        var description = $"Memory usage: {allocated / 1024.0 / 1024.0:N2} MB";
        
        return Task.FromResult(new HealthCheckResult(
            status,
            description,
            data: data));
    }
}
```

**Kubernetes Integration with .NET 9:**

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
  namespace: production
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
        version: v1.0
    spec:
      containers:
      - name: api
        image: order-service:latest
        ports:
        - containerPort: 8080
          name: http
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ASPNETCORE_URLS
          value: "http://+:8080"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        startupProbe:
          httpGet:
            path: /health/startup
            port: 8080
            scheme: HTTP
          initialDelaySeconds: 0
          periodSeconds: 10
          timeoutSeconds: 3
          failureThreshold: 30
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
            scheme: HTTP
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
            scheme: HTTP
          initialDelaySeconds: 10
          periodSeconds: 5
          timeoutSeconds: 3
          failureThreshold: 3
          successThreshold: 1
```

**Azure Container Apps Health Checks:**

```yaml
apiVersion: apps/v1alpha1
kind: ContainerApp
metadata:
  name: order-service
spec:
  configuration:
    ingress:
      external: true
      targetPort: 8080
  template:
    containers:
    - name: order-service
      image: myregistry.azurecr.io/order-service:latest
      resources:
        cpu: 0.5
        memory: 1Gi
      probes:
      - type: Liveness
        httpGet:
          path: /health/live
          port: 8080
        initialDelaySeconds: 30
        periodSeconds: 10
        failureThreshold: 3
      - type: Readiness
        httpGet:
          path: /health/ready
          port: 8080
        initialDelaySeconds: 10
        periodSeconds: 5
        failureThreshold: 3
      - type: Startup
        httpGet:
          path: /health/startup
          port: 8080
        initialDelaySeconds: 0
        periodSeconds: 10
        failureThreshold: 30
```

### Graceful Shutdown

**Enhanced Graceful Shutdown in .NET 9:**

```csharp
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Configure graceful shutdown timeout
        builder.Host.ConfigureHostOptions(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });
        
        // Configure Kestrel for graceful shutdown
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = 100;
            options.Limits.MaxConcurrentUpgradedConnections = 100;
        });
        
        var app = builder.Build();
        
        // Register shutdown handlers
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        
        lifetime.ApplicationStopping.Register(() =>
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Application is stopping - no new requests will be accepted");
            
            // Signal health checks that we're shutting down
            var startupCheck = app.Services.GetService<StartupHealthCheck>();
            // Mark as not ready to stop receiving traffic
        });
        
        lifetime.ApplicationStopped.Register(() =>
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Application stopped gracefully");
        });
        
        await app.RunAsync();
    }
}

// Background service with graceful shutdown
public class OrderProcessor : BackgroundService
{
    private readonly ILogger<OrderProcessor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<OrderMessage> _channel;
    
    public OrderProcessor(
        ILogger<OrderProcessor> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _channel = Channel.CreateUnbounded<OrderMessage>();
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order processor starting");
        
        await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessOrderAsync(message, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Processing canceled, completing in-flight work");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order {OrderId}", message.OrderId);
            }
        }
        
        _logger.LogInformation("Order processor stopped");
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping order processor gracefully");
        
        // Signal no more messages
        _channel.Writer.Complete();
        
        // Wait for in-flight operations to complete (with timeout)
        var completionTask = _channel.Reader.Completion;
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
        
        await Task.WhenAny(completionTask, timeoutTask);
        
        if (!completionTask.IsCompleted)
        {
            _logger.LogWarning("Order processor shutdown timed out with pending work");
        }
        
        await base.StopAsync(cancellationToken);
    }
    
    private async Task ProcessOrderAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        await orderService.ProcessAsync(message, cancellationToken);
    }
}
```

**Graceful Shutdown with Azure Service Bus:**

```csharp
public class ServiceBusProcessor : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<ServiceBusProcessor> _logger;
    
    public ServiceBusProcessor(
        ServiceBusClient client,
        ILogger<ServiceBusProcessor> logger,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        
        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        };
        
        _processor = _client.CreateProcessor(
            configuration["ServiceBus:QueueName"]!,
            options);
        
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Service Bus processor");
        await _processor.StartProcessingAsync(stoppingToken);
        
        // Keep running until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Shutdown requested");
        }
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Service Bus processor gracefully");
        
        // Stop accepting new messages
        await _processor.StopProcessingAsync(cancellationToken);
        
        // Wait a bit for in-flight messages to complete
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        
        await base.StopAsync(cancellationToken);
        
        _logger.LogInformation("Service Bus processor stopped");
    }
    
    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            _logger.LogInformation("Processing message {MessageId}", args.Message.MessageId);
            
            // Process the message
            await ProcessBusinessLogicAsync(args.Message, args.CancellationToken);
            
            // Complete the message
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", args.Message.MessageId);
            
            // Abandon message for retry
            await args.AbandonMessageAsync(args.Message);
        }
    }
    
    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Error in Service Bus processor: {ErrorSource}",
            args.ErrorSource);
        return Task.CompletedTask;
    }
    
    private async Task ProcessBusinessLogicAsync(
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        // Business logic here
        await Task.Delay(100, cancellationToken);
    }
}
```

### Observability and Telemetry

**OpenTelemetry Integration for Resilience:**

```csharp
// Configure OpenTelemetry with resilience metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Microsoft.Extensions.Resilience")
            .AddMeter("Microsoft.Extensions.Http.Resilience")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Microsoft.Extensions.Resilience")
            .AddAzureMonitorTraceExporter(options =>
            {
                options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
            });
    });

// Configure telemetry for resilience pipelines
services.AddResiliencePipeline("monitored-pipeline", (builder, context) =>
{
    var telemetryOptions = new TelemetryOptions
    {
        LoggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>(),
        MeterFactory = context.ServiceProvider.GetRequiredService<IMeterFactory>()
    };
    
    builder
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 10,
            BreakDuration = TimeSpan.FromSeconds(30)
        })
        .ConfigureTelemetry(telemetryOptions);
});
```

### Best Practices Summary

1. **Combine Multiple Resilience Patterns**: Use retries, circuit breakers, timeouts, and bulkheads together for comprehensive protection.

2. **Configure Appropriate Timeouts**: Set realistic timeouts based on your SLAs and use hierarchical timeouts (per-attempt and total).

3. **Implement Health Checks**: Use startup, liveness, and readiness probes to enable orchestrators to manage your application effectively.

4. **Monitor and Alert**: Instrument your resilience strategies with metrics and logging to understand failure patterns.

5. **Test Failure Scenarios**: Use chaos engineering principles to test your resilience strategies under failure conditions.

6. **Graceful Degradation**: Ensure your application can provide reduced functionality when dependencies fail.

7. **Load Shedding**: Protect your system by rejecting requests when overloaded rather than failing completely.

8. **Azure-Native Features**: Leverage built-in resilience features in Azure services like Service Bus, Storage, and App Configuration.

9. **Circuit Breaker State Management**: Monitor circuit breaker states and alert on prolonged open states.

10. **Documentation**: Document your resilience strategies and failure modes so operations teams understand system behavior during incidents.

### Additional Resources

- [Microsoft Learn: Resiliency patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/category/resiliency)
- [Polly documentation](https://www.pollydocs.org/)
- [.NET Resilience documentation](https://learn.microsoft.com/en-us/dotnet/core/resilience/)
- [Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/)
