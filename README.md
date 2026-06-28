# NativeRelay

> *Thread-safe relay for native async callbacks in Unity — dispatched on the main thread, one request ↔ one result.*

任何 Unity 项目只要调用了原生异步能力（录音、定位、蓝牙、相机、推送、第三方 SDK 回调）都会撞到同一个坑：**原生回调发生在子线程，而 Unity API 只能在主线程调**。NativeRelay 把「子线程回调 → 安全切回主线程 → 按请求一一对应派发给业务」这条链路标准化，零 GC、无第三方依赖、clone 即跑（纯 C# Mock 通道，无需真机/key/联网）。

## 核心机制
`seed 指令序号（Interlocked 自增）` + `pending 表（Dictionary<seed, ctx>）` + `双缓冲队列（零 GC、锁内 O(1) 交换）` + `MainThreadDispatcher（每帧主线程统一派发）` + `超时清理`。

## 状态
🚧 开发中（0.1.0-dev）。当前完成 M0 脚手架 + M1 核心通信链路，文档与示例（README 30 秒上手、API、接真实 Android 通道指引、demo 场景）将在 M3 补全。

## 环境
- **最低 Unity 6（6000.4）**。本地验证于 Unity `6000.4.10f1`。
- 纯 C#，无第三方 DLL 依赖。MIT License。

## 安装（开发中，接口未冻结）
UPM `git url` 安装与 OpenUPM 将在发布（M4）时提供。

---
作者：Likeon · GitHub [forestlii](https://github.com/forestlii) · License: MIT · © 2026 Likeon
