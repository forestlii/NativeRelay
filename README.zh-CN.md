# NativeRelay

[English](README.md) · **简体中文**

> *把 Unity 里原生层的子线程异步回调，安全中继回主线程，按请求一一对应派发 —— 线程安全、热路径低分配。*

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Unity 6+](https://img.shields.io/badge/Unity-6000.4%2B-black)

## 解决什么问题

任何调用了原生异步能力（录音、定位、蓝牙、相机、推送、第三方 SDK 回调）的 Unity 项目，都会撞到
同一堵墙：**原生结果回调发生在子线程，而 Unity API 只能在主线程调用。** 而且多个请求并发时，
结果**乱序**返回，你必须分清哪个结果对应哪一次请求。

NativeRelay 把这整条链路标准化 —— *子线程回调 → 安全切回主线程 → 按请求派发* —— 你不必为每个 SDK
重写一遍。核心中继**尽量压低稳态热路径分配**（双缓冲复用、委托缓存）、**无第三方依赖**，且**克隆下来即可运行**
（纯 C# 模拟通道，无需真机/key/联网）。

## 安装

**推荐 —— Package Manager + git URL：**

1. 打开你的 Unity 6 工程 → **Window → Package Manager**。
2. 左上角 **`+` → Add package from git URL…**，粘贴并 **Add**：
   ```
   https://github.com/forestlii/NativeRelay.git
   ```
   这会把框架（`Runtime/`）作为 `com.likeon.nativerelay` 装进来。
   想锁定某个版本，在末尾加 tag，例如 `…/NativeRelay.git#v0.2.0`。
3. *（可选，想跑示例）* 选中 **NativeRelay** → **Samples** 标签 → **Import** 一个示例。

要求 **Unity 6（6000.4）及以上**。

> **更新** git-URL 包：Unity 会缓存解析到的提交。要拉新版本，请移除该包再重新 Add git URL
> （或改 `Packages/packages-lock.json` 里它的条目）。如果你要频繁改包，建议用 **Add package from disk**
> 指向本地克隆的 `package.json`，Unity 会自动跟随你的改动。

### 一分钟跑通示例

1. 用上面的 git URL 安装，然后 **Samples → Import "Basic Mock Demo"**。
2. 新建空场景 → 加一个空 **GameObject**。
3. **Add Component** → 搜 **`BasicMockDemo`** → 挂上。
4. 按 **Play**，点 **Send**，看并发请求*乱序*回来、在**主线程**被派发（日志显示每个 seed 的发出帧 → 返回帧）。

示例用 IMGUI，零额外依赖 —— 不需要搭场景/预制体。

## 30 秒上手

```csharp
using Likeon.NativeRelay;

// 1) 按当前平台选一个原生通道（编辑器/Windows→Mock、Android→JNI、iOS→P/Invoke）。
INativeChannel channel = NativeChannelFactory.CreateForCurrentPlatform();

// 2) 创建一个由（自动生成的）主线程 dispatcher 每帧驱动的桥。
Bridge bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

// 3) 发起请求。onResult 在【主线程】被调用，可安全用 Unity API。
//    结果是 (int code, string data)：code 是你的状态码（1/0/10086…）或框架保留码
//    （RelayCode.Timeout/Disposed）；data 是可选文本/路径。
bridge.Request(
    command: (int)MyCommand.DoSomething,     // 你自己的枚举，强转 int
    payload: null,                           // 可选 string 输入（参数/路径/url）
    onResult: (code, data) =>
    {
        if (code == RelayCode.Timeout) { /* 超时 */ return; }
        if (code == 1) { /* 成功：用 data（文本/路径） */ }
        else { /* 业务错误码：自己包文案 */ }
    });

// 用完时：
bridge.Dispose(); // 给所有未完成请求回调 Disposed 码，并 Dispose 底层通道
```

你定义自己的命令枚举并强转 `int`（核心层从不假设业务含义）：

```csharp
public enum MyCommand { DoSomething = 1, DoAnother = 2 }
```

## API 速查

| 类型 | 作用 |
|---|---|
| `Bridge.Request(int command, string payload, Action<int code, string data> onResult)` | 发起请求，返回 `seed`；回调在主线程执行。 |
| `MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds, capacity)` | 创建每帧被驱动的桥。 |
| `NativeChannelFactory.CreateForCurrentPlatform()` | 按平台挑通道（唯一 `#if` 处）。 |
| `INativeChannel` | 原生层接缝：`Send(long seed, int command, string payload)` + `event Action<long, int, string> OnResult`（可子线程触发）+ `Dispose()`。 |
| `MockChannel` | 纯 C# 通道，子线程随机延迟后回 `(code, data)`。 |
| `RelayCode` | 框架保留码：`Timeout` / `Disposed`。业务码（1/0/10086…）自定义。 |

`command`/`code` 刻意用 `int` —— 跨线程、跨 JNI/P-Invoke 边界都不装箱，原生侧 `switch(int)` 干净分发。
`payload`/`data` 用 `string`（覆盖文本/路径结果；大块二进制走文件**路径**，不传内存字节）。
**框架从不解释 `code`、从不碰 `data`，只做中继。**

## 平台分发与原生通道

`NativeChannelFactory.CreateForCurrentPlatform()` 按平台挑通道——**唯一**用 `#if` 的地方
（编辑器/Windows→`MockChannel`、Android→`AndroidChannel`(JNI)、iOS→`IosChannel`(P/Invoke)），藏在
干净的 `INativeChannel` 接口后面。

包内含 `AndroidChannel`(JNI) 与 `IosChannel`(P/Invoke + `GCHandle`) 的 **C# 侧**；你提供配套原生库
（Android 打 `.aar`，见 [docs/native-android.md](docs/native-android.md)；iOS 暴露 C ABI 的静态库，见
[docs/native-ios.md](docs/native-ios.md)）。想自己写通道，实现 `INativeChannel` 即可。

### 原生代码在哪、怎么打包

**本包是 100% C#** —— 不含任何 `.java` / `.aar` / `.so`。原生侧是*你的* `INativeChannel` 实现，
因为它具体做什么（录音、ASR、蓝牙……）因项目而异。所以通过 git URL 安装本包**只会拉到 C# 框架**，
不会拉下任何原生源码（这里根本没有）。

真实移动端发布时，原生层以**预编译二进制**形式分发，而非散落的源码：

- **安卓** —— 在 Android Studio 把 Java/Kotlin 编成 **`.aar`**（或 `.jar`），放进
  `Assets/Plugins/Android/`，Unity/Gradle 打包时并入；C# 侧 `AndroidChannel` 用
  `AndroidJavaObject` / `AndroidJavaClass`（JNI）调用它。
- **iOS** —— 编成 `.framework` / `.a`，C# 经 `[DllImport]`（P/Invoke）调用。

> UPM 包*可以*携带原生二进制（如 `Runtime/Plugins/Android/xxx.aar`），那样 git 安装就会把它拉下来——
> 这正是原生插件的分发方式。NativeRelay 核心刻意不带，以保持纯 C#、体积小、易跨引擎移植。

## 示例

在 Package Manager → NativeRelay → **Samples** 里导入：

- **Basic Mock Demo** —— 并发请求乱序回来、在主线程派发（每个 seed 的发出帧 → 返回帧）。
- **ASR Demo** —— `MockChannel` 模拟语音→文本驱动一段假对话（无真实 ASR、无麦克风、无 key）。

两个示例都用 IMGUI、零额外依赖，挂一个组件到空物体即可跑。

## 工作原理

`seed 序号（Interlocked）+ pending 表 + 双缓冲队列（持锁只做 O(1) 交换）+ 每帧主线程派发 + 超时清理`。
结果即到即派发（不保证顺序，只保证 seed 一一对应，以避免队头阻塞）。

分层图与一次请求的完整时序图见 [docs/architecture.md](docs/architecture.md)。

## 状态

当前版本：**`0.2.0`**。纯码契约（`Request(int, string, Action<int,string>)`、结果 `(code, data)`）已定型；
`1.0` 之前小版本仍可能演进。

## 环境要求

- **Unity 6（6000.4）+**（本地验证于 `6000.4.10f1`）。
- 纯 C#、无第三方 DLL。业务层若想用 Lua（xLua/toLua），通过绑定调 `Bridge.Request` 即可——核心不依赖 Lua。

---
作者：**Likeon** · GitHub [forestlii](https://github.com/forestlii) · License: MIT · © 2026 Likeon
