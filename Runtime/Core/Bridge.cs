using System;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 插件对外主入口：把「发起异步请求 → 安全收回结果」整条链路封装成一个调用。
    /// 业务层只需 <see cref="Request(int, string, Action{int, string})"/>，
    /// 结果 <c>(code, data)</c> 会在<b>主线程</b>经 <see cref="Pump"/> 派发回 onResult。
    /// </summary>
    /// <remarks>
    /// 纯 C#（不依赖 UnityEngine）：时间由注入的 <c>clock</c> 提供，便于脱离 Unity 测试；
    /// 在 Unity 里由 <c>MainThreadDispatcher</c> 注入 <c>Time.realtimeSinceStartupAsDouble</c> 并每帧调 <see cref="Pump"/>。
    /// <para>
    /// 线程模型：<see cref="Request"/> 与 <see cref="Pump"/> 须在<b>主线程</b>调用（二者都会动 pending 表，故无锁）；
    /// channel 的 <c>OnResult</c> 可在子线程触发，本类已订阅它把结果安全塞进 <see cref="DoubleBufferQueue{T}"/>，
    /// 真正的派发推迟到主线程 <see cref="Pump"/>。
    /// </para>
    /// 🔴 纯码契约：结果统一是 <c>(int code, string data)</c>，框架不解释 code、不碰 data，原样透传给业务；
    /// 框架只在拿不到原生结果时自产码（<see cref="RelayCode.Timeout"/>/<see cref="RelayCode.Disposed"/>）。
    /// </remarks>
    public sealed class Bridge : IDisposable
    {
        private readonly INativeChannel _channel;
        private readonly SeedGenerator _seeds;
        private readonly RelayPump _pump;
        private readonly Func<double> _clock;
        private readonly Action<long, int, string> _onChannelResult; // 缓存订阅委托，便于退订
        private bool _disposed;

        /// <param name="channel">原生通道实现（如 <see cref="MockChannel"/> 或真实 Android 适配）。</param>
        /// <param name="clock">当前时刻（秒）提供者；用于请求时间戳与超时判定，Register/Pump 共用同一时钟。</param>
        /// <param name="timeoutSeconds">请求超时阈值（秒）。</param>
        /// <param name="capacity">队列/pending 初始容量（按峰值预估免运行期扩容分配）。</param>
        /// <param name="onError">可选：业务 onResult 回调抛出的异常会被就地隔离并交给它（一个回调出错不连累同批其它回调，
        /// 也不打断每帧 Pump）；传 null 则异常被静默吞掉。Unity 下由 <c>MainThreadDispatcher</c> 默认接 <c>Debug.LogException</c>。</param>
        public Bridge(INativeChannel channel, Func<double> clock, double timeoutSeconds = 10.0, int capacity = 64, Action<Exception> onError = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _seeds = new SeedGenerator();
            _pump = new RelayPump(timeoutSeconds, capacity, onError);

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
        /// <param name="payload">请求参数字符串（参数/路径/url），可为 null。</param>
        /// <param name="onResult">结果回调（主线程）：<c>(code, data)</c>。code 是业务码或框架保留码
        /// （<see cref="RelayCode.Timeout"/>/<see cref="RelayCode.Disposed"/>），框架不解释；data 为可选数据字符串。</param>
        /// <returns>本次请求的 seed。</returns>
        public long Request(int command, string payload, Action<int, string> onResult)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Bridge));
            if (onResult == null) throw new ArgumentNullException(nameof(onResult));

            long seed = _seeds.Next();
            double now = _clock();
            _pump.Register(seed, onResult, now);

            try
            {
                _channel.Send(seed, command, payload);
            }
            catch
            {
                // 框架不处理错误：发送异常如实抛回业务（调用线程），但先撤掉刚登记的 pending 防泄漏。
                _pump.TryCancel(seed, out _);
                throw;
            }

            return seed;
        }

        /// <summary>主线程每帧调用：排干结果并按 seed 派发 + 清理超时。</summary>
        public void Pump()
        {
            _pump.Pump(_clock());
        }

        /// <summary>
        /// 关闭桥：① 退订通道回调（停止新结果入队）；② 给所有未完成请求回调 <see cref="RelayCode.Disposed"/>
        /// 并清空 pending（防泄漏，业务能据此收尾）；③ Dispose 底层通道。之后 <see cref="Request"/> 抛 <see cref="ObjectDisposedException"/>。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.OnResult -= _onChannelResult;
            _pump.CancelAll();
            _channel.Dispose();
        }
    }
}
