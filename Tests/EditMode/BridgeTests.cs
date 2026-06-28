using System;
using System.Collections.Generic;
using NativeRelay;
using NUnit.Framework;

namespace NativeRelay.Tests
{
    public sealed class BridgeTests
    {
        // 可控测试替身：记录 Send，允许测试手动触发 OnResult（模拟子线程回传），可配置 Send 抛异常。
        private sealed class FakeChannel : INativeChannel
        {
            public readonly List<(long seed, int cmd, byte[] payload)> Sends = new();
            public bool ThrowOnSend;
            public bool Disposed;
            public event Action<long, byte[]> OnResult;

            public void Send(long seed, int command, byte[] payload)
            {
                if (ThrowOnSend) throw new InvalidOperationException("boom");
                Sends.Add((seed, command, payload));
            }

            public void FireResult(long seed, byte[] bytes) => OnResult?.Invoke(seed, bytes);
            public void Dispose() => Disposed = true;
        }

        private static Bridge NewBridge(FakeChannel ch, Func<double> clock, double timeout = 100)
            => new Bridge(ch, clock, timeoutSeconds: timeout);

        [Test]
        public void Request_GeneratesUniqueSeeds_AndRegistersPending()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            long s1 = bridge.Request(10, null, _ => { });
            long s2 = bridge.Request(11, null, _ => { });

            Assert.That(s1, Is.Not.EqualTo(s2), "seed 唯一");
            Assert.That(s2, Is.GreaterThan(s1), "seed 单调递增");
            Assert.That(bridge.PendingCount, Is.EqualTo(2), "两次请求都登记 pending");
        }

        [Test]
        public void Request_CallsChannelSend_WithSeedCommandPayload()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);
            var payload = new byte[] { 1, 2 };

            long seed = bridge.Request(42, payload, _ => { });

            Assert.That(ch.Sends.Count, Is.EqualTo(1));
            Assert.That(ch.Sends[0].seed, Is.EqualTo(seed));
            Assert.That(ch.Sends[0].cmd, Is.EqualTo(42));
            Assert.That(ch.Sends[0].payload, Is.SameAs(payload));
        }

        [Test]
        public void ResultFromChannel_DispatchedToCallback_AfterPump()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);
            byte[] got = null;
            long seed = bridge.Request(1, null, p => got = p);

            ch.FireResult(seed, new byte[] { 9 }); // 模拟子线程回传：仅入队，尚未派发
            Assert.That(got, Is.Null, "Pump 之前不应派发（结果只是入队）");

            bridge.Pump(); // 主线程派发
            Assert.That(got, Is.EqualTo(new byte[] { 9 }));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_Timeout_FiresOnError()
        {
            double now = 0;
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => now, timeout: 1.0);

            BridgeError err = default;
            bool errCalled = false;
            bridge.Request(1, null, _ => Assert.Fail("不应成功"), e => { err = e; errCalled = true; });

            now = 2.0;     // 推进时钟越过超时
            bridge.Pump();

            Assert.That(errCalled, Is.True);
            Assert.That(err.Kind, Is.EqualTo(BridgeErrorKind.Timeout));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Request_ChannelSendThrows_FiresChannelFailure_NoPendingLeak()
        {
            var ch = new FakeChannel { ThrowOnSend = true };
            var bridge = NewBridge(ch, () => 0);

            BridgeError err = default;
            bool errCalled = false;
            bridge.Request(1, null, _ => Assert.Fail("不应成功"), e => { err = e; errCalled = true; });

            Assert.That(errCalled, Is.True, "Send 抛异常应回调 ChannelFailure");
            Assert.That(err.Kind, Is.EqualTo(BridgeErrorKind.ChannelFailure));
            Assert.That(bridge.PendingCount, Is.EqualTo(0), "失败请求不得泄漏在 pending");
        }

        [Test]
        public void Dispose_DisposesChannel_AndRequestAfterThrows()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            bridge.Dispose();
            Assert.That(ch.Disposed, Is.True, "应 Dispose 底层通道");
            Assert.That(() => bridge.Request(1, null, _ => { }), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_FiresDisposedForPending_AndClears()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            var disposedSeeds = new List<long>();
            int wrongKind = 0;
            for (int i = 0; i < 3; i++)
            {
                long captured = 0;
                captured = bridge.Request(1, null, _ => Assert.Fail("不应成功"),
                    err =>
                    {
                        if (err.Kind != BridgeErrorKind.Disposed || err.Seed != captured) wrongKind++;
                        disposedSeeds.Add(captured);
                    });
            }
            Assert.That(bridge.PendingCount, Is.EqualTo(3));

            bridge.Dispose();

            Assert.That(disposedSeeds.Count, Is.EqualTo(3), "所有未完成请求都应收到 Disposed 收尾");
            Assert.That(wrongKind, Is.EqualTo(0), "应为 Disposed 且 seed 对得上");
            Assert.That(bridge.PendingCount, Is.EqualTo(0), "Dispose 后 pending 清空，无泄漏");
        }

        [Test]
        public void Dispose_Idempotent()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);
            bridge.Request(1, null, _ => { }, _ => { });
            bridge.Dispose();
            Assert.DoesNotThrow(() => bridge.Dispose(), "重复 Dispose 应安全无副作用");
        }
    }
}
