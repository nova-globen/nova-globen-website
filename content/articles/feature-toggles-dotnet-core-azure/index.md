---
title: "Feature Toggles & Feature Management in .NET and Azure"
date: 2025-10-02
slug: feature-toggles-dotnet-core-azure
description: "Feature toggles (also known as feature flags) enable runtime control of application functionality without deploying new code. This is essential for progressive rollouts, A/B testing, canary releases, and safe deployments in modern cloud-native applications."
tags: [delivery, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Understanding the Feature Ecosystem

### Microsoft.AspNetCore.Http.Features

**Purpose:** Defines low-level HTTP feature interfaces used by the ASP.NET Core hosting and middleware pipeline.

**Think of it as:** "How ASP.NET Core describes what an HTTP request/response can do at the protocol level."

```csharp
// Accessing low-level HTTP features
app.Use(async (context, next) =>
{
    var features = context.Features;
    
    // Connection information
    var connection = features.Get<IHttpConnectionFeature>();
    var remoteIp = connection?.RemoteIpAddress;
    var localPort = connection?.LocalPort;
    
    // Request body control
    var bodyControl = features.Get<IHttpBodyControlFeature>();
    bodyControl?.AllowSynchronousIO = true; // Use with caution
    
    // HTTP/2 or WebSocket upgrade
    var upgradeFeature = features.Get<IHttpUpgradeFeature>();
    var isUpgradeable = upgradeFeature?.IsUpgradableRequest ?? false;
    
    await next();
});
```

**Key feature interfaces:**
- `IHttpRequestFeature` — Raw HTTP request data
- `IHttpResponseFeature` — Raw HTTP response data  
- `IHttpConnectionFeature` — Connection info (remote IP, local port)
- `IHttpUpgradeFeature` — WebSocket or HTTP/2 upgrade handling
- `IHttpRequestBodyDetectionFeature` — Request body detection (.NET 8+)
- `IHttpResponseBodyFeature` — Response body streaming (.NET 6+)

**When to use:** Custom middleware, hosting adapters, or low-level protocol handling. Most application code never needs this.

---

### Microsoft.Extensions.Features

**Completely unrelated** to HTTP — this is a generic feature collection abstraction.

**Purpose:** Provides a reusable feature pattern for any system, not limited to HTTP.

**Think of it as:** "A general-purpose feature system inspired by ASP.NET Core's pattern, but framework-agnostic."

```csharp
// Define custom feature
public interface IMyCustomFeature
{
    string GetValue();
    void SetValue(string value);
}

public class MyCustomFeatureImpl : IMyCustomFeature
{
    private string _value = string.Empty;
    
    public string GetValue() => _value;
    public void SetValue(string value) => _value = value;
}

// Use feature collection
var features = new FeatureCollection();
features.Set<IMyCustomFeature>(new MyCustomFeatureImpl());

var feature = features.Get<IMyCustomFeature>();
feature?.SetValue("Hello, Features!");
Console.WriteLine(feature?.GetValue());
```

**Used by:**
- YARP (Yet Another Reverse Proxy)
- .NET Aspire components
- gRPC services
- Custom extensibility layers

**Not related to:** Feature flags or feature management.

---

## Microsoft.FeatureManagement.AspNetCore

The primary library for implementing feature flags in .NET applications.

**Purpose:** Runtime feature toggles controlled by configuration, enabling progressive rollouts, A/B testing, and safe deployments.

**Key capabilities:**
- Declarative `[FeatureGate]` attributes
- `IFeatureManager` for programmatic checks
- Dynamic configuration via multiple sources
- Targeting filters for user/group-based rollouts
- Variants for A/B testing
- Time window filters
- Custom filters

### Basic Setup (.NET 9)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add feature management
builder.Services.AddFeatureManagement();

var app = builder.Build();

app.MapGet("/api/orders", async (IFeatureManager featureManager) =>
{
    if (await featureManager.IsEnabledAsync("NewOrderApi"))
    {
        return Results.Ok("New order API");
    }
    
    return Results.Ok("Legacy order API");
});

app.Run();
```

```json
// appsettings.json
{
  "FeatureManagement": {
    "NewOrderApi": true,
    "AdvancedReporting": false,
    "BetaUI": true
  }
}
```

### Using Feature Gates

```csharp
// Controller-level feature gate
[ApiController]
[Route("api/[controller]")]
public class OrdersController(
    IFeatureManager featureManager,
    IOrderService orderService,
    ILogger<OrdersController> logger) : ControllerBase
{
    // Action-level feature gate
    [FeatureGate("AdvancedReporting")]
    [HttpGet("advanced-report")]
    public async Task<IActionResult> GetAdvancedReport(CancellationToken ct)
    {
        var report = await orderService.GenerateAdvancedReportAsync(ct);
        return Ok(report);
    }
    
    // Programmatic feature check
    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        CreateOrderRequest request,
        CancellationToken ct)
    {
        var order = await orderService.CreateAsync(request, ct);
        
        // Conditional logic based on feature flag
        if (await featureManager.IsEnabledAsync("NewCheckoutFlow"))
        {
            logger.LogInformation("Using new checkout flow for order {OrderId}", order.Id);
            await orderService.ProcessWithNewCheckoutAsync(order, ct);
        }
        else
        {
            logger.LogInformation("Using legacy checkout flow for order {OrderId}", order.Id);
            await orderService.ProcessWithLegacyCheckoutAsync(order, ct);
        }
        
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }
    
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        var order = await orderService.GetByIdAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }
}
```

### Minimal APIs with Feature Gates

```csharp
// Custom extension for feature-gated endpoints
public static class FeatureEndpointExtensions
{
    public static RouteHandlerBuilder RequireFeature(
        this RouteHandlerBuilder builder, 
        string featureName)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var featureManager = context.HttpContext.RequestServices
                .GetRequiredService<IFeatureManager>();
            
            if (!await featureManager.IsEnabledAsync(featureName))
            {
                return Results.NotFound(new 
                { 
                    message = "This feature is not currently available" 
                });
            }
            
            return await next(context);
        });
    }
}

// Usage
app.MapGet("/api/beta/dashboard", () => Results.Ok("Beta dashboard"))
    .RequireFeature("BetaDashboard");

app.MapPost("/api/orders/advanced", async (
    CreateOrderRequest request,
    IOrderService orderService,
    CancellationToken ct) =>
{
    var order = await orderService.CreateAdvancedAsync(request, ct);
    return Results.Created($"/api/orders/{order.Id}", order);
})
.RequireFeature("AdvancedOrderCreation");
```

---

## Azure App Configuration Integration

Azure App Configuration provides centralized feature flag management with real-time updates.

> Read more: [.NET feature management - Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-app-configuration/feature-management-dotnet-reference)

### Setup with Azure App Configuration

```csharp
// Install packages:
// Microsoft.Azure.AppConfiguration.AspNetCore
// Microsoft.FeatureManagement.AspNetCore

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Connect to Azure App Configuration
builder.Configuration.AddAzureAppConfiguration(options =>
{
    var connectionString = builder.Configuration["AppConfig:ConnectionString"]
        ?? throw new InvalidOperationException("AppConfig connection string not found");
    
    options
        .Connect(connectionString)
        .ConfigureRefresh(refresh =>
        {
            // Refresh configuration when this sentinel key changes
            refresh.Register("Settings:Sentinel", refreshAll: true)
                   .SetCacheExpiration(TimeSpan.FromSeconds(30));
        })
        .UseFeatureFlags(featureFlagOptions =>
        {
            // Cache feature flags for 30 seconds
            featureFlagOptions.CacheExpirationInterval = TimeSpan.FromSeconds(30);
            
            // Optional: Select specific feature flags by label
            featureFlagOptions.Label = builder.Environment.EnvironmentName;
        });
});

// Add Azure App Configuration middleware support
builder.Services.AddAzureAppConfiguration();

// Add feature management
builder.Services.AddFeatureManagement();

var app = builder.Build();

// Enable configuration refresh middleware
app.UseAzureAppConfiguration();

app.Run();
```

### User-Based Targeting with Filters

**Targeting Filter** enables user-specific and group-based feature rollouts.

```json
// Azure App Configuration - Feature flag with targeting
{
  "id": "NewDashboard",
  "description": "New dashboard experience",
  "enabled": true,
  "conditions": {
    "client_filters": [
      {
        "name": "Microsoft.Targeting",
        "parameters": {
          "Audience": {
            "Users": [
              "alice@company.com",
              "bob@company.com"
            ],
            "Groups": [
              {
                "Name": "BetaTesters",
                "RolloutPercentage": 50
              },
              {
                "Name": "InternalUsers",
                "RolloutPercentage": 100
              }
            ],
            "DefaultRolloutPercentage": 10
          }
        }
      }
    ]
  }
}
```

```csharp
// Configure targeting context accessor
builder.Services.AddHttpContextAccessor();

builder.Services.AddFeatureManagement()
    .WithTargeting<CustomTargetingContextAccessor>();

// Custom targeting context implementation
public class CustomTargetingContextAccessor(
    IHttpContextAccessor httpContextAccessor) : ITargetingContextAccessor
{
    public ValueTask<TargetingContext> GetContextAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return new ValueTask<TargetingContext>((TargetingContext)null!);
        }
        
        var user = httpContext.User;
        
        var context = new TargetingContext
        {
            UserId = user.FindFirst(ClaimTypes.Email)?.Value 
                     ?? user.Identity.Name 
                     ?? "anonymous",
            Groups = user.Claims
                .Where(c => c.Type == "groups" || c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList()
        };
        
        return new ValueTask<TargetingContext>(context);
    }
}
```

### Percentage-Based Rollout

```json
// Gradual rollout to percentage of users
{
  "id": "NewRecommendationEngine",
  "enabled": true,
  "conditions": {
    "client_filters": [
      {
        "name": "Microsoft.Percentage",
        "parameters": {
          "Value": 25
        }
      }
    ]
  }
}
```

### Time Window Filter

```json
// Feature available only during specific time window
{
  "id": "BlackFridaySale",
  "enabled": true,
  "conditions": {
    "client_filters": [
      {
        "name": "Microsoft.TimeWindow",
        "parameters": {
          "Start": "2025-11-29T00:00:00Z",
          "End": "2025-12-02T00:00:00Z"
        }
      }
    ]
  }
}
```

---

## Feature Variants for A/B Testing

Feature variants enable A/B testing and multi-variate experiments.

```json
// Feature with variants
{
  "id": "RecommendationAlgorithm",
  "enabled": true,
  "allocation": {
    "percentile": [
      {
        "variant": "Collaborative",
        "from": 0,
        "to": 33
      },
      {
        "variant": "ContentBased",
        "from": 33,
        "to": 66
      },
      {
        "variant": "Hybrid",
        "from": 66,
        "to": 100
      }
    ],
    "seed": "user_id",
    "default_when_disabled": "Collaborative"
  },
  "variants": [
    {
      "name": "Collaborative",
      "configuration_value": {
        "algorithm": "collaborative_filtering",
        "weight": 1.0
      }
    },
    {
      "name": "ContentBased",
      "configuration_value": {
        "algorithm": "content_based",
        "weight": 0.8
      }
    },
    {
      "name": "Hybrid",
      "configuration_value": {
        "algorithm": "hybrid",
        "collaborative_weight": 0.6,
        "content_weight": 0.4
      }
    }
  ]
}
```

```csharp
// Using feature variants
public class RecommendationService(
    IFeatureManager featureManager,
    IHttpContextAccessor httpContextAccessor,
    ILogger<RecommendationService> logger)
{
    public async Task<IEnumerable<Product>> GetRecommendationsAsync(
        string userId,
        CancellationToken ct = default)
    {
        var variant = await featureManager.GetVariantAsync(
            "RecommendationAlgorithm",
            httpContextAccessor.HttpContext,
            ct);
        
        if (variant is null)
        {
            logger.LogInformation("Using default recommendation algorithm for user {UserId}", userId);
            return await GetCollaborativeRecommendationsAsync(userId, ct);
        }
        
        var config = variant.Configuration?.Get<RecommendationConfig>();
        
        logger.LogInformation(
            "Using {Algorithm} algorithm (variant: {Variant}) for user {UserId}",
            config?.Algorithm,
            variant.Name,
            userId);
        
        return config?.Algorithm switch
        {
            "collaborative_filtering" => await GetCollaborativeRecommendationsAsync(userId, ct),
            "content_based" => await GetContentBasedRecommendationsAsync(userId, ct),
            "hybrid" => await GetHybridRecommendationsAsync(userId, config, ct),
            _ => await GetCollaborativeRecommendationsAsync(userId, ct)
        };
    }
    
    private async Task<IEnumerable<Product>> GetHybridRecommendationsAsync(
        string userId,
        RecommendationConfig config,
        CancellationToken ct)
    {
        var collaborative = await GetCollaborativeRecommendationsAsync(userId, ct);
        var contentBased = await GetContentBasedRecommendationsAsync(userId, ct);
        
        // Blend results based on weights
        return BlendRecommendations(
            collaborative,
            contentBased,
            config.CollaborativeWeight ?? 0.5,
            config.ContentWeight ?? 0.5);
    }
    
    private IEnumerable<Product> BlendRecommendations(
        IEnumerable<Product> source1,
        IEnumerable<Product> source2,
        double weight1,
        double weight2)
    {
        // Implementation details...
        return source1.Take(10);
    }
    
    private async Task<IEnumerable<Product>> GetCollaborativeRecommendationsAsync(
        string userId,
        CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return [];
    }
    
    private async Task<IEnumerable<Product>> GetContentBasedRecommendationsAsync(
        string userId,
        CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return [];
    }
}

public record RecommendationConfig
{
    public string? Algorithm { get; init; }
    public double? Weight { get; init; }
    public double? CollaborativeWeight { get; init; }
    public double? ContentWeight { get; init; }
}
```

---

## Custom Feature Filters

Create custom filters for business-specific scenarios.

```csharp
// Ring-based deployment filter
[FilterAlias("RingBased")]
public class RingBasedFeatureFilter(
    IHttpContextAccessor httpContextAccessor,
    ILogger<RingBasedFeatureFilter> logger) : IFeatureFilter
{
    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        var parameters = context.Parameters.Get<RingSettings>();
        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext is null)
        {
            return Task.FromResult(false);
        }
        
        // Get deployment ring from header or claim
        var deploymentRing = httpContext.Request.Headers["X-Deployment-Ring"].FirstOrDefault()
            ?? httpContext.User.FindFirst("deployment_ring")?.Value;
        
        if (string.IsNullOrEmpty(deploymentRing))
        {
            logger.LogDebug("No deployment ring found, feature disabled");
            return Task.FromResult(false);
        }
        
        var ringOrder = new[] { "Canary", "Ring1", "Ring2", "Ring3", "Production" };
        var userRingIndex = Array.IndexOf(ringOrder, deploymentRing);
        var targetRingIndex = Array.IndexOf(ringOrder, parameters.TargetRing);
        
        if (userRingIndex == -1 || targetRingIndex == -1)
        {
            logger.LogWarning("Invalid ring configuration: User={UserRing}, Target={TargetRing}",
                deploymentRing, parameters.TargetRing);
            return Task.FromResult(false);
        }
        
        var isEnabled = userRingIndex <= targetRingIndex;
        
        logger.LogDebug(
            "Ring-based evaluation: User={UserRing}, Target={TargetRing}, Enabled={IsEnabled}",
            deploymentRing,
            parameters.TargetRing,
            isEnabled);
        
        return Task.FromResult(isEnabled);
    }
}

public record RingSettings
{
    public required string TargetRing { get; init; }
}

// Register custom filter
builder.Services.AddFeatureManagement()
    .AddFeatureFilter<RingBasedFeatureFilter>();
```

```json
// Configuration
{
  "FeatureManagement": {
    "NewPaymentGateway": {
      "EnabledFor": [
        {
          "Name": "RingBased",
          "Parameters": {
            "TargetRing": "Ring1"
          }
        }
      ]
    }
  }
}
```

### Geographic-Based Filter

```csharp
[FilterAlias("Geographic")]
public class GeographicFeatureFilter(
    IHttpContextAccessor httpContextAccessor,
    ILogger<GeographicFeatureFilter> logger) : IFeatureFilter
{
    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        var parameters = context.Parameters.Get<GeographicSettings>();
        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext is null)
        {
            return Task.FromResult(false);
        }
        
        // Get country from header, claim, or IP geolocation
        var country = httpContext.Request.Headers["X-Country-Code"].FirstOrDefault()
            ?? httpContext.User.FindFirst("country")?.Value
            ?? "US"; // default
        
        var isEnabled = parameters.AllowedCountries?.Contains(
            country, 
            StringComparer.OrdinalIgnoreCase) ?? false;
        
        logger.LogDebug(
            "Geographic evaluation: Country={Country}, Allowed={Allowed}, Enabled={IsEnabled}",
            country,
            string.Join(",", parameters.AllowedCountries ?? []),
            isEnabled);
        
        return Task.FromResult(isEnabled);
    }
}

public record GeographicSettings
{
    public List<string>? AllowedCountries { get; init; }
}

// Configuration
{
  "FeatureManagement": {
    "EuCompliantFeature": {
      "EnabledFor": [
        {
          "Name": "Geographic",
          "Parameters": {
            "AllowedCountries": ["DE", "FR", "ES", "IT", "NL", "BE"]
          }
        }
      ]
    }
  }
}
```

---

## Safe Rollout Strategies

### Phased Rollout Approach

**Progressive rollout stages:**

1. **Internal Testing** (0-5% of traffic) — Development team only
2. **Beta Users** (5-20%) — Early adopters and power users  
3. **Early Majority** (20-50%) — Expanded user base
4. **General Availability** (50-100%) — All users

```csharp
// Background service for gradual rollout automation
public class FeatureRolloutService(
    IConfigurationRefresher configRefresher,
    ILogger<FeatureRolloutService> logger) : BackgroundService
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(30));
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Feature rollout service started");
        
        while (!stoppingToken.IsCancellationRequested &&
               await _timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Trigger configuration refresh
                await configRefresher.TryRefreshAsync(stoppingToken);
                logger.LogDebug("Feature flags refreshed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing feature flags");
            }
        }
        
        logger.LogInformation("Feature rollout service stopped");
    }
    
    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}

// Registration
builder.Services.AddSingleton(sp =>
{
    var refreshers = sp.GetRequiredService<IEnumerable<IConfigurationRefresher>>();
    return refreshers.First();
});

builder.Services.AddHostedService<FeatureRolloutService>();
```

### Circuit Breaker for New Features

Automatically disable new features if error rates exceed thresholds.

```csharp
public class FeatureCircuitBreakerService(
    IFeatureManager featureManager,
    IDistributedCache cache,
    ILogger<FeatureCircuitBreakerService> logger)
{
    private const string ErrorCountPrefix = "feature:errors:";
    private const int ErrorThreshold = 50;
    private const int TimeWindowMinutes = 5;
    
    public async Task<bool> IsFeatureHealthyAsync(
        string featureName,
        CancellationToken ct = default)
    {
        var isEnabled = await featureManager.IsEnabledAsync(featureName);
        
        if (!isEnabled)
        {
            return false;
        }
        
        var errorCountKey = $"{ErrorCountPrefix}{featureName}";
        var errorCountStr = await cache.GetStringAsync(errorCountKey, ct);
        
        if (int.TryParse(errorCountStr, out var errorCount) && errorCount >= ErrorThreshold)
        {
            logger.LogWarning(
                "Feature {FeatureName} circuit breaker OPEN - error count: {ErrorCount}",
                featureName,
                errorCount);
            return false;
        }
        
        return true;
    }
    
    public async Task RecordErrorAsync(
        string featureName,
        CancellationToken ct = default)
    {
        var errorCountKey = $"{ErrorCountPrefix}{featureName}";
        var errorCountStr = await cache.GetStringAsync(errorCountKey, ct);
        
        var errorCount = int.TryParse(errorCountStr, out var count) ? count + 1 : 1;
        
        await cache.SetStringAsync(
            errorCountKey,
            errorCount.ToString(),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TimeWindowMinutes)
            },
            ct);
        
        logger.LogWarning(
            "Error recorded for feature {FeatureName}: {ErrorCount}/{Threshold}",
            featureName,
            errorCount,
            ErrorThreshold);
    }
    
    public async Task ResetCircuitAsync(
        string featureName,
        CancellationToken ct = default)
    {
        var errorCountKey = $"{ErrorCountPrefix}{featureName}";
        await cache.RemoveAsync(errorCountKey, ct);
        
        logger.LogInformation("Circuit breaker reset for feature {FeatureName}", featureName);
    }
}

// Usage in service
public class PaymentService(
    FeatureCircuitBreakerService circuitBreaker,
    ILogger<PaymentService> logger)
{
    public async Task<PaymentResult> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken ct = default)
    {
        const string featureName = "NewPaymentGateway";
        
        var useNewGateway = await circuitBreaker.IsFeatureHealthyAsync(featureName, ct);
        
        try
        {
            if (useNewGateway)
            {
                logger.LogInformation("Using new payment gateway");
                return await ProcessWithNewGatewayAsync(request, ct);
            }
            
            logger.LogInformation("Using legacy payment gateway");
            return await ProcessWithLegacyGatewayAsync(request, ct);
        }
        catch (Exception ex)
        {
            if (useNewGateway)
            {
                await circuitBreaker.RecordErrorAsync(featureName, ct);
            }
            
            logger.LogError(ex, "Payment processing failed");
            throw;
        }
    }
    
    private async Task<PaymentResult> ProcessWithNewGatewayAsync(
        PaymentRequest request,
        CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return new PaymentResult { Success = true };
    }
    
    private async Task<PaymentResult> ProcessWithLegacyGatewayAsync(
        PaymentRequest request,
        CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return new PaymentResult { Success = true };
    }
}
```

---

## Observability & Monitoring

### Telemetry for Feature Flags

```csharp
// Decorator pattern for observable feature manager
public class ObservableFeatureManager(
    IFeatureManager innerFeatureManager,
    ILogger<ObservableFeatureManager> logger,
    IMeterFactory meterFactory) : IFeatureManager
{
    private readonly Meter _meter = meterFactory.Create("Company.FeatureManagement");
    private readonly Counter<long> _evaluationCounter = meterFactory
        .Create("Company.FeatureManagement")
        .CreateCounter<long>("feature.evaluations", "evaluations", "Number of feature evaluations");
    private readonly Histogram<double> _evaluationDuration = meterFactory
        .Create("Company.FeatureManagement")
        .CreateHistogram<double>("feature.evaluation.duration", "ms", "Feature evaluation duration");
    
    public async Task<bool> IsEnabledAsync(string feature)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            var isEnabled = await innerFeatureManager.IsEnabledAsync(feature);
            sw.Stop();
            
            // Record metrics
            _evaluationCounter.Add(1,
                new KeyValuePair<string, object?>("feature", feature),
                new KeyValuePair<string, object?>("enabled", isEnabled));
            
            _evaluationDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("feature", feature));
            
            logger.LogDebug(
                "Feature {FeatureName} evaluated to {IsEnabled} in {ElapsedMs}ms",
                feature,
                isEnabled,
                sw.ElapsedMilliseconds);
            
            return isEnabled;
        }
        catch (Exception ex)
        {
            sw.Stop();
            
            logger.LogError(ex,
                "Error evaluating feature {FeatureName} after {ElapsedMs}ms",
                feature,
                sw.ElapsedMilliseconds);
            
            // Record error metric
            _evaluationCounter.Add(1,
                new KeyValuePair<string, object?>("feature", feature),
                new KeyValuePair<string, object?>("enabled", false),
                new KeyValuePair<string, object?>("error", true));
            
            throw;
        }
    }
    
    public async Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
    {
        return await innerFeatureManager.IsEnabledAsync(feature, context);
    }
    
    public IAsyncEnumerable<string> GetFeatureNamesAsync()
    {
        return innerFeatureManager.GetFeatureNamesAsync();
    }
}

// Registration using Scrutor
builder.Services.AddFeatureManagement();
builder.Services.Decorate<IFeatureManager, ObservableFeatureManager>();
```

### Application Insights Integration

```csharp
public class ApplicationInsightsFeatureManager(
    IFeatureManager innerFeatureManager,
    TelemetryClient telemetryClient,
    IHttpContextAccessor httpContextAccessor) : IFeatureManager
{
    public async Task<bool> IsEnabledAsync(string feature)
    {
        var sw = Stopwatch.StartNew();
        var isEnabled = await innerFeatureManager.IsEnabledAsync(feature);
        sw.Stop();
        
        var properties = new Dictionary<string, string>
        {
            ["FeatureName"] = feature,
            ["IsEnabled"] = isEnabled.ToString(),
            ["EvaluationTimeMs"] = sw.ElapsedMilliseconds.ToString()
        };
        
        // Add user context if available
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            properties["UserId"] = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                                   ?? "unknown";
            properties["UserEmail"] = httpContext.User.FindFirst(ClaimTypes.Email)?.Value 
                                      ?? "unknown";
        }
        
        telemetryClient.TrackEvent("FeatureEvaluated", properties);
        
        return isEnabled;
    }
    
    public async Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
    {
        return await innerFeatureManager.IsEnabledAsync(feature, context);
    }
    
    public IAsyncEnumerable<string> GetFeatureNamesAsync()
    {
        return innerFeatureManager.GetFeatureNamesAsync();
    }
}
```

### A/B Testing Framework

```csharp
public class ABTestingService(
    IFeatureManager featureManager,
    TelemetryClient telemetryClient,
    ILogger<ABTestingService> logger)
{
    public async Task<T> RunExperimentAsync<T>(
        string experimentName,
        Func<Task<T>> controlGroup,
        Func<Task<T>> treatmentGroup,
        CancellationToken ct = default)
    {
        var featureName = $"Experiment_{experimentName}";
        var isInTreatment = await featureManager.IsEnabledAsync(featureName);
        
        var sw = Stopwatch.StartNew();
        var variant = isInTreatment ? "Treatment" : "Control";
        
        logger.LogInformation(
            "Running experiment {ExperimentName} with variant {Variant}",
            experimentName,
            variant);
        
        try
        {
            T result = isInTreatment 
                ? await treatmentGroup() 
                : await controlGroup();
            
            sw.Stop();
            
            TrackExperimentResult(
                experimentName,
                variant,
                sw.Elapsed,
                success: true);
            
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            
            TrackExperimentResult(
                experimentName,
                variant,
                sw.Elapsed,
                success: false,
                exception: ex);
            
            logger.LogError(ex,
                "Experiment {ExperimentName} failed for variant {Variant}",
                experimentName,
                variant);
            
            throw;
        }
    }
    
    private void TrackExperimentResult(
        string experimentName,
        string variant,
        TimeSpan duration,
        bool success,
        Exception? exception = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["Experiment"] = experimentName,
            ["Variant"] = variant,
            ["Success"] = success.ToString(),
            ["DurationMs"] = duration.TotalMilliseconds.ToString("F2")
        };
        
        if (exception is not null)
        {
            properties["Error"] = exception.Message;
            properties["ExceptionType"] = exception.GetType().Name;
        }
        
        var metrics = new Dictionary<string, double>
        {
            ["Duration"] = duration.TotalMilliseconds
        };
        
        telemetryClient.TrackEvent("ExperimentResult", properties, metrics);
    }
}

// Usage example
public class CheckoutService(
    ABTestingService abTestingService,
    ILegacyCheckoutService legacyCheckout,
    INewCheckoutService newCheckout)
{
    public async Task<CheckoutResult> ProcessCheckoutAsync(
        Cart cart,
        CancellationToken ct = default)
    {
        return await abTestingService.RunExperimentAsync(
            "CheckoutOptimization",
            controlGroup: async () => await legacyCheckout.ProcessAsync(cart, ct),
            treatmentGroup: async () => await newCheckout.ProcessAsync(cart, ct),
            ct);
    }
}
```

### Multivariate Testing

```csharp
public class MultivariateTestingService(
    IFeatureManager featureManager,
    TelemetryClient telemetryClient,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<T> RunMultivariateTestAsync<T>(
        string testName,
        Dictionary<string, Func<Task<T>>> variants,
        CancellationToken ct = default)
    {
        var variant = await featureManager.GetVariantAsync(
            testName,
            httpContextAccessor.HttpContext,
            ct);
        
        var variantName = variant?.Name ?? "Control";
        
        if (!variants.TryGetValue(variantName, out var variantFunc))
        {
            variantFunc = variants["Control"];
        }
        
        var sw = Stopwatch.StartNew();
        
        try
        {
            var result = await variantFunc();
            sw.Stop();
            
            telemetryClient.TrackEvent("MultivariateTestResult",
                new Dictionary<string, string>
                {
                    ["TestName"] = testName,
                    ["Variant"] = variantName,
                    ["Success"] = "true",
                    ["DurationMs"] = sw.ElapsedMilliseconds.ToString()
                });
            
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            
            telemetryClient.TrackEvent("MultivariateTestResult",
                new Dictionary<string, string>
                {
                    ["TestName"] = testName,
                    ["Variant"] = variantName,
                    ["Success"] = "false",
                    ["Error"] = ex.Message,
                    ["DurationMs"] = sw.ElapsedMilliseconds.ToString()
                });
            
            throw;
        }
    }
}

// Usage
var recommendations = await multivariateTestingService.RunMultivariateTestAsync(
    "ProductRecommendations",
    new Dictionary<string, Func<Task<List<Product>>>>
    {
        ["Control"] = async () => await GetBasicRecommendationsAsync(),
        ["Collaborative"] = async () => await GetCollaborativeRecommendationsAsync(),
        ["ContentBased"] = async () => await GetContentBasedRecommendationsAsync(),
        ["Hybrid"] = async () => await GetHybridRecommendationsAsync()
    },
    ct);
```

---

## Monitoring Dashboards

### Azure Monitor KQL Queries

```kusto
// Feature flag evaluation trends
customEvents
| where name == "FeatureEvaluated"
| extend FeatureName = tostring(customDimensions.FeatureName),
         IsEnabled = tobool(customDimensions.IsEnabled)
| summarize EnabledCount = countif(IsEnabled), 
            DisabledCount = countif(not(IsEnabled)),
            TotalEvaluations = count()
            by FeatureName, bin(timestamp, 1h)
| project timestamp, FeatureName, EnabledCount, DisabledCount, 
          EnabledPercentage = 100.0 * EnabledCount / TotalEvaluations
| render timechart

// A/B test performance comparison
customEvents
| where name == "ExperimentResult"
| extend Experiment = tostring(customDimensions.Experiment),
         Variant = tostring(customDimensions.Variant),
         Success = tobool(customDimensions.Success),
         Duration = todouble(customDimensions.DurationMs)
| summarize SuccessRate = 100.0 * countif(Success) / count(),
            AvgDuration = avg(Duration),
            P50Duration = percentile(Duration, 50),
            P95Duration = percentile(Duration, 95),
            P99Duration = percentile(Duration, 99),
            Count = count()
            by Experiment, Variant
| order by Experiment, Variant

// Feature flag error rate by feature
customEvents
| where name == "FeatureEvaluated"
| extend FeatureName = tostring(customDimensions.FeatureName),
         IsEnabled = tobool(customDimensions.IsEnabled),
         HasError = tobool(customDimensions.error)
| summarize ErrorCount = countif(HasError),
            TotalCount = count(),
            ErrorRate = 100.0 * countif(HasError) / count()
            by FeatureName, bin(timestamp, 5m)
| where ErrorRate > 1.0
| render timechart

// User targeting distribution
customEvents
| where name == "FeatureEvaluated"
| extend FeatureName = tostring(customDimensions.FeatureName),
         IsEnabled = tobool(customDimensions.IsEnabled),
         UserId = tostring(customDimensions.UserId)
| where isnotempty(UserId)
| summarize UniqueUsers = dcount(UserId),
            EnabledUsers = dcountif(UserId, IsEnabled),
            DisabledUsers = dcountif(UserId, not(IsEnabled))
            by FeatureName
| project FeatureName, UniqueUsers, EnabledUsers, DisabledUsers,
          EnabledPercentage = 100.0 * EnabledUsers / UniqueUsers
| order by EnabledPercentage desc

// Experiment conversion funnel
customEvents
| where name == "ExperimentResult"
| extend Experiment = tostring(customDimensions.Experiment),
         Variant = tostring(customDimensions.Variant),
         Success = tobool(customDimensions.Success)
| summarize TotalAttempts = count(),
            SuccessfulConversions = countif(Success),
            ConversionRate = 100.0 * countif(Success) / count()
            by Experiment, Variant, bin(timestamp, 1d)
| render columnchart
```

### Prometheus Metrics

```csharp
// Export feature flag metrics for Prometheus
public class PrometheusFeatureMetrics(IMeterFactory meterFactory)
{
    private readonly Meter _meter = meterFactory.Create("feature_management");
    
    private readonly Counter<long> _evaluations = meterFactory
        .Create("feature_management")
        .CreateCounter<long>(
            "feature_flag_evaluations_total",
            "evaluations",
            "Total number of feature flag evaluations");
    
    private readonly Histogram<double> _evaluationDuration = meterFactory
        .Create("feature_management")
        .CreateHistogram<double>(
            "feature_flag_evaluation_duration_seconds",
            "s",
            "Feature flag evaluation duration in seconds");
    
    private readonly Counter<long> _evaluationErrors = meterFactory
        .Create("feature_management")
        .CreateCounter<long>(
            "feature_flag_evaluation_errors_total",
            "errors",
            "Total number of feature flag evaluation errors");
    
    public void RecordEvaluation(string featureName, bool isEnabled, double durationSeconds)
    {
        _evaluations.Add(1,
            new KeyValuePair<string, object?>("feature", featureName),
            new KeyValuePair<string, object?>("enabled", isEnabled));
        
        _evaluationDuration.Record(durationSeconds,
            new KeyValuePair<string, object?>("feature", featureName));
    }
    
    public void RecordError(string featureName)
    {
        _evaluationErrors.Add(1,
            new KeyValuePair<string, object?>("feature", featureName));
    }
}

// Configure OpenTelemetry with Prometheus exporter
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("feature_management")
            .AddPrometheusExporter();
    });

app.MapPrometheusScrapingEndpoint();
```

---

## Best Practices

### 1. Feature Flag Lifecycle Management

```csharp
// Document feature flag metadata
public record FeatureFlagMetadata
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Owner { get; init; }
    public required DateOnly CreatedDate { get; init; }
    public DateOnly? TargetRemovalDate { get; init; }
    public required string JiraTicket { get; init; }
    public List<string> Dependencies { get; init; } = [];
}

// Track feature flag age and cleanup
public class FeatureFlagAuditService(
    IFeatureManager featureManager,
    ILogger<FeatureFlagAuditService> logger)
{
    private static readonly Dictionary<string, FeatureFlagMetadata> FlagMetadata = new()
    {
        ["NewCheckoutFlow"] = new()
        {
            Name = "NewCheckoutFlow",
            Description = "Enable new checkout experience",
            Owner = "payments-team",
            CreatedDate = new DateOnly(2025, 1, 15),
            TargetRemovalDate = new DateOnly(2025, 4, 15),
            JiraTicket = "PAY-1234",
            Dependencies = ["NewPaymentGateway"]
        },
        ["AdvancedReporting"] = new()
        {
            Name = "AdvancedReporting",
            Description = "Enable advanced analytics dashboard",
            Owner = "analytics-team",
            CreatedDate = new DateOnly(2025, 2, 1),
            TargetRemovalDate = new DateOnly(2025, 5, 1),
            JiraTicket = "ANAL-5678",
            Dependencies = []
        }
    };
    
    public async Task<List<FeatureFlagMetadata>> GetStaleFlagsAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var staleFlags = new List<FeatureFlagMetadata>();
        
        await foreach (var flagName in featureManager.GetFeatureNamesAsync().WithCancellation(ct))
        {
            if (FlagMetadata.TryGetValue(flagName, out var metadata))
            {
                var age = today.DayNumber - metadata.CreatedDate.DayNumber;
                
                // Flag is stale if older than 90 days or past target removal date
                if (age > 90 || 
                    (metadata.TargetRemovalDate.HasValue && today >= metadata.TargetRemovalDate.Value))
                {
                    staleFlags.Add(metadata);
                    
                    logger.LogWarning(
                        "Feature flag {FlagName} is stale (age: {Age} days, target removal: {TargetDate})",
                        metadata.Name,
                        age,
                        metadata.TargetRemovalDate);
                }
            }
        }
        
        return staleFlags;
    }
}
```

### 2. Testing Feature Flags

```csharp
// Unit test with mocked feature manager
public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrder_WithNewCheckoutFlow_UsesNewService()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock
            .Setup(x => x.IsEnabledAsync("NewCheckoutFlow"))
            .ReturnsAsync(true);
        
        var newCheckoutMock = new Mock<ICheckoutService>();
        var legacyCheckoutMock = new Mock<ICheckoutService>();
        
        var orderService = new OrderService(
            featureManagerMock.Object,
            newCheckoutMock.Object,
            legacyCheckoutMock.Object);
        
        var order = new Order { Id = Guid.NewGuid() };
        
        // Act
        await orderService.ProcessOrderAsync(order);
        
        // Assert
        newCheckoutMock.Verify(x => x.ProcessAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        legacyCheckoutMock.Verify(x => x.ProcessAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task CreateOrder_WithLegacyCheckoutFlow_UsesLegacyService()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock
            .Setup(x => x.IsEnabledAsync("NewCheckoutFlow"))
            .ReturnsAsync(false);
        
        var newCheckoutMock = new Mock<ICheckoutService>();
        var legacyCheckoutMock = new Mock<ICheckoutService>();
        
        var orderService = new OrderService(
            featureManagerMock.Object,
            newCheckoutMock.Object,
            legacyCheckoutMock.Object);
        
        var order = new Order { Id = Guid.NewGuid() };
        
        // Act
        await orderService.ProcessOrderAsync(order);
        
        // Assert
        legacyCheckoutMock.Verify(x => x.ProcessAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        newCheckoutMock.Verify(x => x.ProcessAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

// Integration test with feature flags
public class OrderApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public OrderApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
    
    [Fact]
    public async Task CreateOrder_WithNewCheckoutEnabled_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureManagement:NewCheckoutFlow"] = "true"
                });
            });
        }).CreateClient();
        
        var request = new CreateOrderRequest
        {
            CustomerId = "test-customer",
            Items = [new OrderItem { ProductId = "product-1", Quantity = 2 }]
        };
        
        // Act
        var response = await client.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<Order>();
        Assert.NotNull(order);
    }
}
```

### 3. Feature Flag Naming Conventions

```csharp
// Consistent naming pattern
public static class FeatureFlags
{
    // Format: <Team>_<Feature>_<Aspect>
    public const string Payments_NewGateway_Enabled = "Payments.NewGateway.Enabled";
    public const string Payments_NewGateway_RolloutPercentage = "Payments.NewGateway.RolloutPercentage";
    
    public const string Analytics_AdvancedReporting_Enabled = "Analytics.AdvancedReporting.Enabled";
    public const string Analytics_RealTimeMetrics_Enabled = "Analytics.RealTimeMetrics.Enabled";
    
    public const string Ui_BetaDashboard_Enabled = "UI.BetaDashboard.Enabled";
    public const string Ui_DarkMode_Enabled = "UI.DarkMode.Enabled";
    
    // Experiments use "Experiment_" prefix
    public const string Experiment_CheckoutOptimization = "Experiment.CheckoutOptimization";
    public const string Experiment_PricingStrategy = "Experiment.PricingStrategy";
}

// Usage with strongly-typed access
public class OrderService(IFeatureManager featureManager)
{
    public async Task ProcessOrderAsync(Order order, CancellationToken ct = default)
    {
        if (await featureManager.IsEnabledAsync(FeatureFlags.Payments_NewGateway_Enabled))
        {
            await ProcessWithNewGatewayAsync(order, ct);
        }
        else
        {
            await ProcessWithLegacyGatewayAsync(order, ct);
        }
    }
    
    private async Task ProcessWithNewGatewayAsync(Order order, CancellationToken ct)
    {
        await Task.Delay(10, ct);
    }
    
    private async Task ProcessWithLegacyGatewayAsync(Order order, CancellationToken ct)
    {
        await Task.Delay(10, ct);
    }
}
```

### 4. Default-Off Strategy

```csharp
// Always default to safe/stable behavior when feature flag evaluation fails
public class SafeFeatureManager(
    IFeatureManager innerFeatureManager,
    ILogger<SafeFeatureManager> logger) : IFeatureManager
{
    public async Task<bool> IsEnabledAsync(string feature)
    {
        try
        {
            return await innerFeatureManager.IsEnabledAsync(feature);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error evaluating feature {FeatureName}, defaulting to disabled",
                feature);
            
            // Fail safe: default to disabled
            return false;
        }
    }
    
    public async Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
    {
        try
        {
            return await innerFeatureManager.IsEnabledAsync(feature, context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error evaluating feature {FeatureName} with context, defaulting to disabled",
                feature);
            
            return false;
        }
    }
    
    public IAsyncEnumerable<string> GetFeatureNamesAsync()
    {
        return innerFeatureManager.GetFeatureNamesAsync();
    }
}

// Register safe wrapper
builder.Services.AddFeatureManagement();
builder.Services.Decorate<IFeatureManager, SafeFeatureManager>();
```

---

## Advanced Scenarios

### Feature Flags with Dependency Injection Scopes

```csharp
// Scoped feature-dependent service registration
public static class FeatureDependentServiceExtensions
{
    public static IServiceCollection AddFeatureDependentService<TService, TImplementation, TFallback>(
        this IServiceCollection services,
        string featureName)
        where TService : class
        where TImplementation : class, TService
        where TFallback : class, TService
    {
        services.AddScoped<TImplementation>();
        services.AddScoped<TFallback>();
        
        services.AddScoped<TService>(sp =>
        {
            var featureManager = sp.GetRequiredService<IFeatureManager>();
            var isEnabled = featureManager.IsEnabledAsync(featureName).GetAwaiter().GetResult();
            
            return isEnabled
                ? sp.GetRequiredService<TImplementation>()
                : sp.GetRequiredService<TFallback>();
        });
        
        return services;
    }
}

// Usage
builder.Services.AddFeatureDependentService<IPaymentService, NewPaymentService, LegacyPaymentService>(
    FeatureFlags.Payments_NewGateway_Enabled);
```

### Feature Flags in Background Services

```csharp
public class ScheduledReportService(
    IFeatureManager featureManager,
    IReportGenerator reportGenerator,
    ILogger<ScheduledReportService> logger) : BackgroundService
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromHours(1));
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scheduled report service started");
        
        while (!stoppingToken.IsCancellationRequested &&
               await _timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!await featureManager.IsEnabledAsync(FeatureFlags.Analytics_AdvancedReporting_Enabled))
            {
                logger.LogDebug("Advanced reporting feature disabled, skipping");
                continue;
            }
            
            try
            {
                logger.LogInformation("Generating scheduled report");
                await reportGenerator.GenerateAndSendReportAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating scheduled report");
            }
        }
        
        logger.LogInformation("Scheduled report service stopped");
    }
    
    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}
```

### Feature Flags in gRPC Services

```csharp
// gRPC interceptor for feature gates
public class FeatureGateInterceptor(
    IFeatureManager featureManager,
    ILogger<FeatureGateInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // Check for feature gate metadata
        var featureGate = context.Method.Split('/').Last();
        var featureName = $"gRPC.{featureGate}";
        
        if (!await featureManager.IsEnabledAsync(featureName))
        {
            logger.LogWarning("gRPC method {Method} is disabled by feature flag", context.Method);
            throw new RpcException(new Status(StatusCode.Unavailable, 
                "This feature is currently unavailable"));
        }
        
        return await continuation(request, context);
    }
}

// Register interceptor
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<FeatureGateInterceptor>();
});
```

---

## Summary

Feature toggles are essential for modern software delivery, enabling:

- **Progressive Rollouts**: Safely deploy features to subsets of users
- **A/B Testing**: Experiment with different implementations
- **Kill Switches**: Quickly disable problematic features
- **Operational Control**: Manage features without deployments
- **Testing in Production**: Validate features with real users

**Key Takeaways:**

1. Use Azure App Configuration for centralized management
2. Implement comprehensive observability for feature flags
3. Follow consistent naming conventions
4. Default to safe behavior on failures
5. Track and remove stale feature flags
6. Test both enabled and disabled code paths
7. Use targeting filters for gradual rollouts
8. Monitor circuit breakers for automatic safety

---

## Resources

### Official Documentation
- [Microsoft Feature Management](https://learn.microsoft.com/en-us/azure/azure-app-configuration/feature-management-dotnet-reference)
- [Azure App Configuration](https://learn.microsoft.com/en-us/azure/azure-app-configuration/)
- [Feature Flags Best Practices](https://learn.microsoft.com/en-us/azure/azure-app-configuration/howto-best-practices)

### Libraries
- [Microsoft.FeatureManagement.AspNetCore](https://www.nuget.org/packages/Microsoft.FeatureManagement.AspNetCore/)
- [Microsoft.Azure.AppConfiguration.AspNetCore](https://www.nuget.org/packages/Microsoft.Azure.AppConfiguration.AspNetCore/)

### Tools
- Azure Portal - App Configuration
- Azure CLI - `az appconfig feature`
- Visual Studio Code - Azure App Configuration extension

### Books & Articles
- *Feature Toggles (aka Feature Flags)* by Martin Fowler
- *Continuous Delivery* by Jez Humble and David Farley
- [LaunchDarkly Feature Flag Guide](https://launchdarkly.com/blog/)
