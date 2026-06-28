using System;
using System.Collections.Concurrent;
using System.Threading;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    // MockChannel 同时是 INativeChannel 契约的测试载体（接口本身无行为）。
    // 用小延迟（1~10ms）+ CountdownEvent 等待，保证测试快且稳；验证「子线程乱序回来仍 seed↔payload 一一对应」。
    public sealed class MockChannelTests
    {
        // 默认结果工厂把 seed 编码进字节，便于解码校验是否错配。
        private static long DecodeSeed(byte[] bytes)
        {
            return BitConverter.ToInt64(bytes, 0);
        }

        [Test]
        public void Send_FiresOnResultOnce_WithSameSeed_OnBackgroundThread()
        {
            using var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 5);
            long gotSeed = -1;
            byte[] gotPayload = null;
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int callbackThreadId = mainThreadId;
            var done = new CountdownEvent(1);

            ch.OnResult += (seed, payload) =>
            {
                gotSeed = seed;
                gotPayload = payload;
                callbackThreadId = Thread.CurrentThread.ManagedThreadId;
                done.Signal();
            };

            ch.Send(seed: 77, command: 1, payload: null);
            Assert.That(done.Wait(TimeSpan.FromSeconds(5)), Is.True, "应在超时内回调一次");
            Assert.That(gotSeed, Is.EqualTo(77L));
            Assert.That(DecodeSeed(gotPayload), Is.EqualTo(77L), "结果字节应回显 seed");
            Assert.That(callbackThreadId, Is.Not.EqualTo(mainThreadId), "回调必须发生在子线程（呼应跨线程场景）");
        }

        [Test]
        public void Send_Concurrent_AllSeedsReceivedExactlyOnce_NoMisrouting()
        {
            const int n = 100;
            using var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 10);

            var received = new ConcurrentDictionary<long, int>();
            bool misrouted = false;
            var done = new CountdownEvent(n);

            ch.OnResult += (seed, payload) =>
            {
                // 错配检测：payload 解出的 seed 必须等于回调带回的 seed
                if (DecodeSeed(payload) != seed) Volatile.Write(ref misrouted, true);
                received.AddOrUpdate(seed, 1, (_, c) => c + 1);
                done.Signal();
            };

            for (int i = 1; i <= n; i++) ch.Send(seed: i, command: 1, payload: null);

            Assert.That(done.Wait(TimeSpan.FromSeconds(10)), Is.True, "全部回调应在超时内到齐");
            Assert.That(misrouted, Is.False, "payload 内 seed 必须与回调 seed 一致，零错配");
            Assert.That(received.Count, Is.EqualTo(n), "每个 seed 恰好收到一次");
            foreach (var kv in received)
                Assert.That(kv.Value, Is.EqualTo(1), $"seed {kv.Key} 被回调了 {kv.Value} 次（应为 1）");
        }

        [Test]
        public void Dispose_StopsFiringFurtherResults()
        {
            var ch = new MockChannel(minDelayMs: 50, maxDelayMs: 80);
            int fired = 0;
            ch.OnResult += (_, __) => Interlocked.Increment(ref fired);

            ch.Send(1, 1, null);
            ch.Dispose(); // 在结果回来前 dispose
            Thread.Sleep(200); // 等过原本的延迟窗口

            Assert.That(Volatile.Read(ref fired), Is.EqualTo(0), "Dispose 后不应再派发结果");
        }
    }
}
