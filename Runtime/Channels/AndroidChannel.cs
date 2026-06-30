using System;
using UnityEngine;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// <b>通用 Android（JNI）中继通道 —— 一个原生通道的「C# 侧」模板</b>。
    /// 它把最容易写错的跨 JNI 管线替你做好：把 <c>(seed, command, payload)</c> 传进 Java，
    /// 并通过一个回调代理从 Java 的<b>子线程</b>收回结果，转交给桥（桥再切回主线程派发）。
    /// 你只需提供配套的 <c>.aar</c>（实现下面文档化的 Java 契约），业务侧代码不变。
    /// </summary>
    /// <remarks>
    /// 🔧 <b>使用前提</b>：本类只是「C# 半边」。要真正工作，需在 Android 端实现并打成 <c>.aar</c> 放进
    /// <c>Assets/Plugins/Android/</c>。Java 契约与构建步骤见 <c>docs/native-android.md</c>。
    /// <para>
    /// ⚠️ <b>验证状态（诚实标注）</b>：本 C# 侧已随包发布并通过<b>编译</b>验证；但其<b>运行时行为依赖真机 + 配套 .aar</b>，
    /// <b>尚未在真机上验证过</b>（开发机无 Android SDK/设备）。尤其 <c>string</c> 跨 <see cref="AndroidJavaProxy"/> 的
    /// 编组细节，请在真机上确认。等具备真机条件后再补 Java/.aar 与端到端验证。
    /// </para>
    /// 桥侧契约不变：<see cref="INativeChannel"/>。
    /// </remarks>
    public sealed class AndroidChannel : INativeChannel
    {
        /// <summary>默认对接的 Java 类全名（见 docs/native-android.md 的契约）。</summary>
        public const string DefaultJavaClass = "com.likeon.nativerelay.NativeRelayChannel";

        /// <summary>结果 (seed, code, data) 回来（可能在 Java 子线程触发）。</summary>
        public event Action<long, int, string> OnResult;

        private AndroidJavaObject _java;
        private ResultProxy _proxy;
        private bool _disposed;

        /// <param name="javaClassName">Java 实现类全名；默认 <see cref="DefaultJavaClass"/>。</param>
        public AndroidChannel(string javaClassName = DefaultJavaClass)
        {
            // 回调代理：Java 侧通过它把结果回传到 C#。
            _proxy = new ResultProxy(this);
            // 构造 Java 通道，把回调代理传进去（对应 Java: new NativeRelayChannel(callback)）。
            _java = new AndroidJavaObject(javaClassName, _proxy);
        }

        /// <inheritdoc />
        public void Send(long seed, int command, string payload)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AndroidChannel));
            // 对应 Java: void send(long seed, int command, String payload)
            _java.Call("send", seed, command, payload);
        }

        // 由回调代理在 Java 子线程上调用；这里只把事件抛出去，桥负责切回主线程。
        private void RaiseResult(long seed, int code, string data)
        {
            OnResult?.Invoke(seed, code, data);
        }

        /// <summary>关闭通道：通知 Java 释放并销毁 Java 对象。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnResult = null;
            if (_java != null)
            {
                try { _java.Call("dispose"); }
                catch { /* 忽略关闭期异常 */ }
                _java.Dispose();
                _java = null;
            }
        }

        /// <summary>
        /// 实现 Java 回调接口 <c>com.likeon.nativerelay.NativeRelayChannel$ResultCallback</c>
        /// （<c>void onResult(long seed, int code, String data)</c>）的代理。用 <see cref="Invoke(string, AndroidJavaObject[])"/>
        /// 重载手动取参。
        /// </summary>
        private sealed class ResultProxy : AndroidJavaProxy
        {
            private readonly AndroidChannel _owner;

            public ResultProxy(AndroidChannel owner)
                : base("com.likeon.nativerelay.NativeRelayChannel$ResultCallback")
            {
                _owner = owner;
            }

            public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
            {
                if (methodName == "onResult" && javaArgs != null && javaArgs.Length == 3)
                {
                    long seed = javaArgs[0].Call<long>("longValue");   // 装箱的 java.lang.Long
                    int code = javaArgs[1].Call<int>("intValue");     // 装箱的 java.lang.Integer
                    string data = javaArgs[2] != null ? javaArgs[2].Call<string>("toString") : null; // java.lang.String
                    _owner.RaiseResult(seed, code, data);
                    return null;
                }
                return base.Invoke(methodName, javaArgs);
            }
        }
    }
}
