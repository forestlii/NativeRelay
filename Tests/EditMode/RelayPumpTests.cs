using System.Collections.Generic;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    public sealed class RelayPumpTests
    {
        [Test]
        public void EnqueueThenPump_DispatchesCodeAndData_ThenPendingEmpty()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            int gotCode = 0; string gotData = null;
            pump.Register(1, (code, data) => { gotCode = code; gotData = data; }, now: 0);

            pump.Enqueue(1, 1, "hello");
            pump.Pump(now: 0);

            Assert.That(gotCode, Is.EqualTo(1));
            Assert.That(gotData, Is.EqualTo("hello"));
            Assert.That(pump.PendingCount, Is.EqualTo(0), "完成后从 pending 移除");
        }

        [Test]
        public void Pump_UnknownSeed_Ignored_NoThrow()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            pump.Enqueue(404, 1, "x"); // 从未注册
            Assert.DoesNotThrow(() => pump.Pump(now: 0));
            Assert.That(pump.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_OutOfOrderArrival_EachSeedMapsToItsOwnData()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            var results = new Dictionary<long, string>();
            for (int s = 1; s <= 3; s++)
            {
                long seed = s;
                pump.Register(seed, (code, data) => results[seed] = data, now: 0);
            }

            pump.Enqueue(3, 1, "C");
            pump.Enqueue(1, 1, "A");
            pump.Enqueue(2, 1, "B");
            pump.Pump(now: 0);

            Assert.That(results[1], Is.EqualTo("A"));
            Assert.That(results[2], Is.EqualTo("B"));
            Assert.That(results[3], Is.EqualTo("C"));
            Assert.That(pump.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_DuplicateResultForSameSeed_DispatchedOnlyOnce()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            int calls = 0;
            pump.Register(1, (code, data) => calls++, now: 0);

            pump.Enqueue(1, 1, "a");
            pump.Enqueue(1, 1, "a"); // 重复结果
            pump.Pump(now: 0);

            Assert.That(calls, Is.EqualTo(1), "同一 seed 只派发一次");
        }

        [Test]
        public void CancelAll_FiresDisposedCodeForAllPending_AndClears()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            var disposedSeeds = new List<long>();
            int wrongCode = 0;
            for (int s = 1; s <= 3; s++)
            {
                long seed = s;
                pump.Register(seed, (code, data) =>
                {
                    if (code != RelayCode.Disposed) wrongCode++;
                    disposedSeeds.Add(seed);
                }, now: 0);
            }

            pump.CancelAll();

            Assert.That(disposedSeeds, Is.EquivalentTo(new[] { 1L, 2L, 3L }), "所有未完成请求都应收到 Disposed 码");
            Assert.That(wrongCode, Is.EqualTo(0), "码应为 RelayCode.Disposed");
            Assert.That(pump.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_Timeout_FiresTimeoutCode_AndRemoves()
        {
            var pump = new RelayPump(timeoutSeconds: 1.0);
            int gotCode = 0; bool called = false;
            pump.Register(1, (code, data) => { gotCode = code; called = true; }, now: 0);

            pump.Pump(now: 2.0); // now=2 > start0 + timeout1 → 过期

            Assert.That(called, Is.True, "应触发超时回调");
            Assert.That(gotCode, Is.EqualTo(RelayCode.Timeout));
            Assert.That(pump.PendingCount, Is.EqualTo(0), "超时项被移除");
        }

        [Test]
        public void Pump_ResultArrivesBeforeTimeout_NoTimeout()
        {
            var pump = new RelayPump(timeoutSeconds: 1.0);
            int gotCode = -99;
            pump.Register(1, (code, data) => gotCode = code, now: 0);

            pump.Enqueue(1, 1, "ok");
            pump.Pump(now: 0.5); // 结果在超时前到达

            Assert.That(gotCode, Is.EqualTo(1), "应是成功码，不是超时");
        }
    }
}
