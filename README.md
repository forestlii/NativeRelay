# NativeRelay

> *Thread-safe relay for native async callbacks in Unity — dispatched on the main thread, one request ↔ one result.*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Unity 6+](https://img.shields.io/badge/Unity-6000.4%2B-black)
![Zero-GC](https://img.shields.io/badge/hot--path-zero--GC-green)

## The problem it solves

Any Unity project that calls a native async capability — recording, location, Bluetooth,
camera, push, third-party SDK callbacks — hits the same wall: **the native result comes
back on a background thread, but Unity APIs may only be used on the main thread.** And when
several requests are in flight, results come back **out of order**, so you must know which
result belongs to which request.

NativeRelay standardizes that whole path — *background callback → safely back to the main
thread → dispatched per request* — so you don't re-implement it for every SDK. It is
**zero-GC** on the steady-state hot path, has **no third-party dependencies**, and **runs
the moment you clone it** (a pure-C# mock channel — no device, no key, no network).

## Install

Unity Package Manager → **Add package from git URL**:

```
https://github.com/forestlii/NativeRelay.git
```

Requires **Unity 6 (6000.4)+**. Package id: `com.likeon.nativerelay`.

## 30-second quick start

```csharp
using Likeon.NativeRelay;

// 1) Pick a native channel. MockChannel is pure C# (no device) — swap it for your own later.
var channel = new MockChannel(minDelayMs: 100, maxDelayMs: 800);

// 2) Create a bridge driven each frame by the (auto-created) main-thread dispatcher.
Bridge bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

// 3) Fire a request. onResult is invoked ON THE MAIN THREAD — safe to touch Unity APIs.
bridge.Request(
    command: (int)MyCommand.DoSomething,     // your own enum, cast to int
    payload: null,
    onResult: bytes => { /* main thread: use the result */ },
    onError:  err   => { /* Timeout / ChannelFailure / Disposed */ });

// when done:
bridge.Dispose(); // fails any pending request with BridgeError.Disposed, disposes the channel
```

You define your own command enum and cast to `int` (the core never assumes business
meaning):

```csharp
public enum MyCommand { DoSomething = 1, DoAnother = 2 }
```

## API at a glance

| Type | Purpose |
|---|---|
| `Bridge.Request(int command, byte[] payload, Action<byte[]> onResult, Action<BridgeError> onError = null)` | Fire a request; returns its `seed`. `onResult`/`onError` run on the main thread. |
| `MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds, capacity)` | Create a bridge that's pumped every frame. |
| `INativeChannel` | The native seam: `Send(long seed, int command, byte[] payload)` + `event Action<long, byte[]> OnResult` (may fire off-thread) + `Dispose()`. |
| `MockChannel` | Pure-C# channel: replies on a background thread after a random delay. For demos & tests. |
| `BridgeError` | `Timeout` / `ChannelFailure` / `Disposed`, carrying the failing `seed`. |

`command` is an `int` on purpose — it crosses threads and the JNI/P-Invoke boundary with
zero allocation and switches cleanly on the native side. (No `string`, no generics — those
allocate or box and would break the zero-GC guarantee.)

## Plugging in a real native channel

The plugin core doesn't care what the native side does — implement `INativeChannel`:

```csharp
public sealed class AndroidChannel : INativeChannel
{
    public event Action<long, byte[]> OnResult;

    public void Send(long seed, int command, byte[] payload)
    {
        // Call into Java/JNI; pass the seed across so the native side can return it.
        // e.g. AndroidJavaObject / AndroidJavaClass -> your native module.
    }

    // When the native worker thread finishes, raise OnResult(seed, resultBytes).
    // It is fine to raise this on a background thread — NativeRelay relays it to the main thread.

    public void Dispose() { /* release native resources */ }
}
```

On the native side, switch on the `int command` and, when work completes, call back with
the **same seed** and the result bytes. Then just `CreateBridge(new AndroidChannel())` and
the rest of your code is unchanged. The same shape applies to iOS (Objective-C/Swift via
P-Invoke), BLE, location, or any SDK.

## Samples

Import via Package Manager → NativeRelay → **Samples**:

- **Basic Mock Demo** — click a button, watch concurrent requests come back out of order
  and get dispatched on the main thread (shows send-frame → return-frame per seed).
- **ASR Demo** — a `MockChannel` simulates speech→text results driving a tiny fake dialogue
  (no real ASR engine, no microphone, no key).

Both samples use IMGUI so they have zero extra dependencies and run by attaching one
component to an empty GameObject.

## How it works

`seed (Interlocked) + pending table + double-buffer queue (lock holds only an O(1) swap) +
per-frame main-thread dispatch + timeout cleanup`. Results are dispatched as they arrive
(no ordering guarantee — only seed correspondence — to avoid head-of-line blocking).

See [docs/architecture.md](docs/architecture.md) for the layered diagram and a full
one-request sequence diagram.

## Status

🚧 Early development (`0.1.0-dev`). The public contract (`INativeChannel`, `Bridge.Request`)
is frozen; APIs around it may still evolve before `0.1.0`.

## Requirements

- **Unity 6 (6000.4)+** (verified locally on `6000.4.10f1`).
- Pure C#, no third-party DLLs. A Lua business layer (xLua/toLua) can call `Bridge.Request`
  through a binding if you want — the core doesn't depend on Lua.

---
Author: **Likeon** · GitHub [forestlii](https://github.com/forestlii) · License: MIT · © 2026 Likeon
