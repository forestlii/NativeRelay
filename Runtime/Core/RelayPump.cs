using System;

namespace Likeon.NativeRelay
{
    /// <summary>子线程回传的一条结果：(seed, 结果码, 可选数据字符串)。值类型，入队不额外堆分配。</summary>
    public readonly struct RelayMessage
    {
        public readonly long Seed;
        public readonly int Code;
        public readonly string Data;

        public RelayMessage(long seed, int code, string data)
        {
            Seed = seed;
            Code = code;
            Data = data;
        }
    }

    /// <summary>
    /// 桥的<b>纯 C# 核心泵</b>：把 <see cref="DoubleBufferQueue{T}"/>（线程边界）与 <see cref="PendingTable"/>（请求账本）
    /// 串成「子线程入队 → 主线程统一派发 + 超时清理」。<b>不依赖 UnityEngine</b>，故可脱离 Unity 用断言验证，
    /// 也让未来 JNI/P-Invoke 边界保持干净。MonoBehaviour 外壳（MainThreadDispatcher）只是每帧调一次 <see cref="Pump"/>。
    /// </summary>
    /// <remarks>
    /// <b>纯码模型</b>：结果统一是 <c>(int code, byte[] data)</c>，框架不解释 code，原样透传给业务。
    /// 框架只在拿不到原生结果时自产码：超时 → <see cref="RelayCode.Timeout"/>，关闭 → <see cref="RelayCode.Disposed"/>。
    /// 线程模型：<see cref="Enqueue"/> 可在子线程调用（只塞队列）；<see cref="Register"/>/<see cref="Pump"/> 在主线程。
    /// 零 GC：派发用的 handler 委托与空数据均缓存为字段，稳态成功/超时路径 0 Alloc。
    /// </remarks>
    public sealed class RelayPump
    {
        private const string EmptyData = ""; // 框架自产码（超时/关闭）时的空数据，interned 无分配

        private readonly DoubleBufferQueue<RelayMessage> _queue;
        private readonly PendingTable _pending;
        private readonly double _timeoutSeconds;

        // 缓存的委托（避免每帧分配闭包）
        private readonly Action<RelayMessage> _dispatchOne;
        private readonly Action<long, PendingContext> _onTimeout;
        private readonly Action<long, PendingContext> _onDispose;

        /// <param name="timeoutSeconds">请求超时阈值（秒）；超过则以 <see cref="RelayCode.Timeout"/> 回调。</param>
        /// <param name="capacity">队列与 pending 的初始容量（按峰值预估可免运行期扩容分配）。</param>
        public RelayPump(double timeoutSeconds = 10.0, int capacity = 64)
        {
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 0;
            _queue = new DoubleBufferQueue<RelayMessage>(capacity);
            _pending = new PendingTable(capacity);
            _dispatchOne = DispatchOne;
            _onTimeout = OnTimeout;
            _onDispose = OnDispose;
        }

        /// <summary>当前未完成请求数（诊断 / 测试用）。</summary>
        public int PendingCount
        {
            get { return _pending.Count; }
        }

        /// <summary>主线程：登记一次请求（单一结果回调 (code, data)）。</summary>
        public void Register(long seed, Action<int, string> onResult, double now)
        {
            _pending.Register(seed, new PendingContext(onResult, now));
        }

        /// <summary>
        /// 子线程：把一条结果入队（线程安全，仅塞 <see cref="DoubleBufferQueue{T}"/>，不碰 pending 表）。
        /// 通常作为 <c>INativeChannel.OnResult</c> 的订阅目标。
        /// </summary>
        public void Enqueue(long seed, int code, string data)
        {
            _queue.Enqueue(new RelayMessage(seed, code, data));
        }

        /// <summary>主线程：主动取消一个已登记的请求（如发送失败收尾），从 pending 移除并取出上下文。命中返回 true。</summary>
        public bool TryCancel(long seed, out PendingContext ctx)
        {
            return _pending.TryComplete(seed, out ctx);
        }

        /// <summary>主线程：取消并清空所有未完成请求，逐个以 <see cref="RelayCode.Disposed"/> 回调（桥关闭收尾，防泄漏）。</summary>
        public void CancelAll()
        {
            _pending.DrainAll(_onDispose);
        }

        /// <summary>主线程：每帧调用。① 排干队列、按 seed 把 (code,data) 派发给对应回调；② 扫描并清理超时请求。</summary>
        /// <param name="now">当前时刻（秒），用于超时判定。</param>
        public void Pump(double now)
        {
            _queue.Consume(_dispatchOne);                       // ① 排干派发（锁外、零 GC）
            _pending.ScanTimeouts(now, _timeoutSeconds, _onTimeout); // ② 超时清理
        }

        private void DispatchOne(RelayMessage msg)
        {
            // 命中 pending 则在主线程把 (code, data) 交给业务；未命中 = 已超时清理/未知/重复结果 → 安全忽略。
            if (_pending.TryComplete(msg.Seed, out var ctx))
            {
                ctx.OnResult?.Invoke(msg.Code, msg.Data);
            }
        }

        private void OnTimeout(long seed, PendingContext ctx)
        {
            ctx.OnResult?.Invoke(RelayCode.Timeout, EmptyData);
        }

        private void OnDispose(long seed, PendingContext ctx)
        {
            ctx.OnResult?.Invoke(RelayCode.Disposed, EmptyData);
        }
    }
}
