using System;
using System.Collections.Generic;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 一次未完成请求的上下文：<b>单一结果回调</b>(code, data) + 发起时间戳。
    /// <b>readonly struct</b>，在 <see cref="Dictionary{TKey,TValue}"/> 里内联存储，避免每次请求为上下文额外堆分配。
    /// </summary>
    public readonly struct PendingContext
    {
        /// <summary>
        /// 结果回来时在主线程调用：<c>code</c> = 结果码（业务自定义，或框架保留码 <see cref="RelayCode"/>），
        /// <c>data</c> = 可选数据字符串（成功时如文本/路径，失败/超时可为空）。框架不解释 code、不碰 data。
        /// </summary>
        public readonly Action<int, string> OnResult;

        /// <summary>发起请求时的时间戳（秒）；用于超时扫描。</summary>
        public readonly double StartTime;

        public PendingContext(Action<int, string> onResult, double startTime)
        {
            OnResult = onResult;
            StartTime = startTime;
        }
    }

    /// <summary>
    /// 待处理请求表：<c>Dictionary&lt;seed, <see cref="PendingContext"/>&gt;</c>。
    /// 请求发出时 <see cref="Register"/> 登记；结果回来按 seed <see cref="TryComplete"/> 匹配并移除；
    /// 原生层永不回的请求由 <see cref="ScanTimeouts"/> 定期清理，防止 pending 泄漏。
    /// </summary>
    /// <remarks>
    /// <b>线程亲和：本表仅在主线程访问，故内部不加锁。</b>
    /// 设计上 <see cref="Register"/> 发生在业务调用 <c>Bridge.Request</c>（主线程），
    /// <see cref="TryComplete"/>/<see cref="ScanTimeouts"/> 发生在每帧 dispatcher 派发（主线程）；
    /// 子线程回调只把结果塞进 <see cref="DoubleBufferQueue{T}"/>（线程边界在那里守），不直接碰本表。
    /// 这样可省掉锁开销，同时保证零数据竞争。
    /// </remarks>
    public sealed class PendingTable
    {
        private readonly Dictionary<long, PendingContext> _map;
        private readonly List<long> _timedOutScratch; // 复用缓冲：扫描时先收集过期 seed 再删（避免遍历中改集合）

        public PendingTable(int capacity = 64)
        {
            if (capacity < 0) capacity = 0;
            _map = new Dictionary<long, PendingContext>(capacity);
            _timedOutScratch = new List<long>(capacity);
        }

        /// <summary>当前未完成的请求数。</summary>
        public int Count
        {
            get { return _map.Count; }
        }

        /// <summary>登记一次请求。seed 由 <see cref="SeedGenerator"/> 保证唯一，故不会键冲突。</summary>
        public void Register(long seed, in PendingContext ctx)
        {
            _map.Add(seed, ctx);
        }

        /// <summary>
        /// 按 seed 匹配并移除一次请求。命中返回 true 并输出其上下文；未命中（已超时清理 / 重复回调）返回 false。
        /// </summary>
        public bool TryComplete(long seed, out PendingContext ctx)
        {
            if (_map.TryGetValue(seed, out ctx))
            {
                _map.Remove(seed);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 排干<b>全部</b>未完成请求（用于 Dispose 收尾）：逐个移除并交给 <paramref name="handler"/>，最终清空。
        /// 先收集 key 到复用缓冲再处理，避免遍历中改集合（handler 内即便误调也安全）。
        /// </summary>
        public void DrainAll(Action<long, PendingContext> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _timedOutScratch.Clear();
            foreach (var kv in _map)
            {
                _timedOutScratch.Add(kv.Key);
            }

            for (int i = 0; i < _timedOutScratch.Count; i++)
            {
                long seed = _timedOutScratch[i];
                if (_map.TryGetValue(seed, out var ctx))
                {
                    _map.Remove(seed);
                    handler(seed, ctx);
                }
            }
        }

        /// <summary>
        /// 扫描并清理超时请求：把满足 <c>(now - StartTime) &gt; timeout</c>（严格大于）的项移除，
        /// 并逐个交给 <paramref name="onTimeout"/>（在此处理超时通知，调用方应缓存该委托以免分配）。
        /// 无超时项时仅遍历一次字典（结构体枚举器，零分配）。
        /// </summary>
        public void ScanTimeouts(double now, double timeout, Action<long, PendingContext> onTimeout)
        {
            if (onTimeout == null) throw new ArgumentNullException(nameof(onTimeout));

            _timedOutScratch.Clear();
            foreach (var kv in _map)
            {
                if (now - kv.Value.StartTime > timeout)
                {
                    _timedOutScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _timedOutScratch.Count; i++)
            {
                long seed = _timedOutScratch[i];
                if (_map.TryGetValue(seed, out var ctx))
                {
                    _map.Remove(seed);
                    onTimeout(seed, ctx);
                }
            }
        }
    }
}
