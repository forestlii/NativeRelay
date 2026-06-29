# iOS 原生通道

[English](native-ios.md) · **简体中文**

本文档描述 `IosChannel`（已随包发布的 C# 侧）经 P/Invoke（`[DllImport("__Internal")]`）对接的
**C ABI 契约**。C# 侧把难活做了——用 `GCHandle`「号码牌」标识通道、用 `[MonoPInvokeCallback]` 静态
跳板收结果。你实现三个 C 函数（用 Objective-C/Swift 暴露 C ABI）。

> **状态**：`IosChannel.cs` 用 `#if UNITY_IOS` 守护（只在 iOS 构建编译）。下面 C ABI 的**现成
> Objective-C 实现**在配套仓库 **[NativeRelay-Native](https://github.com/forestlii/NativeRelay-Native)**
> （`ios/Source/`）。iOS 构建需 macOS + Xcode，故**未在此编译/真机验证**——属参考实现，请在你的
> iOS 环境构建并真机验证。本页讲**契约**。

## C ABI 契约

C# 这边导入（经 `__Internal`，静态链接进 iOS app）：

```c
// 由 native（任意线程）调用，把结果交回 C#。
// context = C# 在 Init 传入的不透明指针（一个 GCHandle）。data = UTF-8 C 字符串（或 NULL）。
typedef void (*NativeRelayResultCallback)(void* context, long long seed, int code, const char* data);

// 注册回调 + context（一次）。
void NativeRelayChannel_Init(void* context, NativeRelayResultCallback cb);

// 发起请求。在主线程外干活，再调 cb(context, seed, code, data)。
void NativeRelayChannel_Send(void* context, long long seed, int command, const char* payload);

// 释放。
void NativeRelayChannel_Dispose(void* context);
```

参考实现（Objective-C，放 `Assets/Plugins/iOS/` 下的 `.m`/`.mm`）：

```objc
#import <Foundation/Foundation.h>

typedef void (*NativeRelayResultCallback)(void* context, long long seed, int code, const char* data);

static NativeRelayResultCallback gCallback = NULL;

void NativeRelayChannel_Init(void* context, NativeRelayResultCallback cb) {
    gCallback = cb;   // context 是每通道的，每次回调都带回来
}

void NativeRelayChannel_Send(void* context, long long seed, int command, const char* payload) {
    NSString* input = payload ? [NSString stringWithUTF8String:payload] : @"";
    // 在主线程外干活，再带着同一个 seed 回调。
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        int code = 1;                       // 你的成功码（或错误码）
        NSString* data = input;             // 你的结果文本/路径（默认回显）
        if (gCallback) {
            // const char* 只在本次调用期间有效 —— C# 会当场拷走。
            gCallback(context, seed, code, [data UTF8String]);
        }
    });
}

void NativeRelayChannel_Dispose(void* context) {
    // 释放该 context 的每通道原生资源
}
```

> 大块二进制（音/图）：把**文件路径**作为 `data` 返回，别传裸字节。回调里的 `const char*` 只在调用期间
> 有效，C# 会当场 `Marshal.PtrToStringUTF8` 拷成托管 string。

## 使用

```csharp
using Likeon.NativeRelay;

// 工厂在 iOS 上自动返回 IosChannel。
var channel = NativeChannelFactory.CreateForCurrentPlatform();
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload: null,
    onResult: (code, data) => { /* 主线程：code + data(文本/路径) */ });
```
