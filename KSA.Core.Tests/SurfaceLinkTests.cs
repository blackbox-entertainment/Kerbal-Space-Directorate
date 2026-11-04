using KSA.Core.Data;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-90: the surface hose between landed craft.</summary>
    [TestFixture]
    public class SurfaceLinkTests
    {
        [Test]
        public void Share_is_bounded_by_the_rate_the_room_and_the_donor_reserve()
        {
            // A full 1,000-unit donor with a 5% reserve gives the pump's 15 a step.
            Assert.That(SurfaceLinkRules.Share(1000, 1000, 0.05, 2000, 15), Is.EqualTo(15).Within(1e-9));
            // Down to 60 it has 10 above the reserve.
            Assert.That(SurfaceLinkRules.Share(60, 1000, 0.05, 2000, 15), Is.EqualTo(10).Within(1e-9));
            // At the reserve it gives nothing.
            Assert.That(SurfaceLinkRules.Share(40, 1000, 0.05, 2000, 15), Is.EqualTo(0));
            // The receiver's room caps it.
            Assert.That(SurfaceLinkRules.Share(1000, 1000, 0.05, 4, 15), Is.EqualTo(4).Within(1e-9));
        }

        [Test]
        public void Rules_parse_from_a_node_and_default_without_one()
        {
            SurfaceLinkRules d = SurfaceLinkRules.Parse(null);
            Assert.That(d.Enabled, Is.True);
            Assert.That(d.RangeMetres, Is.EqualTo(100));
            Assert.That(d.LandingOffset, Is.EqualTo(90));

            var node = new DataNode(SurfaceLinkRules.NodeName);
            node.AddValue("range", "150");
            node.AddValue("pumpUnitsPerSecond", "5");
            node.AddValue("chargeReserveFraction", "0.25");
            node.AddValue("landingOffset", "80");
            SurfaceLinkRules r = SurfaceLinkRules.Parse(node);
            Assert.That(r.RangeMetres, Is.EqualTo(150));
            Assert.That(r.PumpUnitsPerSecond, Is.EqualTo(5));
            Assert.That(r.ChargeReserveFraction, Is.EqualTo(0.25));
            Assert.That(r.LandingOffset, Is.EqualTo(80));
        }

        [Test]
        public void Shipped_agency_cfg_carries_the_block()
        {
            SurfaceLinkRules r = ShippedConfigs.Settings().SurfaceLink;
            Assert.That(r.Enabled, Is.True);
            Assert.That(r.RangeMetres, Is.EqualTo(100));
        }
    }
}
