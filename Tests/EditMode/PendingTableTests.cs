using System;
using System.Collections.Generic;
using NativeRelay;
using NUnit.Framework;

namespace NativeRelay.Tests
{
    public sealed class PendingTableTests
    {
        private static PendingContext Ctx(double start)
        {
            return new PendingContext(_ => { }, _ => { }, start);
        }

        [Test]
        public void Register_ThenComplete_ReturnsCtxAndRemoves()
        {
            var t = new PendingTable();
            t.Register(5, Ctx(0));
            Assert.That(t.Count, Is.EqualTo(1));

            Assert.That(t.TryComplete(5, out var ctx), Is.True);
            Assert.That(ctx.StartTime, Is.EqualTo(0d));
            Assert.That(t.Count, Is.EqualTo(0), "匹配后必须移除");

            Assert.That(t.TryComplete(5, out _), Is.False, "同一 seed 不得被完成两次");
        }

        [Test]
        public void Complete_UnknownSeed_ReturnsFalse()
        {
            var t = new PendingTable();
            Assert.That(t.TryComplete(999, out _), Is.False);
        }

        [Test]
        public void ScanTimeouts_RemovesExpired_KeepsFresh_StrictlyGreater()
        {
            var t = new PendingTable();
            t.Register(1, Ctx(0.0));   // 发起于 0s
            t.Register(2, Ctx(1.0));   // 发起于 1s
            t.Register(3, Ctx(2.0));   // 发起于 2s

            var firedSeeds = new List<long>();
            Action<long, PendingContext> onTimeout = (seed, _) => firedSeeds.Add(seed);

            // now=3.0, timeout=1.5 → 过期判定 (now-start)>1.5：
            //   seed1: 3-0=3.0 >1.5 过期；seed2: 3-1=2.0 >1.5 过期；seed3: 3-2=1.0 不过期
            t.ScanTimeouts(now: 3.0, timeout: 1.5, onTimeout: onTimeout);

            Assert.That(firedSeeds, Is.EquivalentTo(new[] { 1L, 2L }), "应只挑出过期项");
            Assert.That(t.Count, Is.EqualTo(1), "过期项被移除，仅留 seed3");
            Assert.That(t.TryComplete(3, out _), Is.True, "未过期的 seed3 仍可正常完成");
        }

        [Test]
        public void ScanTimeouts_BoundaryEqualsTimeout_NotExpired()
        {
            var t = new PendingTable();
            t.Register(1, Ctx(0.0));
            var fired = new List<long>();
            // now-start == timeout（恰好相等）不算过期（严格大于才过期）
            t.ScanTimeouts(now: 2.0, timeout: 2.0, onTimeout: (s, _) => fired.Add(s));
            Assert.That(fired, Is.Empty, "恰好等于 timeout 不应过期");
            Assert.That(t.Count, Is.EqualTo(1));
        }

        [Test]
        public void ScanTimeouts_NoExpired_DoesNotFire()
        {
            var t = new PendingTable();
            t.Register(1, Ctx(10.0));
            bool fired = false;
            t.ScanTimeouts(now: 10.5, timeout: 1.0, onTimeout: (_, __) => fired = true);
            Assert.That(fired, Is.False);
            Assert.That(t.Count, Is.EqualTo(1));
        }
    }
}
