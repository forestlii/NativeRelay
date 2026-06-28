using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    public sealed class RelayCodeTests
    {
        [Test]
        public void ReservedCodes_AreAtIntExtreme_AndDistinct()
        {
            // 放在 int.MinValue 区，业务码（1/0/10086…）几乎不会撞。
            Assert.That(RelayCode.Timeout, Is.EqualTo(int.MinValue));
            Assert.That(RelayCode.Disposed, Is.EqualTo(int.MinValue + 1));
            Assert.That(RelayCode.Timeout, Is.Not.EqualTo(RelayCode.Disposed));
        }
    }
}
