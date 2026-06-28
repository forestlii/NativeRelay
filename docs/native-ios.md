# iOS native channel

**English** · [简体中文](native-ios.zh-CN.md)

This describes the **C ABI contract** that `IosChannel` (the C# side, shipped in the package)
talks to over P/Invoke (`[DllImport("__Internal")]`). The C# side does the tricky part —
a `GCHandle` "claim ticket" identifies the channel and a `[MonoPInvokeCallback]` static
trampoline receives the result. You implement three C functions (from Objective-C/Swift).

> **Status**: `IosChannel.cs` is guarded by `#if UNITY_IOS` (compiles only in iOS builds) —
> **not compiled/device-verified here** (no iOS SDK/device). It is a **reference
> implementation**; build + verify in your iOS environment.

## The C ABI contract

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

## Use it

```csharp
using Likeon.NativeRelay;

// The factory returns IosChannel on iOS automatically.
var channel = NativeChannelFactory.CreateForCurrentPlatform();
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload: null,
    onResult: (code, data) => { /* main thread: code + data(text/path) */ });
```
