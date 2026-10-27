using System.Collections.Generic;
using KSA.Core.Medals;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class MedalEvaluatorTests
    {
        private MedalCatalog _catalog;
        private CrewRegistry _registry;
        private MedalEvaluator _evaluator;

        [SetUp]
        public void SetUp()
        {
            _catalog = ShippedConfigs.Medals();
            _registry = new CrewRegistry();
            _evaluator = new MedalEvaluator(_catalog, _registry);
        }

        private CrewProfile Crew(string name)
        {
            var p = new CrewProfile(name, name) { Role = CrewRole.Pilot };
            _registry.Add(p);
            return p;
        }

        [Test]
        public void ShippedMedalsConfig_ParsesAllSeven()
        {
            var warnings = new List<string>();
            MedalCatalog c = ShippedConfigs.Medals(warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(c.Count, Is.EqualTo(7));
            Assert.That(c.Get("ChiefsCommendation").Trigger, Is.EqualTo(MedalTrigger.Manual));
            Assert.That(c.Get("ChiefsCommendation").PerYearLimit, Is.EqualTo(2));
            Assert.That(c.Get("MemorialStar").Posthumous, Is.True);
            Assert.That(c.Get("OrbitalPioneer").FirstInAgency, Is.True);
        }

        [Test]
        public void OrbitalPioneer_OnlyFirstInAgency()
        {
            CrewProfile jeb = Crew("Jeb");
            CrewProfile val = Crew("Val");
            jeb.Record.Achievements.Add("Orbit:Kerbin");
            val.Record.Achievements.Add("Orbit:Kerbin");

            Assert.That(Ids(_evaluator.Evaluate(jeb, 10)), Is.EqualTo(new[] { "OrbitalPioneer" }));
            Assert.That(Ids(_evaluator.Evaluate(val, 11)), Is.Empty);
            Assert.That(jeb.Record.Medals[0].Note, Is.EqualTo("First in the agency"));
        }

        [Test]
        public void MunWalker_NeedsLandingAndEva()
        {
            CrewProfile p = Crew("Bob");
            p.Record.Achievements.Add("Land:Mun");
            Assert.That(Ids(_evaluator.Evaluate(p, 1)), Is.Empty);
            p.Record.Achievements.Add("EVA:Mun");
            Assert.That(Ids(_evaluator.Evaluate(p, 2)), Is.EqualTo(new[] { "MunWalker" }));
            Assert.That(Ids(_evaluator.Evaluate(p, 3)), Is.Empty, "not awarded twice");
        }

        [Test]
        public void IronKerbal_AtHundredDays()
        {
            CrewProfile p = Crew("Bill");
            p.Record.DaysInSpace = 99.9;
            Assert.That(Ids(_evaluator.Evaluate(p, 1)), Is.Empty);
            p.Record.DaysInSpace = 100;
            Assert.That(Ids(_evaluator.Evaluate(p, 1)), Is.EqualTo(new[] { "IronKerbal" }));
        }

        [Test]
        public void Survivor_RequiresAlive_MemorialStar_RequiresDead()
        {
            CrewProfile p = Crew("Dun");
            p.Record.Incidents.Add(new IncidentEntry { Kind = IncidentKind.Crash });
            Assert.That(Ids(_evaluator.Evaluate(p, 1)), Is.EqualTo(new[] { "Survivor" }));

            p.Record.Incidents.Add(new IncidentEntry { Kind = IncidentKind.Death });
            p.Status = CrewStatus.Dead;
            Assert.That(Ids(_evaluator.Evaluate(p, 2)), Is.EqualTo(new[] { "MemorialStar" }));
        }

        [Test]
        public void Lifesaver_FromRescueIncident()
        {
            CrewProfile p = Crew("Hero");
            p.Record.Incidents.Add(new IncidentEntry { Kind = IncidentKind.Rescue });
            Assert.That(Ids(_evaluator.Evaluate(p, 1)), Is.EqualTo(new[] { "Lifesaver" }));
        }

        private static List<string> Ids(List<MedalDefinition> medals)
        {
            var ids = new List<string>();
            foreach (MedalDefinition m in medals) ids.Add(m.Id);
            return ids;
        }
    }
}
