---
title: "Contract Stability and Versioning in .NET and Azure"
date: 2025-10-02
slug: contract-stability-and-versioning-dotnet-core-azure
description: "In distributed systems, APIs serve as contracts between services and their consumers. As systems evolve, maintaining stable contracts while introducing new features becomes a critical challenge. Breaking changes can cascade failures across dependent services, causing production o"
tags: [apis]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

This article explores proven approaches to API versioning in .NET, consumer-driven contract testing with Pact.NET, and best practices for managing deprecation cycles. We'll cover modern techniques for maintaining contract stability in cloud-native applications deployed to Azure.

## API Versioning Strategies

Different versioning approaches suit different scenarios. Choose based on your API consumers, organizational standards, and technical constraints.

### 1. URL Versioning (Recommended)

The most explicit and widely adopted approach, embedding the version directly in the URL path:

```csharp
// Install package
// dotnet add package Asp.Versioning.Mvc

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true; // Adds version info to response headers
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV"; // Format version as 'v1', 'v2', etc.
    options.SubstituteApiVersionInUrl = true;
});

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;
    
    public OrdersController(
        IOrderService orderService,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }
    
    // Version 1.0 - Original implementation
    [HttpGet("{id}")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<OrderV1Dto>> GetV1(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        
        if (order == null)
            return NotFound();
        
        return Ok(new OrderV1Dto
        {
            Id = order.Id,
            Total = order.TotalAmount,
            Status = order.Status.ToString()
        });
    }
    
    // Version 2.0 - Enhanced with additional fields
    [HttpGet("{id}")]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<OrderV2Dto>> GetV2(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        
        if (order == null)
            return NotFound();
        
        return Ok(new OrderV2Dto
        {
            Id = order.Id,
            TotalAmount = order.TotalAmount,
            Currency = order.Currency,
            Status = order.Status.ToString(),
            CustomerInfo = new CustomerInfoDto
            {
                CustomerId = order.CustomerId,
                CustomerName = order.CustomerName
            },
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        });
    }
    
    // Version 2.0 - New endpoint not available in v1
    [HttpPost]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<OrderV2Dto>> CreateOrder(CreateOrderRequest request)
    {
        var order = await _orderService.CreateOrderAsync(request);
        return CreatedAtAction(nameof(GetV2), 
            new { id = order.Id, version = "2.0" }, 
            order);
    }
}

// DTOs for different versions
public class OrderV1Dto
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
}

public class OrderV2Dto
{
    public Guid Id { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; }
    public string Status { get; set; }
    public CustomerInfoDto CustomerInfo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CustomerInfoDto
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; }
}
```

**Pros:**
- Clear and explicit in URLs and documentation
- Easy to route and cache
- Simple for consumers to understand
- Works well with API gateways

**Cons:**
- URLs change between versions
- Can lead to code duplication

### 2. Header Versioning

Version information is passed via HTTP headers, keeping URLs clean:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new HeaderApiVersionReader("X-API-Version");
});

[ApiController]
[Route("api/[controller]")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class ProductsController : ControllerBase
{
    [HttpGet("{id}")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<ProductV1>> GetV1(Guid id)
    {
        // Implementation
    }
    
    [HttpGet("{id}")]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<ProductV2>> GetV2(Guid id)
    {
        // Implementation
    }
}

// Client usage:
// GET /api/products/123
// X-API-Version: 2.0
```

**Pros:**
- Clean, stable URLs
- Good for RESTful purists
- Easier URL structure

**Cons:**
- Less discoverable
- Harder to test in browsers
- Caching complications

### 3. Media Type Versioning (Content Negotiation)

Version is specified in the Accept header using custom media types:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new MediaTypeApiVersionReader();
});

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    [HttpGet("{id}")]
    [MapToApiVersion("1.0")]
    [Produces("application/vnd.company.customer.v1+json")]
    public async Task<ActionResult<CustomerV1>> GetV1(Guid id)
    {
        // Implementation
    }
    
    [HttpGet("{id}")]
    [MapToApiVersion("2.0")]
    [Produces("application/vnd.company.customer.v2+json")]
    public async Task<ActionResult<CustomerV2>> GetV2(Guid id)
    {
        // Implementation
    }
}

// Client usage:
// GET /api/customers/123
// Accept: application/vnd.company.customer.v2+json
```

**Pros:**
- RESTful and follows HTTP standards
- Clean URLs
- Precise control over content negotiation

**Cons:**
- Most complex to implement
- Harder for consumers to understand
- Limited tooling support

### 4. Query Parameter Versioning

Version is passed as a query string parameter:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new QueryStringApiVersionReader("api-version");
});

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<IEnumerable<OrderV1>>> GetAllV1()
    {
        // Implementation
    }
    
    [HttpGet]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<IEnumerable<OrderV2>>> GetAllV2()
    {
        // Implementation
    }
}

// Client usage:
// GET /api/orders?api-version=2.0
```

**Pros:**
- Easy to implement
- Simple for testing
- Optional versioning

**Cons:**
- Query parameters can be lost or modified
- Not as clean as URL versioning
- Can conflict with other query parameters

## Swagger/OpenAPI Integration

Configure Swagger to document multiple API versions:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Orders API v1",
        Description = "Original version of the Orders API",
        Contact = new OpenApiContact
        {
            Name = "API Support",
            Email = "api-support@company.com"
        }
    });
    
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Version = "v2",
        Title = "Orders API v2",
        Description = "Enhanced version with additional customer information",
        Contact = new OpenApiContact
        {
            Name = "API Support",
            Email = "api-support@company.com"
        }
    });
    
    // Filter endpoints by version
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (!apiDesc.TryGetMethodInfo(out var methodInfo))
            return false;
        
        var versions = methodInfo.DeclaringType?
            .GetCustomAttributes(true)
            .OfType<ApiVersionAttribute>()
            .SelectMany(attr => attr.Versions);
        
        return versions?.Any(v => $"v{v}" == docName) ?? false;
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Orders API V1");
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "Orders API V2");
});
```

## Consumer-Driven Contracts (CDC) with Pact.NET

Consumer-Driven Contract testing ensures that provider services meet the expectations defined by their consumers. This prevents breaking changes from reaching production.

### Setting Up Pact.NET

```bash
# Install Pact.NET packages
dotnet add package PactNet
dotnet add package PactNet.Output.Xunit
```

### Consumer Test (Order Service)

The consumer defines its expectations for the Customer Service API:

```csharp
using PactNet;
using PactNet.Matchers;
using Xunit;
using Xunit.Abstractions;

namespace OrderService.Tests.Pact
{
    public class CustomerServiceConsumerTests : IDisposable
    {
        private readonly IPactBuilderV4 _pactBuilder;
        private readonly ITestOutputHelper _output;
        
        public CustomerServiceConsumerTests(ITestOutputHelper output)
        {
            _output = output;
            
            var pactConfig = new PactConfig
            {
                PactDir = Path.Combine("..", "..", "..", "pacts"),
                LogLevel = PactLogLevel.Debug,
                Outputters = new[] { new XunitOutput(_output) }
            };
            
            _pactBuilder = Pact.V4("OrderService", "CustomerService", pactConfig)
                .WithHttpInteractions();
        }
        
        [Fact]
        public async Task GetCustomer_ReturnsCustomerDetails()
        {
            // Arrange - Define the expected interaction
            _pactBuilder
                .UponReceiving("A request for customer details")
                    .Given("Customer 123 exists")
                    .WithRequest(HttpMethod.Get, "/api/customers/123")
                    .WithHeader("Accept", "application/json")
                .WillRespond()
                    .WithStatus(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithJsonBody(new
                    {
                        id = Match.Type("123"),
                        name = Match.Type("John Doe"),
                        email = Match.Regex("john@example.com", "^[\\w-\\.]+@([\\w-]+\\.)+[\\w-]{2,4}$"),
                        status = Match.Type("Active")
                    });
            
            await _pactBuilder.VerifyAsync(async ctx =>
            {
                // Act - Call the consumer code
                var httpClient = new HttpClient
                {
                    BaseAddress = ctx.MockServerUri
                };
                
                var customerClient = new CustomerServiceClient(httpClient);
                var customer = await customerClient.GetCustomerAsync("123");
                
                // Assert - Verify the consumer behaves correctly
                Assert.NotNull(customer);
                Assert.Equal("123", customer.Id);
                Assert.Equal("John Doe", customer.Name);
                Assert.Equal("john@example.com", customer.Email);
                Assert.Equal("Active", customer.Status);
            });
        }
        
        [Fact]
        public async Task GetCustomer_WhenNotFound_Returns404()
        {
            // Arrange
            _pactBuilder
                .UponReceiving("A request for non-existent customer")
                    .Given("Customer 999 does not exist")
                    .WithRequest(HttpMethod.Get, "/api/customers/999")
                    .WithHeader("Accept", "application/json")
                .WillRespond()
                    .WithStatus(HttpStatusCode.NotFound);
            
            await _pactBuilder.VerifyAsync(async ctx =>
            {
                // Act
                var httpClient = new HttpClient
                {
                    BaseAddress = ctx.MockServerUri
                };
                
                var customerClient = new CustomerServiceClient(httpClient);
                var customer = await customerClient.GetCustomerAsync("999");
                
                // Assert
                Assert.Null(customer);
            });
        }
        
        [Fact]
        public async Task CreateCustomer_ReturnsCreatedCustomer()
        {
            // Arrange
            _pactBuilder
                .UponReceiving("A request to create a customer")
                    .Given("No customer with email john@example.com exists")
                    .WithRequest(HttpMethod.Post, "/api/customers")
                    .WithHeader("Content-Type", "application/json")
                    .WithJsonBody(new
                    {
                        name = "John Doe",
                        email = "john@example.com"
                    })
                .WillRespond()
                    .WithStatus(HttpStatusCode.Created)
                    .WithHeader("Content-Type", "application/json")
                    .WithJsonBody(new
                    {
                        id = Match.Type("new-id"),
                        name = Match.Type("John Doe"),
                        email = Match.Type("john@example.com"),
                        status = Match.Type("Active")
                    });
            
            await _pactBuilder.VerifyAsync(async ctx =>
            {
                // Act
                var httpClient = new HttpClient
                {
                    BaseAddress = ctx.MockServerUri
                };
                
                var customerClient = new CustomerServiceClient(httpClient);
                var customer = await customerClient.CreateCustomerAsync(
                    new CreateCustomerRequest
                    {
                        Name = "John Doe",
                        Email = "john@example.com"
                    });
                
                // Assert
                Assert.NotNull(customer);
                Assert.NotNull(customer.Id);
                Assert.Equal("John Doe", customer.Name);
            });
        }
        
        public void Dispose()
        {
            _pactBuilder.Dispose();
        }
    }
}
```

### Provider Test (Customer Service)

The provider verifies it meets the contract defined by consumers:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using PactNet;
using PactNet.Output.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace CustomerService.Tests.Pact
{
    public class CustomerServiceProviderTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _pactFilePath;
        
        public CustomerServiceProviderTests(ITestOutputHelper output)
        {
            _output = output;
            _pactFilePath = Path.Combine("..", "..", "..", "..", 
                "OrderService.Tests", "pacts", 
                "orderservice-customerservice.json");
        }
        
        [Fact]
        public void EnsureCustomerServiceHonorsContract()
        {
            // Arrange - Start the provider API
            var webHost = new WebHostBuilder()
                .UseStartup<Startup>()
                .UseUrls("http://localhost:5000")
                .Build();
            
            webHost.Start();
            
            try
            {
                // Act & Assert - Verify the provider against the contract
                var config = new PactVerifierConfig
                {
                    Outputters = new[] { new XunitOutput(_output) },
                    LogLevel = PactLogLevel.Debug
                };
                
                new PactVerifier(config)
                    .ServiceProvider("CustomerService", new Uri("http://localhost:5000"))
                    .WithFileSource(new FileInfo(_pactFilePath))
                    .WithProviderStateUrl(new Uri("http://localhost:5000/provider-states"))
                    .Verify();
            }
            finally
            {
                webHost.StopAsync().Wait();
            }
        }
    }
    
    // Provider state controller for test data setup
    [ApiController]
    [Route("provider-states")]
    public class ProviderStatesController : ControllerBase
    {
        private readonly ICustomerRepository _repository;
        
        public ProviderStatesController(ICustomerRepository repository)
        {
            _repository = repository;
        }
        
        [HttpPost]
        public async Task<IActionResult> SetProviderState([FromBody] ProviderState state)
        {
            switch (state.State)
            {
                case "Customer 123 exists":
                    await _repository.SeedTestCustomer(new Customer
                    {
                        Id = "123",
                        Name = "John Doe",
                        Email = "john@example.com",
                        Status = "Active"
                    });
                    break;
                    
                case "Customer 999 does not exist":
                    await _repository.DeleteCustomerIfExists("999");
                    break;
                    
                case "No customer with email john@example.com exists":
                    await _repository.DeleteCustomerByEmail("john@example.com");
                    break;
            }
            
            return Ok();
        }
    }
    
    public class ProviderState
    {
        public string State { get; set; }
        public Dictionary<string, object> Params { get; set; }
    }
}
```

### CI/CD Integration with Pact Broker

Use Pact Broker to share contracts between teams:

```yaml
# Azure DevOps Pipeline
trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: DotNetCoreCLI@2
  displayName: 'Run Consumer Tests'
  inputs:
    command: 'test'
    projects: '**/OrderService.Tests.csproj'

- task: Bash@3
  displayName: 'Publish Pacts to Broker'
  inputs:
    targetType: 'inline'
    script: |
      dotnet tool install --global pact-net-cli
      pact-broker publish \
        --consumer-app-version $(Build.BuildNumber) \
        --broker-base-url $(PactBrokerUrl) \
        --broker-token $(PactBrokerToken) \
        ./pacts

- task: DotNetCoreCLI@2
  displayName: 'Run Provider Tests'
  inputs:
    command: 'test'
    projects: '**/CustomerService.Tests.csproj'
```

## Alternatives to Pact

While Pact is popular, other options exist:

**OpenAPI/Swagger Schema Validation:**
```csharp
// Validate responses against OpenAPI schema
public class OpenApiValidationTests
{
    [Fact]
    public async Task GetCustomer_ResponseMatchesSchema()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/customers/123");
        
        var schema = await File.ReadAllTextAsync("openapi.json");
        var validator = new OpenApiValidator(schema);
        
        await validator.ValidateResponseAsync(
            "/api/customers/{id}",
            "get",
            response);
    }
}
```

**Postman Contract Testing:**
- Define contracts in Postman collections
- Run with Newman in CI/CD
- Good for teams already using Postman

**Specmatic:**
- Specification-first approach
- Generates tests from OpenAPI specs
- Supports multiple languages

## Additive Changes and Backward Compatibility

The golden rule: **Always add, never remove or change** (Postel's Law: "Be conservative in what you send, be liberal in what you accept").

### Safe Additive Changes

```csharp
// V1 - Original
public class OrderDto
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
}

// V1.1 - Adding optional fields (backward compatible)
public class OrderDto
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
    
    // New optional fields with defaults
    public string Currency { get; set; } = "USD";
    public DateTime? CreatedAt { get; set; }
    public List<string> Tags { get; set; } = new();
}

// Configure JSON serialization to ignore nulls
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = 
            JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });
```

### Deprecation Strategy

Mark deprecated fields and provide migration paths:

```csharp
public class OrderDto
{
    public Guid Id { get; set; }
    
    // Old field - deprecated
    [Obsolete("Use TotalAmount instead. Will be removed in v3.0 (2026-01-01)")]
    [JsonPropertyName("total")]
    public decimal Total
    {
        get => TotalAmount;
        set => TotalAmount = value;
    }
    
    // New field - recommended
    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }
    
    public string Status { get; set; }
}
```

### Deprecation Headers

Communicate deprecation to API consumers:

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    [ApiVersion("1.0", Deprecated = true)]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<OrderV1Dto>> GetV1(Guid id)
    {
        // Add deprecation headers
        Response.Headers.Append("X-API-Deprecated", "true");
        Response.Headers.Append("X-API-Sunset", "2025-12-31");
        Response.Headers.Append("X-API-Deprecation-Info", 
            "https://api.company.com/docs/migration/v1-to-v2");
        Response.Headers.Append("X-API-Deprecation-Date", "2025-06-01");
        Response.Headers.Append("Link", 
            "</api/v2/orders/{id}>; rel=\"successor-version\"");
        
        var order = await _orderService.GetOrderByIdAsync(id);
        
        if (order == null)
            return NotFound();
        
        return Ok(MapToV1Dto(order));
    }
    
    [HttpGet("{id}")]
    [ApiVersion("2.0")]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<OrderV2Dto>> GetV2(Guid id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        
        if (order == null)
            return NotFound();
        
        return Ok(MapToV2Dto(order));
    }
}
```

### Deprecation Policy

Establish a clear deprecation policy:

```markdown
# API Deprecation Policy

1. **Announcement**: Deprecation announced at least 6 months before removal
2. **Documentation**: Migration guides published with deprecation notice
3. **Headers**: All deprecated endpoints return deprecation headers
4. **Monitoring**: Track usage of deprecated endpoints
5. **Communication**: Email notifications to registered API consumers
6. **Support Period**: Deprecated versions supported for 6 months minimum
7. **Removal**: Version removed only after support period expires

## Deprecation Timeline Example:
- 2025-01-15: Announce v1 deprecation
- 2025-01-15 to 2025-07-15: Both v1 and v2 supported
- 2025-06-01: Final reminder emails sent
- 2025-07-15: v1 removed, only v2 supported
```

## Monitoring API Versions

Track version usage to inform deprecation decisions:

```csharp
public class ApiVersionMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiVersionMetricsMiddleware> _logger;
    private readonly TelemetryClient _telemetry;
    
    public ApiVersionMetricsMiddleware(
        RequestDelegate next,
        ILogger<ApiVersionMetricsMiddleware> logger,
        TelemetryClient telemetry)
    {
        _next = next;
        _logger = logger;
        _telemetry = telemetry;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var apiVersion = context.GetRequestedApiVersion()?.ToString() ?? "unknown";
        var endpoint = context.Request.Path.Value;
        
        // Track metrics
        _telemetry.TrackMetric("ApiVersionUsage", 1, new Dictionary<string, string>
        {
            ["Version"] = apiVersion,
            ["Endpoint"] = endpoint,
            ["Method"] = context.Request.Method
        });
        
        // Add version to response headers for debugging
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.Append("X-API-Version", apiVersion);
            return Task.CompletedTask;
        });
        
        await _next(context);
    }
}

// Register middleware
app.UseMiddleware<ApiVersionMetricsMiddleware>();
```

## Azure API Management Integration

For enterprise scenarios, use Azure API Management for advanced versioning:

```csharp
// Configure policies in Azure API Management

// Version routing policy
<choose>
    <when condition="@(context.Request.Headers.GetValueOrDefault("X-API-Version","1.0") == "2.0")">
        <set-backend-service base-url="https://api-v2.company.com" />
    </when>
    <otherwise>
        <set-backend-service base-url="https://api-v1.company.com" />
    </otherwise>
</choose>

// Deprecation warning policy
<choose>
    <when condition="@(context.Request.Headers.GetValueOrDefault("X-API-Version","1.0") == "1.0")">
        <set-header name="X-API-Deprecated" exists-action="override">
            <value>true</value>
        </set-header>
        <set-header name="X-API-Sunset" exists-action="override">
            <value>2025-12-31</value>
        </set-header>
    </when>
</choose>
```

## Best Practices Summary

**Versioning Strategy:**
- Use URL versioning for clarity and simplicity
- Version from day one, even v1.0
- Never reuse version numbers
- Document all versions in OpenAPI/Swagger
- Use semantic versioning (Major.Minor.Patch)

**Contract Testing:**
- Implement consumer-driven contracts with Pact
- Run contract tests in CI/CD pipeline
- Use Pact Broker for contract sharing
- Verify providers against all consumer contracts
- Keep contracts in version control

**Backward Compatibility:**
- Follow Postel's Law: add, never remove
- Provide default values for new fields
- Use optional parameters
- Maintain old field names during transition
- Version DTOs separately from domain models

**Deprecation:**
- Establish clear deprecation timeline (6+ months)
- Communicate through multiple channels
- Use deprecation headers consistently
- Monitor deprecated endpoint usage
- Provide comprehensive migration guides
- Never surprise consumers with breaking changes

**Azure-Specific:**
- Use Azure API Management for enterprise versioning
- Implement version routing policies
- Enable analytics for version tracking
- Use Azure Monitor for deprecation metrics
- Leverage Azure DevOps for contract testing in CI/CD

**Documentation:**
- Document all versions in Swagger UI
- Provide migration guides for each version
- Include examples for all versions
- Maintain changelog with breaking changes
- Link to deprecation information in API responses
