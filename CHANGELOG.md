# Changelog

All notable changes to NativeRelay are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- UPM package skeleton (min Unity 6), asmdef layout (`NativeRelay.Runtime`, `NativeRelay.EditModeTests`, `NativeRelay.PlayModeTests`), MIT license.
- Core relay link: `SeedGenerator` (Interlocked), `DoubleBufferQueue<T>` (zero-GC double buffer), `PendingTable` (+ timeout scan), `BridgeError`, `INativeChannel` contract + `MockChannel`, `RelayPump` (pure-C# core, no UnityEngine dependency), `Bridge` (public entry), `MainThreadDispatcher` (MonoBehaviour shell).

### Verified
- Unity 6 Test Framework: EditMode 30/30, PlayMode 2/2 — covering concurrent out-of-order one-to-one dispatch on the main thread and a steady-state zero-GC assertion (`Is.Not.AllocatingGCMemory`).

## [0.1.0-dev]
- Initial development version.
