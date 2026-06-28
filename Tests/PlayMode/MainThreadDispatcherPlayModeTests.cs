using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NativeRelay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NativeRelay.Tests.PlayMode
{
    // PlayMode：验证「派发真的发生在主线程」与「稳态零 GC」——这两条机器只能在 Unity 运行时里证。
    // 逻辑层的一一对应/乱序/超时已由 dotnet EditMode 快测覆盖，这里只补 Unity 运行时特有的两条。
    public sealed class MainThreadDispatcherPlayModeTests
    {
        private static long DecodeSeed(byte[] bytes) => BitConverter.ToInt64(bytes, 0);

        [UnityTest]
        public IEnumerator Concurrent100_OutOfOrder_AllDispatchedOnMainThread_OneToOne()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var dispatcher = MainThreadDispatcher.Instance;
            var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 15);
            var bridge = dispatcher.CreateBridge(ch, timeoutSeconds: 60, capacity: 128);

            const int n = 100;
            int dispatched = 0, misrouted = 0, errors = 0, offMain = 0;
            var seen = new HashSet<long>();

            for (int i = 1; i <= n; i++)
            {
                long captured = 0;
                captured = bridge.Request(
                    command: 1,
                    payload: null,
                    onResult: payload =>
                    {
                        if (Thread.CurrentThread.ManagedThreadId != mainThreadId) offMain++;
                        if (DecodeSeed(payload) != captured) misrouted++;
                        if (!seen.Add(captured)) misrouted++;
                        dispatched++;
                    },
                    onError: _ => errors++);
            }

            // dispatcher.Update 每帧自动 Pump；按墙钟时间等待直到排干或保护上限。
            // 注意：batchmode 下帧推进极快，不能用帧数计时（会在 ThreadPool 回调排干前就超时）；
            // MockChannel 用线程池+Sleep，100 个回调需要真实墙钟时间才能全部回来，故用 realtime 门控。
            double startRt = Time.realtimeSinceStartupAsDouble;
            while (bridge.PendingCount > 0 && (Time.realtimeSinceStartupAsDouble - startRt) < 20.0)
            {
                yield return null;
            }

            try
            {
                Assert.That(errors, Is.EqualTo(0), "不应有超时/失败");
                Assert.That(offMain, Is.EqualTo(0), "全部 onResult 必须在主线程被调用");
                Assert.That(misrouted, Is.EqualTo(0), "seed↔payload 一一对应、无重复派发");
                Assert.That(dispatched, Is.EqualTo(n), "守恒：派发数 == 请求数");
                Assert.That(seen.Count, Is.EqualTo(n));
                Assert.That(bridge.PendingCount, Is.EqualTo(0), "pending 全部排干");
            }
            finally
            {
                bridge.Dispose();
                dispatcher.Unregister(bridge);
            }
        }
    }
}
