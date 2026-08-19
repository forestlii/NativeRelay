using Likeon.NativeRelay;
using NUnit.Framework;

namespace Likeon.NativeRelay.Tests
{
    public sealed class RelayLogTests
    {
        private string _captured;

        [SetUp]
        public void SetUp()
        {
            _captured = null;
            RelayLog.Enabled = false;
            RelayLog.Sink = null;
        }

        [TearDown]
        public void TearDown()
        {
            // 静态开关/出口必须复位，不污染同进程其它测试
            RelayLog.Enabled = false;
            RelayLog.Sink = null;
        }

        [Test]
        public void Disabled_DoesNotWrite()
        {
            RelayLog.Sink = msg => _captured = msg;
            RelayLog.Enabled = false;

            RelayLog.Info("x");

            Assert.That(_captured, Is.Null);
        }

        [Test]
        public void Enabled_WritesWithFrameworkPrefix()
        {
            RelayLog.Sink = msg => _captured = msg;
            RelayLog.Enabled = true;

            RelayLog.Info("hello");

            Assert.That(_captured, Is.EqualTo("[NativeRelay] hello"));
        }

        [Test]
        public void Enabled_WithoutSink_IsSilentAndDoesNotThrow()
        {
            RelayLog.Enabled = true;

            Assert.DoesNotThrow(() => RelayLog.Info("x"));
        }
    }
}
