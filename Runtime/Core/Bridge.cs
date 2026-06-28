using System;

namespace NativeRelay
{
    /// <summary>
    /// 插件对外主入口：把「发起异步请求 → 安全收回结果」整条链路封装成一个调用。
    /// 业务层只需 <see cref="Request(int, byte[], Action{byte[]}, Action{BridgeError})"/>，
    /// 结果会在<b>主线程</b>经 <see cref="Pump"/> 派发回 onResult。
    /// </summary>
    /// <remarks>
    /// 纯 C#（不依赖 UnityEngine）：时间由注入的 <c>clock</c> 提供，便于脱离 Unity 测试；
    /// 在 Unity 里由 <c>MainThreadDispatcher</c> 注入 <c>Time.realtimeSinceStartupAsDouble</c> 并每帧调 <see cref="Pump"/>。
    /// <para>
    /// 线程模型：<see cref="Request"/> 与 <see cref="Pump"/> 须在<b>主线程</b>调用（二者都会动 pending 表，故无锁）；
    /// channel 的 <c>OnResult</c> 可在子线程触发，本类已订阅它把结果安全塞进 <see cref="DoubleBufferQueue{T}"/>，
    /// 真正的派发推迟到主线程 <see cref="Pump"/>。
    /// </para>
    /// 🔴 <see cref="Request(int, byte[], Action{byte[]}, Action{BridgeError})"/> 的 3 参成功形态为冻结契约；
    /// 第 4 参 onError 为可选扩展，不破坏源码兼容。
    /// </remarks>
    public sealed class Bridge : IDisposable
    {
        private readonly INativeChannel _channel;
        private readonly SeedGenerator _seeds;
        private readonly RelayPump _pump;
        private readonly Func<double> _clock;
        private readonly Action<long, byte[]> _onChannelResult; // 缓存订阅委托，便于退订
        private bool _disposed;

        /// <param name="channel">原生通道实现（如 <see cref="MockChannel"/> 或真实 Android 适配）。</param>
        /// <param name="clock">当前时刻（秒）提供者；用于请求时间戳与超时判定，Register/Pump 共用同一时钟。</param>
        /// <param name="timeoutSeconds">请求超时阈值（秒）。</param>
        /// <param name="capacity">队列/pending 初始容量（按峰值预估免运行期扩容分配）。</param>
        public Bridge(INativeChannel channel, Func<double> clock, double timeoutSeconds = 10.0, int capacity = 64)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _seeds = new SeedGenerator();
            _pump = new RelayPump(timeoutSeconds, capacity);

            // 订阅子线程回调：只把结果入队（线程安全），派发推迟到主线程 Pump。
            _onChannelResult = _pump.Enqueue;
            _channel.OnResult += _onChannelResult;
        }

        /// <summary>当前未完成请求数（诊断 / 测试用）。</summary>
        public int PendingCount
        {
            get { return _pump.PendingCount; }
        }

        /// <summary>是否已 Dispose（驱动方据此停止 Pump 并清理）。</summary>
        public bool IsDisposed
        {
            get { return _disposed; }
        }

        /// <summary>
        /// 发起一次异步请求（主线程调用）：生成唯一 seed、登记 pending、调 <c>channel.Send</c>，返回该 seed。
        /// 结果回来后会经 <see cref="Pump"/> 在主线程调用 <paramref name="onResult"/>。
        /// </summary>
        /// <param name="command">行为标识（int），取值由使用者定义。</param>
        /// <param name="payload">请求参数字节，可为 null。</param>
        /// <param name="onResult">结果回调（主线程）。</param>
        /// <param name="onError">可选失败回调（超时 / 通道失败 / 关闭，主线程）。</param>
        /// <returns>本次请求的 seed。</returns>
        public long Request(int command, byte[] payload, Action<byte[]> onResult, Action<BridgeError> onError = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Bridge));
            if (onResult == null) throw new ArgumentNullException(nameof(onResult));

            long seed = _seeds.Next();
            double now = _clock();
            _pump.Register(seed, onResult, onError, now);

            try
            {
                _channel.Send(seed, command, payload);
            }
            catch (Exception ex)
            {
                // 发送失败：把刚登记的请求收掉并通知业务，避免它一直挂在 pending 直到超时。
                if (_pump.TryCancel(seed, out var ctx))
                {
                    ctx.OnError?.Invoke(BridgeError.ChannelFailure(seed, ex.Message));
                }
            }

            return seed;
        }

        /// <summary>主线程每帧调用：排干结果并按 seed 派发 + 清理超时。</summary>
        public void Pump()
        {
            _pump.Pump(_clock());
        }

        /// <summary>关闭桥：退订通道回调、Dispose 底层通道。之后 <see cref="Request"/> 抛 <see cref="ObjectDisposedException"/>。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.OnResult -= _onChannelResult;
            _channel.Dispose();
        }
    }
}
