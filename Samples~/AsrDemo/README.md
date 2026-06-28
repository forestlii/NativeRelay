# ASR Demo (simulated speech-to-text) · 语音识别示例（模拟语音→文本）

An *application* sample: NativeRelay relaying a **simulated ASR (speech recognition)**
result from a background thread back to the main thread to drive a tiny fake dialogue —
**no real ASR engine, no microphone, no key, no network.**

一个*应用*示例：NativeRelay 把一段**模拟的语音识别（ASR）**结果从子线程中继回主线程，
驱动一小段假对话 —— **不接真实 ASR 引擎、无麦克风、无 key、无联网。**

## What it shows · 演示了什么

Type what you want to "say" (or click a quick phrase), then click **Speak**. The business
code fires a `Recognize` request carrying that text as the payload; a `MockChannel` returns
it as the "recognized text" on a **background thread** after a random delay; NativeRelay
relays it safely to the **main thread**, where the business turns the text into an NPC
reply. This is the same relay mechanism as the basic sample — only here the payload is
*text*, showing a realistic "native async result → business logic" flow. (There is no real
microphone, so *you* provide the spoken text and the mock echoes it back as the result.)

在输入框打你想"说"的话（或点一个快捷短语），再点 **Speak**。业务代码会发一个 `Recognize` 请求，
把这句话作为 payload 带下去；`MockChannel` 在**子线程**随机延迟后把它当"识别出的文本"返回；
NativeRelay 安全中继回**主线程**，业务在那里把文本变成 NPC 回应。这和基础示例是同一套中继机制——
只是载荷是*文本*，展示更真实的"原生异步结果 → 业务逻辑"流程。（没有真实麦克风，所以由*你*提供
"说的文本"，Mock 把它原样当识别结果回传。）

## How to run · 如何运行

1. Create an empty scene. / 新建一个空场景。
2. Add an empty GameObject and attach the **`AsrDemo`** component
   (`Likeon.NativeRelay.Samples.AsrDemo`). / 建一个空 GameObject，挂上 **`AsrDemo`** 组件。
3. Press **Play**, type a sentence (or click a quick phrase to fill the box), then click
   **Speak**. / 按 **Play**，在输入框打一句话（或点快捷短语填入），再点 **Speak**。

## Swapping in a real ASR channel · 换成真实 ASR 通道

Replace `MockChannel` with your own `INativeChannel` implementation (Android recording +
recognition, iOS, any SDK). In its `OnResult`, hand back the real recognized text bytes
with the same `seed`. **The business code does not change** — that decoupling is the whole
point of NativeRelay.

把 `MockChannel` 换成你自己的 `INativeChannel` 实现（Android 录音+识别、iOS、任意 SDK）。
在它的 `OnResult` 里，用**同一个 seed** 回传真实识别文本的字节即可。**业务代码无需改动**——
这种解耦正是 NativeRelay 的意义所在。

The UI uses IMGUI (`OnGUI`) to keep the sample dependency-free.

界面用 IMGUI（`OnGUI`）以保持示例零依赖。
