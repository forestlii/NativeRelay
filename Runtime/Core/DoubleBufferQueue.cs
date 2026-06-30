using System;
using System.Collections.Generic;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 线程安全的<b>双缓冲</b>队列：多个子线程并发 <see cref="Enqueue"/> 写入，单个主线程 <see cref="Consume"/> 排干。
    /// 为「子线程回调入队 → 下一帧主线程统一派发」这条热路径设计，目标是<b>零 GC</b> + <b>子线程几乎不被阻塞</b>。
    /// </summary>
    /// <remarks>
    /// 三个关键工程取舍（这也是插件「专业感」的来源）：
    /// <list type="number">
    /// <item><b>锁粒度最小化</b>：两个缓冲（写/读）。<see cref="Consume"/> 持锁<b>只做一件事——交换两个缓冲的引用（O(1)）</b>，
    /// 随后在<b>锁外</b>慢慢遍历读缓冲并派发。这样子线程 <see cref="Enqueue"/> 几乎不被主线程的处理耗时阻塞。</item>
    /// <item><b>零 GC</b>：两个缓冲<b>复用、绝不 new</b>；每批处理完 <c>Clear()</c>（在锁外）而非新建；
    /// 调用方传入的 handler 委托需自行缓存，避免每帧产生闭包分配。稳态下整条链路 0 Alloc。</item>
    /// <item><b>不串批</b>：交换后旧批与新一批物理上是不同的 List，杜绝「上一帧残留混进这一帧」。</item>
    /// </list>
    /// 锁对象用 <c>private readonly object</c>，不锁 this / typeof / 装箱值类型。
    /// </remarks>
    /// <typeparam name="T">入队元素类型（本插件用承载 (seed, payload) 的小结构体）。</typeparam>
    public sealed class DoubleBufferQueue<T>
    {
        private readonly object _lock = new object();
        private List<T> _write;
        private List<T> _read;

        /// <param name="capacity">两个缓冲的初始容量；按业务峰值预估可避免运行期扩容分配（保零 GC）。</param>
        public DoubleBufferQueue(int capacity = 64)
        {
            if (capacity < 0) capacity = 0;
            _write = new List<T>(capacity);
            _read = new List<T>(capacity);
        }

        /// <summary>
        /// 子线程调用：把一个元素塞进写缓冲。持锁时间极短（仅一次 List.Add）。
        /// </summary>
        public void Enqueue(T item)
        {
            lock (_lock)
            {
                _write.Add(item);
            }
        }

        /// <summary>
        /// 主线程调用：交换读写缓冲（持锁 O(1)），然后在<b>锁外</b>把本批元素逐个交给 <paramref name="handler"/>，
        /// 处理完清空本批以备复用。
        /// </summary>
        /// <param name="handler">对每个元素的处理回调。<b>请缓存此委托</b>（如存为字段）以避免每次调用产生闭包分配。</param>
        public void Consume(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            List<T> batch;
            lock (_lock)
            {
                // 持锁只做引用交换（O(1)）：_write 的新一批换到 _read，_write 换成上一批清空后的空缓冲。
                var tmp = _write;
                _write = _read;
                _read = tmp;
                batch = _read;
            }

            // 锁外处理：遍历 + 派发 + 清空，均不阻塞正在 Enqueue 的子线程。
            // try/finally 保证即使 handler 抛异常也必清空本批——否则旧批会随下次交换混进新批被重复派发。
            // （正常情况下 handler 由 RelayPump.SafeInvoke 兜住业务异常、不会抛；此处为纵深防御。）
            try
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    handler(batch[i]);
                }
            }
            finally
            {
                batch.Clear(); // 复用缓冲，不新建；清空后它将作为下次交换的空写缓冲
            }
        }

        /// <summary>当前写缓冲中待处理的元素数（仅供测试/诊断；会瞬时变化）。</summary>
        public int PendingCount
        {
            get { lock (_lock) { return _write.Count; } }
        }
    }
}
