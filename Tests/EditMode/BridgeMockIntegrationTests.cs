using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    // 端到端集成（逻辑层）：Bridge + 真实 MockChannel。默认结果 (code=1, data=seed字符串)。
    public sealed class BridgeMockIntegrationTests
    {
        [Test]
        public void Concurrent100_OutOfOrder_EachSeedMapsToItsOwnResult_PendingDrains()
        {
            const int n = 100;
            var sw = Stopwatch.StartNew();
            using var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 15);
            var bridge = new Bridge(ch, () => sw.Elapsed.TotalSeconds, timeoutSeconds: 60, capacity: 128);

            int dispatched = 0;
            int misrouted = 0;
            var seen = new HashSet<long>();

            for (int i = 1; i <= n; i++)
            {
                long captured = 0;
                captured = bridge.Request(
                    command: 1,
                    payload: null,
                    onResult: (code, data) =>
                    {
                        if (code != 1 || data != captured.ToString()) misrouted++; // data 回显本请求 seed
                        if (!seen.Add(captured)) misrouted++;
                        dispatched++;
                    });
            }

            var deadline = sw.Elapsed + TimeSpan.FromSeconds(15);
            while (bridge.PendingCount > 0 && sw.Elapsed < deadline)
            {
                bridge.Pump();
                Thread.Sleep(1);
            }
            bridge.Pump();

            Assert.That(misrouted, Is.EqualTo(0), "seed↔data 一一对应、无重复派发");
            Assert.That(dispatched, Is.EqualTo(n), "守恒：派发数 == 请求数");
            Assert.That(seen.Count, Is.EqualTo(n));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void PartialDrop_DroppedRequestsTimeout_OthersSucceed_PendingDrains()
        {
            const int n = 40;
            var sw = Stopwatch.StartNew();
            // 偶数 seed 被丢弃（原生层永不回）→ 应走 RelayCode.Timeout；奇数正常回 code=1。
            using var ch = new MockChannel(
                minDelayMs: 1, maxDelayMs: 15,
                shouldDrop: seed => seed % 2 == 0);
            var bridge = new Bridge(ch, () => sw.Elapsed.TotalSeconds, timeoutSeconds: 0.25, capacity: 64);

            var successSeeds = new HashSet<long>();
            var timeoutSeeds = new HashSet<long>();
            int wrong = 0;

            for (int i = 1; i <= n; i++)
            {
                long captured = 0;
                captured = bridge.Request(
                    command: 1,
                    payload: null,
                    onResult: (code, data) =>
                    {
                        if (code == 1)
                        {
                            if (data != captured.ToString()) wrong++;
                            successSeeds.Add(captured);
                        }
                        else if (code == RelayCode.Timeout)
                        {
                            timeoutSeeds.Add(captured);
                        }
                        else wrong++;
                    });
            }

            var deadline = sw.Elapsed + TimeSpan.FromSeconds(10);
            while (bridge.PendingCount > 0 && sw.Elapsed < deadline)
            {
                bridge.Pump();
                Thread.Sleep(2);
            }
            bridge.Pump();

            Assert.That(wrong, Is.EqualTo(0), "成功项 data 对应自身 seed；非成功项必是 Timeout");
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
            Assert.That(successSeeds.Count + timeoutSeeds.Count, Is.EqualTo(n));
            Assert.That(successSeeds.Overlaps(timeoutSeeds), Is.False);
            foreach (var s in successSeeds) Assert.That(s % 2, Is.EqualTo(1L), $"成功 seed {s} 应为奇数");
            foreach (var s in timeoutSeeds) Assert.That(s % 2, Is.EqualTo(0L), $"超时 seed {s} 应为偶数");
        }

        [Test]
        public void Stress_1000Requests_SubThreadFlood_MainConsume_NoLossNoDup()
        {
            const int n = 1000;
            var sw = Stopwatch.StartNew();
            using var ch = new MockChannel(minDelayMs: 0, maxDelayMs: 3);
            var bridge = new Bridge(ch, () => sw.Elapsed.TotalSeconds, timeoutSeconds: 60, capacity: 256);

            int dispatched = 0;
            int misrouted = 0;
            var seen = new HashSet<long>();

            for (int i = 1; i <= n; i++)
            {
                long captured = 0;
                captured = bridge.Request(1, null, (code, data) =>
                {
                    if (data != captured.ToString()) misrouted++;
                    if (!seen.Add(captured)) misrouted++;
                    dispatched++;
                });
            }

            var deadline = sw.Elapsed + TimeSpan.FromSeconds(20);
            while (bridge.PendingCount > 0 && sw.Elapsed < deadline)
            {
                bridge.Pump();
                Thread.Sleep(1);
            }
            bridge.Pump();

            Assert.That(misrouted, Is.EqualTo(0));
            Assert.That(dispatched, Is.EqualTo(n));
            Assert.That(seen.Count, Is.EqualTo(n));
            Assert.That(bridge.PendingCount, Is.EqualTo(0));
        }
    }
}
