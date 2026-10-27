using System.Collections.Generic;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class CareerCatalogTests
    {
        [Test]
        public void ShippedCareersConfig_ParsesCleanly()
        {
            var warnings = new List<string>();
            CareerCatalog catalog = ShippedConfigs.Careers(warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(catalog.TrackCount, Is.EqualTo(4));

            CareerTrackDefinition flight = catalog.GetTrack(CareerTrack.Flight);
            Assert.That(flight.Ranks.Count, Is.EqualTo(6));
            Assert.That(flight.Ranks[0].Id, Is.EqualTo("TestPilot"));
            Assert.That(flight.Ranks[1].DirectorSelected, Is.True);
            Assert.That(flight.Ranks[2].RequiredAchievements, Is.EqualTo(new[] { "Orbit:*" }));
            Assert.That(flight.Ranks[5].MaxSlots, Is.EqualTo(1));

            Assert.That(catalog.GetTrack(CareerTrack.Engineering).Ranks.Count, Is.EqualTo(4));
            Assert.That(catalog.GetTrack(CareerTrack.Science).Ranks.Count, Is.EqualTo(4));
            Assert.That(catalog.GetTrack(CareerTrack.Management).EntryRoles, Is.Empty);
        }

        [Test]
        public void TrackForRole_MapsStockRoles()
        {
            CareerCatalog catalog = ShippedConfigs.Careers();
            Assert.That(catalog.TrackForRole(CrewRole.Pilot), Is.EqualTo(CareerTrack.Flight));
            Assert.That(catalog.TrackForRole(CrewRole.Engineer), Is.EqualTo(CareerTrack.Engineering));
            Assert.That(catalog.TrackForRole(CrewRole.Scientist), Is.EqualTo(CareerTrack.Science));
            Assert.That(catalog.TrackForRole(CrewRole.Tourist), Is.EqualTo(CareerTrack.None));
        }

        [Test]
        public void Parse_SkipsMalformedNodesWithWarnings()
        {
            var nodes = new List<DataNode>
            {
                new DataNode("KSA_CAREER_TRACK").AddValue("id", "Bogus"),
                new DataNode("KSA_CAREER_TRACK").AddValue("id", "Flight"),            // no ranks
                new DataNode("SOMETHING_ELSE").AddValue("id", "Flight"),
            };
            var good = new DataNode("KSA_CAREER_TRACK").AddValue("id", "Science").AddValue("roles", "Scientist, Wizard");
            good.AddChild("RANK").AddValue("id", "B").AddValue("order", "1");
            good.AddChild("RANK").AddValue("id", "A").AddValue("order", "0");
            good.AddChild("RANK");                                                   // no id
            nodes.Add(good);

            var warnings = new List<string>();
            CareerCatalog catalog = CareerCatalog.Parse(nodes, warnings);

            Assert.That(catalog.TrackCount, Is.EqualTo(1));
            Assert.That(catalog.GetTrack(CareerTrack.Science).Ranks[0].Id, Is.EqualTo("A"), "ranks sorted by order");
            Assert.That(warnings.Count, Is.EqualTo(4), string.Join(" | ", warnings));
        }
    }
}
