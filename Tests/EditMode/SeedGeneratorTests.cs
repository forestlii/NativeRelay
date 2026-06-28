using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    // 纯逻辑测试：dotnet 快测 + Unity EditMode 共享此文件（只用约束模型 Assert.That，NUnit3/4 通用，无 UnityEngine 依赖）。
    public sealed class SeedGeneratorTests
    {
        [Test]
        public void Next_FirstSeedIsOne_ZeroReservedAsInvalid()
        {
            var gen = new SeedGenerator();
            Assert.That(gen.Next(), Is.EqualTo(1L), "首个 seed 应为 1，0 保留为无效哨兵");
        }

        [Test]
        public void Next_SingleThread_StrictlyIncreasing()
        {
            var gen = new SeedGenerator();
            long prev = gen.Next();
            for (int i = 0; i < 1000; i++)
            {
                long cur = gen.Next();
                Assert.That(cur, Is.GreaterThan(prev), "单线程下严格递增");
                prev = cur;
            }
        }

        [Test]
        public void Next_Concurrent_AllUniqueAndContiguous()
        {
            const int threads = 8;
            const int perThread = 10000;
            var gen = new SeedGenerator();
            var bags = new List<long>[threads];

            Parallel.For(0, threads, t =>
            {
                var local = new List<long>(perThread);
                for (int i = 0; i < perThread; i++) local.Add(gen.Next());
                bags[t] = local;
            });

            var all = new HashSet<long>();
            foreach (var bag in bags)
                foreach (var s in bag)
                    Assert.That(all.Add(s), Is.True, "并发取号必须无重复");

            // 不变量：总数 == 线程数*每线程，且取值恰好是 1..N 连续区间（守恒，不写死魔法数）
            int total = threads * perThread;
            Assert.That(all.Count, Is.EqualTo(total), "总数守恒");
            long min = long.MaxValue, max = long.MinValue;
            foreach (var s in all) { if (s < min) min = s; if (s > max) max = s; }
            Assert.That(min, Is.EqualTo(1L));
            Assert.That(max, Is.EqualTo((long)total), "Interlocked 自增应产出 1..N 连续");
        }
    }
}
