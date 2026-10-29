---
title: "Shared Libraries Management in .NET and Azure"
date: 2025-10-02
slug: shared-libraries-management-dotnet-core-azure
description: "Shared libraries are essential infrastructure components in microservices architectures, but they require careful design to avoid creating tight coupling between services. This guide explores best practices for managing shared libraries in .NET 9, leveraging modern tooling like ."
tags: [delivery]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

## Core Principle: Avoid Domain Coupling

The fundamental rule is simple: **shared libraries should contain only cross-cutting concerns, never domain logic**. When you share domain entities across services, you break bounded context boundaries and create the very coupling microservices aim to eliminate.

## Recommended Shared Library Structure

```
Solution Structure:
├── src/
│   ├── Services/
│   │   ├── OrderService/
│   │   │   └── OrderService.Domain/        # Private domain model
│   │   ├── CustomerService/
│   │   │   └── CustomerService.Domain/     # Private domain model
│   │   └── PaymentService/
│   │       └── PaymentService.Domain/      # Private domain model
│   └── Shared/
│       ├── Company.Common/                  # ✅ Utilities, extensions
│       ├── Company.Observability/           # ✅ Telemetry, logging
│       ├── Company.Messaging/               # ✅ Message bus abstractions
│       ├── Company.Authentication/          # ✅ Auth middleware
│       ├── Company.Contracts/               # ✅ Integration contracts (DTOs)
│       └── Company.ServiceDefaults/         # ✅ .NET Aspire defaults
```

## What Belongs in Shared Libraries

### 1. Cross-Cutting Utilities (Company.Common)

Infrastructure code that has no business logic:

```csharp
// Company.Common - Extensions and utilities
namespace Company.Common.Extensions;

public static class DateTimeExtensions
{
    public static DateTime ToUtc(this DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Utc 
            ? dateTime 
            : dateTime.ToUniversalTime();
    }
    
    public static DateOnly ToDateOnly(this DateTime dateTime)
        => DateOnly.FromDateTime(dateTime);
}

public static class StringExtensions
{
    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? value)
        => string.IsNullOrWhiteSpace(value);
}

// Result pattern for consistent error handling
public readonly record struct Result<T>
{
    public required T Value { get; init; }
    public required bool IsSuccess { get; init; }
    public string? Error { get; init; }
    
    public static Result<T> Success(T value) 
        => new() { Value = value, IsSuccess = true };
    
    public static Result<T> Failure(string error) 
        => new() { Value = default!, IsSuccess = false, Error = error };
}
```

### 2. Observability Infrastructure (Company.Observability)

Leveraging .NET 9's enhanced observability features:

```csharp
// Company.Observability - Structured logging and telemetry
namespace Company.Observability;

public static class ObservabilityExtensions
{
    public static IHostApplicationBuilder AddCompanyObservability(
        this IHostApplicationBuilder builder)
    {
        // .NET 9 enhanced metrics
        builder.Services.AddMetrics();
        
        // OpenTelemetry with Azure Monitor
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation()
                       .AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation();
                       
                if (builder.Environment.IsProduction())
                {
                    tracing.AddAzureMonitorTraceExporter(options =>
                    {
                        options.ConnectionString = builder.Configuration
                            .GetConnectionString("ApplicationInsights");
                    });
                }
            });
            
        return builder;
    }
}

// Structured logging with LoggerMessage source generators (.NET 9)
public static partial class Log
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Processing order {OrderId} for customer {CustomerId}")]
    public static partial void ProcessingOrder(
        ILogger logger, 
        Guid orderId, 
        Guid customerId);
        
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Failed to process order {OrderId}")]
    public static partial void OrderProcessingFailed(
        ILogger logger, 
        Exception exception,
        Guid orderId);
}
```

### 3. Messaging Abstractions (Company.Messaging)

Event-driven communication patterns without business logic:

```csharp
// Company.Messaging - Message contracts and infrastructure
namespace Company.Messaging;

// Base interfaces - no business rules
public interface IEvent
{
    Guid EventId { get; }
    DateTime OccurredAtUtc { get; }
    string EventType { get; }
}

public interface ICommand
{
    Guid CommandId { get; }
    string CommandType { get; }
}

// Abstract base with common behavior
public abstract record EventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType { get; init; }
    
    protected EventBase()
    {
        EventType = GetType().Name;
    }
}

// Generic message handlers
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
```

### 4. Integration Contracts (Company.Contracts)

Lightweight DTOs for inter-service communication - **no domain logic, just data transfer**:

```csharp
// Company.Contracts - Integration events (versioned)
namespace Company.Contracts.Orders.V1;

// Integration event - published across service boundaries
public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    string Currency = "USD") : EventBase;

// Integration event with versioning
public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    string Status,
    DateTime ChangedAtUtc,
    string? Reason = null) : EventBase;

namespace Company.Contracts.Customers.V1;

public sealed record CustomerRegisteredEvent(
    Guid CustomerId,
    string Email,
    DateTime RegisteredAtUtc) : EventBase;
```

### 5. .NET Aspire Service Defaults (Company.ServiceDefaults)

Standardized service configuration for .NET Aspire orchestration:

```csharp
// Company.ServiceDefaults - Aspire-ready defaults
namespace Company.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddCompanyServiceDefaults(
        this IHostApplicationBuilder builder)
    {
        // Service discovery
        builder.Services.AddServiceDiscovery();
        
        // Resilience with Polly
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        
        // Health checks
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
            
        // Observability
        builder.AddCompanyObservability();
        
        // OpenAPI with scalar (new in .NET 9)
        builder.Services.AddOpenApi();
        
        return builder;
    }
    
    public static WebApplication MapCompanyDefaultEndpoints(
        this WebApplication app)
    {
        // Health checks
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new() 
        {
            Predicate = r => r.Tags.Contains("live")
        });
        
        // OpenAPI
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(); // New in .NET 9
        }
        
        return app;
    }
}
```

## What Does NOT Belong in Shared Libraries

### ❌ Shared Domain Models

This is the most common mistake:

```csharp
// ❌ BAD: Company.Domain - DO NOT DO THIS
public class Order  // Shared domain entity - creates coupling!
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public OrderStatus Status { get; set; }
    
    public void AddLine(string productId, int quantity, decimal price)
    {
        // Business logic shared across services = coupling
        Lines.Add(new OrderLine(productId, quantity, price));
    }
    
    public decimal CalculateTotal() => Lines.Sum(l => l.Price * l.Quantity);
}
```

**Why this is harmful:**

- Breaks bounded context boundaries
- Forces all services to use the same domain model
- Creates deployment dependencies
- Prevents independent evolution
- Violates DDD principles

### ✅ Correct Approach: Private Domain Models

Each service maintains its own domain model:

```csharp
// ✅ GOOD: OrderService.Domain (private to OrderService)
namespace OrderService.Domain;

public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal Total => _lines.Sum(l => l.LineTotal);
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    // Rich domain behavior specific to this service
    public Result<OrderLine> AddLine(string productId, decimal price, int quantity)
    {
        if (quantity <= 0)
            return Result<OrderLine>.Failure("Quantity must be positive");
            
        if (price < 0)
            return Result<OrderLine>.Failure("Price cannot be negative");

        var line = new OrderLine(Guid.NewGuid(), productId, price, quantity);
        _lines.Add(line);
        return Result<OrderLine>.Success(line);
    }

    public void MarkAsConfirmed()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be confirmed");
            
        Status = OrderStatus.Confirmed;
    }

    // Convert to integration event for publishing
    public OrderCreatedEvent ToIntegrationEvent()
    {
        return new OrderCreatedEvent(
            Id, 
            CustomerId, 
            Total, 
            DateTime.UtcNow);
    }
}
```

```csharp
// ✅ GOOD: ShippingService.Domain (different perspective on "Order")
namespace ShippingService.Domain;

// Shipping service has its own view of an order
public sealed class ShipmentOrder
{
    public Guid OrderId { get; private set; }
    public Address ShippingAddress { get; private set; }
    public List<ShipmentItem> Items { get; private set; } = [];
    public ShipmentStatus Status { get; private set; }
    
    // Different behavior, different invariants
    public void PrepareForShipment()
    {
        if (Items.Count == 0)
            throw new InvalidOperationException("Cannot ship empty order");
            
        Status = ShipmentStatus.ReadyForPickup;
    }
    
    // Maps from integration event
    public static ShipmentOrder FromOrderCreatedEvent(
        OrderCreatedEvent evt, 
        Address address)
    {
        return new ShipmentOrder
        {
            OrderId = evt.OrderId,
            ShippingAddress = address,
            Status = ShipmentStatus.Pending
        };
    }
}
```

## Package Management with NuGet

### Semantic Versioning Strategy

Follow [Semantic Versioning](https://semver.org/) and [.NET Versioning Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning):

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <!-- Central version management -->
    <VersionPrefix>2.1.0</VersionPrefix>
    <VersionSuffix Condition="'$(Configuration)' != 'Release'">preview</VersionSuffix>
    
    <!-- .NET 9 settings -->
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>13.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    
    <!-- Package defaults -->
    <Authors>Company Engineering</Authors>
    <Company>YourCompany</Company>
    <PackageProjectUrl>https://nova-globen.se</PackageProjectUrl>
    <RepositoryUrl>https://github.com/your-org/shared-libs</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>
</Project>
```

```xml
<!-- Company.Common.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>Company.Common</PackageId>
    <Description>Common utilities and extensions for Company microservices</Description>
    <PackageTags>utilities;extensions;microservices</PackageTags>
    <Version>$(VersionPrefix)$(VersionSuffix)</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Source Link for better debugging -->
    <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="All"/>
    
    <!-- Nullable annotations -->
    <PackageReference Include="System.Diagnostics.CodeAnalysis" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

### Versioning Strategies

#### Strategy 1: Independent Versioning (Recommended)

Each package evolves independently based on its own changes:

```xml
<!-- Company.Common - Version 2.1.0 -->
<PropertyGroup>
  <Version>2.1.0</Version>
</PropertyGroup>

<!-- Company.Messaging - Version 1.8.3 (depends on Common) -->
<PropertyGroup>
  <Version>1.8.3</Version>
</PropertyGroup>
<ItemGroup>
  <!-- Range allows patch and minor updates -->
  <PackageReference Include="Company.Common" Version="[2.1.0,3.0.0)" />
</ItemGroup>

<!-- Company.Contracts - Version 3.4.1 (depends on Messaging) -->
<PropertyGroup>
  <Version>3.4.1</Version>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Company.Messaging" Version="[1.8.0,2.0.0)" />
</ItemGroup>
```

**Advantages:**

- Each package version reflects its actual changes
- Clear semantic meaning
- Services can upgrade dependencies independently
- Reduces unnecessary updates

**Use when:** Packages have different rates of change and can evolve independently.

#### Strategy 2: Central Package Management (CPM)

.NET supports centralized version management:

```xml
<!-- Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Internal packages -->
    <PackageVersion Include="Company.Common" Version="2.1.0" />
    <PackageVersion Include="Company.Messaging" Version="1.8.3" />
    <PackageVersion Include="Company.Contracts" Version="3.4.1" />
    <PackageVersion Include="Company.Observability" Version="2.0.5" />
    
    <!-- External dependencies -->
    <PackageVersion Include="Azure.Messaging.ServiceBus" Version="7.18.1" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="9.0.0" />
    <PackageReference Include="Aspire.Hosting.AppHost" Version="9.0.0" />
  </ItemGroup>
</Project>
```

```xml
<!-- In project files, just reference without version -->
<ItemGroup>
  <PackageReference Include="Company.Common" />
  <PackageReference Include="Company.Messaging" />
</ItemGroup>
```

**Advantages:**

- Single source of truth for versions
- Easier to ensure consistency
- Prevents version conflicts
- Better for monorepo scenarios

#### Strategy 3: Synchronized Suite Versioning

When packages are released together as a cohesive suite:

```xml
<!-- Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CompanySuiteVersion>3.0.0</CompanySuiteVersion>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageVersion Include="Company.Common" Version="$(CompanySuiteVersion)" />
    <PackageVersion Include="Company.Messaging" Version="$(CompanySuiteVersion)" />
    <PackageVersion Include="Company.Contracts" Version="$(CompanySuiteVersion)" />
    <PackageVersion Include="Company.Observability" Version="$(CompanySuiteVersion)" />
    <PackageVersion Include="Company.ServiceDefaults" Version="$(CompanySuiteVersion)" />
  </ItemGroup>
</Project>
```

**Use when:**

- Packages are designed to work together
- Breaking changes affect multiple packages
- Simplified mental model for consumers
- Regular coordinated releases

## Hotfix and Patching Strategy

### Scenario: Critical Bug in Company.Common

**Current State:**

- Company.Common: 2.1.0
- Company.Messaging: 1.8.3 (depends on Common [2.1.0, 3.0.0))
- Company.Contracts: 3.4.1 (depends on Messaging)

**Hotfix Process:**

```bash
# 1. Create hotfix branch from release tag
git checkout -b hotfix/2.1.1 v2.1.0

# 2. Fix the bug
# ... make changes ...

# 3. Commit with conventional commit
git commit -m "fix: correct UTC conversion edge case

Fixes issue where DateTime.Kind was not preserved
when converting from local to UTC time.

BREAKING CHANGE: none
Closes #1234"

# 4. Update version to 2.1.1
# Edit Company.Common.csproj or Directory.Build.props

# 5. Create pull request and merge to main
# 6. Tag the release
git tag -a v2.1.1 -m "Hotfix: UTC conversion fix"
git push origin v2.1.1
```

**Impact Analysis:**

```
Company.Common: 2.1.0 → 2.1.1 (patch)
├── Company.Messaging: 1.8.3 (no change needed - accepts [2.1.0, 3.0.0))
│   └── Company.Contracts: 3.4.1 (no change needed)
└── Services automatically get fix on next build
```

Services using `Company.Common` will automatically receive the patch because:

1. Version range `[2.1.0, 3.0.0)` includes 2.1.1
2. NuGet restore pulls the latest compatible version
3. No breaking changes in patch releases

### GitVersion Configuration

Automate versioning with GitVersion:

```yaml
# GitVersion.yml
mode: ContinuousDelivery
assembly-versioning-scheme: MajorMinorPatch
tag-prefix: 'v'
continuous-delivery-fallback-tag: ci

branches:
  main:
    regex: ^main$
    mode: ContinuousDelivery
    tag: ''
    increment: Minor
    prevent-increment-of-merged-branch-version: true
    track-merge-target: false
    
  hotfix:
    regex: ^hotfix?[/-]
    mode: ContinuousDelivery
    tag: hotfix
    increment: Patch
    
  feature:
    regex: ^features?[/-]
    mode: ContinuousDelivery
    tag: feature
    increment: Minor
    
  release:
    regex: ^releases?[/-]
    mode: ContinuousDelivery
    tag: ''
    increment: Patch
    
  pull-request:
    regex: ^(pull|pull\-requests|pr)[/-]
    mode: ContinuousDelivery
    tag: pr
    increment: Inherit
```

## CI/CD Pipeline with Azure DevOps

### Build and Pack Pipeline

```yaml
# azure-pipelines-pack.yml
trigger:
  branches:
    include:
    - main
    - release/*
    - hotfix/*
  paths:
    include:
    - 'src/Shared/**'
    - 'azure-pipelines-pack.yml'

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

stages:
- stage: Build
  jobs:
  - job: BuildAndPack
    steps:
    - checkout: self
      fetchDepth: 0  # Required for GitVersion

    - task: UseDotNet@2
      displayName: 'Install .NET 9 SDK'
      inputs:
        version: '9.0.x'
        
    - task: GitVersion@5
      displayName: 'Calculate Version'
      inputs:
        runtime: 'core'
        configFilePath: 'GitVersion.yml'
        
    - script: |
        echo "##vso[build.updatebuildnumber]$(GitVersion.SemVer)"
      displayName: 'Update Build Number'

    - task: DotNetCoreCLI@2
      displayName: 'Restore'
      inputs:
        command: 'restore'
        projects: 'src/Shared/**/*.csproj'
        
    - task: DotNetCoreCLI@2
      displayName: 'Build'
      inputs:
        command: 'build'
        projects: 'src/Shared/**/*.csproj'
        arguments: '--configuration $(buildConfiguration) --no-restore /p:Version=$(GitVersion.NuGetVersion)'
        
    - task: DotNetCoreCLI@2
      displayName: 'Run Tests'
      inputs:
        command: 'test'
        projects: 'tests/**/*.Tests.csproj'
        arguments: '--configuration $(buildConfiguration) --no-build --collect:"XPlat Code Coverage"'
        
    - task: PublishCodeCoverageResults@2
      displayName: 'Publish Code Coverage'
      inputs:
        codeCoverageTool: 'Cobertura'
        summaryFileLocation: '$(Agent.TempDirectory)/**/*cobertura.xml'
        
    - task: DotNetCoreCLI@2
      displayName: 'Pack NuGet Packages'
      inputs:
        command: 'pack'
        packagesToPack: 'src/Shared/**/*.csproj'
        configuration: $(buildConfiguration)
        nobuild: true
        versioningScheme: 'byEnvVar'
        versionEnvVar: 'GitVersion.NuGetVersion'
        packDirectory: '$(Build.ArtifactStagingDirectory)/packages'
        
    - task: PublishPipelineArtifact@1
      displayName: 'Publish Artifacts'
      inputs:
        targetPath: '$(Build.ArtifactStagingDirectory)/packages'
        artifact: 'nuget-packages'
        
- stage: PublishInternal
  dependsOn: Build
  condition: and(succeeded(), or(eq(variables['Build.SourceBranch'], 'refs/heads/main'), startsWith(variables['Build.SourceBranch'], 'refs/heads/release/'), startsWith(variables['Build.SourceBranch'], 'refs/heads/hotfix/')))
  jobs:
  - deployment: PublishToFeed
    environment: 'Internal NuGet Feed'
    strategy:
      runOnce:
        deploy:
          steps:
          - download: current
            artifact: 'nuget-packages'
            
          - task: NuGetCommand@2
            displayName: 'Push to Azure Artifacts'
            inputs:
              command: 'push'
              packagesToPush: '$(Pipeline.Workspace)/nuget-packages/**/*.nupkg;!$(Pipeline.Workspace)/nuget-packages/**/*.symbols.nupkg'
              nuGetFeedType: 'internal'
              publishVstsFeed: 'company-shared-libraries'
              allowPackageConflicts: false
```

### Multi-Stage Pipeline with Approval Gates

```yaml
# azure-pipelines-release.yml
stages:
- stage: Build
  # ... (same as above)

- stage: PublishInternal
  # ... (same as above)
  
- stage: PublishProduction
  dependsOn: PublishInternal
  condition: and(succeeded(), startsWith(variables['Build.SourceBranch'], 'refs/heads/release/'))
  jobs:
  - deployment: PublishToNuGetOrg
    environment: 'Production NuGet' # Requires manual approval
    strategy:
      runOnce:
        deploy:
          steps:
          - download: current
            artifact: 'nuget-packages'
            
          - task: NuGetCommand@2
            displayName: 'Push to NuGet.org'
            inputs:
              command: 'push'
              packagesToPush: '$(Pipeline.Workspace)/nuget-packages/**/*.nupkg;!$(Pipeline.Workspace)/nuget-packages/**/*.symbols.nupkg'
              nuGetFeedType: 'external'
              publishFeedCredentials: 'NuGet.org Service Connection'
```

## Integration with .NET Aspire

### Aspire AppHost Configuration

```csharp
// CompanyPlatform.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// Shared infrastructure
var redis = builder.AddRedis("cache");
var serviceBus = builder.AddAzureServiceBus("messaging");
var sql = builder.AddSqlServer("sql")
    .AddDatabase("orderdb");

// Services with shared defaults
var orderService = builder.AddProject<Projects.OrderService>("order-service")
    .WithReference(sql)
    .WithReference(serviceBus)
    .WithReference(redis);

var customerService = builder.AddProject<Projects.CustomerService>("customer-service")
    .WithReference(serviceBus);

var shippingService = builder.AddProject<Projects.ShippingService>("shipping-service")
    .WithReference(serviceBus);

// All services automatically get Company.ServiceDefaults behavior
builder.Build().Run();
```

### Service Implementation

```csharp
// OrderService/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Apply company defaults (includes observability, resilience, health checks)
builder.AddCompanyServiceDefaults();

// Add Aspire components
builder.AddServiceDefaults();
builder.AddSqlServerDbContext<OrderDbContext>("orderdb");
builder.AddAzureServiceBusClient("messaging");

// Service-specific registrations
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

app.MapCompanyDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
```

## Package Consumption Best Practices

### Version Pinning Strategy

```xml
<!-- For stable production services -->
<ItemGroup>
  <!-- Pin to exact versions for predictability -->
  <PackageReference Include="Company.Common" Version="2.1.0" />
  <PackageReference Include="Company.Messaging" Version="1.8.3" />
</ItemGroup>

<!-- For active development -->
<ItemGroup>
  <!-- Use ranges to get patches automatically -->
  <PackageReference Include="Company.Common" Version="[2.1.0,2.2.0)" />
  <PackageReference Include="Company.Messaging" Version="[1.8.0,2.0.0)" />
</ItemGroup>
```

### Dependency Resolution

Configure NuGet to prioritize your internal feed:

```xml
<!-- nuget.config -->
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <!-- Company internal feed first -->
    <add key="Company" value="https://pkgs.dev.azure.com/yourorg/_packaging/company-shared-libraries/nuget/v3/index.json" />
    <!-- Public NuGet.org second -->
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  
  <packageSourceMapping>
    <packageSource key="Company">
      <package pattern="Company.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

## Monitoring and Observability

### Package Health Dashboard

Track package adoption and health:

```csharp
// Custom metrics for package usage
public class PackageHealthMetrics
{
    private readonly Counter<int> _packageDownloads;
    private readonly Histogram<double> _buildDuration;
    
    public PackageHealthMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Company.Packages");
        
        _packageDownloads = meter.CreateCounter<int>(
            "package.downloads",
            description: "Number of package downloads");
            
        _buildDuration = meter.CreateHistogram<double>(
            "package.build.duration",
            unit: "s",
            description: "Package build duration");
    }
    
    public void RecordDownload(string packageId, string version)
    {
        _packageDownloads.Add(1, 
            new KeyValuePair<string, object?>("package.id", packageId),
            new KeyValuePair<string, object?>("package.version", version));
    }
}
```

### Azure Monitor Integration

```csharp
// Application Insights for package telemetry
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration
        .GetConnectionString("ApplicationInsights");
});

// Custom telemetry for package health
builder.Services.AddSingleton<ITelemetryInitializer, PackageTelemetryInitializer>();

public class PackageTelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        if (telemetry is ISupportProperties propertyTelemetry)
        {
            propertyTelemetry.Properties["SharedLibVersion.Common"] = 
                typeof(Company.Common.Extensions.DateTimeExtensions)
                    .Assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }
}
```

## Breaking Changes Management

### Communicating Breaking Changes

Use attributes and XML documentation to signal changes:

```csharp
// Company.Common
namespace Company.Common.Extensions;

public static class DateTimeExtensions
{
    // Deprecated method - will be removed in v3.0.0
    [Obsolete("Use ToUtc() instead. This method will be removed in v3.0.0", false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime ConvertToUtc(this DateTime dateTime)
        => dateTime.ToUtc();
    
    /// <summary>
    /// Converts a DateTime to UTC, preserving the underlying instant in time.
    /// </summary>
    /// <param name="dateTime">The date and time to convert.</param>
    /// <returns>The date and time in UTC.</returns>
    /// <remarks>
    /// Added in v2.1.0. This replaces the deprecated ConvertToUtc method.
    /// </remarks>
    public static DateTime ToUtc(this DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Utc 
            ? dateTime 
            : dateTime.ToUniversalTime();
    }
}
```

### Migration Guides

Include migration documentation in your packages:

```markdown
<!-- MIGRATION-v3.0.md -->
# Migration Guide: v2.x to v3.0

## Breaking Changes

### Company.Common

**Removed: `DateTimeExtensions.ConvertToUtc()`**
- **Replacement:** Use `DateTimeExtensions.ToUtc()`
- **Migration:**
  ```csharp
  // Before (v2.x)
  var utcTime = localTime.ConvertToUtc();
  
  // After (v3.0)
  var utcTime = localTime.ToUtc();
  ```

**Changed: `Result<T>` now uses `required` properties**
- **Impact:** Cannot use object initializer without all required properties
- **Migration:**
  ```csharp
  // Before (v2.x)
  var result = new Result<string> { Value = "test", IsSuccess = true };
  
  // After (v3.0) - use factory methods
  var result = Result<string>.Success("test");
  ```

### Company.Messaging

**Removed: `EventBase.Timestamp`**
- **Replacement:** Use `EventBase.OccurredAtUtc`
- **Reason:** Improved clarity and UTC enforcement

## New Features

### Company.Common
- Added `StringExtensions.IsNullOrWhiteSpace()` with null checking
- Added `Result<T>.Map()` and `Result<T>.Bind()` for functional composition

### Company.Observability
- Full .NET 9 metrics API support
- Enhanced OpenTelemetry integration
- Azure Monitor auto-configuration
```

## Advanced Patterns

### Feature Flags in Shared Libraries

Enable gradual rollout of changes:

```csharp
// Company.Common - Feature flag support
namespace Company.Common.Features;

public interface IFeatureManager
{
    Task<bool> IsEnabledAsync(string featureName);
    Task<T> GetVariantAsync<T>(string featureName, T defaultValue);
}

public static class FeatureExtensions
{
    public static IServiceCollection AddCompanyFeatureManagement(
        this IServiceCollection services)
    {
        services.AddFeatureManagement()
            .AddFeatureFilter<PercentageFilter>()
            .AddFeatureFilter<TargetingFilter>();
            
        return services;
    }
}

// Usage in shared library
public static class DateTimeExtensions
{
    public static async Task<DateTime> ToUtcAsync(
        this DateTime dateTime,
        IFeatureManager? featureManager = null)
    {
        // New implementation behind feature flag
        if (featureManager != null && 
            await featureManager.IsEnabledAsync("UseEnhancedUtcConversion"))
        {
            return EnhancedToUtc(dateTime);
        }
        
        // Legacy implementation
        return dateTime.Kind == DateTimeKind.Utc 
            ? dateTime 
            : dateTime.ToUniversalTime();
    }
    
    private static DateTime EnhancedToUtc(DateTime dateTime)
    {
        // New implementation with additional validations
        // ...
    }
}
```

### Multi-Targeting for Backward Compatibility

Support multiple .NET versions when needed:

```xml
<!-- Company.Common.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <!-- Conditional compilation -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
  </ItemGroup>
  
  <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
    <PackageReference Include="System.Text.Json" Version="9.0.0" />
  </ItemGroup>
</Project>
```

```csharp
// Conditional compilation in code
namespace Company.Common.Serialization;

public static class JsonExtensions
{
#if NET9_0_OR_GREATER
    // Use new .NET 9 features
    public static T? Deserialize<T>(this string json, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options ?? JsonSerializerOptions.Web);
    }
#else
    // Fallback for .NET 8
    public static T? Deserialize<T>(this string json, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
#endif
}
```

## Testing Shared Libraries

### Unit Testing Strategy

```csharp
// Company.Common.Tests/Extensions/DateTimeExtensionsTests.cs
namespace Company.Common.Tests.Extensions;

public class DateTimeExtensionsTests
{
    [Theory]
    [InlineData("2025-01-15T10:30:00Z", DateTimeKind.Utc)]
    [InlineData("2025-01-15T10:30:00", DateTimeKind.Unspecified)]
    [InlineData("2025-01-15T10:30:00", DateTimeKind.Local)]
    public void ToUtc_ShouldReturnUtcDateTime(string dateTimeString, DateTimeKind kind)
    {
        // Arrange
        var dateTime = DateTime.Parse(dateTimeString);
        dateTime = DateTime.SpecifyKind(dateTime, kind);

        // Act
        var result = dateTime.ToUtc();

        // Assert
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_WhenAlreadyUtc_ShouldReturnSameInstance()
    {
        // Arrange
        var utcTime = DateTime.UtcNow;

        // Act
        var result = utcTime.ToUtc();

        // Assert
        result.Should().BeSameAs(utcTime);
    }
}
```

### Integration Testing with Test Containers

```csharp
// Company.Messaging.Tests/ServiceBusIntegrationTests.cs
using Testcontainers.Azurite;

public class ServiceBusIntegrationTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .Build();
            
        await _azurite.StartAsync();

        var services = new ServiceCollection();
        services.AddAzureClients(builder =>
        {
            builder.AddServiceBusClient(_azurite.GetConnectionString());
        });
        
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishEvent_ShouldSucceed()
    {
        // Arrange
        var publisher = _services.GetRequiredService<IEventPublisher>();
        var testEvent = new OrderCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), 100m, DateTime.UtcNow);

        // Act
        await publisher.PublishAsync(testEvent);

        // Assert - event should be in queue
        // ...
    }

    public async Task DisposeAsync()
    {
        await _azurite.DisposeAsync();
    }
}
```

### Contract Testing

Ensure backward compatibility of contracts:

```csharp
// Company.Contracts.Tests/OrderCreatedEventTests.cs
public class OrderCreatedEventTests
{
    [Fact]
    public void OrderCreatedEvent_ShouldDeserializeFromV1Schema()
    {
        // Arrange - JSON from v1.0 contract
        var v1Json = """
        {
            "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "customerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
            "totalAmount": 299.99,
            "createdAtUtc": "2025-01-15T10:30:00Z"
        }
        """;

        // Act
        var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(v1Json);

        // Assert
        evt.Should().NotBeNull();
        evt!.OrderId.Should().Be(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"));
        evt.Currency.Should().Be("USD"); // Default value added in v1.1
    }

    [Fact]
    public void OrderCreatedEvent_ShouldSerializeWithAllFields()
    {
        // Arrange
        var evt = new OrderCreatedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            299.99m,
            DateTime.UtcNow,
            "EUR");

        // Act
        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

        // Assert
        deserialized.Should().BeEquivalentTo(evt);
    }
}
```

## Security Considerations

### Package Signing

Sign your NuGet packages for authenticity:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>$(MSBuildThisFileDirectory)company-key.snk</AssemblyOriginatorKeyFile>
  
  <!-- NuGet package signing -->
  <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>
  <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
</PropertyGroup>
```

```yaml
# In Azure Pipeline
- task: NuGetCommand@2
  displayName: 'Sign NuGet Packages'
  inputs:
    command: custom
    arguments: 'sign $(Build.ArtifactStagingDirectory)/**/*.nupkg -CertificatePath $(SigningCertificate.secureFilePath) -CertificatePassword $(CertPassword) -Timestamper http://timestamp.digicert.com'
```

### Dependency Scanning

Integrate security scanning into your pipeline:

```yaml
# azure-pipelines-security.yml
- task: DotNetCoreCLI@2
  displayName: 'Restore with Dependency Check'
  inputs:
    command: 'restore'
    projects: '**/*.csproj'
    
- task: ComponentGovernanceComponentDetection@0
  displayName: 'Component Detection'
  inputs:
    scanType: 'Register'
    verbosity: 'Verbose'
    alertWarningLevel: 'High'

- script: |
    dotnet list package --vulnerable --include-transitive
  displayName: 'Check for Vulnerable Dependencies'
  
- script: |
    dotnet list package --deprecated
  displayName: 'Check for Deprecated Packages'
```

### Secrets Management in Shared Libraries

Never hardcode secrets - use configuration:

```csharp
// Company.Common - Secure configuration
namespace Company.Common.Configuration;

public static class SecureConfigurationExtensions
{
    public static IConfigurationBuilder AddCompanySecrets(
        this IConfigurationBuilder builder,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            builder.AddUserSecrets<Program>();
        }
        else
        {
            // Azure Key Vault in production
            var config = builder.Build();
            var keyVaultUri = config["KeyVault:Uri"];
            
            if (!string.IsNullOrEmpty(keyVaultUri))
            {
                builder.AddAzureKeyVault(
                    new Uri(keyVaultUri),
                    new DefaultAzureCredential());
            }
        }
        
        return builder;
    }
}
```

## Documentation and Discoverability

### XML Documentation

Comprehensive documentation improves adoption:

```csharp
/// <summary>
/// Provides extension methods for <see cref="DateTime"/> operations with UTC handling.
/// </summary>
/// <remarks>
/// This class contains utility methods for working with dates and times, with a focus
/// on ensuring consistent UTC time handling across microservices.
/// 
/// <para>
/// <strong>Version History:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>v2.1.0 - Added ToUtc() method</description></item>
/// <item><description>v2.0.0 - Initial release</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var localTime = DateTime.Now;
/// var utcTime = localTime.ToUtc();
/// Console.WriteLine($"UTC: {utcTime:u}");
/// </code>
/// </example>
public static class DateTimeExtensions
{
    /// <summary>
    /// Converts a <see cref="DateTime"/> to UTC, preserving the underlying instant in time.
    /// </summary>
    /// <param name="dateTime">The date and time to convert.</param>
    /// <returns>
    /// A <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.
    /// If the input is already UTC, returns the same instance.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the DateTime represents an invalid time (e.g., during DST transition).
    /// </exception>
    /// <example>
    /// <code>
    /// var localTime = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Local);
    /// var utcTime = localTime.ToUtc();
    /// // utcTime.Kind == DateTimeKind.Utc
    /// </code>
    /// </example>
    public static DateTime ToUtc(this DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Utc 
            ? dateTime 
            : dateTime.ToUniversalTime();
    }
}
```

### README.md for Each Package

````markdown
<!-- Company.Common/README.md -->
# Company.Common

[![NuGet Version](https://img.shields.io/nuget/v/Company.Common.svg)](https://www.nuget.org/packages/Company.Common/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Company.Common.svg)](https://www.nuget.org/packages/Company.Common/)

Common utilities and extensions for Company microservices architecture.

## Installation

```bash
dotnet add package Company.Common
```

## Features

- **DateTime Extensions**: UTC conversion utilities
- **String Extensions**: Null checking helpers
- **Result Pattern**: Functional error handling
- **Performance**: Zero-allocation where possible

## Quick Start

```csharp
using Company.Common.Extensions;

// DateTime utilities
var utcTime = DateTime.Now.ToUtc();
var dateOnly = utcTime.ToDateOnly();

// String helpers
if (!input.IsNullOrWhiteSpace())
{
    // Process input
}

// Result pattern
var result = Result<int>.Success(42);
if (result.IsSuccess)
{
    Console.WriteLine(result.Value);
}
```

## Documentation

Full documentation available at [nova-globen.se](https://nova-globen.se)

## Version Compatibility

| Company.Common | .NET Version | Support |
|----------------|--------------|---------|
| 2.x            | .NET 9       | ✅ Active |
| 1.x            | .NET 8       | ⚠️ Maintenance |

## Contributing

See [CONTRIBUTING.md](../../CONTRIBUTING.md)

## License

MIT License - see [LICENSE](../../LICENSE)
````

## Performance Considerations

### Benchmarking Shared Libraries

Use BenchmarkDotNet to validate performance:

```csharp
// Company.Common.Benchmarks/DateTimeExtensionsBenchmarks.cs
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class DateTimeExtensionsBenchmarks
{
    private DateTime _localTime;
    private DateTime _utcTime;

    [GlobalSetup]
    public void Setup()
    {
        _localTime = DateTime.Now;
        _utcTime = DateTime.UtcNow;
    }

    [Benchmark(Baseline = true)]
    public DateTime ToUniversalTime_Baseline()
    {
        return _localTime.ToUniversalTime();
    }

    [Benchmark]
    public DateTime ToUtc_Extension()
    {
        return _localTime.ToUtc();
    }

    [Benchmark]
    public DateTime ToUtc_AlreadyUtc()
    {
        return _utcTime.ToUtc(); // Should be no-op
    }
}

// Results tracking
/*
| Method                 | Mean      | Error    | StdDev   | Gen0   | Allocated |
|----------------------- |----------:|---------:|---------:|-------:|----------:|
| ToUniversalTime_Base   | 45.23 ns  | 0.234 ns | 0.218 ns | -      | -         |
| ToUtc_Extension        | 45.18 ns  | 0.198 ns | 0.185 ns | -      | -         |
| ToUtc_AlreadyUtc       |  2.34 ns  | 0.012 ns | 0.011 ns | -      | -         |
*/
```

### Optimizing Package Size

```xml
<!-- Reduce package size -->
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>link</TrimMode>
  
  <!-- Debug symbols in separate package -->
  <DebugType>portable</DebugType>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  
  <!-- Exclude unnecessary files -->
  <NoWarn>$(NoWarn);NU5128</NoWarn>
  <ContentTargetFolders>content</ContentTargetFolders>
</PropertyGroup>

<ItemGroup>
  <None Remove="**/*.Development.json" />
  <None Remove="**/*.local.json" />
</ItemGroup>
```

## Governance and Ownership

### Package Ownership Model

Define clear ownership:

```yaml
# CODEOWNERS
# Shared libraries ownership

src/Shared/Company.Common/**           @platform-team @architecture-team
src/Shared/Company.Messaging/**        @platform-team @messaging-team
src/Shared/Company.Contracts/**        @api-team @architecture-team
src/Shared/Company.Observability/**    @platform-team @sre-team
src/Shared/Company.ServiceDefaults/**  @platform-team @devex-team

# Require approval from platform team for all shared library changes
src/Shared/**                          @platform-team
```

### Change Request Process

```markdown
<!-- .github/PULL_REQUEST_TEMPLATE.md -->
## Shared Library Change Request

### Package(s) Affected
- [ ] Company.Common
- [ ] Company.Messaging
- [ ] Company.Contracts
- [ ] Company.Observability
- [ ] Company.ServiceDefaults

### Change Type
- [ ] New Feature
- [ ] Bug Fix
- [ ] Breaking Change
- [ ] Performance Improvement
- [ ] Documentation

### Version Impact
- Current Version: x.y.z
- Proposed Version: x.y.z
- Justification: [Major/Minor/Patch]

### Breaking Changes
<!-- If yes, describe migration path -->
- [ ] No breaking changes
- [ ] Breaking changes documented in MIGRATION.md

### Impact Analysis
- Estimated services affected: X
- Migration effort: [Low/Medium/High]
- Rollout strategy: [describe]

### Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Performance benchmarks run
- [ ] Backward compatibility verified

### Documentation
- [ ] XML documentation updated
- [ ] README.md updated
- [ ] Migration guide created (if breaking)
- [ ] Changelog updated

### Checklist
- [ ] Code follows company standards
- [ ] No secrets or sensitive data
- [ ] Semantic versioning followed
- [ ] Release notes prepared
```

## Summary and Best Practices

### Key Takeaways

1. **Keep shared libraries focused on infrastructure**
   - ✅ Logging, extensions, messaging abstractions
   - ❌ Domain models, business logic

2. **Each microservice owns its domain model**
   - Services communicate via contracts (DTOs/events)
   - No shared domain entities across services

3. **Use semantic versioning rigorously**
   - Major: Breaking changes
   - Minor: New features (backward compatible)
   - Patch: Bug fixes

4. **Leverage .NET 9 features**
   - Enhanced metrics and observability
   - Improved performance
   - Better tooling support

5. **Integrate with .NET Aspire**
   - Standardized service defaults
   - Simplified local development
   - Built-in observability

6. **Automate everything**
   - CI/CD for package publishing
   - Automated versioning with GitVersion
   - Security scanning in pipelines

7. **Document thoroughly**
   - XML documentation
   - README files
   - Migration guides for breaking changes

8. **Monitor and measure**
   - Package download metrics
   - Performance benchmarks
   - Dependency health

### Anti-Patterns to Avoid

❌ **Don't do this:**
- Sharing domain entities across services
- Including business logic in shared libraries
- Creating "God packages" with everything
- Breaking semantic versioning rules
- Hardcoding configuration or secrets
- Publishing without tests
- Ignoring backward compatibility
- Skipping documentation

✅ **Do this instead:**
- Keep packages focused and cohesive
- Share only contracts and infrastructure
- Follow semantic versioning strictly
- Automate testing and publishing
- Document changes and migration paths
- Monitor package health and adoption

## Additional Resources

- [Semantic Versioning](https://semver.org/)
- [.NET Versioning Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [NuGet Package Best Practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)
- [Azure DevOps Artifacts](https://learn.microsoft.com/en-us/azure/devops/artifacts/)
- [More on microservices patterns at nova-globen.se](https://nova-globen.se)

