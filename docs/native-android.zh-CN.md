# Android 原生通道

[English](native-android.md) · **简体中文**

本文档描述 `AndroidChannel`（已随包发布的 C# 侧）所对接的 **Java 契约**，以及如何打成 `.aar`。
它是一个**通用 JNI 中继模板**：把 seed/线程/回调这套管线都做好，你只需在每个 `command` 下填入真正
要做的事，返回 `(int code, String data)`。

> **状态**：C# 侧（`AndroidChannel.cs`）已发布且**通过编译验证**；实现下面 Java 侧的**现成可构建
> Android Studio 工程**在配套仓库 **[NativeRelay-Native](https://github.com/forestlii/NativeRelay-Native)**
> （`android/`）。其 Java 契约已编译通过；打成 `.aar` 与真机验证仍待做。本页讲**契约**——构建请克隆那个仓库。

## Java 契约

`AndroidChannel`（C#）期望一个 Java 类 `com.likeon.nativerelay.NativeRelayChannel`：构造函数接收
`ResultCallback`；`send(long, int, String)`；`dispose()`；内部接口 `ResultCallback`。

```java
package com.likeon.nativerelay;

/** 通用中继通道：收到请求，在 UI 线程外干活，再带着同一个 seed 和 (code, data) 回调。
 *  每个 command 下填你的实际逻辑到 handle()。 */
public class NativeRelayChannel {

    /** C# 通过 AndroidJavaProxy 实现它。 */
    public interface ResultCallback {
        void onResult(long seed, int code, String data);   // code: 1=成功, 0=失败, 10086=…(你定)
    }

    private final ResultCallback callback;
    private volatile boolean disposed = false;

    public NativeRelayChannel(ResultCallback callback) {
        this.callback = callback;
    }

    /** 由 C# 调用。在主/UI 线程外干活，再带着同一个 seed 回调。 */
    public void send(final long seed, final int command, final String payload) {
        if (disposed) return;
        new Thread(new Runnable() {
            @Override public void run() {
                if (disposed) return;
                try {
                    String data = handle(command, payload);     // <-- 你的实际逻辑
                    fire(seed, 1, data);                        // 成功（用你自己的成功码）
                } catch (Exception e) {
                    fire(seed, /*你的错误码*/ 0, e.getMessage());
                }
            }
        }).start();
    }

    /** 按 command 分发你的原生逻辑，返回结果文本/路径。 */
    private String handle(int command, String payload) {
        switch (command) {
            // case 1: return doSomething(payload);     // 如返回文件路径 / 识别文本
            default:
                return payload != null ? payload : "";   // 默认回显
        }
    }

    private void fire(long seed, int code, String data) {
        ResultCallback cb = callback;
        if (cb != null && !disposed) cb.onResult(seed, code, data);
    }

    public void dispose() { disposed = true; }
}
```

> 大块二进制（音/图）：别把字节塞进 `data`。原生写文件、把**路径**作为 `data` 返回，Unity 侧再加载。

## 构建 .aar

1. 克隆 **[NativeRelay-Native](https://github.com/forestlii/NativeRelay-Native)**，用 **Android Studio**
   打开它的 `android/` 目录——里面已含本类（Android Library 模块，包名 `com.likeon.nativerelay`）。
   （或按上面的类自己重建模块。）
2. 构建 release `.aar`：`./gradlew :nativerelay:assembleRelease`，产物在 `nativerelay/build/outputs/aar/`。
3. 把 `.aar` 放进 Unity 工程的 **`Assets/Plugins/Android/`**。

## 使用

```csharp
using Likeon.NativeRelay;

// 工厂在 Android 上自动返回 AndroidChannel。
var channel = NativeChannelFactory.CreateForCurrentPlatform();
var bridge  = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
bridge.Request((int)MyCommand.DoSomething, payload: null,
    onResult: (code, data) => { /* 主线程：code + data(文本/路径) */ });
```

iOS 经 P/Invoke 同理 —— 见 [native-ios.md](native-ios.md)。
