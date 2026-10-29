---
title: "When to choose gRPC over REST and Other Alternatives?"
date: 2025-10-02
slug: grpc-over-rest-and-other-alternatives
description: "When to choose gRPC over REST? and how gRPC, REST, and other alternatives compare—especially in .NET core and the .NET Aspire stack."
tags: [apis]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

# Quick Decision Tree: Which to choose?

* **Public APIs / external clients** → **REST** (simple, widely compatible).
* **Internal service-to-service (microservices)** → **gRPC** (typed, fast, low-latency).
* **Streaming data pipelines** → **gRPC streaming** or **SignalR** (depending on consumer type).
* **Frontend-heavy apps (Blazor/React)** → **GraphQL** if you want client-driven queries; otherwise REST with projections is simpler.
* **Real-time dashboards, chat, trading feeds** → **SignalR/WebSockets**.
* **Data-heavy querying** → consider **OData** or **GraphQL** for flexible filters.

# ✅ My Recommendation for .NET 9:
* Use **REST** at the edges (public endpoints, B2B).
* Use **gRPC** for **internal service-to-service** calls between projects (AppHost, backends, workers).
* Consider **GraphQL** if your frontends need flexible data shaping.
* Add **SignalR** for **real-time streaming to browsers**.

## 1. Core Concepts

### 1.1) REST (Representational State Transfer)

* **Transport:** HTTP/1.1 (can also run on HTTP/2/3).
* **Format:** Typically JSON (but can be XML, HAL, etc.).
* **Style:** Resource-oriented, CRUD-like verbs (`GET/POST/PUT/DELETE`).
* **Strengths:**

  * Simple, universally understood, widely supported by tooling.
  * Works well for public APIs and external consumers.
* **Weaknesses:**

  * Over-fetching/under-fetching (fixed DTOs).
  * Less efficient serialization compared to binary protocols.
  * No native streaming (must fake it with chunked responses, SignalR, or SSE).

### 1.2) gRPC

* **Transport:** HTTP/2 (HTTP/3 support coming in .NET 9).
* **Format:** Protocol Buffers (binary, compact, schema-driven).
* **Style:** RPC (Remote Procedure Calls), proto-first design.
* **Strengths:**

  * **Low latency:** Binary serialization and multiplexed HTTP/2 streams reduce overhead.
  * **Strong typing:** Proto schema compiles into C# types; no runtime surprises.
  * **Streaming:** Native support for server streaming, client streaming, and bidirectional.
  * **Best for internal service-to-service** (S2S) calls within a microservice mesh.
* **Weaknesses:**

  * Harder for browser/JS clients (though gRPC-Web mitigates this).
  * Tooling is heavier (must manage `.proto` files and codegen).
  * Debugging is less straightforward than JSON over HTTP.

### 1.3) Other Alternatives

* **GraphQL**: Flexible queries, avoids over/under-fetching. Best for frontend-heavy apps with diverse client needs. Slightly heavier CPU compared to REST projections.
* **SignalR / WebSockets**: Real-time bidirectional messaging. Best for notifications, dashboards, chat, trading, telemetry. Not a general-purpose API style.
* **OData**: REST with query semantics (`$filter`, `$select`, `$expand`). Good for data-heavy apps but complex and less popular today.
* **JSON-RPC**: Lightweight RPC over HTTP. Rare in .NET ecosystem compared to gRPC.

## 2. Key Terms to know about

* **Low latency:** gRPC avoids JSON serialization/deserialization overhead by using Protocol Buffers (binary).
* **Typed:** Strongly typed contracts generated from `.proto` files → compile-time safety.
* **Internal S2S calls:** Ideal inside a microservice cluster or Aspire-based distributed app, where both ends are .NET or support Protobuf.

### 2.1) Streaming

* gRPC natively supports:
  * **Unary** (request → response, like REST).
  * **Server streaming** (one request → many responses, e.g. live feed).
  * **Client streaming** (many requests → one response).
  * **Bidirectional streaming** (both sides send streams simultaneously).
* REST needs hacks (polling, SignalR, SSE) for this.

### 2.2) HTTP/2

* Multiplexes multiple streams over a single TCP connection (fewer sockets, less head-of-line blocking).
* Header compression (HPACK) saves bandwidth.
* Required by gRPC (classic implementation).

### 2.3) Proto-first design

* Define schema in `.proto` files:

  ```proto
  service OrderService {
    rpc GetOrder (GetOrderRequest) returns (OrderResponse);
    rpc StreamOrders (OrderFilter) returns (stream OrderResponse);
  }
  ```
* From this, C# classes and service stubs are generated. The schema is **the contract**—language-neutral and stable.
* Helps maintain backward/forward compatibility (additive fields, field numbers fixed).

## 3. REST vs gRPC vs Alternatives

| Aspect                 | REST                                                   | gRPC                                                          | GraphQL                                         | SignalR/WebSockets                   |
| ---------------------- | ------------------------------------------------------ | ------------------------------------------------------------- | ----------------------------------------------- | ------------------------------------ |
| **Perf (latency/CPU)** | Higher (JSON parsing, more bytes)                      | Lowest (binary, compact)                                      | Medium (parsing & resolver overhead)            | Low latency                          |
| **Typed contracts**    | JSON schema optional                                   | Proto = strongly typed                                        | Schema strongly typed                           | Custom, looser typing                |
| **Streaming**          | No (workarounds needed)                                | Yes, native (uni/bi-directional)                              | Subscription queries (but heavier)              | Yes, designed for real-time          |
| **Browser support**    | Native                                                 | Needs gRPC-Web                                                | Native                                          | Native                               |
| **Use case fit**       | Public APIs, external consumers                        | Internal microservices, high-perf S2S, streaming              | Complex client-driven queries                   | Real-time updates                    |
| **.NET 9 support**     | Minimal APIs, controllers, JSON source-gen → very fast | First-class gRPC support, HTTP/3 coming                       | HotChocolate GraphQL                            | SignalR (mature)                     |
| **.NET Aspire fit**    | Easy integration with frontends, API gateways          | Perfect for inter-service calls inside Aspire distributed app | Good for complex UI queries in Aspire frontends | Aspire dashboard/telemetry scenarios |
