# NativeRelay

**English** · [简体中文](README.zh-CN.md)

> *Thread-safe relay for native async callbacks in Unity — dispatched on the main thread, one request ↔ one result.*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Unity 6+](https://img.shields.io/badge/Unity-6000.4%2B-black)

## The problem it solves

Any Unity project that calls a native async capability — recording, location, Bluetooth,
camera, push, third-party SDK callbacks — hits the same wall: **the native result comes
back on a background thread, but Unity APIs may only be used on the main thread.** And when
several requests are in flight, results come back **out of order**, so you must know which
result belongs to which request.

NativeRelay standardizes that whole path — *background callback → safely back to the main
thread → dispatched per request* — so you don't re-implement it for every SDK. The core
relay is built to **keep steady-state hot-path allocations low** (reused double buffers,
cached delegates), has **no third-party dependencies**, and **runs the moment you clone it**
(a pure-C# mock channel — no device, no key, no network).

## Install

**Recommended — Unity Package Manager (git URL):**

1. Open your Unity 6 project → **Window → Package Manager**.
2. Top-left **`+` → Add package from git URL…**, paste and **Add**:
   ```
   https://github.com/forestlii/NativeRelay.git
   ```
   This installs the framework (`Runtime/`) as package `com.likeon.nativerelay`.
   To pin a specific release, append a tag, e.g. `…/NativeRelay.git#v0.2.0`.
3. *(optional, to try a demo)* Select **NativeRelay** → **Samples** tab → **Import** a sample.

Requires **Unity 6 (6000.4)+**.

> **Updating** a git-URL package: Unity caches the resolved commit. To pull a newer version,
> remove the package and re-add the git URL (or bump its entry in `Packages/packages-lock.json`).
> If you iterate on the package a lot, prefer **Add package from disk** pointing at a local
> clone's `package.json` — Unity then picks up your edits automatically.

### Try a sample in 1 minute

1. Install via the git URL (above), then **Samples → Import "Basic Mock Demo"**.
2. Create an empty scene → add an empty **GameObject**.
3. **Add Component** → search **`BasicMockDemo`** → attach it.
4. Press **Play**, click **Send**, and watch concurrent requests come back *out of order*
   and get dispatched on the **main thread** (the log shows send-frame → return-frame per seed).

The samples use IMGUI, so they have zero extra dependencies — no scene/prefab setup needed.

## 30-second quick start

```csharp
using Likeon.NativeRelay;

// 1) Pick a native channel for the current platform
//    (Editor/Windows -> Mock, Android -> JNI, iOS -> P/Invoke).
INativeChannel channel = NativeChannelFactory.CreateForCurrentPlatform();

// 2) Create a bridge driven each frame by the (auto-created) main-thread dispatcher.
Bridge bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

// 3) Fire a request. onResult is invoked ON THE MAIN THREAD — safe to touch Unity APIs.
//    The result is (int code, string data): code is your status (1/0/10086…) or a framework
//    reserved code (RelayCode.Timeout/Disposed); data is optional text/path.
bridge.Request(
    command: (int)MyCommand.DoSomething,     // your own enum, cast to int
    payload: null,                           // optional string input (params/path/url)
    onResult: (code, data) =>
    {
        if (code == RelayCode.Timeout) { /* timed out */ return; }
        if (code == 1) { /* success: use data (text/path) */ }
        else { /* business error code -> wrap your own message */ }
    });

// when done:
bridge.Dispose(); // fails any pending request with RelayCode.Disposed, disposes the channel
```

You define your own command enum and cast to `int` (the core never assumes business meaning):

```csharp
public enum MyCommand { DoSomething = 1, DoAnother = 2 }
```

## Using from Lua (xLua)

Driving the bridge from Lua works the same way — every business call, plus all
success / failure / error-code handling, can live in Lua. A ready-to-use boilerplate
(thin Lua wrapper + the required xLua gen config + a pure-Lua business sample) lives in the
companion repo: **[NativeRelay-Native / `examples/xlua/`](https://github.com/forestlii/NativeRelay-Native/tree/main/examples/xlua)**.

```lua
local NR        = CS.Likeon.NativeRelay
local RelayCode = NR.RelayCode

local channel = NR.NativeChannelFactory.CreateForCurrentPlatform()
local bridge  = NR.MainThreadDispatcher.Instance:CreateBridge(channel, 5.0)  -- ':' for instance calls

-- onResult runs on the MAIN thread, so the (non-thread-safe) Lua VM is safe to touch.
bridge:Request(myCommand, payload, function(code, data)
    if code == RelayCode.Timeout then        -- timed out
    elseif code == 1 then                    -- success: use data (text/path)
    else                                     -- your business error code
    end
end)
```

> **IL2CPP / AOT trap:** passing a Lua function as the C# `Action<int,string>` onResult
> throws on device unless that delegate is registered in `[CSharpCallLua]` and xLua code is
> regenerated. The sample's `NativeRelayXLuaConfig.cs` does exactly this — don't skip it.

## API at a glance

| Type | Purpose |
|---|---|
| `Bridge.Request(int command, string payload, Action<int code, string data> onResult)` | Fire a request; returns its `seed`. `onResult` runs on the main thread. |
| `MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds, capacity)` | Create a bridge pumped every frame. |
| `NativeChannelFactory.CreateForCurrentPlatform()` | Pick the right `INativeChannel` per platform (the one place with `#if`). |
| `INativeChannel` | The native seam: `Send(long seed, int command, string payload)` + `event Action<long, int, string> OnResult` (may fire off-thread) + `Dispose()`. |
| `MockChannel` | Pure-C# channel replying `(code, data)` on a background thread after a random delay. |
| `RelayCode` | Framework-reserved result codes: `Timeout` / `Disposed`. Business codes (1/0/10086…) are yours. |

`command`/`code` are `int` on purpose — they cross threads and the JNI/P-Invoke boundary
without boxing and switch cleanly on the native side. `payload`/`data` are `string`
(cover text/path results; big binary is delivered via a file **path**, not in-memory bytes).
**The framework never interprets `code` and never touches `data` — it just relays.**

## Platform dispatch & native channels

`NativeChannelFactory.CreateForCurrentPlatform()` picks the right channel per platform — the
**one place** that uses `#if` (Editor/Windows → `MockChannel`, Android → `AndroidChannel`/JNI,
iOS → `IosChannel`/P-Invoke), behind the clean `INativeChannel` interface.

The package ships the C# side of `AndroidChannel` (JNI) and `IosChannel` (P/Invoke + `GCHandle`).
You provide the matching native binary implementing the documented contract — Android: build an
`.aar` (see [docs/native-android.md](docs/native-android.md)); iOS: a static lib exposing the C
ABI (see [docs/native-ios.md](docs/native-ios.md)). To write your own channel, just implement
`INativeChannel` (`Send(long seed, int command, string payload)` + `event Action<long,int,string> OnResult` + `Dispose`).

### Where the native code lives

**This package is 100% C#** — it contains no `.java` / `.aar` / `.so`. The native side is
*your* `INativeChannel` implementation, because what it does (recording, ASR, BLE, …) is
project-specific. So installing this package via the git URL pulls **only the C# framework**;
no native source comes down (there is none here).

In a real mobile build, the native layer ships as a **prebuilt binary**, not loose source:

- **Android** — build your Java/Kotlin in Android Studio into an **`.aar`** (or `.jar`), drop it
  under `Assets/Plugins/Android/`; Unity/Gradle bundles it. The C# `AndroidChannel` calls it
  via `AndroidJavaObject` / `AndroidJavaClass` (JNI).
- **iOS** — build a `.framework` / `.a` and call it from C# via `[DllImport]` (P/Invoke).

> A package *can* ship native binaries (e.g. `Runtime/Plugins/Android/xxx.aar`) and then git
> install would pull them — that's how native plugins are distributed. NativeRelay's core
> deliberately does not, to stay pure-C#, tiny, and engine-portable.

## Samples

Import via Package Manager → NativeRelay → **Samples**:

- **Basic Mock Demo** — concurrent requests come back out of order and get dispatched on the
  main thread (send-frame → return-frame per seed).
- **ASR Demo** — a `MockChannel` simulates speech→text results driving a tiny fake dialogue
  (no real ASR engine, no microphone, no key).

Both samples use IMGUI, so they have zero extra dependencies and run by attaching one
component to an empty GameObject.

## How it works

`seed (Interlocked) + pending table + double-buffer queue (lock holds only an O(1) swap) +
per-frame main-thread dispatch + timeout cleanup`. Results are dispatched as they arrive
(no ordering guarantee — only seed correspondence — to avoid head-of-line blocking).

See [docs/architecture.md](docs/architecture.md) for the layered diagram and a full
one-request sequence diagram.

## Status

Current version: **`0.2.0`**. The pure-code contract (`Request(int, string, Action<int,string>)`,
results as `(code, data)`) is settled; the API may still evolve in minor versions before `1.0`.

## Requirements

- **Unity 6 (6000.4)+** (verified locally on `6000.4.10f1`).
- Pure C#, no third-party DLLs. A Lua business layer (xLua/toLua) can call `Bridge.Request`
  through a binding if you want — the core doesn't depend on Lua.

---
Author: **Likeon** · GitHub [forestlii](https://github.com/forestlii) · License: MIT · © 2026 Likeon
