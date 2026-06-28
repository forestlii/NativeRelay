using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Likeon.NativeRelay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Likeon.NativeRelay.Tests.PlayMode
{
    // PlayMode：验证「派发真的发生在主线程」与一一对应——Unity 运行时里才能证。
    public sealed class MainThreadDispatcherPlayModeTests
    {
        [UnityTest]
        public IEnumerator Concurrent100_OutOfOrder_AllDispatchedOnMainThread_OneToOne()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var dispatcher = MainThreadDispatcher.Instance;
            var ch = new MockChannel(minDelayMs: 1, maxDelayMs: 15);
            var bridge = dispatcher.CreateBridge(ch, timeoutSeconds: 60, capacity: 128);

            const int n = 100;
            int dispatched = 0, misrouted = 0, offMain = 0;
            var seen = new HashSet<long>();

            for (int i = 1; i <= n; i++)
            {
                long captured = 0;
                captured = bridge.Request(
                    command: 1,
                    payload: null,
                    onResult: (code, data) =>
                    {
                        if (Thread.CurrentThread.ManagedThreadId != mainThreadId) offMain++;
                        if (code != 1 || data != captured.ToString()) misrouted++;
                        if (!seen.Add(captured)) misrouted++;
                        dispatched++;
                    });
            }

            // dispatcher.Update 每帧自动 Pump；按墙钟时间等待直到排干（batchmode 帧太快，不能用帧数计时）。
            double startRt = Time.realtimeSinceStartupAsDouble;
            while (bridge.PendingCount > 0 && (Time.realtimeSinceStartupAsDouble - startRt) < 20.0)
            {
                yield return null;
            }

            try
            {
                Assert.That(offMain, Is.EqualTo(0), "全部 onResult 必须在主线程被调用");
                Assert.That(misrouted, Is.EqualTo(0), "seed↔data 一一对应、无重复派发");
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
