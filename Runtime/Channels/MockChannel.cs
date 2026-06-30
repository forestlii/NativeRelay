using System;
using System.Threading;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 纯 C# 模拟的原生通道：<see cref="Send"/> 被调用后，起一个后台线程，随机延迟若干毫秒，
    /// 然后<b>在那个子线程上</b>触发 <see cref="OnResult"/>。
    /// </summary>
    /// <remarks>
    /// 作用（这是「任何人 clone 下来无需真机/key/联网就能跑」这一红线的关键）：
    /// 不需要任何真实 SDK，就能完整复现「子线程回调 → 桥切回主线程 → 按 seed 派发」这条链路，
    /// 并能在 Profiler 里看零 GC、看并发乱序到达仍一一对应。是 demo 场景与单测的基础。
    /// <para>
    /// 说明：MockChannel 的「调度一次回调」本身会有少量分配（它替代的是真实原生侧，不属于桥的零 GC 热路径）；
    /// 桥的零 GC 硬指标针对的是 <see cref="DoubleBufferQueue{T}"/> 收发与每帧派发那条路径。
    /// </para>
    /// 默认结果 <c>(code=1, data=seed.ToString())</c> 回显 seed，便于测试校验是否错配；可通过构造参数自定义结果工厂（供 ASR demo 用）。
    /// </remarks>
    public sealed class MockChannel : INativeChannel
    {
        /// <summary>原生层 → 框架层：结果 (seed, code, data) 回来（在子线程触发）。</summary>
        public event Action<long, int, string> OnResult;

        private readonly int _minDelayMs;
        private readonly int _maxDelayMs;
        private readonly Func<long, int, string, (int code, string data)> _resultFactory;
        private readonly Func<long, bool> _shouldDrop;
        private readonly Random _rng;
        private readonly object _rngLock = new object();
        private volatile bool _disposed;

        /// <param name="minDelayMs">模拟回调最小延迟（默认 50ms）。</param>
        /// <param name="maxDelayMs">模拟回调最大延迟（默认 500ms）。</param>
        /// <param name="resultFactory">结果工厂 (seed, command, payload) → (code, data)；默认 (1, 回显 seed 字符串)。</param>
        /// <param name="shouldDrop">可选：返回 true 的 seed 将<b>永不回 OnResult</b>（模拟原生层丢结果/崩溃），
        /// 用于测试桥的超时清理。默认 null = 都会回。</param>
        /// <param name="seed">随机种子；默认随机。</param>
        public MockChannel(
            int minDelayMs = 50,
            int maxDelayMs = 500,
            Func<long, int, string, (int code, string data)> resultFactory = null,
            Func<long, bool> shouldDrop = null,
            int? seed = null)
        {
            if (minDelayMs < 0) minDelayMs = 0;
            if (maxDelayMs < minDelayMs) maxDelayMs = minDelayMs;
            _minDelayMs = minDelayMs;
            _maxDelayMs = maxDelayMs;
            _resultFactory = resultFactory ?? DefaultResult;
            _shouldDrop = shouldDrop;
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        private static (int code, string data) DefaultResult(long seed, int command, string payload)
        {
            return (1, seed.ToString()); // 默认 code=1(成功) + data=回显 seed 的字符串
        }

        /// <inheritdoc />
        public void Send(long seed, int command, string payload)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MockChannel));

            // 模拟「原生层收到了请求但永不回结果」（丢失/崩溃）：直接不调度回调，交给桥的超时清理。
            if (_shouldDrop != null && _shouldDrop(seed)) return;

            int delay;
            lock (_rngLock)
            {
                // Random 非线程安全，加锁取值（仅在发起时，极短）
                delay = (_maxDelayMs > _minDelayMs)
                    ? _rng.Next(_minDelayMs, _maxDelayMs + 1)
                    : _minDelayMs;
            }

            // 在后台线程上模拟原生异步：睡一段随机延迟，再于子线程触发 OnResult。
            // 每个请求各自随机延迟 → 天然乱序到达，复现「不保证顺序、只保证 seed 一一对应」。
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (delay > 0) Thread.Sleep(delay);
                if (_disposed) return; // 已关闭则不再派发

                var handler = OnResult; // 本地快照，避免竞态下被置空
                if (handler == null) return;

                var (code, data) = _resultFactory(seed, command, payload);
                handler(seed, code, data);
            });
        }

        /// <summary>关闭通道：之后不再派发任何结果（已在途的回调会在触发前检查并跳过）。</summary>
        public void Dispose()
        {
            _disposed = true;
            OnResult = null;
        }
    }
}
