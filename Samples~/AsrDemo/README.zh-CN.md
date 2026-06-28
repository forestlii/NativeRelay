# ASR Demo（模拟语音→文本）

[English](README.md) · **简体中文**

一个*应用*示例：NativeRelay 把一段**模拟的语音识别（ASR）**结果从子线程中继回主线程，
驱动一小段假对话 —— **不接真实 ASR 引擎、无麦克风、无 key、无联网。**

## 演示了什么

在输入框打你想"说"的话（或点一个快捷短语），再点 **Speak**。业务代码会发一个 `Recognize` 请求，
把这句话作为 payload 带下去；`MockChannel` 在**子线程**随机延迟后把它当"识别出的文本"返回；
NativeRelay 安全中继回**主线程**，业务在那里把文本变成 NPC 回应。这和基础示例是同一套中继机制——
只是载荷是*文本*，展示更真实的"原生异步结果 → 业务逻辑"流程。（没有真实麦克风，所以由*你*提供
"说的文本"，Mock 把它原样当识别结果回传。）

## 如何运行

1. 新建一个空场景。
2. 建一个空 GameObject，挂上 **`AsrDemo`** 组件（`Likeon.NativeRelay.Samples.AsrDemo`）。
3. 按 **Play**，在输入框打一句话（或点快捷短语填入），再点 **Speak**。

## 换成真实 ASR 通道

把通道换成你自己的 `INativeChannel` 实现（Android 录音+识别、iOS、任意 SDK）。在它的 `OnResult` 里，
用**同一个 seed** 回传真实识别文本即可。**业务代码无需改动**——这种解耦正是 NativeRelay 的意义所在。

界面用 IMGUI（`OnGUI`）以保持示例零依赖。
