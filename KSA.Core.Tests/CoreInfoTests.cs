using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class CoreInfoTests
    {
        [Test]
        public void Version_IsThreePartVersionString()
        {
            string v = CoreInfo.Version;

            Assert.That(System.Version.TryParse(v, out var parsed), Is.True, "'" + v + "' should parse as a version");
            Assert.That(v.Split('.').Length, Is.EqualTo(3));
            Assert.That(parsed.Major, Is.GreaterThanOrEqualTo(0));
        }
    }
}
