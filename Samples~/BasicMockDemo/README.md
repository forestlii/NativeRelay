# Basic Mock Demo

The smallest possible NativeRelay sample — no device, no key, no network.

## What it shows
Clicking **Send** fires a batch of requests through a `Bridge` backed by a pure-C#
`MockChannel`. Each result comes back on a **background thread** after a random delay,
and NativeRelay relays it safely to the **main thread**, dispatched per `seed`
(one request ↔ one result). The on-screen log shows, for each request, the frame it was
sent and the frame it returned on the main thread — so you can see the cross-thread hop
and the out-of-order ("first-back, first-dispatched") arrival.

## How to run
1. Create an empty scene.
2. Add an empty GameObject and attach the **`BasicMockDemo`** component
   (`NativeRelay.Samples.BasicMockDemo`).
3. Press **Play** and click **Send**.

The UI is drawn with IMGUI (`OnGUI`) on purpose, so the sample has **zero extra
dependencies** (it does not require `com.unity.ugui`) and the focus stays on the
NativeRelay usage in `Start()` / `SendBatch()`.

## The three lines that matter
```csharp
var channel = new MockChannel(minDelayMs: 100, maxDelayMs: 800);
_bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
_bridge.Request((int)DemoCommand.Ping, payload: null, onResult: r => { /* main thread */ });
```
Swap `MockChannel` for your own `INativeChannel` (Android JNI / iOS / any SDK) and the
rest stays the same.
