using System.Collections.Generic;
using KSA.Core.Goals;
using KSA.Core.Medals;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Custom missions (KSA-59), exploration medals (KSA-60), resupply and the campaign ladder (KSA-61).</summary>
    public class CampaignTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Val Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist, 2)
            });
            a.Tick();
            a.Ledger.SetBalance(2000000);
            return a;
        }

        [Test]
        public void CustomMissionIsPricedDefinedAndSaved()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            var spec = new CustomMissionSpec { Body = "Kerbin", Profile = MissionProfile.Orbit, Orbits = 2, CrewSize = 1, Science = true };
            Assert.That(a.ValidateCustom(spec), Is.Null);
            Assert.That(a.ValidateCustom(new CustomMissionSpec { Body = "Kerbin", Profile = MissionProfile.Land }), Does.Contain("Suborbital or Orbit"));
            MissionType preview = a.PreviewCustom(spec);
            Assert.That(preview.Name, Is.EqualTo("2 orbits of Kerbin"));
            Assert.That(preview.Orbits, Is.EqualTo(2));
            Assert.That(preview.IsScience, Is.True);
            Assert.That(preview.PrimaryAchievement, Is.EqualTo("Science:Kerbin"));
            Assert.That(preview.BaseCost, Is.GreaterThan(15000));

            string msg;
            MissionType defined = a.DefineCustomMission(spec, out msg);
            Assert.That(defined, Is.Not.Null, msg);
            Assert.That(a.TemplatesFor(defined).Count, Is.GreaterThan(0), "custom orbit missions borrow the Kerbin orbit templates");
            Project p = a.CreateProject("Two laps", defined.Id, null, new[] { "Jeb Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);

            Agency b = Staffed(new FakeClock());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            MissionType back = b.Catalogs.Missions.Get(defined.Id);
            Assert.That(back, Is.Not.Null, "custom types travel with the save");
            Assert.That(back.IsCustom, Is.True);
            Assert.That(back.Orbits, Is.EqualTo(2));
            Assert.That(b.GetProject(p.Id).MissionTypeId, Is.EqualTo(defined.Id));
        }

        [Test]
        public void ExplorationMedalsFollowTheRecord()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            CrewProfile jeb = a.Crew.Get("Jeb Kerman");
            jeb.Record.FarthestKm = 12000;
            jeb.Record.Achievements.Add("Flyby:Mun"); jeb.Record.Achievements.Add("Orbit:Minmus"); jeb.Record.Achievements.Add("Land:Duna");
            jeb.Record.Achievements.Add("Flyby:Jool"); jeb.Record.Achievements.Add("Orbit:Eeloo"); jeb.Record.Achievements.Add("Orbit:Kerbin");
            Assert.That(jeb.Record.WorldsVisited("Kerbin"), Is.EqualTo(5));
            jeb.Record.DaysOnOtherWorlds = 31;
            jeb.Record.LongestMissionDays = 210;
            List<MedalDefinition> granted = a.MedalRules.Evaluate(jeb, a.CurrentDay);
            var ids = granted.ConvertAll(m => m.Id);
            Assert.That(ids, Does.Contain("FarTraveller1"));
            Assert.That(ids, Does.Not.Contain("FarTraveller2"));
            Assert.That(ids, Does.Contain("OffworldVeteran"));
            Assert.That(ids, Does.Contain("GrandTourist"));
            Assert.That(ids, Does.Contain("LongHaul"));

            // A simulated Mun landing fills the record from the body table.
            string msg;
            Project p = a.CreateProject("Mun", "MunLanding", null, new[] { "Val Kerman", "Bob Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            a.Rng = new System.Random(2);
            for (int i = 0; i < 80 && p.Report == null; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(p.Report, Is.Not.Null);
            if (p.Report.IsSuccess)
            {
                CrewProfile val = a.Crew.Get("Val Kerman");
                Assert.That(val.Record.FarthestKm, Is.EqualTo(12000));
                Assert.That(val.Record.DaysOnOtherWorlds, Is.GreaterThan(0));
            }
        }

        [Test]
        public void ResupplyRotatesCrewAndCampaignGoalsFollowTheOrder()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Program g = a.CreateProgram("Station Alpha", "KerbinStation", "Kerbin", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            a.Rng = new System.Random(9);
            int guard = 0;
            while (g.Status == ProgramStatus.Active && guard++ < 80)
            {
                clock.AdvanceDays(10); a.Tick();
                if (g.Status == ProgramStatus.Stalled) a.ResumeProgram(g.Id, out msg);
            }
            Assert.That(g.Status, Is.EqualTo(ProgramStatus.Complete), string.Join(" | ", g.Notes));
            Assert.That(a.HasAgencyAchievement("Base:Kerbin"), Is.True, "a finished station is the Base:Kerbin milestone");
            Assert.That(a.ResupplyTargets(), Has.Some.Matches<Program>(x => x.Id == g.Id));

            // Simulated modules leave nobody aboard; put a crew up so the rotation has someone to relieve.
            g.CrewAboard.Add("Bob Kerman");
            CrewProfile bob = a.Crew.Get("Bob Kerman");
            bob.InFlightSinceUT = a.Clock.UniversalTime; bob.Status = CrewStatus.Assigned;
            Assert.That(a.AvailableFor(CrewRole.Scientist), Has.None.Matches<CrewProfile>(c => c.Id == "Bob Kerman"));

            Project r = a.CreateResupply(g.Id, new[] { "Jeb Kerman", "Val Kerman" }, MissionMode.Simulate, out msg);
            Assert.That(r, Is.Not.Null, msg);
            Assert.That(a.ResupplyInFlight(g.Id), Is.True);
            for (int i = 0; i < 60 && r.Report == null; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(r.Report, Is.Not.Null);
            if (r.Report.IsSuccess)
            {
                Assert.That(g.CrewAboard, Is.EquivalentTo(new[] { "Jeb Kerman", "Val Kerman" }), "relief crew stays aboard");
                Assert.That(a.Crew.Get("Bob Kerman").IsFlying, Is.False, "the old crew came home");
                Assert.That(a.HasAgencyAchievement("Resupply:Kerbin"), Is.True);
                Assert.That(a.SupplyStatus(g), Does.StartWith("supplied"));
            }

            // Campaign order: the Mun landing goal is withheld until Orbit:Mun is on the books.
            GoalDefinition land = a.Catalogs.Goals.Get("MunLandGoal");
            Assert.That(land.Requires, Is.EqualTo("Orbit:Mun"));
            var picked = GoalSelector.Pick(a.Catalogs.Goals, 30, pattern => pattern == "Orbit:Kerbin", new HashSet<string>(), new HashSet<string>(), new System.Random(1));
            Assert.That(picked.ConvertAll(x => x.Id), Does.Contain("MunOrbitGoal"));
            Assert.That(picked.ConvertAll(x => x.Id), Does.Not.Contain("MunLandGoal"));
        }

        [Test]
        public void OverdueProgramsWearTheCrewDown()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Program g = a.CreateProgram("Station Beta", "KerbinStation", "Kerbin", null, out msg);
            g.Status = ProgramStatus.Complete;
            g.LastSuppliedDay = 0;
            g.CrewAboard.Add("Bob Kerman");
            int before = a.Crew.Get("Bob Kerman").Morale;
            clock.AdvanceDays(430);
            List<string> notices = a.Tick();
            Assert.That(notices, Has.Some.Contains("overdue for resupply"));
            Assert.That(a.Crew.Get("Bob Kerman").Morale, Is.LessThan(before));
        }
    }
}
