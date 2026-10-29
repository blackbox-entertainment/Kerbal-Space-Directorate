using System.Collections.Generic;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Science missions (KSA-58): a mission family flown for the data, paid by who flew it.</summary>
    public class ScienceMissionTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist, 3)
            });
            a.Tick();
            a.Ledger.SetBalance(400000);
            return a;
        }

        [Test]
        public void ShippedScienceMissionsParseAndLadderIncludesThem()
        {
            MissionCatalog missions = ShippedConfigs.Missions();
            MissionType survey = missions.Get("KerbinBiomeSurvey");
            Assert.That(survey, Is.Not.Null);
            Assert.That(survey.IsScience, Is.True);
            Assert.That(survey.ScienceYield, Is.EqualTo(25));
            Assert.That(survey.PrimaryAchievement, Is.EqualTo("Science:Kerbin"));
            Assert.That(missions.Get("SuborbitalHop").IsScience, Is.False);
            Assert.That(Rivals.MilestoneLadder.Describe("Science:Mun"), Does.Contain("science"));
        }

        [Test]
        public void ScientistsMultiplyTheYield()
        {
            Agency a = Staffed(new FakeClock());
            Assert.That(a.ScienceCrewMultiplier(new[] { "Jeb Kerman" }), Is.EqualTo(1.05).Within(0.001), "a pilot alone adds little");
            Assert.That(a.ScienceCrewMultiplier(new[] { "Bob Kerman" }), Is.EqualTo(2.6).Within(0.001), "a 3-star scientist: 1 + 1 + 0.6");
            Assert.That(a.ScienceCrewMultiplier(new[] { "Jeb Kerman", "Bob Kerman" }), Is.EqualTo(2.65).Within(0.001));
        }

        [Test]
        public void SimulatedScienceMissionCreditsTheYield()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            var earned = new List<double>();
            a.ScienceEarned += (pts, why) => earned.Add(pts);
            string msg;
            Project p = a.CreateProject("Survey", "KerbinBiomeSurvey", null, new[] { "Bob Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            a.Rng = new System.Random(5);
            for (int i = 0; i < 60 && p.Report == null; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(p.Report, Is.Not.Null);
            if (p.Report.Outcome == MissionOutcome.Success)
            {
                Assert.That(p.Report.Achievements, Does.Contain("Science:Kerbin"));
                Assert.That(earned[earned.Count - 1], Is.EqualTo(25 * 2.6).Within(0.6), "yield x scientist multiplier");
            }
        }

        [Test]
        public void LiveScienceMissionNeedsDataAboardToEarnThePrimary()
        {
            MissionType survey = ShippedConfigs.Missions().Get("KerbinBiomeSurvey");
            var crew = new List<string> { "Bob Kerman" };
            var dry = new LiveFlightResult { Recovered = true };
            dry.Achievements.Add("Suborbit:Kerbin");
            Assert.That(LiveFlightResolver.Resolve(null, survey, crew, dry, 1, 0.6, 0.6).Outcome, Is.EqualTo(MissionOutcome.Abort), "flew but brought nothing back: the main objective was missed (2026-10-28)");
            var full = new LiveFlightResult { Recovered = true, SciencePoints = 18.5 };
            full.Achievements.Add("Suborbit:Kerbin");
            MissionReport r = LiveFlightResolver.Resolve(null, survey, crew, full, 1, 0.6, 0.6);
            Assert.That(r.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(r.Achievements, Does.Contain("Science:Kerbin"));
        }
    }
}
