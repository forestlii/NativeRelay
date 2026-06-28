# Changelog

All notable changes to NativeRelay are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
