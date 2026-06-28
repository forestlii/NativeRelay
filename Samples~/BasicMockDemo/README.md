# Basic Mock Demo · 基础 Mock 示例

The smallest possible NativeRelay sample — no device, no key, no network.

最小的 NativeRelay 示例 —— 无需真机、无需 key、无需联网。

## What it shows · 演示了什么

Clicking **Send** fires a batch of requests through a `Bridge` backed by a pure-C#
`MockChannel`. Each result comes back on a **background thread** after a random delay,
and NativeRelay relays it safely to the **main thread**, dispatched per `seed`
(one request ↔ one result). The on-screen log shows, for each request, the frame it was
sent and the frame it returned on the main thread — so you can see the cross-thread hop
and the out-of-order ("first-back, first-dispatched") arrival.

点 **Send** 会通过一个由纯 C# `MockChannel` 支撑的 `Bridge` 发起一批请求。每个结果在**子线程**
随机延迟后回来，NativeRelay 把它安全地中继回**主线程**，按 `seed` 一一对应派发（一个请求 ↔ 一个结果）。
屏幕日志会显示每个请求"第几帧发出、第几帧在主线程返回"——于是你能直观看到跨线程的那一跳，以及
"先回来的先派发"的乱序到达。

## How to run · 如何运行

1. Create an empty scene. / 新建一个空场景。
2. Add an empty GameObject and attach the **`BasicMockDemo`** component
   (`Likeon.NativeRelay.Samples.BasicMockDemo`). / 建一个空 GameObject，挂上 **`BasicMockDemo`** 组件。
3. Press **Play** and click **Send**. / 按 **Play**，点 **Send**。

The UI is drawn with IMGUI (`OnGUI`) on purpose, so the sample has **zero extra
dependencies** (it does not require `com.unity.ugui`) and the focus stays on the
NativeRelay usage in `Start()` / `SendBatch()`.

界面刻意用 IMGUI（`OnGUI`）绘制，因此示例**零额外依赖**（不需要 `com.unity.ugui`），
注意力集中在 `Start()` / `SendBatch()` 里对 NativeRelay 的用法上。

## The three lines that matter · 关键就这三行

```csharp
var channel = new MockChannel(minDelayMs: 100, maxDelayMs: 800);
_bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
_bridge.Request((int)DemoCommand.Ping, payload: null, onResult: r => { /* main thread */ });
```

Swap `MockChannel` for your own `INativeChannel` (Android JNI / iOS / any SDK) and the
rest stays the same.

把 `MockChannel` 换成你自己的 `INativeChannel`（Android JNI / iOS / 任意 SDK），其余代码不变。
