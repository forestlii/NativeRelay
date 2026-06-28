# Changelog

All notable changes to NativeRelay are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- Rebrand to the `Likeon` namespace: C# namespace `NativeRelay` → `Likeon.NativeRelay`, package id `com.like.nativerelay` → `com.likeon.nativerelay`, asmdefs renamed to `Likeon.NativeRelay.*`. Author/copyright is now `Likeon` (still MIT). The product display name stays "NativeRelay". (Breaking for the pre-release `0.1.0-dev`: re-install under the new package id.)

### Added
- UPM package skeleton (min Unity 6), asmdef layout (`NativeRelay.Runtime`, `NativeRelay.EditModeTests`, `NativeRelay.PlayModeTests`), MIT license.
- Core relay link: `SeedGenerator` (Interlocked), `DoubleBufferQueue<T>` (zero-GC double buffer), `PendingTable` (+ timeout scan), `BridgeError`, `INativeChannel` contract + `MockChannel`, `RelayPump` (pure-C# core, no UnityEngine dependency), `Bridge` (public entry), `MainThreadDispatcher` (MonoBehaviour shell).
- Robustness: end-to-end request timeout (`MockChannel.shouldDrop` simulates lost native replies → `BridgeError.Timeout`); `Bridge.Dispose` now drains every pending request with `BridgeError.Disposed` (no leak) and is idempotent.

### Verified
- Unity 6 Test Framework: EditMode 36/36, PlayMode 3/3 — concurrent out-of-order one-to-one dispatch on the main thread, end-to-end timeout (dropped → Timeout, others → result), Dispose cleanup, a 1000-request sub-thread-flood stress run, and steady-state zero-GC assertions for both the success and timeout paths (`Is.Not.AllocatingGCMemory`).

## [0.1.0-dev]
- Initial development version.
