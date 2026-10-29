---
title: "Why Avoid Service Locators in Dependency Injection?"
date: 2025-09-26
slug: dependency-injection-service-locators
description: "Service Locator is an anti-pattern where you inject IServiceProvider and manually resolve dependencies"
tags: [architecture]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

## Why Avoid Service Locators?

**Service Locator** is an anti-pattern where you inject `IServiceProvider` and manually resolve dependencies:

```csharp
// ❌ Bad - Service Locator pattern
public class OrderService
{
    private readonly IServiceProvider _serviceProvider;
    
    public OrderService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public void ProcessOrder()
    {
        var repo = _serviceProvider.GetRequiredService<IOrderRepository>();
        var emailer = _serviceProvider.GetRequiredService<IEmailService>();
        // ...
    }
}
```

**Problems with this approach:**

1. **Hidden dependencies** - You can't tell what the class needs just by looking at its constructor. Dependencies are obscured inside the implementation.

2. **Runtime failures** - Missing dependencies only fail at runtime when that code path executes, not at application startup.

3. **Hard to test** - You have to mock `IServiceProvider` and set up complex mock behaviors instead of just passing in the dependencies.

4. **Breaks IoC principle** - The class is now coupled to the DI container itself, defeating the purpose of dependency injection.

**Better approach - Constructor Injection:**

```csharp
// ✅ Good - Dependencies are explicit
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly IEmailService _emailer;
    
    public OrderService(IOrderRepository repository, IEmailService emailer)
    {
        _repository = repository;
        _emailer = emailer;
    }
    
    public void ProcessOrder()
    {
        // Use _repository and _emailer directly
    }
}
```

## Why Isolate Registrations Per Module?

**Isolation** means organizing your DI registrations by feature/module rather than dumping everything in `Program.cs`:

```csharp
// ❌ Bad - Everything in one place
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // 200+ lines of service registrations for all modules...
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IOrderService, OrderService>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        // ... many more
    }
}
```

**Better approach - Extension methods per module:**

```csharp
// ✅ Good - Orders module owns its registrations
public static class OrdersServiceExtensions
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderValidator, OrderValidator>();
        return services;
    }
}

// Auth module
public static class AuthServiceExtensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}

// Program.cs stays clean
builder.Services.AddOrdersModule();
builder.Services.AddAuthModule();
builder.Services.AddProductsModule();
```

**Benefits:**

1. **Maintainability** - Each module's dependencies are colocated with that module's code.

2. **Discoverability** - Easy to find what services a module provides.

3. **Modularity** - Modules can be enabled/disabled, or even moved to separate assemblies.

4. **Testability** - You can register just the modules needed for integration tests.

5. **Clean startup** - `Program.cs` remains readable and high-level.
