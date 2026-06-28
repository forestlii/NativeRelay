using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NativeRelay;
using NUnit.Framework;

namespace NativeRelay.Tests
{
    // 端到端集成（逻辑层）：Bridge + 真实 MockChannel，100 并发乱序回来，循环 Pump 直到排干。
    // 验证 M1 核心契约：全部正确派发、seed↔payload 一一对应零错配、无丢失/重复、pending 归零。
    // （PlayMode 版本再补：派发发生在主线程 + 稳态零 GC。）
    public sealed class BridgeMockIntegrationTests
    {
        private static long DecodeSeed(byte[] bytes) => BitConverter.ToInt64(bytes, 0);

        [Test]
        public void Concurrent100_OutOfOrder_EachSeedMapsToItsOwnResult_PendingDrains()
        {
            const int n = 100;
            var sw = Stopwatch.StartNew();
            using var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 15);
            // 真实墙钟时钟；超时设大避免误判。回调在 Pump（本测试线程=主线程角色）里同步执行，故计数无需加锁。
            var bridge = new Bridge(ch, () => sw.Elapsed.TotalSeconds, timeoutSeconds: 60, capacity: 128);

            int dispatched = 0;
            int misrouted = 0;
            int errors = 0;
            var seenSeeds = new HashSet<long>();

            for (int i = 1; i <= n; i++)
            {
                long captured = 0; // 迭代内局部：闭包按引用捕获，Pump 触发回调时已被赋为本请求 seed
                captured = bridge.Request(
                    command: 1,
                    payload: null,
                    onResult: payload =>
                    {
                        // MockChannel 把原生侧收到的 seed 回显进 payload；
                        // 本回调登记在 captured（本请求 seed）名下 → decoded 必须 == captured，否则即错配。
                        long decoded = DecodeSeed(payload);
                        if (decoded != captured) misrouted++;
                        if (!seenSeeds.Add(captured)) misrouted++; // 同一请求被派发两次也算异常
                        dispatched++;
                    },
                    onError: _ => errors++);
            }

            // 循环 Pump 直到把 n 个结果全部排干，或超时保护。
            var deadline = sw.Elapsed + TimeSpan.FromSeconds(15);
            while (bridge.PendingCount > 0 && sw.Elapsed < deadline)
            {
                bridge.Pump();
                Thread.Sleep(1);
            }
            bridge.Pump(); // 收尾再排一次

            Assert.That(errors, Is.EqualTo(0), "不应有超时/失败");
            Assert.That(misrouted, Is.EqualTo(0), "seed↔payload 必须一一对应、无重复派发");
            Assert.That(dispatched, Is.EqualTo(n), "守恒：派发数 == 请求数（不丢不重）");
            Assert.That(seenSeeds.Count, Is.EqualTo(n), "n 个不同请求各被派发一次");
            Assert.That(bridge.PendingCount, Is.EqualTo(0), "pending 全部排干，无泄漏");
        }
    }
}
