---
title: "Security Best Practices in .NET Core and Azure"
date: 2025-10-06
slug: security-best-practices-in-dotnet-core-and-azure
description: "Security is paramount in modern distributed systems, especially when deploying to cloud platforms like Azure. A comprehensive security strategy encompasses multiple layers: authentication and authorization, secure service-to-service communication, API protection through rate limi"
tags: [security, azure]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

This article explores essential security patterns and best practices for .NET applications, with a focus on Azure integration and modern authentication protocols.

## Authentication and Authorization

Proper authentication verifies user identity, while authorization ensures users can only access resources they're permitted to use.

### JWT Bearer Authentication

JSON Web Tokens (JWT) provide a stateless authentication mechanism ideal for distributed systems:

```csharp
// Configure JWT authentication in Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"];
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero, // No tolerance for expired tokens
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
        
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                {
                    context.Response.Headers.Append("Token-Expired", "true");
                }
                
                context.Response.Headers.Append("Token-Error", 
                    context.Exception.Message);
                
                return Task.CompletedTask;
            },
            
            OnTokenValidated = context =>
            {
                // Additional validation logic
                var userId = context.Principal?.FindFirst("sub")?.Value;
                // Log successful authentication, check revocation list, etc.
                return Task.CompletedTask;
            },
            
            OnChallenge = context =>
            {
                // Custom challenge handling
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                
                var result = JsonSerializer.Serialize(new
                {
                    error = "unauthorized",
                    error_description = context.ErrorDescription
                });
                
                return context.Response.WriteAsync(result);
            }
        };
        
        // For development only - disable HTTPS requirement
        if (builder.Environment.IsDevelopment())
        {
            options.RequireHttpsMetadata = false;
        }
    });

var app = builder.Build();

// Enable authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();
```

### Authorization Policies

Define fine-grained authorization policies for different access levels:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Role-based policy
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));
    
    // Claims-based policy
    options.AddPolicy("CanViewOrders", policy =>
        policy.RequireClaim("permissions", "orders:read"));
    
    options.AddPolicy("CanManageOrders", policy =>
        policy.RequireClaim("permissions", "orders:write", "orders:delete"));
    
    // Custom requirement policy
    options.AddPolicy("CanCreateOrders", policy =>
        policy.Requirements.Add(new MinimumAgeRequirement(18)));
    
    // Combined requirements
    options.AddPolicy("SeniorAdmin", policy =>
    {
        policy.RequireRole("Admin");
        policy.RequireClaim("seniority", "senior");
        policy.Requirements.Add(new MinimumAgeRequirement(25));
    });
    
    // Default policy for all authorized endpoints
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

### Custom Authorization Handlers

Implement custom authorization logic for complex business rules:

```csharp
// Custom requirement
public class MinimumAgeRequirement : IAuthorizationRequirement
{
    public int MinimumAge { get; }
    
    public MinimumAgeRequirement(int minimumAge)
    {
        MinimumAge = minimumAge;
    }
}

// Handler for the requirement
public class MinimumAgeHandler : AuthorizationHandler<MinimumAgeRequirement>
{
    private readonly ILogger<MinimumAgeHandler> _logger;
    
    public MinimumAgeHandler(ILogger<MinimumAgeHandler> logger)
    {
        _logger = logger;
    }
    
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumAgeRequirement requirement)
    {
        var dateOfBirthClaim = context.User.FindFirst(c => 
            c.Type == "date_of_birth" || c.Type == "birthdate");
        
        if (dateOfBirthClaim == null)
        {
            _logger.LogWarning("Date of birth claim not found for user");
            return Task.CompletedTask;
        }
        
        if (DateTime.TryParse(dateOfBirthClaim.Value, out var dateOfBirth))
        {
            var age = DateTime.Today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > DateTime.Today.AddYears(-age))
            {
                age--;
            }
            
            if (age >= requirement.MinimumAge)
            {
                _logger.LogDebug(
                    "User age {Age} meets minimum requirement {MinAge}",
                    age, requirement.MinimumAge);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning(
                    "User age {Age} does not meet minimum requirement {MinAge}",
                    age, requirement.MinimumAge);
            }
        }
        
        return Task.CompletedTask;
    }
}

// Resource-based authorization
public class OrderAuthorizationHandler : 
    AuthorizationHandler<OperationAuthorizationRequirement, Order>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Order resource)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        
        if (requirement.Name == "Edit" || requirement.Name == "Delete")
        {
            // Only order owner or admin can edit/delete
            if (resource.CustomerId.ToString() == userId || 
                context.User.IsInRole("Admin"))
            {
                context.Succeed(requirement);
            }
        }
        else if (requirement.Name == "View")
        {
            // Order owner, admin, or support can view
            if (resource.CustomerId.ToString() == userId || 
                context.User.IsInRole("Admin") ||
                context.User.IsInRole("Support"))
            {
                context.Succeed(requirement);
            }
        }
        
        return Task.CompletedTask;
    }
}

// Register handlers
builder.Services.AddSingleton<IAuthorizationHandler, MinimumAgeHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, OrderAuthorizationHandler>();
```

### Using Authorization in Controllers

Apply authorization policies to protect endpoints:

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize] // Requires authentication for all actions
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IAuthorizationService _authorizationService;
    
    public OrdersController(
        IOrderService orderService,
        IAuthorizationService authorizationService)
    {
        _orderService = orderService;
        _authorizationService = authorizationService;
    }
    
    [HttpGet]
    [Authorize(Policy = "CanViewOrders")]
    public async Task<IActionResult> GetOrders()
    {
        var orders = await _orderService.GetOrdersAsync();
        return Ok(orders);
    }
    
    [HttpPost]
    [Authorize(Policy = "CanCreateOrders")]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        var order = await _orderService.CreateOrderAsync(request);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        
        if (order == null)
            return NotFound();
        
        // Resource-based authorization
        var authResult = await _authorizationService.AuthorizeAsync(
            User, 
            order, 
            "View");
        
        if (!authResult.Succeeded)
            return Forbid();
        
        return Ok(order);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateOrder(Guid id, UpdateOrderRequest request)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        
        if (order == null)
            return NotFound();
        
        var authResult = await _authorizationService.AuthorizeAsync(
            User,
            order,
            "Edit");
        
        if (!authResult.Succeeded)
            return Forbid();
        
        await _orderService.UpdateOrderAsync(id, request);
        return NoContent();
    }
    
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteOrder(Guid id)
    {
        await _orderService.DeleteOrderAsync(id);
        return NoContent();
    }
}
```

## Azure Entra ID (formerly Azure AD) Integration

For enterprise applications, integrate with Azure Entra ID:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Validate additional claims
                var tenantId = context.Principal?.FindFirst("tid")?.Value;
                var allowedTenants = builder.Configuration
                    .GetSection("AllowedTenants")
                    .Get<List<string>>();
                
                if (allowedTenants != null && 
                    !allowedTenants.Contains(tenantId))
                {
                    context.Fail("Unauthorized tenant");
                }
            }
        };
    },
    options => builder.Configuration.Bind("AzureAd", options));

// appsettings.json
/*
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "yourdomain.onmicrosoft.com",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "Audience": "api://your-api-id"
  },
  "AllowedTenants": ["tenant-id-1", "tenant-id-2"]
}
*/
```

## Service-to-Service Authentication

Secure communication between microservices using Azure Managed Identity:

```csharp
// Configure HTTP client with managed identity authentication
builder.Services.AddHttpClient<IOrderServiceClient, OrderServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:OrderService:Url"]!);
    client.DefaultRequestHeaders.Add("User-Agent", "CustomerService/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ManagedIdentityHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Managed identity authentication handler
public class ManagedIdentityHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string _scope;
    private readonly ILogger<ManagedIdentityHandler> _logger;
    private AccessToken? _cachedToken;
    
    public ManagedIdentityHandler(
        IConfiguration configuration,
        ILogger<ManagedIdentityHandler> logger)
    {
        // DefaultAzureCredential works locally (using Azure CLI, VS, etc.) 
        // and in Azure (using Managed Identity)
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = false,
            ExcludeWorkloadIdentityCredential = false,
            ExcludeManagedIdentityCredential = false,
            ExcludeSharedTokenCacheCredential = true,
            ExcludeVisualStudioCredential = false,
            ExcludeVisualStudioCodeCredential = false,
            ExcludeAzureCliCredential = false,
            ExcludeAzurePowerShellCredential = true,
            ExcludeInteractiveBrowserCredential = true
        });
        
        _scope = configuration["Services:OrderService:Scope"]!;
        _logger = logger;
    }
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Check if cached token is still valid
        if (_cachedToken == null || 
            _cachedToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            try
            {
                var tokenContext = new TokenRequestContext(new[] { _scope });
                _cachedToken = await _credential.GetTokenAsync(
                    tokenContext, 
                    cancellationToken);
                
                _logger.LogDebug("Successfully acquired access token");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acquire access token");
                throw;
            }
        }
        
        request.Headers.Authorization = 
            new AuthenticationHeaderValue("Bearer", _cachedToken.Value.Token);
        
        return await base.SendAsync(request, cancellationToken);
    }
}

// Polly retry policy
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        .WaitAndRetryAsync(3, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

// Circuit breaker policy
static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
```

## API Rate Limiting

Protect your APIs from abuse and ensure fair resource usage with rate limiting (.NET 7+):

```csharp
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(options =>
{
    // Global rejection response
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = 
                retryAfter.TotalSeconds.ToString();
        }
        
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please try again later." },
            cancellationToken: cancellationToken);
    };
    
    // Fixed window limiter
    options.AddFixedWindowLimiter("fixed", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 10;
    });
    
    // Sliding window limiter - more granular than fixed window
    options.AddSlidingWindowLimiter("sliding", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.SegmentsPerWindow = 6; // Divides window into 10-second segments
    });
    
    // Token bucket - allows bursts while maintaining average rate
    options.AddTokenBucketLimiter("token", options =>
    {
        options.TokenLimit = 100;
        options.ReplenishmentPeriod = TimeSpan.FromMinutes(1);
        options.TokensPerPeriod = 20;
        options.AutoReplenishment = true;
    });
    
    // Concurrency limiter - limits concurrent requests
    options.AddConcurrencyLimiter("concurrency", options =>
    {
        options.PermitLimit = 50;
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 100;
    });
    
    // Per-user rate limiting using partition
    options.AddPolicy("per-user", httpContext =>
    {
        var username = httpContext.User.Identity?.Name ?? "anonymous";
        
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: username,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6
            });
    });
    
    // Per-IP rate limiting
    options.AddPolicy("per-ip", httpContext =>
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        return RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: ipAddress,
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 50,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 10,
                AutoReplenishment = true
            });
    });
    
    // Tiered rate limiting based on user role
    options.AddPolicy("tiered", httpContext =>
    {
        var tier = httpContext.User.IsInRole("Premium") ? "premium" : "free";
        
        var limits = tier == "premium"
            ? new { PermitLimit = 1000, Window = TimeSpan.FromMinutes(1) }
            : new { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) };
        
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: $"{tier}:{httpContext.User.Identity?.Name}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limits.PermitLimit,
                Window = limits.Window,
                SegmentsPerWindow = 6
            });
    });
});

var app = builder.Build();

// Enable rate limiting middleware (must be before UseAuthorization)
app.UseRateLimiter();
```

### Applying Rate Limiting to Endpoints

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    // Apply named policy
    [EnableRateLimiting("per-user")]
    [HttpGet]
    public async Task<IActionResult> GetProducts()
    {
        // Implementation
        return Ok();
    }
    
    // Disable rate limiting for specific endpoint
    [DisableRateLimiting]
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy" });
    }
    
    // Apply different policy
    [EnableRateLimiting("token")]
    [HttpPost]
    public async Task<IActionResult> CreateProduct(Product product)
    {
        // Implementation
        return Created();
    }
}

// Apply rate limiting to minimal APIs
app.MapGet("/api/orders", async (IOrderService service) =>
{
    var orders = await service.GetOrdersAsync();
    return Results.Ok(orders);
})
.RequireRateLimiting("per-user");

// Apply globally to all endpoints
app.MapGroup("/api")
    .RequireRateLimiting("sliding");
```

## Secrets Management with Azure Key Vault

Never store secrets in code or configuration files. Use Azure Key Vault:

```csharp
// Install packages:
// - Azure.Identity
// - Azure.Extensions.AspNetCore.Configuration.Secrets

var keyVaultUrl = builder.Configuration["KeyVault:Url"];

if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential());
}

// Access secrets through IConfiguration
public class OrderService
{
    private readonly string _apiKey;
    private readonly string _connectionString;
    
    public OrderService(IConfiguration configuration)
    {
        // These are loaded from Key Vault
        _apiKey = configuration["PaymentGateway-ApiKey"];
        _connectionString = configuration["Database-ConnectionString"];
    }
}

// For more control, use SecretClient directly
builder.Services.AddSingleton(sp =>
{
    var keyVaultUrl = builder.Configuration["KeyVault:Url"];
    return new SecretClient(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential());
});

public class PaymentService
{
    private readonly SecretClient _secretClient;
    
    public PaymentService(SecretClient secretClient)
    {
        _secretClient = secretClient;
    }
    
    public async Task<string> GetApiKeyAsync()
    {
        var secret = await _secretClient.GetSecretAsync("PaymentGateway-ApiKey");
        return secret.Value.Value;
    }
}
```

## Data Protection and Encryption

### Encrypt Sensitive Data at Rest

```csharp
using Microsoft.AspNetCore.DataProtection;

// Configure data protection with Azure Key Vault
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(
        new Uri(builder.Configuration["DataProtection:BlobUri"]!),
        new DefaultAzureCredential())
    .ProtectKeysWithAzureKeyVault(
        new Uri(builder.Configuration["DataProtection:KeyVaultKeyUri"]!),
        new DefaultAzureCredential())
    .SetApplicationName("MyApp");

// Use data protection to encrypt sensitive data
public class CustomerService
{
    private readonly IDataProtector _protector;
    
    public CustomerService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("CustomerService.v1");
    }
    
    public string EncryptSensitiveData(string plainText)
    {
        return _protector.Protect(plainText);
    }
    
    public string DecryptSensitiveData(string cipherText)
    {
        return _protector.Unprotect(cipherText);
    }
}
```

### Always Use HTTPS

```csharp
// Enforce HTTPS
builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
    options.HttpsPort = 443;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
```

## Security Headers

Add security headers to protect against common attacks:

```csharp
app.Use(async (context, next) =>
{
    // Prevent clickjacking
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    
    // Prevent MIME type sniffing
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    
    // XSS protection
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    
    // Content Security Policy
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';");
    
    // Referrer policy
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    
    // Permissions policy
    context.Response.Headers.Append("Permissions-Policy",
        "geolocation=(), microphone=(), camera=()");
    
    await next();
});

// Or use a middleware package
// Install: NWebsec.AspNetCore.Middleware
app.UseXContentTypeOptions();
app.UseReferrerPolicy(opts => opts.StrictOriginWhenCrossOrigin());
app.UseXXssProtection(opts => opts.EnabledWithBlockMode());
app.UseXfo(opts => opts.Deny());
```

## Input Validation and Sanitization

Always validate and sanitize user input:

```csharp
public class CreateOrderRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CustomerName { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [Range(1, int.MaxValue)]
    public decimal Amount { get; set; }
    
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public List<OrderItem> Items { get; set; } = new();
}

// Custom validation
public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-zA-Z\s]+$").WithMessage("Name can only contain letters");
        
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
        
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .LessThan(1000000);
        
        RuleFor(x => x.Items)
            .NotEmpty()
            .Must(items => items.Count <= 100)
            .WithMessage("Cannot exceed 100 items per order");
    }
}
```

## Best Practices Summary

**Authentication & Authorization:**
- Use Azure Entra ID for enterprise applications
- Implement JWT with short expiration times
- Apply principle of least privilege
- Use resource-based authorization for fine-grained control
- Regularly rotate secrets and credentials

**Service-to-Service:**
- Use Managed Identity for Azure resources
- Implement certificate-based authentication for non-Azure services
- Never embed credentials in code
- Use Azure Key Vault for all secrets

**API Protection:**
- Implement rate limiting on all public APIs
- Use different rate limits for different user tiers
- Monitor and alert on rate limit violations
- Consider using Azure API Management for advanced scenarios

**Data Security:**
- Encrypt sensitive data at rest and in transit
- Use Azure Key Vault for key management
- Implement data protection with key rotation
- Follow GDPR and compliance requirements

**General Security:**
- Always use HTTPS in production
- Implement proper security headers
- Validate and sanitize all user input
- Log security events for monitoring
- Conduct regular security audits
- Keep dependencies updated
- Use Azure Security Center for threat detection
- Implement proper error handling without exposing sensitive information

**Azure-Specific:**
- Enable Azure DDoS Protection
- Use Azure Front Door WAF for web application protection
- Implement Azure Monitor and Log Analytics for security monitoring
- Use Azure Private Link for secure service connections
- Enable diagnostic logging on all Azure resources
- Use Azure Policy to enforce security standards
