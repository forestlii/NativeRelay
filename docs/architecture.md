# NativeRelay — Architecture

NativeRelay standardizes one recurring problem in Unity: **native async APIs call back on
a background thread, but Unity APIs may only be touched on the main thread.** It relays
those callbacks safely to the main thread and dispatches each result to the request that
asked for it — *one request ↔ one result* — with **zero steady-state GC allocation**.

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│  Business layer (your game code; C# — or Lua via a binding)  │
│   · calls Bridge.Request(command, payload, onResult)         │
│   · receives onResult ON THE MAIN THREAD, then reacts        │
└───────────────▲───────────────────────────┬─────────────────┘
                │ onResult (main thread)      │ Request
┌───────────────┴───────────────────────────▼─────────────────┐
│  Framework layer  (Likeon.NativeRelay, the plugin core)      │
│   · SeedGenerator      — Interlocked auto-increment seed      │
│   · PendingTable       — Dictionary<seed, ctx> + timeout scan │
│   · DoubleBufferQueue  — sub-thread writes / main reads, 0-GC │
│   · RelayPump          — pure C# (no UnityEngine): drain +    │
│                          dispatch by seed + timeout cleanup   │
│   · MainThreadDispatcher — MonoBehaviour shell, pumps/frame   │
└───────────────▲───────────────────────────┬─────────────────┘
                │ OnResult(seed,code,data)[bg]│ Send(seed,cmd,payload)
┌───────────────┴───────────────────────────▼─────────────────┐
│  Native abstraction  INativeChannel  (a replaceable impl)    │
│   · MockChannel    — pure C#, no device/key (default for demo)│
│   · AndroidChannel — JNI (you provide)                       │
│   · iOS / any SDK  — recording / location / BLE / push …      │
└──────────────────────────────────────────────────────────────┘
```

The core **does not care** what the native side actually does. Anything that produces an
async result on a background thread is just an `INativeChannel` implementation.

## Core mechanism

1. **seed** — every request gets a unique, thread-safe auto-incrementing `long`
   (`Interlocked.Increment`). This is what makes "one request ↔ one result" possible even
   when results come back out of order.
2. **pending table** — `Dictionary<seed, ctx>` stores each request's callbacks + start
   time. Registered on request, removed when matched.
3. **seed is passed through** to the native side, which returns the result carrying the
   **same seed**.
4. **background callback → enqueue only** — the native `OnResult` fires on a worker thread
   and merely pushes `(seed, code, data)` into a thread-safe queue. It never touches Unity APIs.
5. **next-frame main-thread dispatch** — a per-frame pump swaps + drains the queue, looks
   up each seed in the pending table, and invokes its callback **on the main thread**.
6. **no ordering guarantee, only seed correspondence** — whoever comes back first is
   dispatched first (avoids head-of-line blocking from one slow request).

### Three engineering details

- **Minimal lock scope (double buffer).** Two queues — one written by worker threads, one
  read by the main thread. Each frame the main thread holds the lock only long enough to
  **swap the two references (O(1))**, then drains the read queue **outside the lock**, so
  workers are almost never blocked.
- **Zero GC.** The two buffers are reused (never re-`new`ed); each batch is `Clear()`ed
  rather than reallocated; dispatch delegates are cached. Steady-state hot path = 0 alloc,
  asserted by tests (`Is.Not.AllocatingGCMemory` in PlayMode; allocation-byte checks
  off-Unity).
- **Timeout cleanup.** If the native side never replies (lost/crashed), the pump
  periodically scans the pending table and removes entries older than `timeout`, invoking
  a `RelayCode.Timeout` so the business is notified instead of leaking.

## Sequence of one request

```
Business        Bridge            INativeChannel        worker thread        MainThreadDispatcher
   │               │                    │                     │                       │
   │ Request(cmd,  │                    │                     │                       │
   │   payload,    │                    │                     │                       │
   │   onResult) ─►│                    │                     │                       │
   │               │ seed = Next()      │                     │                       │
   │               │ pending.Register   │                     │                       │
   │               │   (seed, ctx)      │                     │                       │
   │               │ Send(seed,cmd, ───►│                     │                       │
   │               │      payload)      │ (does async work) ─►│                       │
   │ ◄── seed      │                    │                     │                       │
   │               │                    │                     │ ... work ...          │
   │               │                    │   OnResult(seed, ◄───│ (BACKGROUND THREAD)   │
   │               │   pump.Enqueue ◄───────   bytes)          │                       │
   │               │   (seed,bytes)     │                     │                       │
   │               │   [thread-safe queue, no Unity API here] │                       │
   │               │                    │                     │                       │
   │               │                    │                     │     Update() each ────│
   │               │                    │                     │     frame: Pump(now)  │
   │               │   SwapAndDrain ◄───────────────────────────────────────────────-│
   │               │   pending.TryComplete(seed) → ctx        │                       │
   │ onResult( ◄───│   ctx.OnResult(code,data) [MAIN THREAD]  │                       │
   │  code,data)   │   pending.Remove(seed)                   │                       │
   │ (safe to      │   ScanTimeouts(now) → RelayCode.Timeout│                       │
   │  touch Unity) │   for anything overdue                   │                       │
```

## Public contract (pure-code)

```csharp
public interface INativeChannel : IDisposable {
    void Send(long seed, int command, string payload);   // command/code are int; payload/data are string
    event Action<long, int, string> OnResult;            // (seed, code, data) — may fire on a background thread
}

public sealed class Bridge {
    long Request(int command, string payload, Action<int, string> onResult); // returns seed
    void Pump();      // driven each frame by MainThreadDispatcher
    void Dispose();   // unsubscribes, fails pending with RelayCode.Disposed, disposes channel
}

public static class RelayCode { public const int Timeout = int.MinValue; public const int Disposed = int.MinValue + 1; }
```

`command`/`code` are `int` (cross threads + the JNI/P-Invoke boundary with zero allocation,
clean `switch` on the native side); `payload`/`data` are `string` (cover the common
text/path results; big binary is delivered via a file **path**, not in-memory bytes).
**The framework never interprets `code` and never touches `data` — it just relays.** It only
mints a code itself when it can't get a native result: `RelayCode.Timeout` / `RelayCode.Disposed`.

Platform dispatch lives in one place — `NativeChannelFactory.CreateForCurrentPlatform()` selects
`MockChannel` (Editor/Windows) / `AndroidChannel` (JNI) / `IosChannel` (P/Invoke) via `#if`, behind
the `INativeChannel` interface.

## Packaging: where the native layer lives

The framework package is **100% C#** — it carries no `.java` / `.aar` / `.so`. The native
side is an `INativeChannel` implementation, and what it does (recording, ASR, BLE, …) is
project-specific, so it lives outside the core:

- **Android**: build Java/Kotlin in Android Studio into an **`.aar`/`.jar`** under
  `Assets/Plugins/Android/` (or a package's `Runtime/Plugins/Android/`); the C# `AndroidChannel`
  calls it over JNI (`AndroidJavaObject`/`AndroidJavaClass`).
- **iOS**: a `.framework`/`.a` called from C# via `[DllImport]` (P/Invoke).

Consequence: installing the core package via UPM git URL pulls **only the C# framework** — no
native source. A package *may* bundle native binaries under `Runtime/Plugins/<platform>/`
(then git install would fetch them); the NativeRelay core deliberately stays pure-C# so it is
small and engine-portable. A concrete native channel is therefore best shipped as a separate
integration example (its own Android Studio / Xcode project producing the binary) rather than
inside this pure-C# core.
