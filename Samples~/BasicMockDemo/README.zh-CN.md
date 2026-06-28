# Basic Mock Demo（基础示例）

[English](README.md) · **简体中文**

最小的 NativeRelay 示例 —— 无需真机、无需 key、无需联网。

## 演示了什么

点 **Send** 会通过一个由纯 C# `MockChannel` 支撑的 `Bridge` 发起一批请求。每个结果在**子线程**
随机延迟后回来，NativeRelay 把它安全地中继回**主线程**，按 `seed` 一一对应派发（一个请求 ↔ 一个结果）。
屏幕日志会显示每个请求"第几帧发出、第几帧在主线程返回"——于是你能直观看到跨线程的那一跳，以及
"先回来的先派发"的乱序到达。

## 如何运行

1. 新建一个空场景。
2. 建一个空 GameObject，挂上 **`BasicMockDemo`** 组件（`Likeon.NativeRelay.Samples.BasicMockDemo`）。
3. 按 **Play**，点 **Send**。

界面刻意用 IMGUI（`OnGUI`）绘制，因此示例**零额外依赖**（不需要 `com.unity.ugui`），
注意力集中在 `Start()` / `SendBatch()` 里对 NativeRelay 的用法上。

## 关键就这三行

```csharp
var channel = NativeChannelFactory.CreateForCurrentPlatform();      // 编辑器里是 MockChannel
_bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
_bridge.Request((int)DemoCommand.Ping, payload: null, onResult: (code, data) => { /* 主线程 */ });
```

把通道换成你自己的 `INativeChannel`（Android JNI / iOS / 任意 SDK），其余代码不变。
