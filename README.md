# NativeRelay

> *Thread-safe relay for native async callbacks in Unity — dispatched on the main thread, one request ↔ one result.*
>
> *把 Unity 里原生层的子线程异步回调，安全中继回主线程，按请求一一对应派发 —— 线程安全、零 GC。*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Unity 6+](https://img.shields.io/badge/Unity-6000.4%2B-black)
![Zero-GC](https://img.shields.io/badge/hot--path-zero--GC-green)

## The problem it solves · 解决什么问题

Any Unity project that calls a native async capability — recording, location, Bluetooth,
camera, push, third-party SDK callbacks — hits the same wall: **the native result comes
back on a background thread, but Unity APIs may only be used on the main thread.** And when
several requests are in flight, results come back **out of order**, so you must know which
result belongs to which request.

任何调用了原生异步能力（录音、定位、蓝牙、相机、推送、第三方 SDK 回调）的 Unity 项目，都会撞到
同一堵墙：**原生结果回调发生在子线程，而 Unity API 只能在主线程调用。** 而且多个请求并发时，
结果**乱序**返回，你必须分清哪个结果对应哪一次请求。

NativeRelay standardizes that whole path — *background callback → safely back to the main
thread → dispatched per request* — so you don't re-implement it for every SDK. It is
**zero-GC** on the steady-state hot path, has **no third-party dependencies**, and **runs
the moment you clone it** (a pure-C# mock channel — no device, no key, no network).

NativeRelay 把这整条链路标准化 —— *子线程回调 → 安全切回主线程 → 按请求派发* —— 你不必为每个 SDK
重写一遍。它在稳态热路径上**零 GC**、**无第三方依赖**，且**克隆下来即可运行**（纯 C# 模拟通道，
无需真机/key/联网）。

## Install · 安装

**Recommended — via Unity Package Manager (git URL) · 推荐做法（Package Manager + git URL）：**

1. Open your Unity 6 project → menu **Window → Package Manager**.
   打开你的 Unity 6 工程 → 菜单 **Window → Package Manager**。
2. Top-left **`+` → Add package from git URL…**, paste and **Add**:
   左上角 **`+` → Add package from git URL…**，粘贴并 **Add**：
   ```
   https://github.com/forestlii/NativeRelay.git
   ```
   This installs the framework (`Runtime/`) as package `com.likeon.nativerelay`.
   这会把框架（`Runtime/`）作为 `com.likeon.nativerelay` 装进来。
   To pin a specific release, append a tag, e.g. `…/NativeRelay.git#v0.1.0`.
   想锁定某个版本，在末尾加 tag，例如 `…/NativeRelay.git#v0.1.0`。
3. *(optional, to try a demo)* Select **NativeRelay** → **Samples** tab → **Import** a sample.
   *（可选，想跑示例）* 选中 **NativeRelay** → **Samples** 标签 → **Import** 一个示例。

Requires **Unity 6 (6000.4)+**. / 要求 **Unity 6（6000.4）及以上**。

> **Updating** a git-URL package: Unity caches the resolved commit. To pull a newer version,
> remove the package and re-add the git URL (or bump its entry in `Packages/packages-lock.json`).
> If you iterate on the package a lot, prefer **Add package from disk** pointing at a local
> clone's `package.json` — Unity then picks up your edits automatically.
>
> **更新** git-URL 包：Unity 会缓存解析到的提交。要拉新版本，请移除该包再重新 Add git URL
> （或改 `Packages/packages-lock.json` 里它的条目）。如果你要频繁改包，建议用 **Add package from disk**
> 指向本地克隆的 `package.json`，Unity 会自动跟随你的改动。

### Try a sample in 1 minute · 一分钟跑通示例

1. Install via the git URL (above), then **Samples → Import "Basic Mock Demo"**.
   用上面的 git URL 安装，然后 **Samples → Import "Basic Mock Demo"**。
2. Create an empty scene → add an empty **GameObject**.
   新建空场景 → 加一个空 **GameObject**。
3. **Add Component** → search **`BasicMockDemo`** → attach it.
   **Add Component** → 搜 **`BasicMockDemo`** → 挂上。
4. Press **Play**, click **Send**, and watch concurrent requests come back *out of order*
   and get dispatched on the **main thread** (the log shows send-frame → return-frame per seed).
   按 **Play**，点 **Send**，看并发请求*乱序*回来、在**主线程**被派发（日志显示每个 seed 的发出帧 → 返回帧）。

The samples use IMGUI, so they have zero extra dependencies — no scene/prefab setup needed.
示例用 IMGUI，零额外依赖 —— 不需要搭场景/预制体。

## 30-second quick start · 30 秒上手

```csharp
using Likeon.NativeRelay;

// 1) Pick a native channel. MockChannel is pure C# (no device) — swap it for your own later.
//    选一个原生通道。MockChannel 是纯 C#（无需真机），以后换成你自己的实现。
var channel = new MockChannel(minDelayMs: 100, maxDelayMs: 800);

// 2) Create a bridge driven each frame by the (auto-created) main-thread dispatcher.
//    创建一个由（自动生成的）主线程 dispatcher 每帧驱动的桥。
Bridge bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

// 3) Fire a request. onResult is invoked ON THE MAIN THREAD — safe to touch Unity APIs.
//    发起请求。onResult 在【主线程】被调用 —— 可安全使用 Unity API。
bridge.Request(
    command: (int)MyCommand.DoSomething,     // your own enum, cast to int / 你自己的枚举，强转 int
    payload: null,
    onResult: bytes => { /* main thread: use the result / 主线程：使用结果 */ },
    onError:  err   => { /* Timeout / ChannelFailure / Disposed */ });

// when done / 用完时：
bridge.Dispose(); // fails any pending request with BridgeError.Disposed, disposes the channel
                  // 给所有未完成请求回调 Disposed，并 Dispose 底层通道
```

You define your own command enum and cast to `int` (the core never assumes business
meaning). / 你定义自己的命令枚举并强转 `int`（核心层从不假设业务含义）：

```csharp
public enum MyCommand { DoSomething = 1, DoAnother = 2 }
```

## API at a glance · API 速查

| Type / 类型 | Purpose / 作用 |
|---|---|
| `Bridge.Request(int command, byte[] payload, Action<byte[]> onResult, Action<BridgeError> onError = null)` | Fire a request; returns its `seed`. Callbacks run on the main thread. / 发起请求，返回 `seed`；回调在主线程执行。 |
| `MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds, capacity)` | Create a bridge pumped every frame. / 创建每帧被驱动的桥。 |
| `INativeChannel` | The native seam: `Send(long seed, int command, byte[] payload)` + `event Action<long, byte[]> OnResult` (may fire off-thread) + `Dispose()`. / 原生层接缝。 |
| `MockChannel` | Pure-C# channel replying on a background thread after a random delay. / 纯 C# 通道，子线程随机延迟后回结果。 |
| `BridgeError` | `Timeout` / `ChannelFailure` / `Disposed`, carrying the failing `seed`. / 失败类别，带出错 seed。 |

`command` is an `int` on purpose — it crosses threads and the JNI/P-Invoke boundary with
zero allocation and switches cleanly on the native side. (No `string`, no generics — those
allocate or box and would break the zero-GC guarantee.)

`command` 刻意用 `int` —— 它跨线程、跨 JNI/P-Invoke 边界都零分配，原生侧 `switch(int)` 干净分发。
（不用 `string`、不用泛型——那会分配或装箱，破坏零 GC 保证。）

## Plugging in a real native channel · 接入真实原生通道

The plugin core doesn't care what the native side does — implement `INativeChannel`:
插件核心完全不关心原生侧具体做什么 —— 实现 `INativeChannel` 即可：

```csharp
public sealed class AndroidChannel : INativeChannel
{
    public event Action<long, byte[]> OnResult;

    public void Send(long seed, int command, byte[] payload)
    {
        // Call into Java/JNI; pass the seed across so the native side can return it.
        // 调入 Java/JNI；把 seed 传过去，让原生侧带着它回结果。
    }

    // When the native worker thread finishes, raise OnResult(seed, resultBytes).
    // It is fine to raise this on a background thread — NativeRelay relays it to the main thread.
    // 原生子线程完成后触发 OnResult(seed, resultBytes)；在子线程触发也没关系，NativeRelay 会切回主线程。

    public void Dispose() { /* release native resources / 释放原生资源 */ }
}
```

On the native side, switch on the `int command` and, when work completes, call back with
the **same seed** and the result bytes. Then just `CreateBridge(new AndroidChannel())` and
the rest of your code is unchanged. The same shape applies to iOS (Objective-C/Swift via
P-Invoke), BLE, location, or any SDK.

原生侧按 `int command` 分发，完成后用**同一个 seed** + 结果字节回调。然后只需
`CreateBridge(new AndroidChannel())`，其余代码不变。iOS（Objective-C/Swift 经 P-Invoke）、
蓝牙、定位或任意 SDK 都是同样的写法。

## Samples · 示例

Import via Package Manager → NativeRelay → **Samples** · 在 Package Manager → NativeRelay → **Samples** 里导入：

- **Basic Mock Demo** — concurrent requests come back out of order and get dispatched on the
  main thread (send-frame → return-frame per seed). / 并发请求乱序回来、在主线程派发。
- **ASR Demo** — a `MockChannel` simulates speech→text results driving a tiny fake dialogue
  (no real ASR engine). / Mock 模拟语音→文本驱动一段假对话（无真实 ASR）。

Both samples use IMGUI, so they have zero extra dependencies and run by attaching one
component to an empty GameObject. / 两个示例都用 IMGUI、零额外依赖，挂一个组件到空物体即可跑。

## How it works · 工作原理

`seed (Interlocked) + pending table + double-buffer queue (lock holds only an O(1) swap) +
per-frame main-thread dispatch + timeout cleanup`. Results are dispatched as they arrive
(no ordering guarantee — only seed correspondence — to avoid head-of-line blocking).

`seed 序号（Interlocked）+ pending 表 + 双缓冲队列（持锁只做 O(1) 交换）+ 每帧主线程派发 + 超时清理`。
结果即到即派发（不保证顺序，只保证 seed 一一对应，以避免队头阻塞）。

See [docs/architecture.md](docs/architecture.md) for the layered diagram and a full
one-request sequence diagram. / 分层图与一次请求的完整时序图见 [docs/architecture.md](docs/architecture.md)。

## Status · 状态

First public release: **`0.1.0`**. The public contract (`INativeChannel`, `Bridge.Request`)
is frozen; the API around it may still evolve in minor versions before `1.0`.

首个公开版本：**`0.1.0`**。公共契约（`INativeChannel`、`Bridge.Request`）已冻结；
`1.0` 之前周边 API 在小版本里仍可能演进。

## Requirements · 环境要求

- **Unity 6 (6000.4)+** (verified locally on `6000.4.10f1`). / **Unity 6（6000.4）+**（本地验证于 `6000.4.10f1`）。
- Pure C#, no third-party DLLs. A Lua business layer (xLua/toLua) can call `Bridge.Request`
  through a binding if you want — the core doesn't depend on Lua. / 纯 C#、无第三方 DLL。
  业务层若想用 Lua（xLua/toLua），通过绑定调 `Bridge.Request` 即可——核心不依赖 Lua。

---
Author / 作者: **Likeon** · GitHub [forestlii](https://github.com/forestlii) · License: MIT · © 2026 Likeon
