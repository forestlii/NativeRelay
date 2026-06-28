using System;
using System.Collections.Generic;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    public sealed class BridgeTests
    {
        // 可控测试替身：记录 Send，允许测试手动触发 OnResult，可配置 Send 抛异常。
        private sealed class FakeChannel : INativeChannel
        {
            public readonly List<(long seed, int cmd, string payload)> Sends = new();
            public bool ThrowOnSend;
            public bool Disposed;
            public event Action<long, int, string> OnResult;

            public void Send(long seed, int command, string payload)
            {
                if (ThrowOnSend) throw new InvalidOperationException("boom");
                Sends.Add((seed, command, payload));
            }

            public void FireResult(long seed, int code, string data) => OnResult?.Invoke(seed, code, data);
            public void Dispose() => Disposed = true;
        }

        private static Bridge NewBridge(FakeChannel ch, Func<double> clock, double timeout = 100)
            => new Bridge(ch, clock, timeoutSeconds: timeout);

        [Test]
        public void Request_GeneratesUniqueSeeds_AndRegistersPending()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            long s1 = bridge.Request(10, null, (c, d) => { });
            long s2 = bridge.Request(11, null, (c, d) => { });

            Assert.That(s1, Is.Not.EqualTo(s2));
            Assert.That(s2, Is.GreaterThan(s1));
            Assert.That(bridge.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void Request_CallsChannelSend_WithSeedCommandPayload()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            long seed = bridge.Request(42, "params", (c, d) => { });

            Assert.That(ch.Sends.Count, Is.EqualTo(1));
            Assert.That(ch.Sends[0].seed, Is.EqualTo(seed));
            Assert.That(ch.Sends[0].cmd, Is.EqualTo(42));
            Assert.That(ch.Sends[0].payload, Is.EqualTo("params"));
        }

        [Test]
        public void ResultFromChannel_DispatchedToCallback_AfterPump()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);
            int gotCode = 0; string gotData = null;
            long seed = bridge.Request(1, null, (c, d) => { gotCode = c; gotData = d; });

            ch.FireResult(seed, 1, "hi"); // 仅入队，尚未派发
            Assert.That(gotData, Is.Null, "Pump 之前不应派发");

            bridge.Pump();
            Assert.That(gotCode, Is.EqualTo(1));
            Assert.That(gotData, Is.EqualTo("hi"));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_Timeout_FiresTimeoutCode()
        {
            double now = 0;
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => now, timeout: 1.0);

            int gotCode = 0; bool called = false;
            bridge.Request(1, null, (c, d) => { gotCode = c; called = true; });

            now = 2.0;
            bridge.Pump();

            Assert.That(called, Is.True);
            Assert.That(gotCode, Is.EqualTo(RelayCode.Timeout));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Request_ChannelSendThrows_RethrowsToCaller_NoPendingLeak()
        {
            var ch = new FakeChannel { ThrowOnSend = true };
            var bridge = NewBridge(ch, () => 0);

            // 框架不处理错误：Send 异常如实抛回调用方，但 pending 不泄漏。
            Assert.That(() => bridge.Request(1, null, (c, d) => { }), Throws.TypeOf<InvalidOperationException>());
            Assert.That(bridge.PendingCount, Is.EqualTo(0), "失败请求不得泄漏在 pending");
        }

        [Test]
        public void Dispose_DisposesChannel_AndRequestAfterThrows()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            bridge.Dispose();
            Assert.That(ch.Disposed, Is.True);
            Assert.That(() => bridge.Request(1, null, (c, d) => { }), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_FiresDisposedCodeForPending_AndClears()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);

            var disposedSeeds = new List<long>();
            int wrongCode = 0;
            for (int i = 0; i < 3; i++)
            {
                bridge.Request(1, null, (code, data) =>
                {
                    if (code != RelayCode.Disposed) wrongCode++;
                    disposedSeeds.Add(1);
                });
            }
            Assert.That(bridge.PendingCount, Is.EqualTo(3));

            bridge.Dispose();

            Assert.That(disposedSeeds.Count, Is.EqualTo(3), "所有未完成请求都应收到 Disposed 码");
            Assert.That(wrongCode, Is.EqualTo(0));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_Idempotent()
        {
            var ch = new FakeChannel();
            var bridge = NewBridge(ch, () => 0);
            bridge.Request(1, null, (c, d) => { });
            bridge.Dispose();
            Assert.DoesNotThrow(() => bridge.Dispose());
        }
    }
}
