# Android native channel · Android 原生通道

This describes the **Java contract** that `AndroidChannel` (the C# side, shipped in the
package) talks to, and how to build it into an `.aar`. It is a **generic JNI relay
template**: it handles all the seed/payload/thread/callback plumbing, and you fill in the
actual work per `command`.

本文档描述 `AndroidChannel`（已随包发布的 C# 侧）所对接的 **Java 契约**，以及如何把它打成 `.aar`。
它是一个**通用 JNI 中继模板**：把 seed/payload/线程/回调这套管线都做好，你只需在每个 `command`
下填入真正要做的事。

> ⚠️ **Status / 状态**：The C# side (`AndroidChannel.cs`) ships and is **compile-verified**.
> The Java side below is a **reference contract**; it has **not yet been built into an `.aar`
> or device-tested** here (the dev machine has no Android SDK / device). Build it in Android
> Studio and verify on a device. In particular, confirm the `byte[]` marshaling across the
> callback proxy on a real device.
>
> C# 侧（`AndroidChannel.cs`）已发布且**通过编译验证**；下面的 Java 侧是**参考契约**，**尚未在此
> 构建成 `.aar`、也未真机验证**（开发机无 Android SDK/设备）。请在 Android Studio 构建并真机验证，
> 尤其确认 `byte[]` 跨回调代理的编组。

## The Java contract · Java 契约

`AndroidChannel` (C#) expects a Java class `com.likeon.nativerelay.NativeRelayChannel` with:

`AndroidChannel`（C#）期望一个 Java 类 `com.likeon.nativerelay.NativeRelayChannel`，包含：

- a constructor taking a `ResultCallback` / 一个接收 `ResultCallback` 的构造函数；
- `void send(long seed, int command, byte[] payload)` / 发起请求；
- `void dispose()` / 释放；
- an inner interface `ResultCallback { void onResult(long seed, byte[] result); }` / 回调接口。

```java
package com.likeon.nativerelay;

/** Generic relay channel: receives a request, does work on a background thread,
 *  then calls back with the SAME seed. Fill in handle() per command. */
public class NativeRelayChannel {

    /** C# implements this via an AndroidJavaProxy. */
    public interface ResultCallback {
        void onResult(long seed, byte[] result);
    }

    private final ResultCallback callback;
    private volatile boolean disposed = false;

    public NativeRelayChannel(ResultCallback callback) {
        this.callback = callback;
    }

    /** Called from C#. Do the work off the main/UI thread, then call back with the same seed. */
    public void send(final long seed, final int command, final byte[] payload) {
        if (disposed) return;
        new Thread(new Runnable() {
            @Override public void run() {
                if (disposed) return;
                byte[] result = handle(command, payload);   // <-- your actual work
                ResultCallback cb = callback;
                if (cb != null && !disposed) {
                    cb.onResult(seed, result);               // back to C# (worker thread is fine)
                }
            }
        }).start();
    }

    /** Plug your real native work here, dispatched by command. */
    private byte[] handle(int command, byte[] payload) {
        switch (command) {
            // case 1: return doSomething(payload);
            // case 2: return doAnother(payload);
            default:
                return payload != null ? payload : new byte[0]; // echo by default
        }
    }

    public void dispose() { disposed = true; }
}
```

## Build the .aar · 构建 .aar

1. In **Android Studio**, create an **Android Library** module (package
   `com.likeon.nativerelay`), add the class above. / 在 Android Studio 建一个 **Android Library**
   模块（包名 `com.likeon.nativerelay`），加入上面的类。
2. Build the release `.aar` (Gradle task `:module:assembleRelease`); the output is under
   `module/build/outputs/aar/`. / 构建 release `.aar`（Gradle `assembleRelease`），产物在
   `module/build/outputs/aar/`。
3. Drop the `.aar` into your Unity project at **`Assets/Plugins/Android/`** (Unity/Gradle
   bundles it into the APK/AAB). / 把 `.aar` 放进 Unity 工程的 **`Assets/Plugins/Android/`**
   （Unity/Gradle 打包时并入）。

## Use it · 使用

```csharp
using Likeon.NativeRelay;

// Same as MockChannel — only the channel changes; your business code is identical.
// 和 MockChannel 一样 —— 只换通道，业务代码不变。
var channel = new AndroidChannel();                 // C# side talks to your .aar over JNI
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload, onResult: bytes => { /* main thread */ });
```

iOS follows the same shape: a `.framework`/`.a` called from a C# channel via `[DllImport]`
(P/Invoke), raising results back on the bridge.

iOS 同理：`.framework`/`.a` 由一个 C# 通道经 `[DllImport]`（P/Invoke）调用，把结果抛回桥。
