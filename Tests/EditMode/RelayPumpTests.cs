using System;
using System.Collections.Generic;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    public sealed class RelayPumpTests
    {
        private static byte[] B(params byte[] bytes) => bytes;

        [Test]
        public void EnqueueThenPump_DispatchesToRegisteredCallback_ThenPendingEmpty()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            byte[] got = null;
            pump.Register(1, payload => got = payload, _ => Assert.Fail("不应超时"), now: 0);

            pump.Enqueue(1, B(9, 8, 7));
            pump.Pump(now: 0);

            Assert.That(got, Is.EqualTo(B(9, 8, 7)), "应把 payload 派发给对应回调");
            Assert.That(pump.PendingCount, Is.EqualTo(0), "完成后从 pending 移除");
        }

        [Test]
        public void Pump_UnknownSeed_Ignored_NoThrow()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            pump.Enqueue(404, B(1)); // 从未注册
            Assert.DoesNotThrow(() => pump.Pump(now: 0));
            Assert.That(pump.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_OutOfOrderArrival_EachSeedMapsToItsOwnPayload()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            var results = new Dictionary<long, byte[]>();
            for (int s = 1; s <= 3; s++)
            {
                long seed = s;
                pump.Register(seed, payload => results[seed] = payload, _ => { }, now: 0);
            }

            // 乱序入队：3、1、2
            pump.Enqueue(3, B(30));
            pump.Enqueue(1, B(10));
            pump.Enqueue(2, B(20));
            pump.Pump(now: 0);

            Assert.That(results[1], Is.EqualTo(B(10)));
            Assert.That(results[2], Is.EqualTo(B(20)));
            Assert.That(results[3], Is.EqualTo(B(30)));
            Assert.That(pump.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Pump_DuplicateResultForSameSeed_DispatchedOnlyOnce()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            int calls = 0;
            pump.Register(1, _ => calls++, _ => { }, now: 0);

            pump.Enqueue(1, B(1));
            pump.Enqueue(1, B(1)); // 重复结果（如原生层误发两次）
            pump.Pump(now: 0);

            Assert.That(calls, Is.EqualTo(1), "同一 seed 只派发一次，重复结果被忽略");
        }

        [Test]
        public void Pump_Timeout_FiresOnError_NotOnResult_AndRemoves()
        {
            var pump = new RelayPump(timeoutSeconds: 1.0);
            bool resultCalled = false;
            BridgeError err = default;
            bool errCalled = false;
            pump.Register(1, _ => resultCalled = true, e => { err = e; errCalled = true; }, now: 0);

            // now=2 > start0 + timeout1 → 过期
            pump.Pump(now: 2.0);

            Assert.That(errCalled, Is.True, "应触发超时错误回调");
            Assert.That(err.Kind, Is.EqualTo(BridgeErrorKind.Timeout));
            Assert.That(err.Seed, Is.EqualTo(1L));
            Assert.That(resultCalled, Is.False, "超时后不得再走成功回调");
            Assert.That(pump.PendingCount, Is.EqualTo(0), "超时项被移除");
        }

        [Test]
        public void CancelAll_FiresDisposedForAllPending_AndClears()
        {
            var pump = new RelayPump(timeoutSeconds: 100);
            var disposedSeeds = new List<long>();
            int wrongKind = 0;
            for (int s = 1; s <= 3; s++)
            {
                long seed = s;
                pump.Register(seed, _ => Assert.Fail("不应成功"),
                    err =>
                    {
                        if (err.Kind != BridgeErrorKind.Disposed || err.Seed != seed) wrongKind++;
                        disposedSeeds.Add(seed);
                    }, now: 0);
            }

            pump.CancelAll();

            Assert.That(disposedSeeds, Is.EquivalentTo(new[] { 1L, 2L, 3L }), "所有未完成请求都应收到 Disposed");
            Assert.That(wrongKind, Is.EqualTo(0), "错误类别应为 Disposed 且 seed 对得上");
            Assert.That(pump.PendingCount, Is.EqualTo(0), "CancelAll 后 pending 清空");
        }

        [Test]
        public void Pump_ResultArrivesBeforeTimeout_NoTimeoutFired()
        {
            var pump = new RelayPump(timeoutSeconds: 1.0);
            bool errCalled = false;
            byte[] got = null;
            pump.Register(1, p => got = p, _ => errCalled = true, now: 0);

            pump.Enqueue(1, B(5));
            pump.Pump(now: 0.5); // 结果在超时前到达

            Assert.That(got, Is.EqualTo(B(5)));
            Assert.That(errCalled, Is.False, "已完成的请求不应再超时");
        }
    }
}
