using System;
using System.Collections.Concurrent;
using System.Threading;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    // MockChannel 同时是 INativeChannel 契约的测试载体。默认结果 = (code=1, data=seed 的字符串)。
    public sealed class MockChannelTests
    {
        [Test]
        public void Send_FiresOnResultOnce_WithSameSeed_OnBackgroundThread()
        {
            using var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 5);
            long gotSeed = -1; int gotCode = 0; string gotData = null;
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int callbackThreadId = mainThreadId;
            var done = new CountdownEvent(1);

            ch.OnResult += (seed, code, data) =>
            {
                gotSeed = seed; gotCode = code; gotData = data;
                callbackThreadId = Thread.CurrentThread.ManagedThreadId;
                done.Signal();
            };

            ch.Send(seed: 77, command: 1, payload: null);
            Assert.That(done.Wait(TimeSpan.FromSeconds(5)), Is.True, "应在超时内回调一次");
            Assert.That(gotSeed, Is.EqualTo(77L));
            Assert.That(gotCode, Is.EqualTo(1));
            Assert.That(gotData, Is.EqualTo("77"), "默认 data = seed 字符串");
            Assert.That(callbackThreadId, Is.Not.EqualTo(mainThreadId), "回调必须在子线程");
        }

        [Test]
        public void Send_Concurrent_AllSeedsReceivedExactlyOnce_NoMisrouting()
        {
            const int n = 100;
            using var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 10);

            var received = new ConcurrentDictionary<long, int>();
            int misrouted = 0;
            var done = new CountdownEvent(n);

            ch.OnResult += (seed, code, data) =>
            {
                if (data != seed.ToString()) Interlocked.Increment(ref misrouted); // data 应回显本 seed
                received.AddOrUpdate(seed, 1, (_, c) => c + 1);
                done.Signal();
            };

            for (int i = 1; i <= n; i++) ch.Send(seed: i, command: 1, payload: null);

            Assert.That(done.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(misrouted, Is.EqualTo(0), "data 内 seed 必须与回调 seed 一致");
            Assert.That(received.Count, Is.EqualTo(n));
            foreach (var kv in received)
                Assert.That(kv.Value, Is.EqualTo(1), $"seed {kv.Key} 被回调 {kv.Value} 次（应为 1）");
        }

        [Test]
        public void CustomResultFactory_ReturnsCodeAndData()
        {
            using var ch = new MockChannel(
                minDelayMs: 1, maxDelayMs: 3,
                resultFactory: (seed, command, payload) => (10086, "boom"));
            int gotCode = 0; string gotData = null;
            var done = new CountdownEvent(1);
            ch.OnResult += (seed, code, data) => { gotCode = code; gotData = data; done.Signal(); };

            ch.Send(1, 1, null);
            Assert.That(done.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(gotCode, Is.EqualTo(10086));
            Assert.That(gotData, Is.EqualTo("boom"));
        }

        [Test]
        public void Dispose_StopsFiringFurtherResults()
        {
            var ch = new MockChannel(minDelayMs: 50, maxDelayMs: 80);
            int fired = 0;
            ch.OnResult += (_, __, ___) => Interlocked.Increment(ref fired);

            ch.Send(1, 1, null);
            ch.Dispose();
            Thread.Sleep(200);

            Assert.That(Volatile.Read(ref fired), Is.EqualTo(0), "Dispose 后不应再派发");
        }
    }
}
