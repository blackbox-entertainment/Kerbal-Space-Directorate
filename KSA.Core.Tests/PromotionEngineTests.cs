using KSA.Core.Careers;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class PromotionEngineTests
    {
        private CareerCatalog _catalog;
        private CrewRegistry _registry;
        private PromotionEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _catalog = ShippedConfigs.Careers();
            _registry = new CrewRegistry();
            _engine = new PromotionEngine(_catalog, _registry);
        }

        private CrewProfile Pilot(string name, int stars = 0)
        {
            var p = new CrewProfile(name, name) { Role = CrewRole.Pilot, Stars = stars };
            _registry.Add(p);
            _engine.AssignStartingRank(p);
            return p;
        }

        [Test]
        public void NewPilot_StartsAsTestPilot_WithSalary()
        {
            CrewProfile p = Pilot("Jeb");
            Assert.That(p.Track, Is.EqualTo(CareerTrack.Flight));
            Assert.That(p.RankId, Is.EqualTo("TestPilot"));
            Assert.That(p.Salary, Is.EqualTo(8000));
        }

        [Test]
        public void NewEngineerAndScientist_StartAtBottomOfTheirTracks()
        {
            var e = new CrewProfile("Bill", "Bill") { Role = CrewRole.Engineer };
            var s = new CrewProfile("Bob", "Bob") { Role = CrewRole.Scientist };
            _engine.AssignStartingRank(e);
            _engine.AssignStartingRank(s);
            Assert.That(e.RankId, Is.EqualTo("JuniorEngineer"));
            Assert.That(s.RankId, Is.EqualTo("ResearchAssistant"));
        }

        [Test]
        public void Tourist_GetsNoTrack()
        {
            var t = new CrewProfile("Tour", "Tour") { Role = CrewRole.Tourist };
            Assert.That(_engine.AssignStartingRank(t), Is.False);
            Assert.That(t.Track, Is.EqualTo(CareerTrack.None));
        }

        [Test]
        public void TestPilot_IsNotPromotedAutomatically_EvenWithStars()
        {
            CrewProfile p = Pilot("Val", stars: 5);
            p.Record.Achievements.Add("Orbit:Kerbin");

            Assert.That(_engine.PromoteAutomatically(p), Is.Empty);
            Assert.That(p.RankId, Is.EqualTo("TestPilot"));
            Assert.That(_engine.CanSelect(p), Is.True, "Candidate is a Director decision");
        }

        [Test]
        public void DirectorSelection_ThenAutomaticClimb()
        {
            CrewProfile p = Pilot("Val", stars: 3);
            p.Record.Achievements.Add("Orbit:Kerbin");
            for (int i = 0; i < 3; i++) p.Record.Missions.Add(new MissionEntry { MissionId = "M" + i, Outcome = MissionOutcome.Success });

            RankDefinition selected = _engine.Select(p);
            Assert.That(selected.Id, Is.EqualTo("AstronautCandidate"));

            var granted = _engine.PromoteAutomatically(p);
            Assert.That(granted.Count, Is.EqualTo(2), "Astronaut (orbit) then Senior Astronaut (3 stars, 3 missions)");
            Assert.That(p.RankId, Is.EqualTo("SeniorAstronaut"));
            Assert.That(p.Salary, Is.EqualTo(18000));
        }

        [Test]
        public void Astronaut_RequiresOrbitAchievement()
        {
            CrewProfile p = Pilot("Val", stars: 5);
            _engine.Select(p);
            Assert.That(_engine.PromoteAutomatically(p), Is.Empty, "no orbit yet");
            p.Record.Achievements.Add("Orbit:Mun");
            Assert.That(_engine.PromoteAutomatically(p).Count, Is.EqualTo(1));
            Assert.That(p.RankId, Is.EqualTo("Astronaut"));
        }

        [Test]
        public void SlotCap_OnlyOneChiefAstronaut()
        {
            CrewProfile first = FullyQualified("A");
            CrewProfile second = FullyQualified("B");

            _engine.PromoteAutomatically(first);
            _engine.PromoteAutomatically(second);

            Assert.That(first.RankId, Is.EqualTo("ChiefAstronaut"));
            Assert.That(second.RankId, Is.EqualTo("MissionCommander"), "cap of one Chief Astronaut");
        }

        [Test]
        public void DeadCrew_AreNeverPromoted()
        {
            CrewProfile p = FullyQualified("Dead");
            p.Status = CrewStatus.Dead;
            Assert.That(_engine.PromoteAutomatically(p), Is.Empty);
        }

        private CrewProfile FullyQualified(string name)
        {
            CrewProfile p = Pilot(name, stars: 5);
            p.Record.Achievements.Add("Orbit:Kerbin");
            for (int i = 0; i < 10; i++) p.Record.Missions.Add(new MissionEntry { MissionId = name + i, Outcome = MissionOutcome.Success });
            _engine.Select(p);
            return p;
        }
    }
}
