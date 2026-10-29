---
title: "Service Discovery in Modern .NET Applications and Azure"
date: 2025-10-02
slug: service-discovery-dotnet-core-azure
description: "Service discovery enables microservices to locate and communicate with each other dynamically without hardcoding endpoints. As services scale up or down, restart, or move between hosts, service discovery ensures seamless connectivity by maintaining an up-to-date registry of avail"
tags: [microservices, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Service Discovery in Kubernetes

### How Kubernetes DNS Works

Kubernetes automatically creates DNS entries for services, enabling simple name-based discovery. A service named `order-service` in the `production` namespace becomes accessible at `order-service.production.svc.cluster.local`. For services within the same namespace, you can use the short name `order-service`.

**Service Definition:**

```yaml
apiVersion: v1
kind: Service
metadata:
  name: order-service
  namespace: production
  labels:
    app: order-service
    version: v1
spec:
  selector:
    app: order-service
  ports:
  - name: http
    protocol: TCP
    port: 80
    targetPort: 8080
  type: ClusterIP
```

### Native Service Discovery in .NET 9

.NET 9 introduces enhanced service discovery capabilities with improved configuration and resilience features.

**Basic Configuration:**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add service discovery with .NET 9 enhancements
builder.Services.AddServiceDiscovery();

// Configure HTTP client with service discovery
builder.Services.AddHttpClient<IOrderServiceClient, OrderServiceClient>(client =>
{
    // Use service name - discovery resolves to actual endpoint
    client.BaseAddress = new Uri("http://order-service");
})
.AddServiceDiscovery()
.AddStandardResilienceHandler(); // .NET 9 resilience patterns

var app = builder.Build();
```

**Advanced Configuration with appsettings.json:**

```json
{
  "ServiceDiscovery": {
    "Providers": {
      "Kubernetes": {
        "Namespace": "production",
        "RefreshPeriod": "00:01:00"
      }
    },
    "Services": {
      "order-service": {
        "Scheme": "https",
        "EndpointNames": ["http", "https"],
        "HealthCheckPath": "/health"
      },
      "payment-service": {
        "Scheme": "http",
        "AllowAllHosts": false
      }
    },
    "AllowAllHosts": true,
    "AllowedHosts": ["*.svc.cluster.local"]
  }
}
```

### Service-to-Service Communication Patterns

**Using Typed HTTP Clients:**

```csharp
public interface IOrderServiceClient
{
    Task<Order> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetOrdersByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
}

public class OrderServiceClient : IOrderServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OrderServiceClient> _logger;

    public OrderServiceClient(HttpClient httpClient, ILogger<OrderServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Order> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<Order>(
                $"api/orders/{orderId}", 
                cancellationToken);
            
            return response ?? throw new OrderNotFoundException(orderId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to retrieve order {OrderId}", orderId);
            throw;
        }
    }

    public async Task<IEnumerable<Order>> GetOrdersByCustomerAsync(
        Guid customerId, 
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IEnumerable<Order>>(
            $"api/orders/customer/{customerId}", 
            cancellationToken) ?? [];
    }
}
```

**Service Registration with Resilience:**

```csharp
builder.Services.AddHttpClient<IOrderServiceClient, OrderServiceClient>(client =>
{
    client.BaseAddress = new Uri("http://order-service");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddServiceDiscovery()
.AddStandardResilienceHandler(options =>
{
    // Configure circuit breaker
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 10;
    
    // Configure retry policy
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
    
    // Configure timeout
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
});
```

## Service Discovery in Azure Container Apps

Azure Container Apps provides built-in service discovery within the same environment, eliminating the need for manual endpoint configuration.

### Environment Variables Approach

**Container App Configuration:**

```json
{
  "properties": {
    "environmentId": "/subscriptions/.../containerappsenvironments/production-env",
    "configuration": {
      "ingress": {
        "external": false,
        "targetPort": 8080,
        "transport": "http",
        "allowInsecure": false
      }
    },
    "template": {
      "containers": [{
        "name": "api-service",
        "image": "myregistry.azurecr.io/api-service:latest",
        "env": [{
          "name": "OrderService__Url",
          "value": "https://order-service.internal.<environment-unique-id>.azurecontainerapps.io"
        },
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        }],
        "resources": {
          "cpu": 0.5,
          "memory": "1Gi"
        }
      }]
    }
  }
}
```

**Consuming in .NET:**

```csharp
services.AddHttpClient<IOrderServiceClient, OrderServiceClient>(client =>
{
    var orderServiceUrl = configuration["OrderService:Url"] 
        ?? throw new InvalidOperationException("OrderService URL not configured");
    client.BaseAddress = new Uri(orderServiceUrl);
});
```

### Native Service Discovery in Container Apps

Azure Container Apps now supports simplified service discovery using service names directly.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Azure Container Apps service discovery
builder.Services.AddServiceDiscovery();

builder.Services.AddHttpClient<IOrderServiceClient, OrderServiceClient>(client =>
{
    // Reference by Container App name within the same environment
    client.BaseAddress = new Uri("http://order-service");
})
.AddServiceDiscovery()
.AddStandardResilienceHandler();

// For external Container Apps (different environment)
builder.Services.AddHttpClient<IPaymentServiceClient, PaymentServiceClient>(client =>
{
    client.BaseAddress = new Uri("https://payment-service.politeocean-12345678.westeurope.azurecontainerapps.io");
})
.AddStandardResilienceHandler();
```

### Dapr Integration for Service Invocation

Dapr (Distributed Application Runtime) provides a robust service invocation model with built-in retry, tracing, and security.

**Dapr Configuration:**

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Dapr services
builder.Services.AddDaprClient();
builder.Services.AddControllers().AddDapr();

var app = builder.Build();
app.UseRouting();
app.UseCloudEvents(); // Enable CloudEvents for pub/sub
app.MapSubscribeHandler(); // Map Dapr subscription endpoint
app.MapControllers();
```

**Service Invocation with Dapr:**

```csharp
public class OrderController : ControllerBase
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<OrderController> _logger;
    
    public OrderController(DaprClient daprClient, ILogger<OrderController> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        try
        {
            // Invoke customer service via Dapr sidecar
            var customer = await _daprClient.InvokeMethodAsync<Customer>(
                HttpMethod.Get,
                "customer-service", // Dapr App ID
                $"api/customers/{id}");
            
            if (customer is null)
            {
                return NotFound();
            }
            
            return Ok(customer);
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex, "Failed to invoke customer service for customer {CustomerId}", id);
            return StatusCode(503, "Service temporarily unavailable");
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        // Invoke with request/response
        var inventoryCheck = await _daprClient.InvokeMethodAsync<InventoryCheckRequest, InventoryCheckResponse>(
            HttpMethod.Post,
            "inventory-service",
            "api/inventory/check",
            new InventoryCheckRequest 
            { 
                ProductId = request.ProductId, 
                Quantity = request.Quantity 
            });
        
        if (!inventoryCheck.IsAvailable)
        {
            return BadRequest("Insufficient inventory");
        }
        
        // Process order creation...
        return CreatedAtAction(nameof(GetOrder), new { id = Guid.NewGuid() }, null);
    }
}
```

**Dapr Component Configuration for Container Apps:**

```yaml
# dapr-components.yaml
componentType: state.azure.cosmosdb
version: v1
metadata:
- name: url
  value: "https://mycosmosdb.documents.azure.com:443/"
- name: masterKey
  secretRef: cosmosdb-key
- name: database
  value: "ordersdb"
- name: collection
  value: "orders"
scopes:
- order-service
```

## API Gateway and Ingress Patterns

### What is an API Gateway?

An API gateway serves as a single entry point for client requests, providing:

- **Request routing** to appropriate microservices
- **Authentication and authorization** enforcement
- **Rate limiting and throttling** to prevent abuse
- **Request/response transformation** for protocol translation
- **Caching** to improve performance
- **Load balancing** across service instances
- **Monitoring and analytics** for observability

### Azure API Management

Azure API Management provides enterprise-grade API gateway capabilities with comprehensive policy support.

> Read [Configure ingress for an Azure Container Apps environment](https://learn.microsoft.com/en-us/azure/container-apps/ingress-environment-configuration?tabs=azure-cli)

**Policy Configuration:**

```xml
<policies>
    <inbound>
        <base />
        <!-- Rate limiting -->
        <rate-limit-by-key calls="100" 
                           renewal-period="60" 
                           counter-key="@(context.Request.IpAddress)" />
        
        <!-- JWT validation -->
        <validate-jwt header-name="Authorization" 
                      failed-validation-httpcode="401" 
                      failed-validation-error-message="Unauthorized">
            <openid-config url="https://login.microsoftonline.com/{tenant-id}/v2.0/.well-known/openid-configuration" />
            <required-claims>
                <claim name="roles" match="any">
                    <value>api.read</value>
                    <value>api.write</value>
                </claim>
            </required-claims>
        </validate-jwt>
        
        <!-- Dynamic backend routing -->
        <choose>
            <when condition="@(context.Request.Url.Path.StartsWith("/api/orders"))">
                <set-backend-service base-url="http://order-service.production.svc.cluster.local" />
            </when>
            <when condition="@(context.Request.Url.Path.StartsWith("/api/customers"))">
                <set-backend-service base-url="http://customer-service.production.svc.cluster.local" />
            </when>
            <otherwise>
                <return-response>
                    <set-status code="404" reason="Not Found" />
                </return-response>
            </otherwise>
        </choose>
        
        <!-- Add correlation ID -->
        <set-header name="X-Correlation-ID" exists-action="skip">
            <value>@(Guid.NewGuid().ToString())</value>
        </set-header>
    </inbound>
    <backend>
        <base />
        <retry condition="@(context.Response.StatusCode >= 500)" 
               count="3" 
               interval="2" 
               delta="1" 
               first-fast-retry="true" />
    </backend>
    <outbound>
        <base />
        <!-- Remove internal headers -->
        <set-header name="X-Powered-By" exists-action="delete" />
        <set-header name="X-AspNet-Version" exists-action="delete" />
    </outbound>
    <on-error>
        <base />
        <set-body>@{
            return new JObject(
                new JProperty("error", context.LastError.Message),
                new JProperty("correlationId", context.Request.Headers.GetValueOrDefault("X-Correlation-ID"))
            ).ToString();
        }</set-body>
    </on-error>
</policies>
```

### YARP - .NET Native API Gateway

YARP (Yet Another Reverse Proxy) is a highly performant, .NET-native reverse proxy toolkit ideal for building custom API gateways.

**Configuration:**

```json
{
  "ReverseProxy": {
    "Routes": {
      "order-route": {
        "ClusterId": "order-cluster",
        "Match": {
          "Path": "/api/orders/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api" },
          { "RequestHeader": "X-Forwarded-For", "Append": "{RemoteIpAddress}" },
          { "RequestHeader": "X-Original-Host", "Set": "{OriginalHost}" }
        ],
        "Metadata": {
          "requireAuth": "true"
        }
      },
      "customer-route": {
        "ClusterId": "customer-cluster",
        "Match": {
          "Path": "/api/customers/{**catch-all}"
        },
        "RateLimiterPolicy": "fixed",
        "AuthorizationPolicy": "authenticated"
      },
      "websocket-route": {
        "ClusterId": "notification-cluster",
        "Match": {
          "Path": "/notifications"
        }
      }
    },
    "Clusters": {
      "order-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://order-service",
            "Health": "http://order-service/health"
          }
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:10",
            "Timeout": "00:00:05",
            "Policy": "ConsecutiveFailures",
            "Path": "/health"
          }
        },
        "LoadBalancingPolicy": "RoundRobin"
      },
      "customer-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://customer-service"
          },
          "destination2": {
            "Address": "http://customer-service-2"
          }
        }
      },
      "notification-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://notification-service"
          }
        }
      }
    }
  }
}
```

**Program.cs with Advanced Features:**

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add YARP with service discovery
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscovery();

// Add authentication
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/{tenant-id}/v2.0";
        options.Audience = "api://myapi";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy =>
        policy.RequireAuthenticatedUser());
});

// Add rate limiting (.NET 9)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100;
        opt.QueueLimit = 10;
    });
    
    options.AddSlidingWindowLimiter("sliding", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100;
        opt.SegmentsPerWindow = 6;
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Map YARP routes with custom middleware
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        // Add custom headers
        context.Request.Headers.Append("X-Gateway", "YARP");
        
        await next();
        
        // Log response
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation(
            "Proxied {Method} {Path} -> {StatusCode}", 
            context.Request.Method, 
            context.Request.Path, 
            context.Response.StatusCode);
    });
});

app.Run();
```

### Kubernetes Ingress

Kubernetes Ingress resources manage external access to services, providing HTTP/HTTPS routing with SSL termination.

**Ingress Configuration:**

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: api-ingress
  namespace: production
  annotations:
    nginx.ingress.kubernetes.io/rewrite-target: /$2
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/rate-limit: "100"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
    nginx.ingress.kubernetes.io/cors-allow-origin: "https://app.company.com"
spec:
  ingressClassName: nginx
  tls:
  - hosts:
    - api.company.com
    secretName: api-tls-secret
  rules:
  - host: api.company.com
    http:
      paths:
      - path: /orders(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: order-service
            port:
              number: 80
      - path: /customers(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: customer-service
            port:
              number: 80
      - path: /payments(/|$)(.*)
        pathType: Prefix
        backend:
          service:
            name: payment-service
            port:
              number: 80
```

**Gateway API (Next Generation):**

Kubernetes Gateway API provides more expressive and extensible routing capabilities.

```yaml
apiVersion: gateway.networking.k8s.io/v1
kind: Gateway
metadata:
  name: api-gateway
  namespace: production
spec:
  gatewayClassName: nginx
  listeners:
  - name: http
    protocol: HTTP
    port: 80
  - name: https
    protocol: HTTPS
    port: 443
    tls:
      certificateRefs:
      - name: api-tls-secret
---
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: order-route
  namespace: production
spec:
  parentRefs:
  - name: api-gateway
  hostnames:
  - "api.company.com"
  rules:
  - matches:
    - path:
        type: PathPrefix
        value: /api/orders
    backendRefs:
    - name: order-service
      port: 80
    filters:
    - type: URLRewrite
      urlRewrite:
        path:
          type: ReplacePrefixMatch
          replacePrefixMatch: /
```

## Best Practices

### Health Checks

Implement comprehensive health checks for proper service discovery and load balancing.

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<RedisHealthCheck>("redis")
    .AddHttpClient("order-service", client =>
    {
        client.BaseAddress = new Uri("http://order-service");
    })
    .AddCheck("order-service-health");

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

### Observability

Enable distributed tracing and metrics for monitoring service communication.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("OrderService");
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
    });
```

### Security Considerations

- Always use HTTPS for external service communication
- Implement mutual TLS (mTLS) for service-to-service communication
- Use service mesh (Linkerd, Istio) for advanced security policies
- Apply principle of least privilege for service accounts
- Regularly rotate credentials and certificates
- Implement API keys or OAuth for external API access

## Summary

Service discovery is fundamental to building resilient microservices architectures. .NET 9 provides native service discovery capabilities that integrate seamlessly with Kubernetes, Azure Container Apps, and custom solutions. Combining service discovery with API gateways like YARP or Azure API Management creates a robust, scalable foundation for distributed applications.

Choose your approach based on your infrastructure:
- **Kubernetes**: Use native DNS with .NET service discovery
- **Azure Container Apps**: Leverage built-in service discovery or Dapr
- **Hybrid/Multi-cloud**: Implement YARP or Azure API Management for unified routing

Always prioritize observability, security, and resilience patterns to ensure production-ready services.
