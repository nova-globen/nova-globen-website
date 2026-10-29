---
title: "Approach to Versioning REST APIs in .NET Core"
date: 2025-09-26
slug: versioning-rest-api
description: "Approach to versioning REST APIs by header-based versioning, backward-compatibility, deprecation policy; shared contracts via OpenAPI and consumer communication."
tags: [apis]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# Approach to Versioning REST APIs

## Core principles

* **Stability first:** once released, **don’t break clients**. Favor additive, backward-compatible changes.
* **Predictable versioning:** expose a clear, discoverable version and keep **one latest stable** plus **one previous** supported.
* **Explicit deprecation:** communicate timelines, provide headers, and a migration guide.

## Versioning styles (and when I use them)

1. **URI versioning (preferred for public APIs)**

   * Format: `/v1/orders`, `/v2/orders/{id}`
   * Pros: obvious, cache-friendly, great for docs & routing.
   * Cons: path churn when upgrading.
2. **Header-based versioning (preferred for internal/partner APIs)**

   * Custom header: `x-api-version: 2025-09-01` or `x-api-version: 2`
   * Media type: `Accept: application/vnd.contoso.orders+json;v=2`
   * Pros: cleaner URIs, can support multiple representations of the same resource.
   * Cons: less discoverable; requires clients to set headers correctly.

### Rule of thumb: 
- **Public → URI**.
- **Internal/advanced clients → header/media type**. 

> Support **one strategy per API** to avoid confusion.

## Backward-compatibility policy

* **Allowed without bumping major version:** add fields (response), add endpoints, relax validation, add optional query params.
* **Requires new major version:** remove/rename fields, change response shapes, tighten validation, repurpose semantics, change status codes.
* **Minor/patch versions:** used internally for deployment tracking and SDK docs; **never force clients to specify minor/patch**.

## Deprecation policy (what clients experience)

* Announce deprecation in release notes & email/portal.
* Emit headers for deprecated versions:

  * `Deprecation: true`
  * `Sunset: Tue, 31 Mar 2026 00:00:00 GMT`
  * `Link: </docs/migrate-v1-to-v2>; rel="deprecation"`
* Keep deprecated versions live for a **fixed window** (e.g., **9–12 months**) with **overlapping support**.
* Provide **migration guides** and **change logs** tied to OpenAPI diffs.

## Contracts & communication

* **Single source of truth:** OpenAPI (Swagger) checked into version control per major version (e.g., `openapi.v1.yaml`, `openapi.v2.yaml`).
* **Compatibility checks:** run OpenAPI-diff in CI to block breaking changes.
* **Consumer comms:** changelog, upgrade guides, sample payloads, and SDK bumps. For breaking changes, **version both API and SDK**.

## Rollout workflow

1. Design changes; mark breaking vs non-breaking.
2. Update OpenAPI; generate docs & mock.
3. Implement `vNext` alongside existing version (side-by-side routing).
4. Canary with real traffic; monitor error/latency deltas.
5. Announce deprecation of old version with timelines.
6. After sunset, remove old routing & artifacts.

## Example in ASP.NET Core (.NET)

**Routing and Swagger grouping**

```csharp
// Program.cs
builder.Services.AddApiVersioning(o =>
{
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.ReportApiVersions = true; // adds API-supported/deprecated headers
});
builder.Services.AddVersionedApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV"; // v1, v2
    o.SubstituteApiVersionInUrl = true;
});

// Example controller with URI versioning
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/orders")]
public class OrdersControllerV1 : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult Get(string id) => Ok(new { id, status = "processing" /* no breaking changes */ });
}

[ApiController]
[ApiVersion("2.0")]
[Route("v{version:apiVersion}/orders")]
public class OrdersControllerV2 : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult Get(string id) => Ok(new { id, orderStatus = "processing" /* renamed → breaking, so v2 */ });
}
```

**Header-based example**

```http
GET /orders/123 HTTP/1.1
Host: api.contoso.com
Accept: application/json
x-api-version: 2
```

**Deprecation headers (middleware sketch)**

```csharp
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/v1"))
    {
        ctx.Response.Headers["Deprecation"] = "true";
        ctx.Response.Headers["Sunset"] = "Tue, 31 Mar 2026 00:00:00 GMT";
        ctx.Response.Headers["Link"] = "</docs/migrate-v1-to-v2>; rel=\"deprecation\"";
    }
    await next();
});
```

## Testing & monitoring

* Contract tests from OpenAPI examples.
* Backward-compat snapshot tests on representative payloads.
* Dashboard by **version**: request volume, error rate, P95 latency.
* Alert if deprecated version traffic > X% after N weeks.

## Quick dos & don’ts

* **Do:** keep versions **immutable**, document clearly, version **breaking** changes only.
* **Don’t:** overload query parameters for versioning, mix multiple strategies, or silently change behavior within a version.
