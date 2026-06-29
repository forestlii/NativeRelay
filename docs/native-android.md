# Android native channel

**English** · [简体中文](native-android.zh-CN.md)

This describes the **Java contract** that `AndroidChannel` (the C# side, shipped in the
package) talks to, and how to build it into an `.aar`. It is a **generic JNI relay
template**: it handles all the seed/thread/callback plumbing, and you fill in the actual
work per `command`, returning `(int code, String data)`.

> **Status**: The C# side (`AndroidChannel.cs`) ships and is **compile-verified**. A
> ready-to-build Android Studio project implementing the Java side below lives in the companion
> repo **[NativeRelay-Native](https://github.com/forestlii/NativeRelay-Native)** (`android/`).
> Its Java contract compiles; building the `.aar` and on-device verification are still pending.
> This page documents the **contract** — clone that repo to build.

## The Java contract

`AndroidChannel` (C#) expects a Java class `com.likeon.nativerelay.NativeRelayChannel` with a
constructor taking a `ResultCallback`, `void send(long seed, int command, String payload)`,
`void dispose()`, and an inner interface `ResultCallback { void onResult(long seed, int code, String data); }`.

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

## Build the .aar

1. Clone **[NativeRelay-Native](https://github.com/forestlii/NativeRelay-Native)** and open its
   `android/` folder in **Android Studio** — it already ships this class as an Android Library
   module (package `com.likeon.nativerelay`). (Or recreate the module yourself from the class above.)
2. Build the release `.aar`: `./gradlew :nativerelay:assembleRelease`; output under
   `nativerelay/build/outputs/aar/`.
3. Drop the `.aar` into your Unity project at **`Assets/Plugins/Android/`**.

## Use it

```csharp
using Likeon.NativeRelay;

// The factory returns AndroidChannel on Android automatically.
var channel = NativeChannelFactory.CreateForCurrentPlatform();
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload: null,
    onResult: (code, data) => { /* main thread: code + data(text/path) */ });
```

iOS follows the same shape via P/Invoke — see [native-ios.md](native-ios.md).
