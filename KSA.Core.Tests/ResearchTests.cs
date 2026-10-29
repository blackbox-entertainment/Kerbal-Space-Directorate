using System.Collections.Generic;
using KSA.Core.Goals;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Career progression (M3 research decision, built 2026-10-27): missions and goals follow the researched parts; the lab and missions earn Science.</summary>
    public class ResearchTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2)
            });
            a.Tick();
            a.Ledger.SetBalance(400000);
            return a;
        }

        private static PartCatalog StartingParts()
        {
            var c = new PartCatalog();
            c.Add(new PartInfo { Name = "mk1pod_v2", Title = "Mk1 Command Pod", Kind = PartKind.Command, Mass = 0.84 });
            c.Add(new PartInfo { Name = "solidBooster_sm_v2", Title = "RT-5 Flea", Kind = PartKind.Booster, Mass = 0.45, FuelMass = 1.1 });
            c.Add(new PartInfo { Name = "parachuteSingle", Title = "Mk16 Parachute", Kind = PartKind.Parachute, Mass = 0.1 });
            return c;
        }

        [Test]
        public void StartingPartsAllowAHopButNotOrbit_AndGoalsFollow()
        {
            Agency a = Staffed(new FakeClock());
            a.Parts = StartingParts();
            // Only the starting-parts Flea hop can be built (the host would say which parts the others lack).
            a.TemplateCheck = t => t.Id == "StockFleaHop" ? null : "needs research: Swivel";
            MissionType hop = a.Catalogs.Missions.Get("FleaHop");
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            MissionType mun = a.Catalogs.Missions.Get("MunLanding");
            Assert.That(a.MissionBlocker(hop), Is.Null, "a pod, a booster and a chute fly a hop");
            Assert.That(a.MissionBlocker(orbit), Does.Contain("no design it can build").And.Contains("Swivel"), "locked when no design can be built (2026-10-28)");
            Assert.That(a.PartKindsBlocker(orbit), Does.Contain("liquid engine"));
            Assert.That(a.PartKindsBlocker(mun), Does.Contain("landing legs"));

            GoalDefinition reachSpace = null, achieveOrbit = null;
            foreach (GoalDefinition g in a.Catalogs.Goals.Goals) { if (g.Id == "ReachSpace") reachSpace = g; if (g.Id == "AchieveOrbit") achieveOrbit = g; }
            Assert.That(a.GoalFeasible(achieveOrbit), Is.False, "nothing researched can reach orbit");
            Assert.That(a.GoalFeasible(reachSpace), Is.False, "day-one boosters alone do not reach space: it needs staging (2026-10-28)");
            ((PartCatalog)a.Parts).Add(new PartInfo { Name = "Decoupler_1", Title = "TD-12 Decoupler", Kind = PartKind.Decoupler, Mass = 0.04 });
            Assert.That(a.GoalFeasible(reachSpace), Is.True, "with a decoupler a staged hop can reach space");

            string msg;
            Assert.That(a.CreateProject("Too soon", "KerbinOrbit", null, new[] { "Jeb Kerman" }, null, null, null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("no design it can build"));
        }

        [Test]
        public void ACrewLossFailsTheNoLossGoalAtOnce()
        {
            Agency a = Staffed(new FakeClock());
            var gs = new KSA.Core.Goals.GoalState { GoalId = "NoCrewLosses", YearIssued = 1 };
            Assert.That(a.GoalStatusText(gs), Is.EqualTo("[met]"));
            a.RecordDeath("Jeb Kerman", "test");
            Assert.That(a.GoalStatusText(gs), Is.EqualTo("[failed: 1 kerbal lost this year]"));
        }

        [Test]
        public void LabAndMissionsEarnScience()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            var earned = new List<double>();
            a.ScienceEarned += (pts, why) => earned.Add(pts);
            string msg;
            Assert.That(a.SetLab("Bob Kerman", true, out msg), Is.True, msg);
            Assert.That(a.AvailableFor(CrewRole.Scientist), Has.None.Matches<CrewProfile>(c => c.Id == "Bob Kerman"), "lab staff do not fly");
            Assert.That(a.ProjectedLabScience(), Is.EqualTo(2 + 4 * 3).Within(0.01));
            List<string> notices = a.ForceYearEnd();
            Assert.That(notices, Has.Some.Contains("Research lab delivered"));
            Assert.That(earned.Count, Is.EqualTo(1));
            Assert.That(a.State.ScienceEarned, Is.EqualTo(14).Within(0.01));

            Agency b = Staffed(new FakeClock());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.Crew.Get("Bob Kerman").InLab, Is.True);
            Assert.That(b.State.ScienceEarned, Is.EqualTo(14).Within(0.01));

            // A successful early mission returns Science too.
            Projects.Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            a.Rng = new System.Random(1);
            for (int i = 0; i < 40 && p.Report == null; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(p.Report, Is.Not.Null);
            if (p.Report.IsSuccess) Assert.That(earned.Count, Is.EqualTo(2), "mission science credited");
        }
    }
}
