# NativeRelay — 架构

[English](architecture.md) · **简体中文**

NativeRelay 解决 Unity 里反复出现的一个问题：**原生异步 API 在子线程回调，而 Unity API 只能在主线程
碰。** 它把这些回调安全中继回主线程，并把每个结果派发回发起它的那次请求——*一个请求 ↔ 一个结果*——
并**尽量压低稳态热路径分配**。

## 分层

```
┌─────────────────────────────────────────────────────────────┐
│  业务层 Business（你的游戏代码；C# —— 或经绑定用 Lua）        │
│   · 调 Bridge.Request(command, payload, onResult)            │
│   · 在【主线程】收到 onResult 后处理                          │
└───────────────▲───────────────────────────┬─────────────────┘
                │ onResult (主线程)           │ Request
┌───────────────┴───────────────────────────▼─────────────────┐
│  框架层 Framework（Likeon.NativeRelay 插件核心）             │
│   · SeedGenerator      —— Interlocked 自增 seed               │
│   · PendingTable       —— Dictionary<seed, ctx> + 超时扫描    │
│   · DoubleBufferQueue  —— 子线程写 / 主线程读，复用           │
│   · RelayPump          —— 纯 C#（不依赖 UnityEngine）：排干 + │
│                           按 seed 派发 + 超时清理             │
│   · MainThreadDispatcher —— MonoBehaviour 薄壳，每帧 Pump     │
└───────────────▲───────────────────────────┬─────────────────┘
                │ OnResult(seed,code,data)[子] │ Send(seed,cmd,payload)
┌───────────────┴───────────────────────────▼─────────────────┐
│  原生抽象 INativeChannel（可替换实现）                       │
│   · MockChannel    —— 纯 C#，无需真机/key（demo 默认）        │
│   · AndroidChannel —— JNI（你提供 .aar）                     │
│   · IosChannel     —— P/Invoke（你提供原生库）               │
└──────────────────────────────────────────────────────────────┘
```

核心**完全不关心**原生侧具体做什么。任何在子线程产出异步结果的东西，都只是一个 `INativeChannel` 实现。

## 核心机制

1. **seed** —— 每次请求得到一个唯一、线程安全的自增 `long`（`Interlocked.Increment`）。这是"一个请求 ↔
   一个结果"在乱序返回时仍成立的关键。
2. **pending 表** —— `Dictionary<seed, ctx>` 存每次请求的回调 + 发起时间，请求时登记、匹配时移除。
3. **seed 透传**到原生侧，原生带着**同一个 seed** 把结果回来。
4. **子线程回调只入队** —— 原生 `OnResult` 在子线程触发，只把 `(seed, code, data)` 塞进线程安全队列，
   绝不碰 Unity API。
5. **下一帧主线程统一派发** —— 每帧的 pump 交换+排干队列，按 seed 在 pending 表查到回调，在**主线程**调用。
6. **不保证顺序、只保证 seed 一一对应** —— 谁先回来先派发（避免一个慢请求造成队头阻塞）。

### 三个工程细节

- **锁粒度最小化（双缓冲）。** 两个队列——一个子线程写、一个主线程读。每帧主线程持锁只够**交换两个
  引用（O(1)）**，然后在**锁外**排干读队列，子线程几乎不被阻塞。
- **低 GC。** 两个缓冲复用（绝不重新 new）；每批 `Clear()` 而非重新分配；派发委托缓存。这让核心中继的
  稳态热路径分配很轻——有测试守着（PlayMode 的 `Is.Not.AllocatingGCMemory`；以及脱离 Unity 的分配字节检查）。
  注意：`string` 形式的 payload/结果本身在调用点仍会分配；框架压低的是*它自己*每次派发的开销，并不能让整条请求路径零分配。
- **超时清理。** 原生若永不回（丢失/崩溃），pump 定期扫 pending 表、移除超过 `timeout` 的项，并以
  `RelayCode.Timeout` 通知业务，防止泄漏。

## 一次请求的时序

```
业务            Bridge            INativeChannel        子线程               MainThreadDispatcher
 │               │                    │                  │                       │
 │ Request(cmd,  │                    │                  │                       │
 │  payload,     │                    │                  │                       │
 │  onResult) ─► │                    │                  │                       │
 │               │ seed = Next()      │                  │                       │
 │               │ pending.Register   │                  │                       │
 │               │ Send(seed,cmd, ───►│                  │                       │
 │               │      payload)      │ （子线程干活）─► │                       │
 │ ◄── seed      │                    │                  │                       │
 │               │                    │  OnResult(seed, ◄─│ （子线程）            │
 │               │  pump.Enqueue ◄────── code,data)      │                       │
 │               │  [线程安全队列，这里不碰 Unity API]   │                       │
 │               │                    │                  │   每帧 Update：Pump   │
 │               │  SwapAndDrain ◄──────────────────────────────────────────────│
 │               │  pending.TryComplete(seed) → ctx      │                       │
 │ onResult( ◄───│  ctx.OnResult(code,data) [主线程]     │                       │
 │  code,data)   │  pending.Remove(seed)                 │                       │
 │ (可安全碰     │  ScanTimeouts(now) → RelayCode.Timeout│                       │
 │  Unity)       │  清理任何超时项                       │                       │
```

## 对外契约（纯码）

```csharp
public interface INativeChannel : IDisposable {
    void Send(long seed, int command, string payload);   // command/code 是 int；payload/data 是 string
    event Action<long, int, string> OnResult;            // (seed, code, data) —— 可子线程触发
}

public sealed class Bridge {
    long Request(int command, string payload, Action<int, string> onResult); // 返回 seed
    void Pump();      // 由 MainThreadDispatcher 每帧驱动
    void Dispose();   // 退订、给未完成请求回 RelayCode.Disposed、Dispose 通道
}

public static class RelayCode { public const int Timeout = int.MinValue; public const int Disposed = int.MinValue + 1; }
```

`command`/`code` 用 `int`（跨线程 + JNI/P-Invoke 边界不装箱，原生侧干净 `switch`）；`payload`/`data` 用
`string`（覆盖常见的文本/路径结果；大块二进制走文件**路径**，不传内存字节）。**框架从不解释 `code`、
从不碰 `data`，只做中继。** 它只在拿不到原生结果时自产码：`RelayCode.Timeout` / `RelayCode.Disposed`。

平台分发集中在一处——`NativeChannelFactory.CreateForCurrentPlatform()` 用 `#if` 选
`MockChannel`（编辑器/Windows）/ `AndroidChannel`（JNI）/ `IosChannel`（P/Invoke），藏在 `INativeChannel` 接口后。

## 打包：原生层在哪

框架包是 **100% C#** —— 不含 `.java` / `.aar` / `.so`。原生侧是 `INativeChannel` 实现，它具体做什么
（录音、ASR、蓝牙……）因项目而异，所以放在核心之外：

- **Android**：在 Android Studio 把 Java/Kotlin 编成 **`.aar`/`.jar`**，放进 `Assets/Plugins/Android/`
  （或某个包的 `Runtime/Plugins/Android/`）；C# 侧 `AndroidChannel` 经 JNI（`AndroidJavaObject`/`AndroidJavaClass`）调用。
- **iOS**：编成 `.framework`/`.a`，C# 经 `[DllImport]`（P/Invoke）调用。

结论：经 UPM git URL 安装核心包**只会拉到 C# 框架**，无原生源码。包*可以*在 `Runtime/Plugins/<平台>/` 下
携带原生二进制（那样 git 安装会拉下来）；NativeRelay 核心刻意保持纯 C#，体积小、易跨引擎移植。具体的
原生通道因此最好作为独立的集成示例（各自的 Android Studio / Xcode 工程产出二进制），而非塞进纯 C# 核心。
