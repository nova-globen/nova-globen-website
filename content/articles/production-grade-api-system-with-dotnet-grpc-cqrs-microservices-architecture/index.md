---
title: "Building a Production-Grade API System with .NET, gRPC, and Microservices Architecture"
date: 2025-10-21
slug: production-grade-api-system-with-dotnet-grpc-cqrs-microservices-architecture
description: "A comprehensive exploration of modern .NET architecture, distributed systems design, and enterprise-grade observability. In this article, I'll walk you through building a production-ready Library Management System that de"
tags: [apis, microservices, architecture]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

*This article is part of the [Comprehensive Guide to Microservices Architecture in .NET Core, Cloud and Azure](https://nova-globen.se/blog/microservices-architecture-in-dotnet-core-and-azure/) series.*

## Introduction

This isn't just another CRUD application. We'll build a system that showcases:
- **Domain-Driven Design** (DDD) principles
- **Command Query Responsibility Segregation** (CQRS) pattern
- **gRPC** for high-performance internal communication
- **Microservices** architecture with proper separation of concerns
- **Full observability** with structured logging and distributed tracing
- **Comprehensive testing** strategy across all layers
- **Multiple deployment options** using .NET Aspire and Docker Compose

By the end of this guide, you'll understand not just *how* to build each component, but *why* specific architectural decisions matter in production systems.

---

## The Business Requirements

Before diving into architecture, let's understand what we're building. Our Library Management System needs to answer key business questions:

**Inventory Insights:**
- What are the most borrowed books within a specific time range?
- Which books are frequently borrowed together?

**User Activity:**
- Who are the most active borrowers in a given period?
- What is each user's reading pace (pages per day)?

**Circulation Operations:**
- Allow users to borrow books with proper tracking
- Handle book returns with validation

These requirements drive our architectural decisions. We need a system that can efficiently query large datasets (for analytics) while maintaining transactional integrity (for borrowing operations).

---

## Architecture Overview: Why This Design?

### The Layered Approach

Our system uses a six-layer architecture, each with a specific responsibility:

**1. Domain Layer** (`src/Library.Domain/`)
The heart of the application. Contains business entities, value objects, and domain policies. This layer is framework-agnostic and contains the core business logic. By keeping it isolated, we ensure that business rules are testable and independent of infrastructure concerns.

**2. Application Layer** (`src/Library.Application/`)
Orchestrates the domain layer using the CQRS pattern. Commands handle write operations (borrowing, returning books), while Queries handle read operations (analytics, reporting). This separation allows us to optimize each path independently and provides natural boundaries for caching strategies.

**3. Infrastructure Layer** (`src/Library.Infrastructure/`)
Implements data persistence using Entity Framework Core and PostgreSQL. This layer depends on the Domain layer but is hidden behind repository interfaces, following the Dependency Inversion Principle. The domain layer never knows about databases, ORMs, or persistence details.

**4. Contracts Layer** (`src/Library.Contracts/`)
Defines gRPC service contracts using Protocol Buffers. These contracts form the API boundary between internal services, providing strong typing, versioning, and cross-platform compatibility.

**5. gRPC Service Layer** (`src/Library.Grpc/`)
Implements the gRPC services defined in contracts. This is our "backend" service that owns the business logic and database access. It exposes three main services: Inventory, Circulation, and UserActivity.

**6. HTTP API Gateway** (`src/Library.Api/`)
Provides a RESTful HTTP interface for external clients. This gateway translates HTTP requests to gRPC calls, handles HTTP-specific concerns (CORS, OpenAPI documentation), and provides a familiar REST interface while leveraging gRPC's performance internally.

### Why gRPC Between Layers?

You might wonder: why introduce gRPC between the API Gateway and the business logic service?

**Performance:** gRPC uses HTTP/2 and Protocol Buffers, offering significant performance improvements over JSON/HTTP REST for internal service communication.

**Strong Contracts:** Protocol Buffers provide compile-time type safety and automatic client/server code generation.

**Microservices Ready:** This architecture allows easy horizontal scaling. The API Gateway and gRPC service can be deployed independently, scaled separately based on load, and even deployed across different data centers.

**Technology Agnostic:** gRPC clients can be generated for any language, making it easy to add services in different tech stacks later.

---

## Step 1: Foundation - The Domain Layer

The Domain layer is where we start because it represents the core business logic, independent of any framework or infrastructure.

### Designing Rich Domain Entities

In `src/Library.Domain/Entities/`, we define three main entities:

**Book** - Represents books in the library catalog with properties like ISBN, Title, Author, and PageCount. The entity enforces invariants (e.g., PageCount must be positive) directly in its constructor.

**User** - Represents library members with FullName and RegisteredAt timestamp.

**Loan** - The most interesting entity, representing a book borrowing transaction. This entity contains business logic such as:
- A loan cannot be returned before it was borrowed
- A loan cannot be returned twice
- The Return method encapsulates the business rules for returning books

These aren't anemic data containers. They're rich objects that protect their invariants and expose behavior through methods, not just property setters.

### Value Objects and Domain Policies

In `src/Library.Domain/ValueObjects/`, we create immutable value objects like `ReadingPaceResult` and `LoanReadingPace`. These represent domain concepts that don't have identity but have value through their properties.

The `src/Library.Domain/Policies/ReadingPacePolicy.cs` class contains the business logic for calculating reading pace. By extracting this into a policy class, we:
- Keep the calculation logic testable in isolation
- Make it reusable across different contexts
- Express business rules explicitly rather than hiding them in entity methods

### Repository Interfaces

In `src/Library.Domain/Repositories/`, we define repository interfaces (`IBookRepository`, `IUserRepository`, `ILoanRepository`). These interfaces are defined in the Domain layer but implemented in the Infrastructure layer. This is the Dependency Inversion Principle in action: high-level domain logic doesn't depend on low-level infrastructure details.

---

## Step 2: Application Layer - CQRS with MediatR

The Application layer (`src/Library.Application/`) orchestrates domain objects and implements the CQRS pattern.

### Understanding CQRS

CQRS separates read operations (Queries) from write operations (Commands). Why?

**Different Optimization Strategies:** Reads can be cached aggressively, use read replicas, or even use different data stores. Writes need transactional consistency and validation.

**Different Models:** Queries often need denormalized data for performance, while commands work with normalized domain entities.

**Scalability:** Read and write workloads can be scaled independently.

### Commands for Write Operations

In `src/Library.Application/Commands/`, we implement:

**BorrowBookCommand** - Handles the borrowing workflow:
1. Validate the user exists
2. Validate the book exists
3. Create a new Loan entity
4. Persist via repository
5. Return the loan ID

**ReturnBookCommand** - Handles returns:
1. Retrieve the loan
2. Call the domain entity's Return method (which enforces business rules)
3. Persist the change

Each command has a corresponding handler that uses MediatR for request pipeline processing.

### Queries for Read Operations

In `src/Library.Application/Queries/`, we implement:

**GetMostBorrowedBooksQuery** - Analyzes borrowing patterns to return the most popular books.

**GetTopBorrowersQuery** - Identifies the most active library users.

**GetUserReadingPaceQuery** - Calculates how fast a user reads based on loan history.

**GetAlsoBorrowedBooksQuery** - Finds books that were borrowed by the same users (recommendation engine foundation).

These queries bypass the domain entities and go directly to optimized query services, returning denormalized DTOs perfect for display.

### Validation with FluentValidation

In `src/Library.Application/Validators/`, we create validators for every command and query. FluentValidation provides:
- Declarative validation rules
- Clear error messages
- Easy unit testing
- Separation of validation logic from business logic

For example, the BorrowBookCommand validator ensures the user ID and book ID are valid GUIDs, and the borrow date is not in the future.

### MediatR Pipeline Behaviors

MediatR allows us to create pipeline behaviors that wrap all commands and queries. We implement:

**Validation Behavior** - Automatically validates all requests before they reach handlers.

**Logging Behavior** - Logs all requests and their execution time.

**Caching Behavior** - Caches query results based on custom cache policies.

This cross-cutting concern approach keeps handlers clean and focused on business logic.

---

## Step 3: Infrastructure Layer - Data Persistence

The Infrastructure layer (`src/Library.Infrastructure/`) provides concrete implementations of repository interfaces and database access.

### DbContext Design

In `src/Library.Infrastructure/Data/LibraryDbContext.cs`, we configure Entity Framework Core with:

**Entity Configuration:** Each entity's mapping, constraints, indexes, and relationships are explicitly configured using Fluent API.

**UTC DateTime Handling:** All DateTime values are automatically converted to UTC to avoid timezone issues.

**Change Tracking:** Optimized for the workload (tracking enabled for commands, disabled for read-only queries).

### Repository Pattern Implementation

In `src/Library.Infrastructure/Repositories/`, we implement the repository interfaces:

**BookRepository** - Standard CRUD operations with EF Core.

**UserRepository** - User management operations.

**LoanRepository** - Loan operations with navigation property loading.

These repositories encapsulate all EF Core-specific logic, keeping the domain and application layers clean.

### Query Service for CQRS Reads

In `src/Library.Infrastructure/Queries/QueryService.cs`, we implement optimized read operations:

Instead of loading full entity graphs, we project directly to DTOs using EF Core's projection capabilities. This:
- Reduces memory allocation
- Improves query performance
- Allows database-side aggregation

For example, the "most borrowed books" query groups loans by book, counts them, orders by count, and projects to a simple DTO - all executed as a single SQL query.

### Database Migrations

Entity Framework migrations track schema changes over time. In `src/Library.Infrastructure/Migrations/`, we maintain migration history, allowing us to:
- Version control our schema
- Deploy schema changes alongside application updates
- Rollback if needed
- Generate the database from scratch in new environments

---

## Step 4: gRPC Contracts - The Service Boundary

In `src/Library.Contracts/Protos-v1/`, we define our gRPC service contracts using Protocol Buffers (`.proto` files).

### Defining Service Contracts

**inventory.proto** - Defines InventoryService with methods:
- GetMostBorrowedBooks
- GetAlsoBorrowedBooks

**circulation.proto** - Defines CirculationService with methods:
- BorrowBook
- ReturnBook

**user_activity.proto** - Defines UserActivityService with methods:
- GetTopBorrowers
- GetReadingPace

Each service definition includes request/response message types with proper field numbering for versioning.

### Why Protocol Buffers?

1. **Efficiency:** Binary serialization is faster and smaller than JSON
2. **Type Safety:** Strongly typed contracts prevent errors at compile time
3. **Evolution:** Field numbers allow backward-compatible changes
4. **Multi-Language:** Generate clients in any language
5. **Documentation:** The `.proto` files serve as living documentation

### Versioning Strategy

Notice the `v1` in the proto file names and namespaces. This allows us to:
- Release breaking changes as v2 while maintaining v1
- Run multiple API versions simultaneously
- Migrate clients gradually
- Maintain backward compatibility

---

## Step 5: gRPC Service Implementation

In `src/Library.Grpc/Services/`, we implement the gRPC services defined in our contracts.

### Service Implementation Pattern

Each service follows a consistent pattern:

1. **Dependency Injection:** Constructor receives IMediator for CQRS
2. **Request Mapping:** Convert gRPC request messages to Commands/Queries
3. **Execution:** Send to MediatR pipeline
4. **Response Mapping:** Convert results back to gRPC response messages
5. **Error Handling:** Catch exceptions and convert to appropriate gRPC status codes

For example, `InventoryServiceImpl.cs` implements GetMostBorrowedBooks by:
- Parsing date parameters from the request
- Creating a GetMostBorrowedBooksQuery
- Sending it through MediatR
- Mapping the results to the gRPC response type

### Server Configuration

In `src/Library.Grpc/Program.cs`, we configure:

**gRPC Services:** Register all service implementations

**Server Reflection:** Enables tools like grpcurl to discover services dynamically

**Health Checks:** Implement gRPC health checking protocol

**Dependency Injection:** Wire up all layers (Application, Infrastructure, Domain)

**Database:** Configure DbContext with connection string

**Observability:** Add Serilog for logging, OpenTelemetry for tracing

---

## Step 6: HTTP API Gateway

The `src/Library.Api/` project provides a RESTful HTTP interface that internally calls our gRPC services.

### Minimal APIs Design

We use ASP.NET Core Minimal APIs for a lightweight, functional approach. Endpoints are organized by feature:

**InventoryEndpoints.cs** - Maps HTTP routes to InventoryService gRPC calls
**CirculationEndpoints.cs** - Maps HTTP routes to CirculationService gRPC calls
**UserActivityEndpoints.cs** - Maps HTTP routes to UserActivityService gRPC calls

Each endpoint module:
- Defines routes with OpenAPI annotations
- Validates HTTP parameters
- Calls the appropriate gRPC service
- Handles gRPC exceptions and translates to HTTP status codes
- Maps responses to HTTP-friendly models

### Why Not Call MediatR Directly?

You might ask: why add gRPC between the API and the application layer? Why not call MediatR directly?

**Separation of Concerns:** The API Gateway should only know about HTTP. The business logic service should only know about business logic.

**Independent Scaling:** We can scale the public-facing API gateway independently from the business logic service.

**Security Boundary:** The gRPC service never exposes HTTP, reducing attack surface.

**Deployment Flexibility:** Services can be deployed to different machines, regions, or even cloud providers.

**Technology Freedom:** Tomorrow, we could add a GraphQL gateway or a WebSocket service, all talking to the same gRPC backend.

### OpenAPI / Swagger Integration

In `Program.cs`, we configure Swagger to:
- Generate OpenAPI specification automatically
- Provide interactive API documentation
- Include XML documentation comments
- Show request/response examples

This gives developers a playground to test the API without writing code.

---

## Step 7: Database Migrations and Seeding

The `src/Library.MigrationService/` project handles database initialization separately from the application.

### Why a Separate Migration Service?

**Separation of Concerns:** Migration is a one-time task, not part of the application runtime.

**Explicit Control:** With .NET Aspire, migrations run before the application starts, ensuring the database is ready.

**No Startup Delays:** The main application doesn't wait for migrations, improving startup time.

**Idempotent:** Can be run multiple times safely; EF migrations are idempotent.

### Data Seeding

The migration service also seeds sample data, which:
- Provides realistic test data for development
- Demonstrates the domain model
- Enables testing of queries immediately
- Shows proper entity creation patterns

The seeder in `src/Library.MigrationService/DataSeeder.cs` creates books, users, and loans with varied dates to make analytics queries meaningful.

---

## Step 8: Observability - Logging and Tracing

Production systems need visibility. We implement comprehensive observability using industry-standard tools.

### Structured Logging with Serilog and Seq

**Why Serilog?**
- Structured logging (not just strings)
- Multiple sinks (console, file, Seq, etc.)
- Enrichers (add context like request ID, user ID)
- High performance

**Why Seq?**
- Centralized log aggregation
- Powerful query language
- Search across all services
- Alerting on log patterns

In `src/Library.ServiceDefaults/`, we configure Serilog to:
- Log to console (for development)
- Log to Seq (for centralized viewing)
- Include context (service name, timestamp, log level)
- Respect minimum log levels per environment

### Distributed Tracing with OpenTelemetry and Jaeger

**Why OpenTelemetry?**
- Vendor-neutral standard
- Automatic instrumentation for ASP.NET, EF Core, gRPC
- Trace context propagation across services
- Exporters for any backend

**Why Jaeger?**
- Visualizes request flows across services
- Identifies performance bottlenecks
- Shows service dependencies
- Correlates with logs

When a request flows from the API Gateway to the gRPC Service to the database, OpenTelemetry creates a trace with spans for each operation. Jaeger visualizes this, showing:
- Total request duration
- Time spent in each service
- Database query performance
- gRPC call overhead

This makes performance optimization data-driven, not guesswork.

### Health Checks

Both services implement health checks:
- **Liveness:** Is the service running?
- **Readiness:** Is the service ready to handle requests (database connected, etc.)?

These endpoints integrate with Kubernetes, Docker health checks, and .NET Aspire monitoring.

---

## Step 9: Testing Strategy

A production system needs comprehensive testing. We implement four types of tests, each serving a specific purpose.

### Unit Tests

**Domain Unit Tests** (`tests/Library.UnitTests.Domain/`)
- Test domain entities in isolation
- Verify business rules (e.g., loan cannot be returned twice)
- Test domain policies (reading pace calculation)
- Fast, no dependencies

**Application Unit Tests** (`tests/Library.UnitTests.Application/`)
- Test command/query handlers with mocked repositories
- Verify validation logic
- Test pipeline behaviors
- Ensure CQRS handlers work correctly

### Integration Tests

**Infrastructure Integration Tests** (`tests/Library.IntegrationTests.Infrastructure/`)
- Test repositories against a real PostgreSQL database
- Verify EF Core mappings
- Test complex queries
- Use TestContainers to spin up PostgreSQL in Docker

These tests ensure our ORM configuration works correctly and catch issues like:
- Missing indexes
- Incorrect relationships
- Query performance problems

### Functional Tests

**gRPC Functional Tests** (`tests/Library.FunctionalTests.Grpc/`)
- Test gRPC services with a real database
- Verify service contracts
- Test request/response mapping
- Ensure error handling works

These tests use `WebApplicationFactory` to spin up the gRPC service in-memory and TestContainers for PostgreSQL. They verify that the entire gRPC stack works correctly.

### System Tests

**API System Tests** (`tests/Library.SystemTests.Api/`)
- Test the entire system end-to-end
- HTTP requests → API Gateway → gRPC Service → Database
- Verify the full user experience
- Test error scenarios

These tests ensure all layers integrate correctly and the system behaves as users expect.

### Test Organization

Notice the naming pattern: `Library.[TestType].[LayerUnderTest]`

This makes it clear:
- What kind of test it is
- What it's testing
- Where to add new tests

---

## Step 10: Deployment Options

We provide two deployment strategies, each suited for different scenarios.

### .NET Aspire for Development and Local Testing

**What is .NET Aspire?**
.NET Aspire is Microsoft's new cloud-ready stack for building observable, production-ready distributed apps. It provides:
- Orchestration (like docker-compose, but .NET-native)
- Service discovery
- Automatic observability wiring
- Dashboard for monitoring
- Local development parity with production

**The AppHost** (`apphost/Library.AppHost/Program.cs`)

This is the orchestration definition. It:
- Defines all services (PostgreSQL, Seq, Jaeger, Migration Service, gRPC Service, API Gateway)
- Wires up dependencies (API Gateway depends on gRPC Service)
- Configures wait conditions (wait for database before starting services)
- Passes configuration (connection strings, endpoints)
- Adds observability automatically

**Running with Aspire:**

Simply run the AppHost project, and .NET Aspire:
1. Starts PostgreSQL in a container
2. Starts Seq for logging
3. Starts Jaeger for tracing
4. Runs migrations
5. Starts the gRPC Service
6. Starts the API Gateway
7. Opens a dashboard showing all services, logs, and traces

It's development environment as code.

### Docker Compose for Production-Like Deployment

**Why Docker Compose?**
- Standard, widely supported
- Works in CI/CD pipelines
- Closer to production (Kubernetes, cloud services)
- No .NET-specific tooling required

**The Compose File** (`deploy/docker-compose.yml`)

Defines five services:
1. **postgres** - PostgreSQL database with health checks
2. **seq** - Log aggregation
3. **jaeger** - Distributed tracing
4. **library-grpc** - The gRPC backend service
5. **library-api** - The HTTP API Gateway

Each service:
- Has health checks
- Depends on required services
- Uses environment variables for configuration
- Exposes appropriate ports
- Mounts volumes for data persistence

**Dockerfiles** (`deploy/Dockerfile.grpc`, `deploy/Dockerfile.api`)

Multi-stage builds that:
1. **Build Stage:** Restore packages, build the project
2. **Publish Stage:** Publish optimized release build
3. **Runtime Stage:** Copy published files to minimal runtime image

The runtime images:
- Use the minimal ASP.NET runtime (not the SDK)
- Run as non-root user for security
- Include health checks
- Set appropriate environment variables

---

## Step 11: Advanced Patterns and Considerations

### Caching Strategy

The Application layer includes caching for queries through MediatR behaviors. Queries implement `ICacheableQuery` to opt into caching with:
- Cache key generation
- TTL (time to live)
- Invalidation strategy

For example, "most borrowed books" is cached for 5 minutes because the data doesn't change frequently, but user-specific reading pace is not cached because it's personalized.

### Error Handling

Each layer handles errors appropriately:

**Domain Layer:** Throws domain-specific exceptions (e.g., `InvalidOperationException` when trying to return an already-returned loan)

**Application Layer:** Catches domain exceptions and translates them to application results

**gRPC Layer:** Catches application exceptions and returns gRPC status codes (InvalidArgument, NotFound, etc.)

**API Gateway:** Catches gRPC exceptions and returns HTTP status codes (400, 404, 500)

This layered error handling ensures:
- Inner layers don't know about outer layer concerns
- Errors are translated appropriately at each boundary
- Clients receive meaningful error messages

### Validation Strategy

We validate at multiple layers, each with a different purpose:

**Domain Layer:** Enforces business invariants (e.g., PageCount > 0)

**Application Layer:** Validates commands/queries with FluentValidation (e.g., valid GUIDs, dates)

**API Gateway:** Validates HTTP-specific concerns (e.g., proper date format, parameter presence)

This defense-in-depth approach catches errors early and provides clear error messages.

---

## Lessons Learned and Best Practices

### Start with the Domain

Always start with the Domain layer. If you don't understand the business logic, no amount of fancy architecture will save you. Spend time modeling the domain correctly, and the rest flows naturally.

### CQRS Isn't Always Necessary

CQRS adds complexity. Use it when:
- Read and write patterns differ significantly
- You need different optimization strategies
- The business naturally separates commands and queries

For simple CRUD operations, CQRS might be overkill.

### gRPC for Internal, REST for Public

gRPC excels for internal service communication but has a learning curve for external clients. The API Gateway pattern gives you the best of both worlds:
- gRPC's performance internally
- REST's familiarity externally

### Observability from Day One

Don't add logging and tracing as an afterthought. Bake it in from the start. When (not if) things go wrong in production, you'll thank yourself for having comprehensive observability.

### Test Strategically

Not all code needs the same level of testing:
- Domain logic: Unit test heavily
- API endpoints: System test the happy paths, unit test error cases
- Infrastructure: Integration test key scenarios
- Simple DTOs and mappers: Don't bother

Focus testing effort where bugs have the highest impact.

### Configuration Management

Use strongly-typed configuration (IOptions pattern). Validate configuration at startup. Fail fast if configuration is wrong. Don't let misconfiguration make it to production.

### Database Migrations

Never auto-apply migrations in production. Review them, test them in staging, and apply them as a separate deployment step. Database changes are risky; treat them with respect.

---

## Scaling and Production Considerations

### Horizontal Scaling

This architecture supports horizontal scaling:
- **API Gateway:** Stateless, can run multiple instances behind a load balancer
- **gRPC Service:** Stateless, can scale horizontally
- **PostgreSQL:** Can use read replicas for queries, primary for commands

### Caching Layer

For high-scale scenarios, add Redis:
- Distributed caching across API Gateway instances
- Session state (if needed)
- Query result caching with shorter TTLs

### Message Queue for Async Operations

For operations that don't need immediate consistency, add a message queue (RabbitMQ, Azure Service Bus):
- Send email notifications asynchronously
- Generate analytics reports in the background
- Decouple services further

### Database Optimizations

As data grows:
- Add indexes on frequently queried columns
- Partition large tables (loans table by date)
- Archive old data
- Use materialized views for complex analytics

### Security Considerations

Before production:
- Add authentication (JWT, OAuth2)
- Add authorization (role-based or policy-based)
- Enable HTTPS/TLS everywhere
- Implement rate limiting
- Add API key validation for service-to-service calls
- Scan dependencies for vulnerabilities

---

## Conclusion

Building a production-grade system requires more than just writing code that works. It requires thoughtful architecture, comprehensive testing, observability, and deployment automation.

This Library Management System demonstrates:
- **Clean Architecture** with proper separation of concerns
- **Domain-Driven Design** for rich business logic
- **CQRS** for optimized read and write paths
- **gRPC** for high-performance internal communication
- **Modern .NET** features and best practices
- **Full observability** with logging and tracing
- **Multiple deployment options** for different scenarios
- **Comprehensive testing** at all levels

The key takeaway isn't the specific technologies used (gRPC, PostgreSQL, etc.). It's the architectural principles:
- Dependency inversion
- Separation of concerns
- Explicit boundaries between layers
- Testability
- Observability
- Scalability

These principles apply regardless of your tech stack.

Start with a solid domain model, add clean architecture layers, implement proper observability, test thoroughly, and automate deployment. Do these things, and you'll build systems that are maintainable, scalable, and actually enjoyable to work on.

---

## Next Steps

To build on this design:

1. **Set up the project structure** in `src/` and `tests/` as described above
2. **Run it locally** with .NET Aspire to see the dashboard and observability in action
3. **Document as you go** - XML comments that explain the "why"
4. **Run the tests** to understand the testing strategy
5. **Deploy with Docker Compose** to see the production-like setup
6. **Modify and extend** - try adding new features using the existing patterns

Happy coding, and may your services always return 200 OK! 🚀

---

*This article is part of a series on building production-grade .NET applications. For more in-depth articles, [visit my blog](https://nova-globen.se/blog/).*
