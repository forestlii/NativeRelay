#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// iOS（P/Invoke）原生中继通道的<b>C# 侧参考实现</b>。把跨边界最易错的「<see cref="GCHandle"/> 号码牌 +
    /// <c>[MonoPInvokeCallback]</c> 静态跳板」管线替你做好：native 不能直接调 C# 实例方法，所以注册一个<b>静态</b>
    /// 回调函数指针，并用 <see cref="GCHandle"/> 把本对象「钉住 + 给个稳定 IntPtr 身份(context)」交给 native，
    /// native 回调时凭 context 取回本对象。
    /// </summary>
    /// <remarks>
    /// 🔧 需配套的原生库（Objective-C/Swift 经 C ABI 暴露）实现 <c>NativeRelayChannel_Init/Send/Dispose</c>，
    /// 见 <c>docs/native-ios.md</c>。⚠️ <b>验证状态</b>：开发机无 iOS SDK/设备，本文件整体 <c>#if UNITY_IOS</c>
    /// 守护、<b>只在 iOS 构建里编译</b>，未经此处编译/真机验证，属<b>参考实现</b>，请在你的 iOS 环境构建 + 真机验证。
    /// </remarks>
    public sealed class IosChannel : INativeChannel
    {
        // native → C# 的回调签名：(context=GCHandle, seed, code, utf8 data 指针)。
        private delegate void ResultCallback(IntPtr context, long seed, int code, IntPtr utf8Data);

        // 静态保活，避免委托被 GC 回收（传给 native 的函数指针必须一直有效）。
        private static readonly ResultCallback s_callback = OnNativeResult;

        [DllImport("__Internal")]
        private static extern void NativeRelayChannel_Init(IntPtr context, ResultCallback cb);
        [DllImport("__Internal")]
        private static extern void NativeRelayChannel_Send(IntPtr context, long seed, int command, string payload);
        [DllImport("__Internal")]
        private static extern void NativeRelayChannel_Dispose(IntPtr context);

        public event Action<long, int, string> OnResult;

        private GCHandle _self;     // 号码牌：钉住本对象 + 稳定身份
        private IntPtr _context;
        private bool _disposed;

        public IosChannel()
        {
            _self = GCHandle.Alloc(this);          // 拿牌（不拿东西本身，资源移动牌还有效）
            _context = GCHandle.ToIntPtr(_self);
            NativeRelayChannel_Init(_context, s_callback);
        }

        /// <inheritdoc />
        public void Send(long seed, int command, string payload)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IosChannel));
            NativeRelayChannel_Send(_context, seed, command, payload);
        }

        // 由 native 子线程调用（IL2CPP/AOT 下必须是静态 + [MonoPInvokeCallback]）。
        [AOT.MonoPInvokeCallback(typeof(ResultCallback))]
        private static void OnNativeResult(IntPtr context, long seed, int code, IntPtr utf8Data)
        {
            var handle = GCHandle.FromIntPtr(context);  // 凭牌取回托管对象
            if (!(handle.Target is IosChannel ch) || ch._disposed) return;
            // native 缓冲只在本回调期间有效 → 当场拷成托管 string。
            string data = utf8Data != IntPtr.Zero ? Marshal.PtrToStringUTF8(utf8Data) : null;
            ch.OnResult?.Invoke(seed, code, data);      // 在子线程触发，桥会切回主线程
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnResult = null;
            if (_context != IntPtr.Zero)
            {
                try { NativeRelayChannel_Dispose(_context); }
                catch { /* 忽略关闭期异常 */ }
                _context = IntPtr.Zero;
            }
            if (_self.IsAllocated) _self.Free();        // 还牌
        }
    }
}
#endif
