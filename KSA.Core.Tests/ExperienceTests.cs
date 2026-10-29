using System.Collections.Generic;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-63: every mission pays experience; stars follow the ladder and never fall below the game's level.</summary>
    [TestFixture]
    public class ExperienceTests
    {
        [Test]
        public void LadderMapsPointsToStars()
        {
            var rules = new ExperienceRules();
            Assert.That(rules.StarsFor(0), Is.EqualTo(0));
            Assert.That(rules.StarsFor(4), Is.EqualTo(1));
            Assert.That(rules.StarsFor(27.9), Is.EqualTo(2));
            Assert.That(rules.StarsFor(110), Is.EqualTo(5));
            Assert.That(rules.StarsFor(500), Is.EqualTo(5));
            Assert.That(rules.ToNextStar(5), Is.EqualTo(7));
            Assert.That(rules.ToNextStar(200), Is.EqualTo(0));
        }

        [Test]
        public void ConfigLadderIsParsedFromAgencyCfg()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            Assert.That(a.Settings.Experience.Enabled, Is.True);
            Assert.That(a.Settings.Experience.StarThresholds, Is.EqualTo(new[] { 4.0, 12, 28, 60, 110 }));
            Assert.That(a.Settings.Experience.EarlyPoints, Is.EqualTo(2));
        }

        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 0), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 0) });
            a.Tick();
            a.Ledger.SetBalance(900000);
            a.LiveFlightAvailable = true;
            return a;
        }

        [Test]
        public void RepeatedHopsEarnStarsAndTheGameLevelStaysAFloor()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            CrewProfile jeb = a.Crew.Get("Jeb Kerman");
            for (int i = 0; i < 3; i++)
            {
                string msg;
                Project p = a.AdoptHangarLaunch("SuborbitalHop", "v" + i, "Hopper", new List<string> { "Jeb Kerman" }, out msg);
                Assert.That(p, Is.Not.Null, msg);
                var result = new LiveFlightResult { Recovered = true, MaxAltitude = 90000, MaxSpeed = 1200, DaysInSpace = 0.1 };
                result.Achievements.Add("Flight:Kerbin");
                result.Achievements.Add("Suborbit:Kerbin");
                result.Achievements.Add("Recover:-");
                MissionReport r = a.ResolveLive(p.Id, result);
                Assert.That(r.IsSuccess, Is.True);
            }
            // Early tier 2 x 1.5 = 3 per success, the first one also claimed a First (+4): 7 + 3 + 3 = 13 -> two stars.
            Assert.That(jeb.Record.Experience, Is.EqualTo(13).Within(0.01));
            Assert.That(a.LadderStars(jeb), Is.EqualTo(2));
            Assert.That(jeb.Stars, Is.EqualTo(2));

            // The game still says 0 stars: the ladder wins. The game says 4: the game wins.
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 0), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 0) });
            Assert.That(jeb.Stars, Is.EqualTo(2));
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 4), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 0) });
            Assert.That(jeb.Stars, Is.EqualTo(4));
        }

        [Test]
        public void ExperienceSurvivesSaveAndLoad()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.Crew.Get("Jeb Kerman").Record.Experience = 31.5;
            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Assert.That(b.Crew.Get("Jeb Kerman").Record.Experience, Is.EqualTo(31.5));
        }

        [Test]
        public void TheLeadEngineerIsPaidWhenTheDesignFlies()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            CrewProfile bill = a.Crew.Get("Bill Kerman");
            string msg;
            Project p = a.AdoptHangarLaunch("SuborbitalHop", "v1", "Hopper", new List<string> { "Jeb Kerman" }, out msg);
            Assert.That(p, Is.Not.Null, msg);
            p.LeadEngineer = bill.Id;
            var result = new LiveFlightResult { Recovered = true, MaxAltitude = 90000, MaxSpeed = 1200, DaysInSpace = 0.1 };
            result.Achievements.Add("Flight:Kerbin");
            result.Achievements.Add("Suborbit:Kerbin");
            result.Achievements.Add("Recover:-");
            Assert.That(a.ResolveLive(p.Id, result).IsSuccess, Is.True);
            // Early 2 x 1.5 = 3, plus the First bonus 4: the same 7 the pilot earned, at designLead = 1.
            Assert.That(bill.Record.Experience, Is.EqualTo(7).Within(0.01));
            Assert.That(bill.Record.DesignsFlown, Is.EqualTo(1));
            Assert.That(KSA.Core.Careers.PromotionEngine.MissionCredit(bill), Is.EqualTo(1));
        }

        [Test]
        public void LabYearsPayExperienceAndCountTowardScienceRanks()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Bob Kerman", CrewRole.Scientist, 0) });
            a.Tick();
            CrewProfile bob = a.Crew.Get("Bob Kerman");
            string msg;
            Assert.That(a.SetLab(bob.Id, true, out msg), Is.True, msg);
            clock.AdvanceDays(213);
            a.Tick();
            Assert.That(bob.Record.Experience, Is.EqualTo(1.5).Within(0.01));
            Assert.That(bob.Record.LabYears, Is.EqualTo(0));
            clock.AdvanceDays(213);
            a.Tick();
            Assert.That(bob.Record.Experience, Is.EqualTo(3).Within(0.01));
            Assert.That(bob.Record.LabYears, Is.EqualTo(1));
            Assert.That(KSA.Core.Careers.PromotionEngine.MissionCredit(bob), Is.EqualTo(1));

            // Leaving the lab pays the days so far and stops the clock.
            clock.AdvanceDays(71);
            Assert.That(a.SetLab(bob.Id, false, out msg), Is.True, msg);
            Assert.That(bob.Record.Experience, Is.EqualTo(3.5).Within(0.01));
            clock.AdvanceDays(200);
            a.Tick();
            Assert.That(bob.Record.Experience, Is.EqualTo(3.5).Within(0.01));

            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Assert.That(b.Crew.Get("Bob Kerman").Record.LabYears, Is.EqualTo(1));
            Assert.That(b.Crew.Get("Bob Kerman").Record.LabDays, Is.EqualTo(497).Within(0.01));
        }

        [Test]
        public void GroundWorkCountsOnlyForItsOwnTrack()
        {
            var c = new CrewProfile("x", "X Kerman") { Track = CareerTrack.Flight };
            c.Record.DesignsFlown = 3;
            c.Record.LabYears = 2;
            Assert.That(KSA.Core.Careers.PromotionEngine.MissionCredit(c), Is.EqualTo(0));
            c.Track = CareerTrack.Engineering;
            Assert.That(KSA.Core.Careers.PromotionEngine.MissionCredit(c), Is.EqualTo(3));
            c.Track = CareerTrack.Science;
            Assert.That(KSA.Core.Careers.PromotionEngine.MissionCredit(c), Is.EqualTo(2));
        }
    }
}
