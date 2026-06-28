# ASR Demo (simulated speech-to-text)

An *application* sample: NativeRelay relaying a **simulated ASR (speech recognition)**
result from a background thread back to the main thread to drive a tiny fake dialogue —
**no real ASR engine, no microphone, no key, no network.**

## What it shows
Click **Speak** to pretend you said something. The business code fires a `Recognize`
request; a `MockChannel` returns a "recognized phrase" on a **background thread** after a
random delay; NativeRelay relays it safely to the **main thread**, where the business
turns the text into an NPC reply. This is exactly the same relay mechanism as the basic
sample — only here the payload is *text* instead of an echoed seed, showing a realistic
"native async result → business logic" flow.

## How to run
1. Create an empty scene.
2. Add an empty GameObject and attach the **`AsrDemo`** component
   (`Likeon.NativeRelay.Samples.AsrDemo`).
3. Press **Play** and click **Speak**.

## Swapping in a real ASR channel
Replace `MockChannel` with your own `INativeChannel` implementation (Android recording +
recognition, iOS, any SDK). In its `OnResult`, hand back the real recognized text bytes
with the same `seed`. **The business code does not change** — that decoupling is the whole
point of NativeRelay.

The UI uses IMGUI (`OnGUI`) to keep the sample dependency-free.
