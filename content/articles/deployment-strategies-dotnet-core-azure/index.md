---
title: "Deployment Strategies in .NET Core and Azure"
date: 2025-10-02
slug: deployment-strategies-dotnet-core-azure
description: "Deploying distributed systems to production requires sophisticated strategies that minimize downtime, reduce risk, and enable rapid rollback when issues arise. Modern deployment strategies like blue-green and canary deployments have evolved from the painful lessons of traditional"
tags: [delivery, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

This article explores proven deployment strategies for .NET microservices, including blue-green deployments for instant rollback capability, canary deployments for gradual risk mitigation, and automated metrics guards that detect issues before they impact users.

## Blue-Green Deployment

### What is Blue-Green Deployment?

Blue-green deployment maintains two identical production environments: Blue (currently serving traffic) and Green (idle or running the new version). After deploying and validating the new version in Green, traffic switches instantly. If issues arise, switching back to Blue provides immediate rollback.

**Benefits:**
- Zero-downtime deployments
- Instant rollback capability
- Full testing in production-like environment before cutover
- Simple to understand and implement

**Trade-offs:**
- Requires double the infrastructure resources
- Database migrations need special handling
- Stateful applications require session migration

### Azure App Service Implementation

Azure App Service provides built-in deployment slots for blue-green deployments:

```bash
# Create a deployment slot (Green environment)
az webapp deployment slot create \
    --name my-app \
    --resource-group my-rg \
    --slot green \
    --configuration-source my-app

# Deploy new version to green slot
az webapp deployment source config-zip \
    --name my-app \
    --resource-group my-rg \
    --slot green \
    --src app.zip

# Warm up the green slot
az webapp deployment slot start \
    --name my-app \
    --resource-group my-rg \
    --slot green

# Run smoke tests on green slot before switching
curl https://my-app-green.azurewebsites.net/health
curl https://my-app-green.azurewebsites.net/api/health/ready

# Perform the swap (instant traffic switch)
az webapp deployment slot swap \
    --name my-app \
    --resource-group my-rg \
    --slot green \
    --target-slot production

# If issues detected, rollback by swapping back
az webapp deployment slot swap \
    --name my-app \
    --resource-group my-rg \
    --slot green \
    --target-slot production
```

### Configuration with Azure CLI and ARM Templates

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
  "contentVersion": "1.0.0.0",
  "resources": [
    {
      "type": "Microsoft.Web/sites/slots",
      "apiVersion": "2022-03-01",
      "name": "[concat(parameters('siteName'), '/green')]",
      "location": "[parameters('location')]",
      "properties": {
        "serverFarmId": "[parameters('appServicePlanId')]",
        "siteConfig": {
          "appSettings": [
            {
              "name": "DEPLOYMENT_SLOT",
              "value": "green"
            },
            {
              "name": "APP_VERSION",
              "value": "[parameters('appVersion')]"
            }
          ]
        }
      }
    }
  ]
}
```

### Kubernetes Implementation

For containerized .NET applications on Azure Kubernetes Service (AKS):

```yaml
# Blue deployment (current production)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service-blue
  namespace: production
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
      version: blue
  template:
    metadata:
      labels:
        app: order-service
        version: blue
    spec:
      containers:
      - name: api
        image: myacr.azurecr.io/order-service:v1.0
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: DEPLOYMENT_VERSION
          value: "blue"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
---
# Green deployment (new version)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service-green
  namespace: production
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
      version: green
  template:
    metadata:
      labels:
        app: order-service
        version: green
    spec:
      containers:
      - name: api
        image: myacr.azurecr.io/order-service:v2.0
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: DEPLOYMENT_VERSION
          value: "green"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
---
# Service initially routes to blue
apiVersion: v1
kind: Service
metadata:
  name: order-service
  namespace: production
spec:
  selector:
    app: order-service
    version: blue  # Change to 'green' to switch traffic
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
  type: LoadBalancer
```

### Health Check Endpoints for Validation

Implement comprehensive health checks to validate deployments before switching:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHealthCheckService _healthCheckService;
    private readonly ILogger<HealthController> _logger;
    
    public HealthController(
        IConfiguration configuration,
        IHealthCheckService healthCheckService,
        ILogger<HealthController> logger)
    {
        _configuration = configuration;
        _healthCheckService = healthCheckService;
        _logger = logger;
    }
    
    [HttpGet("deployment")]
    public IActionResult GetDeploymentInfo()
    {
        return Ok(new
        {
            Environment = _configuration["DEPLOYMENT_SLOT"] ?? "unknown",
            Version = _configuration["APP_VERSION"] ?? "unknown",
            Hostname = Environment.MachineName,
            Timestamp = DateTime.UtcNow,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        });
    }
    
    [HttpGet("live")]
    public IActionResult Liveness()
    {
        // Basic liveness check - is the application running?
        return Ok(new { status = "alive" });
    }
    
    [HttpGet("ready")]
    public async Task<IActionResult> Readiness()
    {
        var report = await _healthCheckService.CheckHealthAsync();
        
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };
        
        if (report.Status == HealthStatus.Healthy)
        {
            return Ok(response);
        }
        
        _logger.LogWarning(
            "Readiness check failed. Status: {Status}",
            report.Status);
        
        return StatusCode(503, response);
    }
}

// Configure health checks in Program.cs
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "database",
        timeout: TimeSpan.FromSeconds(5))
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "cache",
        timeout: TimeSpan.FromSeconds(5))
    .AddAzureServiceBusTopic(
        builder.Configuration.GetConnectionString("ServiceBus")!,
        "orders",
        name: "servicebus",
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<ExternalApiHealthCheck>("external-api");

// Custom health check for external dependencies
public class ExternalApiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalApiHealthCheck> _logger;
    
    public ExternalApiHealthCheck(
        IHttpClientFactory httpClientFactory,
        ILogger<ExternalApiHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("external-api");
            var response = await client.GetAsync("/health", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("External API is responsive");
            }
            
            return HealthCheckResult.Degraded(
                $"External API returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External API health check failed");
            return HealthCheckResult.Unhealthy(
                "External API is unreachable",
                ex);
        }
    }
}
```

### Automated Smoke Testing

Run automated tests against the green environment before switching:

```csharp
public class BlueGreenSmokeTests
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BlueGreenSmokeTests> _logger;
    
    public async Task<bool> RunSmokeTestsAsync(string slotUrl)
    {
        _logger.LogInformation("Running smoke tests against {Url}", slotUrl);
        
        var tests = new Func<Task<bool>>[]
        {
            () => TestHealthEndpoint(slotUrl),
            () => TestApiResponsiveness(slotUrl),
            () => TestDatabaseConnectivity(slotUrl),
            () => TestCacheConnectivity(slotUrl),
            () => TestCriticalBusinessFlow(slotUrl)
        };
        
        var results = await Task.WhenAll(tests.Select(test => test()));
        var allPassed = results.All(r => r);
        
        if (allPassed)
        {
            _logger.LogInformation("All smoke tests passed");
        }
        else
        {
            _logger.LogError("Some smoke tests failed");
        }
        
        return allPassed;
    }
    
    private async Task<bool> TestHealthEndpoint(string baseUrl)
    {
        try
        {
            var response = await _client.GetAsync($"{baseUrl}/health/ready");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health endpoint test failed");
            return false;
        }
    }
    
    private async Task<bool> TestCriticalBusinessFlow(string baseUrl)
    {
        try
        {
            // Test a critical path like creating an order
            var orderRequest = new
            {
                customerId = Guid.NewGuid(),
                items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } }
            };
            
            var response = await _client.PostAsJsonAsync(
                $"{baseUrl}/api/orders",
                orderRequest);
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical business flow test failed");
            return false;
        }
    }
}
```

## Canary Deployment

### What is Canary Deployment?

Canary deployment gradually rolls out changes to a small subset of users, monitors key metrics, and progressively increases traffic if the deployment proves successful. If issues are detected, traffic is immediately redirected back to the stable version.

**Benefits:**
- Early detection of issues with minimal user impact
- Real-world validation before full rollout
- Data-driven deployment decisions
- Reduces blast radius of failures

**Typical Stages:**
1. Deploy to 5% of infrastructure (canary)
2. Monitor for 5-10 minutes
3. If metrics healthy, increase to 25%
4. Continue to 50%, 75%, 100%
5. Rollback immediately if issues detected

### Kubernetes with Argo Rollouts

Argo Rollouts extends Kubernetes with advanced deployment strategies:

```bash
# Install Argo Rollouts
kubectl create namespace argo-rollouts
kubectl apply -n argo-rollouts -f https://github.com/argoproj/argo-rollouts/releases/latest/download/install.yaml

# Install kubectl plugin
curl -LO https://github.com/argoproj/argo-rollouts/releases/latest/download/kubectl-argo-rollouts-linux-amd64
chmod +x kubectl-argo-rollouts-linux-amd64
sudo mv kubectl-argo-rollouts-linux-amd64 /usr/local/bin/kubectl-argo-rollouts
```

```yaml
apiVersion: argoproj.io/v1alpha1
kind: Rollout
metadata:
  name: order-service
  namespace: production
spec:
  replicas: 10
  revisionHistoryLimit: 3
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
    spec:
      containers:
      - name: api
        image: myacr.azurecr.io/order-service:v2.0
        ports:
        - containerPort: 8080
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
  strategy:
    canary:
      # Define canary steps
      steps:
      - setWeight: 10      # Route 10% traffic to new version
      - pause: {duration: 5m}  # Monitor for 5 minutes
      - setWeight: 25      # Increase to 25%
      - pause: {duration: 5m}
      - setWeight: 50      # Increase to 50%
      - pause: {duration: 5m}
      - setWeight: 75      # Increase to 75%
      - pause: {duration: 5m}
      # Final step implicitly promotes to 100%
      
      # Services for canary and stable versions
      canaryService: order-service-canary
      stableService: order-service-stable
      
      # Traffic routing configuration
      trafficRouting:
        nginx:
          stableIngress: order-service
        
      # Automated analysis during rollout
      analysis:
        templates:
        - templateName: success-rate
        - templateName: latency-check
        startingStep: 1  # Start analysis after first step
        args:
        - name: service-name
          value: order-service
---
# Canary service
apiVersion: v1
kind: Service
metadata:
  name: order-service-canary
  namespace: production
spec:
  selector:
    app: order-service
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
---
# Stable service
apiVersion: v1
kind: Service
metadata:
  name: order-service-stable
  namespace: production
spec:
  selector:
    app: order-service
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
---
# Analysis template for success rate
apiVersion: argoproj.io/v1alpha1
kind: AnalysisTemplate
metadata:
  name: success-rate
  namespace: production
spec:
  args:
  - name: service-name
  metrics:
  - name: success-rate
    interval: 1m
    successCondition: result >= 0.95  # 95% success rate required
    failureLimit: 3  # Fail after 3 consecutive failures
    provider:
      prometheus:
        address: http://prometheus-server.monitoring:9090
        query: |
          sum(rate(http_requests_total{
            service="{{args.service-name}}",
            status=~"2.."
          }[5m])) /
          sum(rate(http_requests_total{
            service="{{args.service-name}}"
          }[5m]))
---
# Analysis template for latency
apiVersion: argoproj.io/v1alpha1
kind: AnalysisTemplate
metadata:
  name: latency-check
  namespace: production
spec:
  args:
  - name: service-name
  metrics:
  - name: p95-latency
    interval: 1m
    successCondition: result <= 500  # P95 latency under 500ms
    failureLimit: 3
    provider:
      prometheus:
        address: http://prometheus-server.monitoring:9090
        query: |
          histogram_quantile(0.95,
            sum(rate(http_request_duration_milliseconds_bucket{
              service="{{args.service-name}}"
            }[5m])) by (le)
          )
  - name: p99-latency
    interval: 1m
    successCondition: result <= 1000  # P99 latency under 1000ms
    failureLimit: 3
    provider:
      prometheus:
        address: http://prometheus-server.monitoring:9090
        query: |
          histogram_quantile(0.99,
            sum(rate(http_request_duration_milliseconds_bucket{
              service="{{args.service-name}}"
            }[5m])) by (le)
          )
```

### Managing Rollouts

```bash
# Watch rollout progress
kubectl argo rollouts get rollout order-service -n production --watch

# Promote to next step manually
kubectl argo rollouts promote order-service -n production

# Abort rollout (rollback)
kubectl argo rollouts abort order-service -n production

# Retry failed rollout
kubectl argo rollouts retry order-service -n production

# Get rollout history
kubectl argo rollouts history order-service -n production

# Rollback to specific revision
kubectl argo rollouts undo order-service -n production --to-revision=3
```

### Azure Traffic Manager for Canary

Use Azure Traffic Manager for weighted traffic distribution:

```bash
# Create Traffic Manager profile with weighted routing
az network traffic-manager profile create \
    --name order-service-canary \
    --resource-group my-rg \
    --routing-method Weighted \
    --unique-dns-name order-service-canary \
    --ttl 30 \
    --protocol HTTP \
    --port 80 \
    --path /health

# Add stable endpoint (90% weight initially)
az network traffic-manager endpoint create \
    --name stable \
    --profile-name order-service-canary \
    --resource-group my-rg \
    --type azureEndpoints \
    --target-resource-id "/subscriptions/{sub-id}/resourceGroups/my-rg/providers/Microsoft.Web/sites/order-service-stable" \
    --weight 90 \
    --endpoint-status Enabled

# Add canary endpoint (10% weight initially)
az network traffic-manager endpoint create \
    --name canary \
    --profile-name order-service-canary \
    --resource-group my-rg \
    --type azureEndpoints \
    --target-resource-id "/subscriptions/{sub-id}/resourceGroups/my-rg/providers/Microsoft.Web/sites/order-service-canary" \
    --weight 10 \
    --endpoint-status Enabled

# Gradually increase canary weight over time
az network traffic-manager endpoint update \
    --name canary \
    --profile-name order-service-canary \
    --resource-group my-rg \
    --weight 25

# Continue increasing: 50, 75, 100
az network traffic-manager endpoint update \
    --name canary \
    --profile-name order-service-canary \
    --resource-group my-rg \
    --weight 100

# Remove stable endpoint after successful rollout
az network traffic-manager endpoint delete \
    --name stable \
    --profile-name order-service-canary \
    --resource-group my-rg
```

### Application-Level Canary with Feature Flags

Implement canary routing at the application level using Azure App Configuration and feature flags:

```csharp
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

// Program.cs
builder.Configuration.AddAzureAppConfiguration(options =>
{
    options.Connect(builder.Configuration["AppConfiguration:ConnectionString"])
           .UseFeatureFlags(flagOptions =>
           {
               flagOptions.CacheExpirationInterval = TimeSpan.FromSeconds(30);
           });
});

builder.Services.AddFeatureManagement()
    .AddFeatureFilter<PercentageFilter>()
    .AddFeatureFilter<TargetingFilter>();

// Middleware for canary routing
public class CanaryDeploymentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<CanaryDeploymentMiddleware> _logger;
    
    public CanaryDeploymentMiddleware(
        RequestDelegate next,
        IFeatureManager featureManager,
        ILogger<CanaryDeploymentMiddleware> logger)
    {
        _next = next;
        _featureManager = featureManager;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Determine if request should go to canary
        var targetingContext = new TargetingContext
        {
            UserId = context.User.Identity?.Name ?? "anonymous",
            Groups = context.User.Claims
                .Where(c => c.Type == "groups")
                .Select(c => c.Value)
                .ToList()
        };
        
        var isCanary = await _featureManager.IsEnabledAsync(
            "CanaryDeployment",
            targetingContext);
        
        if (isCanary)
        {
            context.Request.Headers.Append("X-Deployment-Target", "canary");
            context.Items["DeploymentRing"] = "Canary";
            _logger.LogDebug("Routing request to canary deployment");
        }
        else
        {
            context.Request.Headers.Append("X-Deployment-Target", "stable");
            context.Items["DeploymentRing"] = "Stable";
        }
        
        await _next(context);
    }
    
    private bool IsInternalIp(IPAddress? ipAddress)
    {
        if (ipAddress == null)
            return false;
        
        var bytes = ipAddress.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}

// Register middleware
app.UseMiddleware<CanaryDeploymentMiddleware>();
```

### Feature Flag Configuration (appsettings.json)

```json
{
  "FeatureManagement": {
    "CanaryDeployment": {
      "EnabledFor": [
        {
          "Name": "Percentage",
          "Parameters": {
            "Value": 10
          }
        },
        {
          "Name": "Targeting",
          "Parameters": {
            "Audience": {
              "Users": ["beta-user@company.com"],
              "Groups": ["BetaTesters", "InternalEmployees"],
              "DefaultRolloutPercentage": 10,
              "Exclusion": {
                "Users": [],
                "Groups": []
              }
            }
          }
        }
      ]
    }
  }
}
```

## Metrics Guards and Automated Rollback

### What are Metrics Guards?

Metrics guards are automated checks that continuously monitor key performance and business metrics during deployment. If thresholds are violated, they trigger automatic rollback to prevent user impact.

### Key Metrics to Monitor

**Technical Metrics:**
- Error rate (HTTP 4xx, 5xx responses)
- Latency (P50, P95, P99 response times)
- Throughput (requests per second)
- CPU and memory utilization
- Database query performance

**Business Metrics:**
- Conversion rate
- Transaction success rate
- Shopping cart abandonment
- Revenue per user
- Feature usage rates

### Implementation with Application Insights

```csharp
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

public class MetricsGuardService : BackgroundService
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<MetricsGuardService> _logger;
    private readonly IDeploymentService _deploymentService;
    private readonly IConfiguration _configuration;
    private readonly MetricsGuardConfiguration _config;
    
    public MetricsGuardService(
        TelemetryClient telemetryClient,
        ILogger<MetricsGuardService> logger,
        IDeploymentService deploymentService,
        IConfiguration configuration)
    {
        _telemetryClient = telemetryClient;
        _logger = logger;
        _deploymentService = deploymentService;
        _configuration = configuration;
        _config = configuration.GetSection("MetricsGuard")
            .Get<MetricsGuardConfiguration>() ?? new();
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics guard service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var evaluation = await EvaluateMetricsAsync();
                
                if (!evaluation.IsHealthy)
                {
                    _logger.LogError(
                        "Metrics guard violations detected: {Violations}",
                        string.Join("; ", evaluation.Violations));
                    
                    // Trigger automated rollback
                    await _deploymentService.RollbackAsync(
                        "Automated rollback due to metrics violations");
                    
                    // Send alerts
                    await SendAlertAsync(evaluation);
                    
                    // Stop monitoring after rollback
                    break;
                }
                else
                {
                    _logger.LogDebug("All metrics within acceptable thresholds");
                }
                
                await Task.Delay(_config.EvaluationInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating metrics");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
    
    private async Task<MetricsEvaluation> EvaluateMetricsAsync()
    {
        // Query Application Insights using Kusto
        var query = @"
            let timeRange = 5m;
            let deploymentRing = 'Canary';
            requests
            | where timestamp > ago(timeRange)
            | where customDimensions.DeploymentRing == deploymentRing
            | summarize 
                TotalRequests = count(),
                FailedRequests = countif(success == false),
                ErrorRate = 100.0 * countif(success == false) / count(),
                P50Latency = percentile(duration, 50),
                P95Latency = percentile(duration, 95),
                P99Latency = percentile(duration, 99)
        ";
        
        var results = await ExecuteAnalyticsQueryAsync(query);
        
        var violations = new List<string>();
        
        // Check error rate
        if (results.ErrorRate > _config.MaxErrorRatePercent)
        {
            violations.Add(
                $"Error rate too high: {results.ErrorRate:F2}% " +
                $"(threshold: {_config.MaxErrorRatePercent}%)");
        }
        
        // Check P95 latency
        if (results.P95Latency > _config.MaxP95LatencyMs)
        {
            violations.Add(
                $"P95 latency too high: {results.P95Latency:F0}ms " +
                $"(threshold: {_config.MaxP95LatencyMs}ms)");
        }
        
        // Check P99 latency
        if (results.P99Latency > _config.MaxP99LatencyMs)
        {
            violations.Add(
                $"P99 latency too high: {results.P99Latency:F0}ms " +
                $"(threshold: {_config.MaxP99LatencyMs}ms)");
        }
        
        // Check minimum traffic
        if (results.TotalRequests < _config.MinimumTrafficForEvaluation)
        {
            violations.Add(
                $"Insufficient traffic for evaluation: {results.TotalRequests} requests " +
                $"(minimum: {_config.MinimumTrafficForEvaluation})");
        }
        
        // Compare canary vs stable
        var comparison = await CompareCanaryToStableAsync();
        if (comparison.ErrorRateDelta > _config.MaxErrorRateDeltaPercent)
        {
            violations.Add(
                $"Error rate delta too high: +{comparison.ErrorRateDelta:F2}% " +
                $"compared to stable");
        }
        
        return new MetricsEvaluation
        {
            IsHealthy = violations.Count == 0,
            Violations = violations,
            Metrics = results,
            Timestamp = DateTime.UtcNow
        };
    }
    
    private async Task<CanaryComparison> CompareCanaryToStableAsync()
    {
        var query = @"
            let timeRange = 5m;
            requests
            | where timestamp > ago(timeRange)
            | summarize 
                ErrorRate = 100.0 * countif(success == false) / count(),
                P95Latency = percentile(duration, 95)
                by DeploymentRing = tostring(customDimensions.DeploymentRing)
        ";
        
        var results = await ExecuteAnalyticsQueryAsync(query);
        
        var canary = results.FirstOrDefault(r => r.DeploymentRing == "Canary");
        var stable = results.FirstOrDefault(r => r.DeploymentRing == "Stable");
        
        if (canary == null || stable == null)
        {
            return new CanaryComparison();
        }
        
        return new CanaryComparison
        {
            ErrorRateDelta = canary.ErrorRate - stable.ErrorRate,
            LatencyDelta = canary.P95Latency - stable.P95Latency
        };
    }
    
    private async Task SendAlertAsync(MetricsEvaluation evaluation)
    {
        // Send to multiple channels
        var tasks = new[]
        {
            SendSlackAlertAsync(evaluation),
            SendEmailAlertAsync(evaluation),
            SendPagerDutyAlertAsync(evaluation)
        };
        
        await Task.WhenAll(tasks);
    }
    
    private async Task SendSlackAlertAsync(MetricsEvaluation evaluation)
    {
        var slackWebhook = _configuration["Alerts:SlackWebhook"];
        if (string.IsNullOrEmpty(slackWebhook))
            return;
        
        var message = new
        {
            text = "🚨 Deployment Metrics Guard Violation",
            blocks = new[]
            {
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = $"*Automated rollback triggered*\n{string.Join("\n", evaluation.Violations)}"
                    }
                },
                new
                {
                    type = "section",
                    fields = new[]
                    {
                        new { type = "mrkdwn", text = $"*Error Rate:*\n{evaluation.Metrics.ErrorRate:F2}%" },
                        new { type = "mrkdwn", text = $"*P95 Latency:*\n{evaluation.Metrics.P95Latency:F0}ms" },
                        new { type = "mrkdwn", text = $"*P99 Latency:*\n{evaluation.Metrics.P99Latency:F0}ms" },
                        new { type = "mrkdwn", text = $"*Timestamp:*\n{evaluation.Timestamp:u}" }
                    }
                }
            }
        };
        
        using var httpClient = new HttpClient();
        await httpClient.PostAsJsonAsync(slackWebhook, message);
    }
}

public class MetricsGuardConfiguration
{
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);
    public double MaxErrorRatePercent { get; set; } = 5.0;
    public double MaxP95LatencyMs { get; set; } = 1000;
    public double MaxP99LatencyMs { get; set; } = 2000;
    public int MinimumTrafficForEvaluation { get; set; } = 100;
    public double MaxErrorRateDeltaPercent { get; set; } = 2.0;
}

public class MetricsEvaluation
{
    public bool IsHealthy { get; set; }
    public List<string> Violations { get; set; } = new();
    public MetricsSnapshot Metrics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class MetricsSnapshot
{
    public long TotalRequests { get; set; }
    public long FailedRequests { get; set; }
    public double ErrorRate { get; set; }
    public double P50Latency { get; set; }
    public double P95Latency { get; set; }
    public double P99Latency { get; set; }
}

public class CanaryComparison
{
    public double ErrorRateDelta { get; set; }
    public double LatencyDelta { get; set; }
}
```

### Prometheus-Based Metrics Guards

For Kubernetes deployments, use Prometheus for metrics collection:

```yaml
# PrometheusRule for automated alerts
apiVersion: monitoring.coreos.com/v1
kind: PrometheusRule
metadata:
  name: canary-deployment-rules
  namespace: production
spec:
  groups:
  - name: canary-metrics
    interval: 30s
    rules:
    # High error rate alert
    - alert: CanaryHighErrorRate
      expr: |
        (
          sum(rate(http_requests_total{deployment="canary",status=~"5.."}[5m]))
          /
          sum(rate(http_requests_total{deployment="canary"}[5m]))
        ) > 0.05
      for: 2m
      labels:
        severity: critical
        deployment: canary
      annotations:
        summary: "Canary deployment has high error rate"
        description: "Error rate is {{ $value | humanizePercentage }}"
        dashboard: "https://grafana/d/canary-metrics"
    
    # High latency alert
    - alert: CanaryHighLatency
      expr: |
        histogram_quantile(0.95,
          sum(rate(http_request_duration_seconds_bucket{
            deployment="canary"
          }[5m])) by (le)
        ) > 1.0
      for: 2m
      labels:
        severity: critical
        deployment: canary
      annotations:
        summary: "Canary deployment has high latency"
        description: "P95 latency is {{ $value }}s"
    
    # Error rate comparison
    - alert: CanaryErrorRateHigherThanStable
      expr: |
        (
          sum(rate(http_requests_total{deployment="canary",status=~"5.."}[5m]))
          /
          sum(rate(http_requests_total{deployment="canary"}[5m]))
        )
        >
        (
          sum(rate(http_requests_total{deployment="stable",status=~"5.."}[5m]))
          /
          sum(rate(http_requests_total{deployment="stable"}[5m]))
        ) + 0.02
      for: 3m
      labels:
        severity: warning
        deployment: canary
      annotations:
        summary: "Canary error rate significantly higher than stable"
        description: "Canary error rate is {{ $value | humanizePercentage }} higher"
---
# AlertManager configuration for rollback webhook
apiVersion: v1
kind: ConfigMap
metadata:
  name: alertmanager-config
  namespace: monitoring
data:
  alertmanager.yml: |
    global:
      resolve_timeout: 5m
    
    route:
      group_by: ['alertname', 'deployment']
      group_wait: 10s
      group_interval: 10s
      repeat_interval: 12h
      receiver: 'rollback-webhook'
      routes:
      - match:
          severity: critical
          deployment: canary
        receiver: 'rollback-webhook'
    
    receivers:
    - name: 'rollback-webhook'
      webhook_configs:
      - url: 'http://deployment-controller.production.svc.cluster.local:8080/rollback'
        send_resolved: true
```

### Automated Rollback Service

Implement automated rollback triggered by metrics violations:

```csharp
public class AutomatedRollbackService
{
    private readonly ILogger<AutomatedRollbackService> _logger;
    private readonly IKubernetesClient _k8sClient;
    private readonly INotificationService _notificationService;
    private readonly TelemetryClient _telemetry;
    
    public AutomatedRollbackService(
        ILogger<AutomatedRollbackService> logger,
        IKubernetesClient k8sClient,
        INotificationService notificationService,
        TelemetryClient telemetry)
    {
        _logger = logger;
        _k8sClient = k8sClient;
        _notificationService = notificationService;
        _telemetry = telemetry;
    }
    
    public async Task RollbackDeploymentAsync(
        string deploymentName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        _logger.LogWarning(
            "Initiating automated rollback for {Deployment}. Reason: {Reason}",
            deploymentName, reason);
        
        _telemetry.TrackEvent("AutomatedRollback", new Dictionary<string, string>
        {
            ["Deployment"] = deploymentName,
            ["Reason"] = reason,
            ["Timestamp"] = startTime.ToString("O")
        });
        
        try
        {
            // Using Argo Rollouts
            await _k8sClient.AbortRolloutAsync(deploymentName, cancellationToken);
            
            _logger.LogInformation(
                "Rollback completed successfully for {Deployment} in {Duration}ms",
                deploymentName,
                (DateTime.UtcNow - startTime).TotalMilliseconds);
            
            _telemetry.TrackMetric(
                "RollbackDuration",
                (DateTime.UtcNow - startTime).TotalMilliseconds,
                new Dictionary<string, string>
                {
                    ["Deployment"] = deploymentName,
                    ["Success"] = "true"
                });
            
            // Send success notifications
            await _notificationService.SendAsync(new Notification
            {
                Title = $"✅ Rollback Completed: {deploymentName}",
                Message = $"Deployment rolled back successfully due to: {reason}",
                Severity = NotificationSeverity.High,
                Channels = new[] { NotificationChannel.Slack, NotificationChannel.Email }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to rollback {Deployment}",
                deploymentName);
            
            _telemetry.TrackException(ex, new Dictionary<string, string>
            {
                ["Deployment"] = deploymentName,
                ["Operation"] = "Rollback"
            });
            
            // Escalate to on-call
            await _notificationService.SendAsync(new Notification
            {
                Title = $"🚨 URGENT: Rollback Failed for {deploymentName}",
                Message = $"Automated rollback failed. Manual intervention required. Error: {ex.Message}",
                Severity = NotificationSeverity.Critical,
                Channels = new[] 
                { 
                    NotificationChannel.PagerDuty,
                    NotificationChannel.Slack,
                    NotificationChannel.Email 
                }
            });
            
            throw;
        }
    }
    
    public async Task RollbackAzureAppServiceAsync(
        string appName,
        string resourceGroup,
        string reason)
    {
        _logger.LogWarning(
            "Initiating Azure App Service rollback for {AppName}. Reason: {Reason}",
            appName, reason);
        
        try
        {
            // Swap slots back to previous version
            var command = $"az webapp deployment slot swap " +
                         $"--name {appName} " +
                         $"--resource-group {resourceGroup} " +
                         $"--slot green " +
                         $"--target-slot production";
            
            await ExecuteAzureCliCommandAsync(command);
            
            _logger.LogInformation(
                "Azure App Service rollback completed for {AppName}",
                appName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to rollback Azure App Service {AppName}",
                appName);
            throw;
        }
    }
}
```

### Canary Comparison Dashboard

Create a comparison service to visualize canary vs stable metrics:

```csharp
public class CanaryComparisonService
{
    private readonly IMetricsProvider _metricsProvider;
    private readonly ILogger<CanaryComparisonService> _logger;
    
    public async Task<ComparisonResult> CompareVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var canaryMetrics = await _metricsProvider.GetMetricsAsync(
            "canary", 
            TimeSpan.FromMinutes(5),
            cancellationToken);
            
        var stableMetrics = await _metricsProvider.GetMetricsAsync(
            "stable",
            TimeSpan.FromMinutes(5),
            cancellationToken);
        
        var comparison = new ComparisonResult
        {
            CanaryMetrics = canaryMetrics,
            StableMetrics = stableMetrics,
            ErrorRateDelta = canaryMetrics.ErrorRate - stableMetrics.ErrorRate,
            LatencyDelta = canaryMetrics.P95Latency - stableMetrics.P95Latency,
            ThroughputDelta = canaryMetrics.Throughput - stableMetrics.Throughput,
            Timestamp = DateTime.UtcNow
        };
        
        // Decision logic
        if (comparison.ErrorRateDelta > 0.02) // 2% increase
        {
            comparison.Recommendation = DeploymentDecision.Rollback;
            comparison.Reason = $"Error rate increased by {comparison.ErrorRateDelta:P2}";
            comparison.Confidence = ConfidenceLevel.High;
        }
        else if (comparison.LatencyDelta > 200) // 200ms increase
        {
            comparison.Recommendation = DeploymentDecision.Rollback;
            comparison.Reason = $"P95 latency increased by {comparison.LatencyDelta:F0}ms";
            comparison.Confidence = ConfidenceLevel.High;
        }
        else if (comparison.ErrorRateDelta < -0.01 && comparison.LatencyDelta < -50)
        {
            comparison.Recommendation = DeploymentDecision.Proceed;
            comparison.Reason = "Canary shows significant improvement";
            comparison.Confidence = ConfidenceLevel.High;
        }
        else if (Math.Abs(comparison.ErrorRateDelta) < 0.005 && 
                 Math.Abs(comparison.LatencyDelta) < 50)
        {
            comparison.Recommendation = DeploymentDecision.Proceed;
            comparison.Reason = "Metrics are comparable";
            comparison.Confidence = ConfidenceLevel.Medium;
        }
        else
        {
            comparison.Recommendation = DeploymentDecision.ContinueMonitoring;
            comparison.Reason = "Metrics within acceptable range but require more data";
            comparison.Confidence = ConfidenceLevel.Low;
        }
        
        _logger.LogInformation(
            "Canary comparison: {Recommendation} (Confidence: {Confidence}). {Reason}",
            comparison.Recommendation,
            comparison.Confidence,
            comparison.Reason);
        
        return comparison;
    }
}

public class ComparisonResult
{
    public DeploymentMetrics CanaryMetrics { get; set; }
    public DeploymentMetrics StableMetrics { get; set; }
    public double ErrorRateDelta { get; set; }
    public double LatencyDelta { get; set; }
    public double ThroughputDelta { get; set; }
    public DeploymentDecision Recommendation { get; set; }
    public ConfidenceLevel Confidence { get; set; }
    public string Reason { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum DeploymentDecision
{
    Proceed,
    Rollback,
    ContinueMonitoring
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}
```

## Best Practices Summary

**Blue-Green Deployments:**
- Maintain identical environments for accurate testing
- Always run comprehensive smoke tests before switching
- Automate the swap process to reduce human error
- Plan database migrations carefully with backward compatibility
- Keep both environments for quick rollback
- Monitor both environments during transition

**Canary Deployments:**
- Start with small traffic percentages (5-10%)
- Define clear success criteria before deployment
- Monitor business metrics, not just technical metrics
- Use automated analysis to make promotion decisions
- Have rollback procedures ready and tested
- Communicate rollout progress to stakeholders

**Metrics Guards:**
- Establish baseline metrics from stable deployments
- Set realistic thresholds based on historical data
- Monitor both absolute values and deltas
- Combine multiple metrics for better accuracy
- Automate rollback decisions for critical violations
- Include business metrics in evaluation

**General Deployment Practices:**
- Always deploy during low-traffic periods when possible
- Implement comprehensive health checks
- Use feature flags to decouple deployment from release
- Maintain detailed deployment logs and audit trails
- Practice rollback procedures regularly
- Document incident response procedures

**Azure-Specific:**
- Leverage Azure App Service deployment slots for blue-green
- Use Azure Traffic Manager for weighted traffic distribution
- Enable Application Insights for metrics collection
- Configure Azure Monitor alerts for automated responses
- Use Azure DevOps pipelines for deployment automation
- Implement Azure Policy for deployment guardrails

**Kubernetes-Specific:**
- Use Argo Rollouts for advanced deployment strategies
- Configure Prometheus for metrics collection
- Implement proper liveness and readiness probes
- Use service mesh (Istio, Linkerd) for advanced traffic control
- Set resource limits and requests appropriately
- Monitor pod health and restart patterns

By implementing these deployment strategies with proper metrics guards and automated rollback, teams can deploy with confidence, minimize user impact from issues, and maintain high availability in production environments.
