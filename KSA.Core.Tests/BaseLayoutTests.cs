using System.Collections.Generic;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>2026-10-30: surface bases grow on the ground with every module, resupply and, once a colony, on their own.</summary>
    [TestFixture]
    public class BaseLayoutTests
    {
        [Test]
        public void NothingStandsUntilAModuleIsDelivered()
        {
            Assert.That(BaseLayout.Sections(new List<string>(), 3, 0), Is.Empty);
        }

        [Test]
        public void EachDeliveredModuleAddsItsSectionAndTheReturnRocketStaysARocket()
        {
            List<BaseSection> s = BaseLayout.Sections(new[] { "core", "habitation", "power", "lab", "return", "industry" }, 0, 0);
            Assert.That(s, Is.EqualTo(new[] { BaseSection.Core, BaseSection.Habitat, BaseSection.Power, BaseSection.Lab, BaseSection.Industry }));
            Assert.That(BaseLayout.Key(s), Is.EqualTo("CHPLI"));
        }

        [Test]
        public void ResuppliesAddHabitatsAndGreenhousesUpToSixThenOnlyAColonyGrows()
        {
            var core = new[] { "core" };
            Assert.That(BaseLayout.Key(BaseLayout.Sections(core, 3, 0)), Is.EqualTo("CHGH"));
            Assert.That(BaseLayout.Sections(core, 10, 0).Count, Is.EqualTo(1 + BaseLayout.ResupplySections), "resupplies past six add nothing");
            int grown = BaseLayout.Sections(core, 6, 3 * BaseLayout.RoomPerHabitat).Count;
            Assert.That(grown, Is.EqualTo(1 + 6 + 3), "a colony adds a habitat per few residents of room it built");
            Assert.That(BaseLayout.Sections(core, 6, 1000).Count, Is.EqualTo(BaseLayout.MaxSections));
        }

        [Test]
        public void TheBaseSiteAndLayoutAreSaved()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            var g = new Program { Id = "G1", Name = "Mun Base Alpha", ProgramTypeId = "MunBase", Body = "Mun", SiteLat = 1.25, SiteLon = -40.5, BaseVesselId = "12345", BaseLayoutKey = "CH|x" };
            a.Programs.Add(g);
            Agency b = Make.Agency(new FakeClock(), new ListLogger());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Program h = b.GetProgram("G1");
            Assert.That(h.SiteLat, Is.EqualTo(1.25).Within(1e-9));
            Assert.That(h.SiteLon, Is.EqualTo(-40.5).Within(1e-9));
            Assert.That(h.BaseVesselId, Is.EqualTo("12345"));
            Assert.That(h.BaseLayoutKey, Is.EqualTo("CH|x"));
            var fresh = new Program();
            Assert.That(double.IsNaN(fresh.SiteLat), Is.True, "no site until one is chosen");
        }
    }
}
