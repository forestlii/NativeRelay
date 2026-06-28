using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    public sealed class BridgeErrorTests
    {
        [Test]
        public void Factories_SetKindAndSeed()
        {
            var t = BridgeError.Timeout(42, "no reply");
            Assert.That(t.Kind, Is.EqualTo(BridgeErrorKind.Timeout));
            Assert.That(t.Seed, Is.EqualTo(42L));
            Assert.That(t.Message, Is.EqualTo("no reply"));

            var c = BridgeError.ChannelFailure(7);
            Assert.That(c.Kind, Is.EqualTo(BridgeErrorKind.ChannelFailure));
            Assert.That(c.Seed, Is.EqualTo(7L));
            Assert.That(c.Message, Is.Null);

            var d = BridgeError.Disposed(9);
            Assert.That(d.Kind, Is.EqualTo(BridgeErrorKind.Disposed));
        }

        [Test]
        public void IsValueType_DefaultIsZeroSeed()
        {
            Assert.That(typeof(BridgeError).IsValueType, Is.True, "必须是值类型，派发不装箱");
            BridgeError def = default;
            Assert.That(def.Seed, Is.EqualTo(0L));
        }

        [Test]
        public void ToString_IncludesKindAndSeed()
        {
            var s = BridgeError.Timeout(123).ToString();
            Assert.That(s, Does.Contain("Timeout"));
            Assert.That(s, Does.Contain("123"));
        }
    }
}
