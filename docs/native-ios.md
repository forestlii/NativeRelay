# iOS native channel · iOS 原生通道

This describes the **C ABI contract** that `IosChannel` (the C# side, shipped in the package)
talks to over P/Invoke (`[DllImport("__Internal")]`). The C# side does the tricky part —
a `GCHandle` "claim ticket" identifies the channel and a `[MonoPInvokeCallback]` static
trampoline receives the result. You implement three C functions (from Objective-C/Swift).

本文档描述 `IosChannel`（已随包发布的 C# 侧）经 P/Invoke 对接的 **C ABI 契约**。C# 侧把难活做了——
用 `GCHandle`「号码牌」标识通道、用 `[MonoPInvokeCallback]` 静态跳板收结果。你实现三个 C 函数
（用 Objective-C/Swift 暴露 C ABI）。

> ⚠️ **Status / 状态**：`IosChannel.cs` is guarded by `#if UNITY_IOS` (compiles only in iOS
> builds) — **not compiled/device-verified here** (no iOS SDK/device). It is a **reference
> implementation**; build + verify in your iOS environment.
>
> `IosChannel.cs` 用 `#if UNITY_IOS` 守护（只在 iOS 构建编译）——**未在此编译/真机验证**，属**参考实现**，
> 请在你的 iOS 环境构建并真机验证。

## The C ABI contract · C ABI 契约

C# imports (via `__Internal`, statically linked into the iOS app):

```c
// Called from native (on any thread) to hand a result back to C#.
// context = the opaque pointer C# passed in Init (a GCHandle). data = UTF-8 C string (or NULL).
typedef void (*NativeRelayResultCallback)(void* context, long long seed, int code, const char* data);

// Register the callback + context once.
void NativeRelayChannel_Init(void* context, NativeRelayResultCallback cb);

// Start a request. Do work off the main thread, then call cb(context, seed, code, data).
void NativeRelayChannel_Send(void* context, long long seed, int command, const char* payload);

// Release.
void NativeRelayChannel_Dispose(void* context);
```

Reference implementation (Objective-C, in a `.m`/`.mm` under `Assets/Plugins/iOS/`):

参考实现（Objective-C，放 `Assets/Plugins/iOS/` 下的 `.m`/`.mm`）：

```objc
#import <Foundation/Foundation.h>

typedef void (*NativeRelayResultCallback)(void* context, long long seed, int code, const char* data);

static NativeRelayResultCallback gCallback = NULL;

void NativeRelayChannel_Init(void* context, NativeRelayResultCallback cb) {
    gCallback = cb;   // context is per-channel; passed back on every callback
}

void NativeRelayChannel_Send(void* context, long long seed, int command, const char* payload) {
    NSString* input = payload ? [NSString stringWithUTF8String:payload] : @"";
    // Do the work OFF the main thread, then call back with the SAME seed.
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        int code = 1;                       // your success code (or an error code)
        NSString* data = input;             // your result text/path (echo by default)
        if (gCallback) {
            // const char* is valid only during this call — C# copies it immediately.
            gCallback(context, seed, code, [data UTF8String]);
        }
    });
}

void NativeRelayChannel_Dispose(void* context) {
    // release per-channel native resources for this context
}
```

> Big binary (audio/image): return a **file path** as `data`, not raw bytes; load it Unity-side.
> The callback's `const char*` is valid only during the call — C# copies it into a managed string
> right away (`Marshal.PtrToStringUTF8`).
>
> 大块二进制：把**文件路径**作为 `data` 返回，别传裸字节。回调里的 `const char*` 只在调用期间有效，
> C# 会当场 `Marshal.PtrToStringUTF8` 拷成托管 string。

## Use it · 使用

```csharp
using Likeon.NativeRelay;

// The factory returns IosChannel on iOS automatically.
// 工厂在 iOS 上自动返回 IosChannel。
var channel = NativeChannelFactory.CreateForCurrentPlatform();
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload: null,
    onResult: (code, data) => { /* main thread: code + data(text/path) */ });
```
