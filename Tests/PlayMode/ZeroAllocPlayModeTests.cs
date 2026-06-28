using System;
using Likeon.NativeRelay;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
// Unity 的零分配约束 AllocatingGCMemory 是 ConstraintExpression 上的扩展方法（来自上面命名空间）；
// 其 Is 与 NUnit 的 Is 冲突，故本文件用别名把裸 Is 指向 Unity 的（本文件不用 NUnit 的 Is.EqualTo 等）。
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Likeon.NativeRelay.Tests.PlayMode
{
    // PlayMode 零 GC 佐证（Unity 运行时侧）：稳态成功/超时路径不产生 GC 分配。
    // 关键：用<b>同一个</b>委托先 warmup 再测——Mono 的 Is.Not.AllocatingGCMemory 单次测量对首次 JIT/容量增长敏感，
    // 先把它跑热（撑大字典/列表容量 + JIT），再断言这同一个委托稳态零分配。
    public sealed class ZeroAllocPlayModeTests
    {
        [Test]
        public void RelayPump_SteadySuccessPath_DoesNotAllocateGC()
        {
            var pump = new RelayPump(timeoutSeconds: 1000, capacity: 512);
            Action<int, string> onResult = (c, d) => { };
            const string data = "x";
            long seed = 0;

            TestDelegate body = () =>
            {
                for (int i = 0; i < 64; i++) { seed++; pump.Register(seed, onResult, 0); pump.Enqueue(seed, 1, data); }
                pump.Pump(0);
            };

            for (int r = 0; r < 16; r++) body(); // warmup 同一委托：撑容量 + JIT
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void RelayPump_SteadyTimeoutPath_DoesNotAllocateGC()
        {
            var pump = new RelayPump(timeoutSeconds: 1.0, capacity: 512);
            Action<int, string> onResult = (c, d) => { };
            long seed = 0;
            double t = 0;

            TestDelegate body = () =>
            {
                for (int i = 0; i < 64; i++) pump.Register(++seed, onResult, t);
                t += 10;
                pump.Pump(t); // 全部过期
            };

            for (int r = 0; r < 16; r++) body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
