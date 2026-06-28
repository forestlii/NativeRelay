# Changelog

All notable changes to NativeRelay are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] - 2026-06-29

### Changed (breaking)
- **Pure-code string contract.** `Bridge.Request(int command, string payload, Action<int code, string data> onResult)` — a single result callback delivering `(int code, string data)`. `INativeChannel` is now `Send(long seed, int command, string payload)` + `event Action<long, int, string> OnResult`. The framework never interprets `code` and never touches `data` — it just relays.
- **Removed `BridgeError` / `onError`.** Errors are codes: business defines `1`/`0`/`10086`/…; the framework only mints `RelayCode.Timeout` (`int.MinValue`) / `RelayCode.Disposed` (`int.MinValue+1`) when it can't get a native result. A failing `channel.Send` now rethrows to the caller (framework doesn't swallow errors).

### Added
- `NativeChannelFactory.CreateForCurrentPlatform()` — platform dispatch in one place via `#if` (Editor/Windows → `MockChannel`, Android → `AndroidChannel`/JNI, iOS → `IosChannel`/P-Invoke), behind the `INativeChannel` interface.
- `AndroidChannel` (JNI) and `IosChannel` (P/Invoke + `GCHandle` + `[MonoPInvokeCallback]`) — the C# side of generic native relay templates. Compile-verified; the matching `.aar`/native lib + on-device verification are pending (see `docs/native-android.md`, `docs/native-ios.md`).

### Verified
- dotnet fast loop: 38 tests green. Unity 6: compile clean, EditMode 35/35, PlayMode 3/3 (main-thread dispatch, end-to-end timeout, 1000-request stress, steady-state zero-GC for success + timeout paths).

## [0.1.0] - 2026-06-29

First public release.

### Added
- Core relay link: `SeedGenerator` (Interlocked), `DoubleBufferQueue<T>` (zero-GC double buffer), `PendingTable` (+ timeout scan), `BridgeError`, `INativeChannel` contract + `MockChannel`, `RelayPump` (pure-C# core, no UnityEngine dependency), `Bridge` (public entry), `MainThreadDispatcher` (MonoBehaviour shell).
- Robustness: end-to-end request timeout (`MockChannel.shouldDrop` simulates lost native replies → `BridgeError.Timeout`); `Bridge.Dispose` drains every pending request with `BridgeError.Disposed` (no leak) and is idempotent.
- Samples (IMGUI, zero extra dependencies): **Basic Mock Demo** (concurrent out-of-order dispatch on the main thread) and **ASR Demo** (simulated speech→text driving a fake dialogue, with a text input + quick phrases).
- UPM package: min Unity 6, asmdef layout (`Likeon.NativeRelay.Runtime` / `.EditModeTests` / `.PlayModeTests`), MIT license, `documentationUrl` / `changelogUrl` / `licensesUrl` pointing at GitHub.
- Docs: `docs/architecture.md` (layered diagram + one-request sequence diagram); a complete bilingual (EN/中文) `README.md` (install, 30-second quick start, API, how to plug in a real native channel).

### Verified
- Unity 6 Test Framework: EditMode 36/36, PlayMode 3/3 — concurrent out-of-order one-to-one dispatch on the main thread, end-to-end timeout (dropped → Timeout, others → result), Dispose cleanup, a 1000-request sub-thread-flood stress run, and steady-state zero-GC assertions for both the success and timeout paths (`Is.Not.AllocatingGCMemory`).
