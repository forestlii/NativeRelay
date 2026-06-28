using System;
using Likeon.NativeRelay;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
// Unity 的零分配约束 AllocatingGCMemory 是 ConstraintExpression 上的扩展方法（来自上面命名空间）；
// 其 Is 与 NUnit 的 Is 冲突，故本文件用别名把裸 Is 指向 Unity 的（本文件不用 NUnit 的 Is.EqualTo 等）。
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Likeon.NativeRelay.Tests.PlayMode
{
    // PlayMode 零 GC 佐证（Unity 运行时侧）：稳态成功路径 Register+Enqueue+Pump 不产生 GC 分配。
    // 逻辑正确性已由 dotnet/EditMode 覆盖；这里专测 Unity 运行时下的零 GC 硬指标。
    public sealed class ZeroAllocPlayModeTests
    {
        [Test]
        public void RelayPump_SteadySuccessPath_DoesNotAllocateGC()
        {
            var pump = new RelayPump(timeoutSeconds: 1000, capacity: 256);
            Action<byte[]> onResult = _ => { };
            Action<BridgeError> onError = _ => { };
            byte[] payload = new byte[8];
            long seed = 0;

            // warmup：撑大字典/列表容量到稳态，避免扩容分配混入测量
            for (int r = 0; r < 8; r++)
            {
                for (int i = 0; i < 128; i++)
                {
                    seed++;
                    pump.Register(seed, onResult, onError, 0);
                    pump.Enqueue(seed, payload);
                }
                pump.Pump(0);
            }

            // 稳态一轮：Register + Enqueue + Pump 成功派发，必须零 GC 分配。
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    seed++;
                    pump.Register(seed, onResult, onError, 0);
                    pump.Enqueue(seed, payload);
                }
                pump.Pump(0);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void RelayPump_SteadyTimeoutPath_DoesNotAllocateGC()
        {
            var pump = new RelayPump(timeoutSeconds: 1.0, capacity: 256);
            Action<byte[]> onResult = _ => { };
            Action<BridgeError> onError = _ => { };
            long seed = 0;
            double t = 0;

            // warmup：撑大容量；每轮登记一批（不喂结果），Pump 时已越过 timeout → 全部走超时清理
            for (int r = 0; r < 8; r++)
            {
                for (int i = 0; i < 128; i++) pump.Register(++seed, onResult, onError, t);
                t += 10;
                pump.Pump(t);
            }

            // 稳态超时清理：BridgeError 是值类型、扫描用字典结构体枚举器 → 应零 GC
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++) pump.Register(++seed, onResult, onError, t);
                t += 10;
                pump.Pump(t);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
