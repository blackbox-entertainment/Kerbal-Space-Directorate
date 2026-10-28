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
    }
}
