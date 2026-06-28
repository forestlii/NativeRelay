# ASR Demo (simulated speech-to-text)

**English** · [简体中文](README.zh-CN.md)

An *application* sample: NativeRelay relaying a **simulated ASR (speech recognition)**
result from a background thread back to the main thread to drive a tiny fake dialogue —
**no real ASR engine, no microphone, no key, no network.**

## What it shows

Type what you want to "say" (or click a quick phrase), then click **Speak**. The business
code fires a `Recognize` request carrying that text as the payload; a `MockChannel` returns
it as the "recognized text" on a **background thread** after a random delay; NativeRelay
relays it safely to the **main thread**, where the business turns the text into an NPC
reply. This is the same relay mechanism as the basic sample — only here the payload is
*text*, showing a realistic "native async result → business logic" flow. (There is no real
microphone, so *you* provide the spoken text and the mock echoes it back as the result.)

## How to run

1. Create an empty scene.
2. Add an empty GameObject and attach the **`AsrDemo`** component
   (`Likeon.NativeRelay.Samples.AsrDemo`).
3. Press **Play**, type a sentence (or click a quick phrase to fill the box), then click **Speak**.

## Swapping in a real ASR channel

Replace the channel with your own `INativeChannel` implementation (Android recording +
recognition, iOS, any SDK). In its `OnResult`, hand back the real recognized text with the
same `seed`. **The business code does not change** — that decoupling is the whole point of
NativeRelay.

The UI uses IMGUI (`OnGUI`) to keep the sample dependency-free.
