---
title: "Ensuring Thread Safety — .NET core-centric"
date: 2025-09-26
slug: threads-safety-in-dotnet-core
description: "Multiple threads (or async tasks) touching the same mutable data can corrupt state or produce surprises. The safest strategy is to design so sharing doesn’t happen; when it must, control access tightly."
tags: [performance, reliability]
cover: ./cover.png
draft: false
templateEngineOverride: md
---

## Prefer immutability

**What:** Make data **read-only** after construction. Instead of editing objects, create new ones.

**Why:** If nothing changes, many threads can read safely with **no locks**.

**How (.NET):**

```csharp
public readonly record struct Money(decimal Amount, string Currency);

public record Order(Guid Id, IReadOnlyList<OrderLine> Lines)
{
    public Order AddLine(OrderLine line) => this with { Lines = Lines.Append(line).ToList() };
}
```

* Use `record`/`readonly struct`, `IReadOnlyList<>`, and `with` (copy-on-write).
* Keep collections immutable (`ImmutableList<T>`, `ImmutableDictionary<K,V>`).

## Avoid shared state

**What:** Don’t let unrelated code touch the same mutable object.

**Why:** If each operation owns its data, there’s nothing to synchronize.

**How:**

* **Per-request scope**: create new service instances that hold request-specific state.
* No static mutable fields; if you must cache, use `ConcurrentDictionary`:

```csharp
private static readonly ConcurrentDictionary<string, Widget> _cache = new();
var widget = _cache.GetOrAdd(key, k => LoadWidget(k));
```

## Use `lock` / `SemaphoreSlim` cautiously

**What:** Synchronization primitives that serialize access to critical sections.

**When:**

* Short, minimal **critical sections** where mutation is unavoidable.
* `lock` for synchronous code; `SemaphoreSlim` when `await` is involved (never block in async code).

**Patterns & pitfalls:**

```csharp
private readonly object _gate = new();

void Update()
{
    lock (_gate) // keep work tiny inside
    {
        // mutate a small piece of shared state
        _count++;
    }
}

private readonly SemaphoreSlim _sem = new(1,1);

async Task UpdateAsync()
{
    await _sem.WaitAsync();
    try { _count++; }
    finally { _sem.Release(); }
}
```

* **Never** `lock(this)` or a public object (external code could deadlock you).
* Keep lock duration short; avoid I/O under locks.
* If multiple locks are needed, **fix a global order** to prevent deadlocks.

**Atomic counters (avoid locks entirely):**

```csharp
Interlocked.Increment(ref _count);
```

## Leverage actor-style or message queues

**What:** Push work as **messages** to a single-threaded “actor” that **owns** its state. Or use an external queue/bus so workers don’t share memory.

**Why:** Eliminates shared writes; logic becomes “handle one message at a time.”

**How (lightweight actors with Channels):**

```csharp
public sealed class CounterActor
{
    private readonly Channel<Action<State>> _in = Channel.CreateUnbounded<Action<State>>();
    private readonly State _state = new();

    public CounterActor()
    {
        _ = Task.Run(async () =>
        {
            await foreach (var msg in _in.Reader.ReadAllAsync())
                msg(_state); // single-threaded access
        });
    }

    public ValueTask Tell(Action<State> msg) => _in.Writer.WriteAsync(msg);

    public sealed class State { public int Count; }
}
```

**At scale:** use Orleans/Akka.NET (virtual actors) or external queues (Azure Service Bus/Storage Queues) to process messages concurrently **without shared memory**.

## Timeouts, cancellation, and async correctness

* Always pass `CancellationToken` so long operations end promptly—reduces lock contention.
* In ASP.NET Core, never block the thread (`.Result`, `.Wait()`); use `await` to avoid thread pool starvation.

## Observability for thread-safety issues

* Metric: lock contention, queue length, actor mailbox size.
* Logs with **correlation IDs** to trace races.
* Load/stress tests that hammer critical paths (many parallel tasks) to catch races early.

## Quick decision guide

* **Can it be immutable?** Do that first.
* **Must it be shared & mutable?** Encapsulate state behind an actor or a queue.
* **Tiny unavoidable mutation?** Guard with `Interlocked`/`lock`/`SemaphoreSlim` (short, ordered, no I/O).
* **Collections?** Prefer immutable or `Concurrent*` types.
