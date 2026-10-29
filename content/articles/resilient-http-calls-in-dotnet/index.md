---
title: "Strategies for resilient HTTP calls in .NET core"
date: 2025-09-26
slug: resilient-http-calls-in-dotnet
description: "Big picture of resilient HTTP calls—what it is, why it matters, and how to do it well in .NET/Azure with a sample."
tags: [reliability]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# What “resilience” means

Your app depends on other services (payments, search, auth). Networks hiccup, services spike, and anything can slow or fail. **Resilience** means your outbound HTTP calls **degrade gracefully** instead of cascading failures through your system.

**Resilient HTTP calls**
Designing `HttpClient` calls to **cope with transient failures** (network flakiness, 5xx, 429), **limit blast radius**, and **recover gracefully** instead of crashing or hanging.

**Why this order?**

* **Timeout (inner)** caps each attempt.
* **Retry** wraps timeouts/transient failures to try again.
* **Circuit Breaker** observes failures and opens if things look bad.
* **Bulkhead (outer)** preserves capacity across features.

# Failure types to design for

* **Transient**: brief spikes, 5xx/429, timeouts → *retry with jittered backoff*.
* **Persistent**: sustained failure/outage → *circuit-break, fall back, or fail fast*.
* **Slowdown**: high latency, thread/connection starvation → *timeouts, bulkheads, concurrency limits*.

# Core building blocks (with .NET lenses)

## Timeouts
Cap *each attempt* (e.g., 1–3s) and cap the *overall call budget* (e.g., ≤ client SLA).
> Never wait forever. Set a **per-try** timeout that’s smaller than your overall budget. Prefer Polly’s Timeout (per attempt) over only `HttpClient.Timeout`.

## Retries with jittered backoff
2–3 tries on transient errors only; add randomness to avoid herds.

**Retries**
Automatically try again on **transient** errors (e.g., `HttpRequestException`, `5xx`, `408`, `429`). Only retry **idempotent** operations (GET, or POST with idempotency keys). Cap attempts.

**Jittered backoff**
Wait longer between retries (exponential backoff) **plus randomness** (“jitter”) to avoid a thundering herd. Example: `base * 2^n + random(0..x)` milliseconds.


## Circuit breakers
Open after a failure rate/volume threshold; try again after cool-down (half-open).

**Circuit breakers**: Track recent failures. After a threshold, the breaker **opens** and **short-circuits** calls for a cool-down period (protects the downstream and your thread pool). States: **Closed → Open → Half-Open**.

## Bulkheads
Limit **concurrent** calls to a dependency (and optionally queue a few) to contain blast radius. If one service is slow, it can’t starve your whole app—other features keep working.
* **Cancellation tokens**: propagate from incoming request → `HttpClient.SendAsync(ct)`. Cooperative cancellation for callers: pass a `CancellationToken` to `SendAsync` so work stops quickly when the user navigates away, the request times out, or the server shuts down.
* **Observability**: log correlation IDs; metrics for success/failure/timeout/retry/break counts; traces.
* **Observability around failure rates**
Measure and see what’s happening: **logs** (with correlation IDs), **metrics** (success/timeout/retry/break counts, P95 latency), and **traces** (distributed tracing). Alert when error or open-breaker rates exceed thresholds.

> In .NET, use **HttpClientFactory** + **Polly** (or .NET 8’s **AddStandardResilienceHandler**) to compose these policies per dependency.

**Polly**
A .NET resilience library that provides **policies** such as **Retry**, **Timeout**, **Circuit Breaker**, and **Bulkhead**. You compose these to protect your outbound calls.

# Designing the “latency & retry budget”

Think top-down from the user SLO:

* If the page SLO is **2.0s**, budget maybe **1.2s** for remote calls.
* Choose **per-try timeout (T)** and **retries (N)** so:
  `overall ≈ N * T + backoff ≤ 1.2s`.
  Example: `T=300ms`, `N=3`, backoff ≈ 100–200ms → \~1.1–1.2s.
* **Never** let outer gateways (e.g., APIM) and clients both retry aggressively → tune *one layer* to own retries.

# Idempotency & correctness

* Retry only **idempotent** ops (GET, HEAD).
* For POST/PUT, use **idempotency keys** so duplicates don’t double-charge/create.
* Treat **429/503** as retryable; **400/401/403** as non-retryable.

# Capacity & connection hygiene

* Use **HttpClientFactory** (avoids socket exhaustion, good DNS refresh).
* Tune `SocketsHttpHandler`:

  * `PooledConnectionLifetime` to rotate connections (DNS changes).
  * `MaxConnectionsPerServer` to prevent oversubscription.
  * `ConnectTimeout` (don’t wait forever to connect).

# Degrade gracefully (fallbacks)

* **Stale cache** or **reduced quality** response when upstream is down.
* **Async handoff** for non-urgent work (queue to **Azure Service Bus/Storage Queues**, process later).
* **Feature flags** to turn off non-critical integrations under stress.

# Observability you absolutely need

* **Metrics** per dependency: success rate, error rate, **open-breaker count**, **bulkhead rejections**, **P95/P99 latency**.
* **Structured logs** with correlation/trace IDs.
* **Traces** (OpenTelemetry + App Insights): one span per remote call with outcome & attempt count.
* Alert when:

  * Error rate or P95 latency crosses thresholds.
  * Traffic to deprecated/unstable endpoints rises.
  * Circuit is open for > X minutes.

# Testing resilience (shift-left)

* Unit: verify policies wired correctly (right status codes retry).
* Integration: inject faults (timeouts/5xx/429) and measure behavior.
* Staging/chaos: latency injection, dependency kill switches; confirm breakers open and recover.

# Architecture do’s & don’ts

**Do**

* Apply policies **per dependency** (payments vs. search need different budgets).
* Keep retry counts **small** (2–3) and **add jitter**.
* Document policies next to the client (readme + code comments).

**Don’t**

* Nest multiple retry layers unknowingly (SDK + gateway + client).
* Use infinite or giant timeouts.
* Retry non-idempotent operations without safeguards.

# Quick .NET/Azure cookbook

* **.NET 8**: `AddStandardResilienceHandler()` or Polly policies via `AddPolicyHandler`.
* **Azure Functions/App Service**: propagate `CancellationToken`, set app-level request timeout consistent with your budgets.
* **Azure API Management**: use light retry at the edge *or* in the app—avoid both. Add request/response size and duration limits.
* **App Insights + OpenTelemetry**: export metrics/traces; create dashboards/alerts by dependency and version.


## Minimal .NET example (Polly + HttpClientFactory)

```csharp
// Program.cs
using Polly;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

var services = new ServiceCollection();

var retry = Policy<HttpResponseMessage>
    .Handle<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode is 408 or 429 or >= 500)
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt =>
            TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)) +
            TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200)), // jitter
        onRetry: (outcome, delay, attempt, ctx) =>
            Console.WriteLine($"Retry {attempt} after {delay}"));

var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(2));

var breaker = Policy<HttpResponseMessage>
    .Handle<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500)
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 10,
        durationOfBreak: TimeSpan.FromSeconds(30),
        onBreak: (outcome, span) => Console.WriteLine($"Breaker OPEN for {span}"),
        onReset: () => Console.WriteLine("Breaker RESET"),
        onHalfOpen: () => Console.WriteLine("Breaker HALF-OPEN"));

var bulkhead = Policy.BulkheadAsync<HttpResponseMessage>(
    maxParallelization: 50,
    maxQueuingActions: 100,
    onBulkheadRejectedAsync: _ => {
        Console.WriteLine("Bulkhead rejected");
        return Task.CompletedTask;
    });

// Recommended order (outer → inner): Bulkhead → CircuitBreaker → Retry → Timeout
var pipeline = Policy.WrapAsync(bulkhead, breaker, retry, timeout);

services.AddHttpClient("resilient")
    .AddPolicyHandler(pipeline);

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("resilient");

// Usage with cancellation
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var resp = await client.GetAsync("https://api.example.com/data", cts.Token);
```
