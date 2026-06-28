# Android native channel · Android 原生通道

This describes the **Java contract** that `AndroidChannel` (the C# side, shipped in the
package) talks to, and how to build it into an `.aar`. It is a **generic JNI relay
template**: it handles all the seed/thread/callback plumbing, and you fill in the actual
work per `command`, returning `(int code, String data)`.

本文档描述 `AndroidChannel`（已随包发布的 C# 侧）所对接的 **Java 契约**，以及如何打成 `.aar`。
它是一个**通用 JNI 中继模板**：把 seed/线程/回调这套管线都做好，你只需在每个 `command` 下填入真正
要做的事，返回 `(int code, String data)`。

> ⚠️ **Status / 状态**：The C# side (`AndroidChannel.cs`) ships and is **compile-verified**.
> The Java side below is a **reference contract**, **not yet built into an `.aar` or
> device-tested** here (no Android SDK/device). Build it in Android Studio and verify on a device.
>
> C# 侧（`AndroidChannel.cs`）已发布且**通过编译验证**；下面的 Java 侧是**参考契约**，**尚未在此构建成
> `.aar` 或真机验证**。请在 Android Studio 构建并真机验证。

## The Java contract · Java 契约

`AndroidChannel` (C#) expects a Java class `com.likeon.nativerelay.NativeRelayChannel` with a
constructor taking a `ResultCallback`, `void send(long seed, int command, String payload)`,
`void dispose()`, and an inner interface `ResultCallback { void onResult(long seed, int code, String data); }`.

`AndroidChannel`（C#）期望一个 Java 类 `com.likeon.nativerelay.NativeRelayChannel`：构造函数接收
`ResultCallback`；`send(long, int, String)`；`dispose()`；内部接口 `ResultCallback`。

```java
package com.likeon.nativerelay;

/** Generic relay channel: receive a request, do work off the UI thread, then call back
 *  with the SAME seed and (code, data). Fill in handle() per command. */
public class NativeRelayChannel {

    /** C# implements this via an AndroidJavaProxy. */
    public interface ResultCallback {
        void onResult(long seed, int code, String data);   // code: 1=ok, 0=fail, 10086=…(yours)
    }

    private final ResultCallback callback;
    private volatile boolean disposed = false;

    public NativeRelayChannel(ResultCallback callback) {
        this.callback = callback;
    }

    /** Called from C#. Do the work off the main/UI thread, then call back with the same seed. */
    public void send(final long seed, final int command, final String payload) {
        if (disposed) return;
        new Thread(new Runnable() {
            @Override public void run() {
                if (disposed) return;
                try {
                    String data = handle(command, payload);     // <-- your actual work
                    fire(seed, 1, data);                        // success (your own success code)
                } catch (Exception e) {
                    fire(seed, /*your error code*/ 0, e.getMessage());
                }
            }
        }).start();
    }

    /** Plug your real native work here, dispatched by command. Return the result text/path. */
    private String handle(int command, String payload) {
        switch (command) {
            // case 1: return doSomething(payload);     // e.g. return a file path / recognized text
            default:
                return payload != null ? payload : "";   // echo by default
        }
    }

    private void fire(long seed, int code, String data) {
        ResultCallback cb = callback;
        if (cb != null && !disposed) cb.onResult(seed, code, data);
    }

    public void dispose() { disposed = true; }
}
```

> Big binary (audio/image): don't push raw bytes through `data`. Save to a file natively and
> return the **path** as `data`; load it Unity-side.
> 大块二进制（音/图）：别把字节塞进 `data`。原生写文件、把**路径**作为 `data` 返回，Unity 侧再加载。

## Build the .aar · 构建 .aar

1. In **Android Studio**, create an **Android Library** module (package
   `com.likeon.nativerelay`), add the class above. / 建一个 **Android Library** 模块（包名
   `com.likeon.nativerelay`），加入上面的类。
2. Build the release `.aar` (Gradle `assembleRelease`); output under
   `module/build/outputs/aar/`. / 构建 release `.aar`。
3. Drop the `.aar` into your Unity project at **`Assets/Plugins/Android/`**. / 把 `.aar` 放进
   Unity 工程的 **`Assets/Plugins/Android/`**。

## Use it · 使用

```csharp
using Likeon.NativeRelay;

// The factory returns AndroidChannel on Android automatically.
// 工厂在 Android 上自动返回 AndroidChannel。
var channel = NativeChannelFactory.CreateForCurrentPlatform();
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload: null,
    onResult: (code, data) => { /* main thread: code + data(text/path) */ });
```

iOS follows the same shape via P/Invoke — see [native-ios.md](native-ios.md).
iOS 经 P/Invoke 同理 —— 见 [native-ios.md](native-ios.md)。
