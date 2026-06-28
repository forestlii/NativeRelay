using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    // 纯逻辑测试（dotnet 快测 + Unity EditMode 共享）：双缓冲队列的正确性。
    // 零 GC 由 dotnet 侧 ZeroAllocTests（GC 字节）与 Unity PlayMode（Is.Not.AllocatingGCMemory）分别覆盖。
    public sealed class DoubleBufferQueueTests
    {
        [Test]
        public void Consume_DrainsAllEnqueuedInOrder_ThenEmpty()
        {
            var q = new DoubleBufferQueue<int>();
            for (int i = 0; i < 5; i++) q.Enqueue(i);

            var got = new List<int>();
            q.Consume(x => got.Add(x));
            Assert.That(got, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }), "应按入队顺序全部取出");

            // 第二次 Consume：上一批已清空，且无新入队 → 不应再吐出任何元素（防重复派发）
            got.Clear();
            q.Consume(x => got.Add(x));
            Assert.That(got, Is.Empty, "已消费批次必须清空，不得重复派发");
        }

        [Test]
        public void Consume_InterleavedEnqueue_NoCrossBatchLeak()
        {
            var q = new DoubleBufferQueue<int>();
            q.Enqueue(1); q.Enqueue(2);
            var batch1 = new List<int>();
            q.Consume(x => batch1.Add(x));
            Assert.That(batch1, Is.EqualTo(new[] { 1, 2 }));

            // 第一批消费后再入队，第二批只应见到新元素（缓冲交换不串批）
            q.Enqueue(3);
            var batch2 = new List<int>();
            q.Consume(x => batch2.Add(x));
            Assert.That(batch2, Is.EqualTo(new[] { 3 }), "交换后旧批不得混入新批");
        }

        [Test]
        public void EnqueueFromManyThreads_ConsumeDrainsExactlyOnce_NoLossNoDup()
        {
            const int producers = 8;
            const int perProducer = 20000;
            const int expected = producers * perProducer;

            var q = new DoubleBufferQueue<int>();
            var seen = new HashSet<int>();
            int collected = 0;
            var done = 0;

            // 主消费循环：边生产边消费，最后再扫一遍尾批
            void DrainOnce()
            {
                q.Consume(x =>
                {
                    Assert.That(seen.Add(x), Is.True, "同一元素不得被派发两次");
                    collected++;
                });
            }

            var producersTask = Task.Run(() =>
            {
                Parallel.For(0, producers, p =>
                {
                    int baseVal = p * perProducer;
                    for (int i = 0; i < perProducer; i++) q.Enqueue(baseVal + i);
                });
                Interlocked.Exchange(ref done, 1);
            });

            while (Volatile.Read(ref done) == 0) DrainOnce();
            producersTask.Wait();
            // 收尾：把生产者结束后残留的尾批排干
            DrainOnce();
            DrainOnce();

            Assert.That(collected, Is.EqualTo(expected), "守恒：消费总数 == 生产总数（不丢）");
            Assert.That(seen.Count, Is.EqualTo(expected), "唯一性：无重复派发");
        }
    }
}
