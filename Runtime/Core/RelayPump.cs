using System;

namespace NativeRelay
{
    /// <summary>子线程回传的一条结果：(seed, 结果字节)。值类型，入队不额外堆分配。</summary>
    public readonly struct RelayMessage
    {
        public readonly long Seed;
        public readonly byte[] Payload;

        public RelayMessage(long seed, byte[] payload)
        {
            Seed = seed;
            Payload = payload;
        }
    }

    /// <summary>
    /// 桥的<b>纯 C# 核心泵</b>：把 <see cref="DoubleBufferQueue{T}"/>（线程边界）与 <see cref="PendingTable"/>（请求账本）
    /// 串成「子线程入队 → 主线程统一派发 + 超时清理」。<b>不依赖 UnityEngine</b>，故可脱离 Unity 用断言验证、
    /// 也让未来 JNI/P-Invoke 边界保持干净。MonoBehaviour 外壳（MainThreadDispatcher）只是每帧调一次 <see cref="Pump"/>。
    /// </summary>
    /// <remarks>
    /// 线程模型：<see cref="Enqueue"/> 可在子线程调用（只塞队列，不碰 pending 表）；
    /// <see cref="Register"/>/<see cref="Pump"/> 在主线程调用。pending 表因此天然单线程、无需锁。
    /// 零 GC：派发用的 handler 委托缓存为字段（不在每帧 new lambda）；稳态成功路径 0 Alloc。
    /// </remarks>
    public sealed class RelayPump
    {
        private readonly DoubleBufferQueue<RelayMessage> _queue;
        private readonly PendingTable _pending;
        private readonly double _timeoutSeconds;

        // 缓存的委托（避免每帧分配闭包）
        private readonly Action<RelayMessage> _dispatchOne;
        private readonly Action<long, PendingContext> _onTimeout;

        /// <param name="timeoutSeconds">请求超时阈值（秒）；超过则回调 <see cref="BridgeError"/> Timeout。</param>
        /// <param name="capacity">队列与 pending 的初始容量（按峰值预估可免运行期扩容分配）。</param>
        public RelayPump(double timeoutSeconds = 10.0, int capacity = 64)
        {
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 0;
            _queue = new DoubleBufferQueue<RelayMessage>(capacity);
            _pending = new PendingTable(capacity);
            _dispatchOne = DispatchOne;   // 一次性绑定方法组，后续复用
            _onTimeout = OnTimeout;
        }

        /// <summary>当前未完成请求数（诊断 / 测试用）。</summary>
        public int PendingCount
        {
            get { return _pending.Count; }
        }

        /// <summary>
        /// 主线程：登记一次请求。由 <see cref="Bridge"/> 在生成 seed、调 channel.Send 前后调用。
        /// </summary>
        public void Register(long seed, Action<byte[]> onResult, Action<BridgeError> onError, double now)
        {
            _pending.Register(seed, new PendingContext(onResult, onError, now));
        }

        /// <summary>
        /// 子线程：把一条结果入队（线程安全，仅塞 <see cref="DoubleBufferQueue{T}"/>，不碰 pending 表）。
        /// 通常作为 <c>INativeChannel.OnResult</c> 的订阅目标。
        /// </summary>
        public void Enqueue(long seed, byte[] payload)
        {
            _queue.Enqueue(new RelayMessage(seed, payload));
        }

        /// <summary>
        /// 主线程：主动取消一个已登记的请求（如发送失败时收尾），从 pending 移除并取出其上下文。
        /// 命中返回 true。
        /// </summary>
        public bool TryCancel(long seed, out PendingContext ctx)
        {
            return _pending.TryComplete(seed, out ctx);
        }

        /// <summary>
        /// 主线程：每帧调用。① 排干队列、按 seed 把结果派发给对应回调；② 扫描并清理超时请求。
        /// </summary>
        /// <param name="now">当前时刻（秒），用于超时判定。</param>
        public void Pump(double now)
        {
            // ① 排干本批结果并派发（锁外处理，零 GC：handler 已缓存）
            _queue.Consume(_dispatchOne);

            // ② 超时清理
            _pending.ScanTimeouts(now, _timeoutSeconds, _onTimeout);
        }

        private void DispatchOne(RelayMessage msg)
        {
            // 按 seed 匹配 pending；命中则在主线程调用成功回调。
            // 未命中 = 该 seed 已超时清理 / 未知 / 重复结果 → 安全忽略（不重复派发、不抛）。
            if (_pending.TryComplete(msg.Seed, out var ctx))
            {
                ctx.OnResult?.Invoke(msg.Payload);
            }
        }

        private void OnTimeout(long seed, PendingContext ctx)
        {
            ctx.OnError?.Invoke(BridgeError.Timeout(seed));
        }
    }
}
