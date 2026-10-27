using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class AgencyTests
    {
        private FakeClock _clock;
        private ListLogger _log;
        private Agency _agency;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            _log = new ListLogger();
            _agency = new Agency(ShippedConfigs.All(), _clock, _log);
        }

        private SyncResult SyncStarters()
        {
            return _agency.SyncRoster(new[]
            {
                Make.Snapshot("Jebediah Kerman", CrewRole.Pilot),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist),
                Make.Snapshot("Valentina Kerman", CrewRole.Pilot),
            });
        }

        [Test]
        public void SyncRoster_CreatesProfilesWithRanksAndAges()
        {
            SyncResult r = SyncStarters();

            Assert.That(r.Hired.Count, Is.EqualTo(4));
            Assert.That(_agency.Crew.Count, Is.EqualTo(4));
            CrewProfile jeb = _agency.Crew.Get("Jebediah Kerman");
            Assert.That(jeb.Track, Is.EqualTo(CareerTrack.Flight));
            Assert.That(jeb.RankId, Is.EqualTo("TestPilot"));
            Assert.That(jeb.HiredDay, Is.EqualTo(0));
            int age = jeb.AgeOn(_agency.CurrentDay, _clock.DaysPerYear);
            Assert.That(age, Is.InRange(Agency.MinHireAge, Agency.MaxHireAge));
            Assert.That(Agency.HireAgeFor("Jebediah Kerman"), Is.EqualTo(age), "age is deterministic per name");
            Assert.That(r.Notices, Has.Some.Contains("hired as Test Pilot"));
        }

        [Test]
        public void SyncRoster_SecondSync_IsIdempotent_AndMarksDepartures()
        {
            SyncStarters();
            SyncResult r = _agency.SyncRoster(new[]
            {
                Make.Snapshot("Jebediah Kerman", CrewRole.Pilot, stars: 2),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist),
            });

            Assert.That(r.Hired, Is.Empty);
            Assert.That(r.Departed.Count, Is.EqualTo(1));
            Assert.That(_agency.Crew.Get("Valentina Kerman").Departed, Is.True);
            Assert.That(_agency.Crew.Get("Jebediah Kerman").Stars, Is.EqualTo(2));
            Assert.That(_agency.Crew.Count, Is.EqualTo(4), "departed crew stay on file for the hall of fame");
        }

        [Test]
        public void Mission_BeginAndEnd_BooksDaysAndMedals()
        {
            SyncStarters();
            string id = _agency.BeginMission(new[] { "Jebediah Kerman" }, "Kerbal 1");
            Assert.That(_agency.Crew.Get("Jebediah Kerman").Status, Is.EqualTo(CrewStatus.Assigned));

            _clock.AdvanceDays(2.5);
            _agency.AddAchievements("Jebediah Kerman", new[] { "Launch:Kerbin", "Orbit:Kerbin" });
            List<string> notices = _agency.EndMission("Jebediah Kerman", MissionOutcome.Success);

            CrewProfile jeb = _agency.Crew.Get("Jebediah Kerman");
            Assert.That(jeb.Record.Missions.Count, Is.EqualTo(1));
            Assert.That(jeb.Record.Missions[0].MissionId, Is.EqualTo(id));
            Assert.That(jeb.Record.Missions[0].Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(jeb.Record.Missions[0].EndDay, Is.EqualTo(2));
            Assert.That(jeb.Record.DaysInSpace, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(jeb.IsFlying, Is.False);
            Assert.That(jeb.Status, Is.EqualTo(CrewStatus.Available));
            Assert.That(jeb.Record.HasMedal("OrbitalPioneer"), Is.True);
            Assert.That(jeb.RankId, Is.EqualTo("TestPilot"), "still needs Director selection to become a Candidate");
        }

        [Test]
        public void Death_ClosesMissionAndAwardsMemorialStar()
        {
            SyncStarters();
            _agency.BeginMission(new[] { "Valentina Kerman" }, "Doomed");
            _clock.AdvanceDays(1);
            List<string> notices = _agency.RecordDeath("Valentina Kerman", "Crashed on launch");

            CrewProfile val = _agency.Crew.Get("Valentina Kerman");
            Assert.That(val.Status, Is.EqualTo(CrewStatus.Dead));
            Assert.That(val.Record.Missions[0].Outcome, Is.EqualTo(MissionOutcome.LossOfCrew));
            Assert.That(val.Record.HasIncident(IncidentKind.Death), Is.True);
            Assert.That(val.Record.HasMedal("MemorialStar"), Is.True);
            Assert.That(notices, Has.Some.Contains("Memorial Star"));
        }

        [Test]
        public void SelectForRank_PromotesTestPilotToCandidate()
        {
            SyncStarters();
            Assert.That(_agency.SelectForRank("Jebediah Kerman").Id, Is.EqualTo("AstronautCandidate"));
            Assert.That(_agency.SelectForRank("Jebediah Kerman"), Is.Null, "nothing further pending");
            Assert.That(_agency.SelectForRank("Bill Kerman"), Is.Null, "engineers have no Director-selected rank");
        }

        [Test]
        public void SaveAndLoad_RoundTripsEverything()
        {
            SyncStarters();
            _agency.BeginMission(new[] { "Jebediah Kerman", "Bob Kerman" }, "Kerbal 1");
            _clock.AdvanceDays(3);
            _agency.AddAchievements("Jebediah Kerman", new[] { "Orbit:Kerbin" });
            _agency.EndMission("Jebediah Kerman", MissionOutcome.Success);
            _agency.RecordIncident("Bob Kerman", IncidentKind.Crash, "Hard landing");
            _agency.EndMission("Bob Kerman", MissionOutcome.PartialSuccess);
            _agency.SelectForRank("Jebediah Kerman");
            _agency.Crew.Get("Bill Kerman").PersonalityTags.Add("Cautious");
            _agency.Crew.Get("Bill Kerman").Record.Quotes.Add("It'll hold.");

            var store = new MemoryPersistence();
            _agency.Save(store);
            string text = DataNodeText.Write(store.Stored);

            var reloaded = new Agency(ShippedConfigs.All(), _clock, _log);
            Assert.That(reloaded.Load(new MemoryPersistence { Stored = DataNodeText.ParseAll(text)[0] }), Is.True);

            Assert.That(reloaded.Crew.Count, Is.EqualTo(4));
            CrewProfile jeb = reloaded.Crew.Get("Jebediah Kerman");
            Assert.That(jeb.RankId, Is.EqualTo("AstronautCandidate"));
            Assert.That(jeb.Record.DaysInSpace, Is.EqualTo(3).Within(1e-9));
            Assert.That(jeb.Record.Missions[0].Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(jeb.Record.HasMedal("OrbitalPioneer"), Is.True);
            Assert.That(jeb.Record.HasAchievement("Orbit:*"), Is.True);
            Assert.That(jeb.BornDay, Is.EqualTo(_agency.Crew.Get("Jebediah Kerman").BornDay));

            CrewProfile bob = reloaded.Crew.Get("Bob Kerman");
            Assert.That(bob.Record.Incidents[0].Kind, Is.EqualTo(IncidentKind.Crash));
            Assert.That(bob.Record.Incidents[0].Description, Is.EqualTo("Hard landing"));
            Assert.That(bob.Record.HasMedal("Survivor"), Is.True);

            CrewProfile bill = reloaded.Crew.Get("Bill Kerman");
            Assert.That(bill.PersonalityTags, Is.EqualTo(new[] { "Cautious" }));
            Assert.That(bill.Record.Quotes, Is.EqualTo(new[] { "It'll hold." }));
            Assert.That(reloaded.NextMissionNumber, Is.EqualTo(2));
        }

        [Test]
        public void Load_WithNoSavedState_ReturnsFalse_AndKeepsWorking()
        {
            Assert.That(_agency.Load(new MemoryPersistence()), Is.False);
            SyncStarters();
            Assert.That(_agency.Crew.Count, Is.EqualTo(4));
        }
    }
}
