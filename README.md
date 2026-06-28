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
   To pin a specific release, append a tag, e.g. `…/NativeRelay.git#v0.2.0`.
   想锁定某个版本，在末尾加 tag，例如 `…/NativeRelay.git#v0.2.0`。
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

// 1) Pick a native channel for the current platform (Editor/Windows -> Mock, Android -> JNI, iOS -> P/Invoke).
//    按当前平台选一个原生通道（编辑器/Windows→Mock、Android→JNI、iOS→P/Invoke）。
INativeChannel channel = NativeChannelFactory.CreateForCurrentPlatform();

// 2) Create a bridge driven each frame by the (auto-created) main-thread dispatcher.
//    创建一个由（自动生成的）主线程 dispatcher 每帧驱动的桥。
Bridge bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

// 3) Fire a request. onResult is invoked ON THE MAIN THREAD — safe to touch Unity APIs.
//    The result is (int code, string data): code is your status (1/0/10086…) or a framework
//    reserved code (RelayCode.Timeout/Disposed); data is optional text/path.
//    发起请求。onResult 在【主线程】被调用，可安全用 Unity API。结果是 (int code, string data)：
//    code 是你的状态码（1/0/10086…）或框架保留码（RelayCode.Timeout/Disposed）；data 是可选文本/路径。
bridge.Request(
    command: (int)MyCommand.DoSomething,     // your own enum, cast to int / 你自己的枚举，强转 int
    payload: null,                           // optional string input / 可选 string 输入
    onResult: (code, data) =>
    {
        if (code == RelayCode.Timeout) { /* timed out / 超时 */ return; }
        if (code == 1) { /* success: use data (text/path) / 成功：用 data */ }
        else { /* business error code -> wrap your own message / 业务错误码：自己包文案 */ }
    });

// when done / 用完时：
bridge.Dispose(); // fails any pending request with RelayCode.Disposed, disposes the channel
                  // 给所有未完成请求回调 Disposed 码，并 Dispose 底层通道
```

You define your own command enum and cast to `int` (the core never assumes business
meaning). / 你定义自己的命令枚举并强转 `int`（核心层从不假设业务含义）：

```csharp
public enum MyCommand { DoSomething = 1, DoAnother = 2 }
```

## API at a glance · API 速查

| Type / 类型 | Purpose / 作用 |
|---|---|
| `Bridge.Request(int command, string payload, Action<int code, string data> onResult)` | Fire a request; returns its `seed`. `onResult` runs on the main thread. / 发起请求，返回 `seed`；回调在主线程执行。 |
| `MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds, capacity)` | Create a bridge pumped every frame. / 创建每帧被驱动的桥。 |
| `NativeChannelFactory.CreateForCurrentPlatform()` | Pick the right `INativeChannel` per platform (the one place with `#if`). / 按平台挑通道（唯一 `#if` 处）。 |
| `INativeChannel` | The native seam: `Send(long seed, int command, string payload)` + `event Action<long, int, string> OnResult` (may fire off-thread) + `Dispose()`. / 原生层接缝。 |
| `MockChannel` | Pure-C# channel replying `(code, data)` on a background thread after a random delay. / 纯 C# 通道。 |
| `RelayCode` | Framework-reserved result codes: `Timeout` / `Disposed`. Business codes (1/0/10086…) are yours. / 框架保留码；业务码自定义。 |

`command` is an `int` on purpose — it crosses threads and the JNI/P-Invoke boundary with
zero allocation and switches cleanly on the native side. (No `string`, no generics — those
allocate or box and would break the zero-GC guarantee.)

`command` 刻意用 `int` —— 它跨线程、跨 JNI/P-Invoke 边界都零分配，原生侧 `switch(int)` 干净分发。
（不用 `string`、不用泛型——那会分配或装箱，破坏零 GC 保证。）

## Platform dispatch & native channels · 平台分发与原生通道

`NativeChannelFactory.CreateForCurrentPlatform()` picks the right channel per platform — the
**one place** that uses `#if` (Editor/Windows → `MockChannel`, Android → `AndroidChannel`/JNI,
iOS → `IosChannel`/P-Invoke), behind the clean `INativeChannel` interface.

`NativeChannelFactory.CreateForCurrentPlatform()` 按平台挑通道——**唯一**用 `#if` 的地方
（编辑器/Windows→`MockChannel`、Android→`AndroidChannel`(JNI)、iOS→`IosChannel`(P/Invoke)），藏在
干净的 `INativeChannel` 接口后面。

The package ships the C# side of `AndroidChannel` (JNI) and `IosChannel` (P/Invoke + `GCHandle`).
You provide the matching native binary implementing the documented contract — Android: build an
`.aar` (see [docs/native-android.md](docs/native-android.md)); iOS: a static lib exposing the C
ABI (see [docs/native-ios.md](docs/native-ios.md)). To write your own channel, just implement
`INativeChannel` (`Send(long seed, int command, string payload)` + `event Action<long,int,string> OnResult` + `Dispose`).

包内含 `AndroidChannel`(JNI) 与 `IosChannel`(P/Invoke + `GCHandle`) 的 **C# 侧**；你提供配套原生库
（Android 打 `.aar`，见 [docs/native-android.md](docs/native-android.md)；iOS 暴露 C ABI 的静态库，见
[docs/native-ios.md](docs/native-ios.md)）。想自己写通道，实现 `INativeChannel` 即可。

### Where the native code lives · 原生代码在哪、怎么打包

**This package is 100% C#** — it contains no `.java` / `.aar` / `.so`. The native side is
*your* `INativeChannel` implementation, because what it does (recording, ASR, BLE, …) is
project-specific. So installing this package via the git URL pulls **only the C# framework**;
no native source comes down (there is none here).

**本包是 100% C#** —— 不含任何 `.java` / `.aar` / `.so`。原生侧是*你的* `INativeChannel` 实现，
因为它具体做什么（录音、ASR、蓝牙……）因项目而异。所以通过 git URL 安装本包**只会拉到 C# 框架**，
不会拉下任何原生源码（这里根本没有）。

In a real mobile build, the native layer ships as a **prebuilt binary**, not loose source:

真实移动端发布时，原生层以**预编译二进制**形式分发，而非散落的源码：

- **Android** — build your Java/Kotlin in Android Studio into an **`.aar`** (or `.jar`), drop it
  under `Assets/Plugins/Android/`; Unity/Gradle bundles it. The C# `AndroidChannel` calls it
  via `AndroidJavaObject` / `AndroidJavaClass` (JNI).
  **安卓** —— 在 Android Studio 把 Java/Kotlin 编成 **`.aar`**（或 `.jar`），放进
  `Assets/Plugins/Android/`，Unity/Gradle 打包时并入；C# 侧 `AndroidChannel` 用
  `AndroidJavaObject` / `AndroidJavaClass`（JNI）调用它。
- **iOS** — build a `.framework` / `.a` and call it from C# via `[DllImport]` (P/Invoke).
  **iOS** —— 编成 `.framework` / `.a`，C# 经 `[DllImport]`（P/Invoke）调用。

> A package *can* ship native binaries (e.g. `Runtime/Plugins/Android/xxx.aar`) and then git
> install would pull them — that's how native plugins are distributed. NativeRelay's core
> deliberately does not, to stay pure-C#, tiny, and engine-portable.
>
> UPM 包*可以*携带原生二进制（如 `Runtime/Plugins/Android/xxx.aar`），那样 git 安装就会把它拉下来——
> 这正是原生插件的分发方式。NativeRelay 核心刻意不带，以保持纯 C#、体积小、易跨引擎移植。

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

Current version: **`0.2.0`**. The pure-code contract (`Request(int, string, Action<int,string>)`,
results as `(code, data)`) is settled; the API may still evolve in minor versions before `1.0`.

当前版本：**`0.2.0`**。纯码契约（`Request(int, string, Action<int,string>)`、结果 `(code, data)`）已定型；
`1.0` 之前小版本仍可能演进。

## Requirements · 环境要求

- **Unity 6 (6000.4)+** (verified locally on `6000.4.10f1`). / **Unity 6（6000.4）+**（本地验证于 `6000.4.10f1`）。
- Pure C#, no third-party DLLs. A Lua business layer (xLua/toLua) can call `Bridge.Request`
  through a binding if you want — the core doesn't depend on Lua. / 纯 C#、无第三方 DLL。
  业务层若想用 Lua（xLua/toLua），通过绑定调 `Bridge.Request` 即可——核心不依赖 Lua。

---
Author / 作者: **Likeon** · GitHub [forestlii](https://github.com/forestlii) · License: MIT · © 2026 Likeon
