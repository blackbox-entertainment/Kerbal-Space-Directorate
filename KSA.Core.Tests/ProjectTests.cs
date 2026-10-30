using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Project pipeline, simulated resolution, programs, and v3 persistence (M3).</summary>
    [TestFixture]
    public class ProjectTests
    {
        private FakeClock _clock;
        private ListLogger _log;
        private Agency _agency;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            _log = new ListLogger();
            _agency = Make.Agency(_clock, _log);
            _agency.SyncRoster(new[]
            {
                Make.Snapshot("Jebediah Kerman", CrewRole.Pilot, stars: 2),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, stars: 1),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist),
                Make.Snapshot("Valentina Kerman", CrewRole.Pilot, stars: 2),
            });
            _agency.Tick();
            _agency.Settings.DefaultMissionMode = DefaultMissionMode.Simulate;
        }

        [Test]
        public void CreateProject_ChargesBudget_AssignsStaff_AndPicksBestStockTemplate()
        {
            double before = _agency.Ledger.Balance;
            string msg;
            Project p = _agency.CreateProject("Jeb's hop", "SuborbitalHop", null, new[] { "Jebediah Kerman" }, new[] { "Bill Kerman" }, null, null, out msg);

            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.TemplateId, Is.EqualTo("StockKerbal2"), "best stock template by design score");
            Assert.That(p.Budget, Is.EqualTo(8000));
            Assert.That(_agency.Ledger.Balance, Is.EqualTo(before - 8000));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Design));
            Assert.That(p.LeadEngineer, Is.EqualTo("Bill Kerman"));
            Assert.That(_agency.Crew.Get("Jebediah Kerman").ProjectId, Is.EqualTo(p.Id));
            Assert.That(_agency.Crew.Get("Bill Kerman").ProjectId, Is.EqualTo(p.Id));
            Assert.That(_agency.AvailableFor(CrewRole.Pilot), Has.Count.EqualTo(1), "only Valentina is free now");

            Project dup = _agency.CreateProject("again", "SuborbitalHop", null, new[] { "Jebediah Kerman" }, null, null, null, out msg);
            Assert.That(dup, Is.Null);
            Assert.That(msg, Does.Contain("already assigned"));
        }

        [Test]
        public void CreateProject_RejectsWrongCrewCountAndExperience_AndUnaffordable()
        {
            string msg;
            Assert.That(_agency.CreateProject("x", "MunOrbit", null, new[] { "Jebediah Kerman" }, null, null, null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("needs 2"));
            Assert.That(_agency.CreateProject("x", "DunaLanding", null, new[] { "Jebediah Kerman", "Valentina Kerman", "Bob Kerman" }, null, null, null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("experience"));
            _agency.Ledger.SetBalance(100);
            Assert.That(_agency.CreateProject("x", "SuborbitalHop", null, null, null, null, null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("Not enough funds"));
        }

        [Test]
        public void Pipeline_DesignBuildReadyLaunchResolve_AsDaysPass()
        {
            string msg;
            Project p = _agency.CreateProject("Orbit 1", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            int designDays = p.DesignDays, buildDays = p.BuildDays;

            _clock.AdvanceDays(designDays);
            _agency.Tick();
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Build));

            _clock.AdvanceDays(buildDays);
            List<string> notices = _agency.Tick();
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.InFlight), "Ready auto-launches in Simulate mode");
            Assert.That(p.MissionId, Is.Not.Empty);
            Assert.That(_agency.Crew.Get(p.Crew[0]).Record.CurrentMission, Is.Not.Null);
            Assert.That(notices, Has.Some.Contains("launched"));

            _clock.AdvanceDays(2);
            notices = _agency.Tick();
            Assert.That(p.IsActive, Is.False);
            Assert.That(p.Report, Is.Not.Null);
            Assert.That(_agency.Reports.Count, Is.EqualTo(1));
            CrewProfile pilot = _agency.Crew.Get(p.Crew[0]);
            Assert.That(pilot.ProjectId, Is.Empty, "staff released");
            Assert.That(pilot.Record.CurrentMission, Is.Null, "mission closed");
            Assert.That(pilot.Record.Missions[0].Outcome, Is.EqualTo(p.Report.Outcome));
            if (p.Report.IsSuccess) Assert.That(pilot.Record.HasAchievement("Orbit:Kerbin"), Is.True);
            Assert.That(pilot.Record.DaysInSpace, Is.EqualTo(p.Report.DaysInSpace).Within(1e-9));
        }

        [Test]
        public void HybridDefault_FliesAFirstLive_ThenFallsBackWhenLiveIsUnavailable()
        {
            _agency.Settings.DefaultMissionMode = DefaultMissionMode.Hybrid;
            Assert.That(_agency.DefaultModeFor(_agency.Catalogs.Missions.Get("KerbinOrbit")), Is.EqualTo(MissionMode.WatchLive));
            _agency.AddAchievements("Jebediah Kerman", new[] { "Orbit:Kerbin" });
            Assert.That(_agency.DefaultModeFor(_agency.Catalogs.Missions.Get("KerbinOrbit")), Is.EqualTo(MissionMode.WatchLive), "held, but never flown live (2026-10-30)");
            var flown = new MissionReport { MissionTypeId = "KerbinOrbit", Mode = MissionMode.WatchLive, Outcome = MissionOutcome.Success };
            flown.Achievements.Add("Suborbit:Kerbin"); flown.Achievements.Add("Orbit:Kerbin");
            _agency.Reports.Add(flown);
            Assert.That(_agency.DefaultModeFor(_agency.Catalogs.Missions.Get("KerbinOrbit")), Is.EqualTo(MissionMode.Simulate), "already flown live");

            string msg;
            Project p = _agency.CreateProject("Hop", "SuborbitalHop", null, null, null, null, null, out msg);
            Assert.That(p.Mode, Is.EqualTo(MissionMode.WatchLive));
            _clock.AdvanceDays(p.DesignDays + p.BuildDays);
            List<string> notices = _agency.Tick();
            Assert.That(p.Mode, Is.EqualTo(MissionMode.Simulate), "no live flight yet, so it simulates");
            Assert.That(notices, Has.Some.Contains("not available"));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.InFlight));
        }

        [Test]
        public void CancelProject_RefundsHalf_ReleasesStaff()
        {
            string msg;
            Project p = _agency.CreateProject("Hop", "SuborbitalHop", null, null, null, null, null, out msg);
            double before = _agency.Ledger.Balance;
            Assert.That(_agency.CancelProject(p.Id, out msg), Is.True);
            Assert.That(_agency.Ledger.Balance, Is.EqualTo(before + 4000));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Cancelled));
            Assert.That(_agency.Crew.Get(p.Crew[0]).ProjectId, Is.Empty);
            Assert.That(_agency.CancelProject(p.Id, out msg), Is.False);
        }

        [Test]
        public void Program_RunsModulesInOrder_AndCompletes()
        {
            _agency.Ledger.SetBalance(2000000);
            // Make success near-certain so the program runs through: top crew, top morale.
            foreach (CrewProfile c in _agency.Crew.All) { c.Stars = 5; c.Morale = 100; c.Courage = 0.8f; c.Stupidity = 0.1f; }
            _agency.Rng = new System.Random(3);

            string msg;
            Program g = _agency.CreateProgram("Station Alpha", "KerbinStation", "Kerbin", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            Assert.That(g.ModuleProjectIds.Count, Is.EqualTo(1), "first module started");
            Assert.That(_agency.GetProject(g.ModuleProjectIds[0]).ModuleRole, Is.EqualTo("core"));

            int guard = 0;
            while (g.Status == ProgramStatus.Active && guard++ < 60)
            {
                _clock.AdvanceDays(10);
                _agency.Tick();
                if (g.Status == ProgramStatus.Stalled)
                {
                    string why;
                    _agency.ResumeProgram(g.Id, out why); // retry a failed module
                }
            }
            Assert.That(g.Status, Is.EqualTo(ProgramStatus.Complete), string.Join(" | ", g.Notes));
            Assert.That(g.ModuleProjectIds.Count, Is.EqualTo(4));
            Assert.That(_agency.Reports.Count, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void SurfaceBase_PicksASiteOnce_AndUsesItForEveryModule()
        {
            _agency.Ledger.SetBalance(1000000);
            string msg;
            Program g = _agency.CreateProgram("Mun Base Alpha", "MunBase", "Mun", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            Assert.That(g.Site, Does.Contain("on Mun"));
            Assert.That(g.SiteSlope, Is.InRange(0.0, 0.5));
            Assert.That(_agency.CreateProgram("x", "MunBase", "Duna", null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("cannot be built at Duna"));
        }

        [Test]
        public void SaveAndLoad_V3_RoundTripsProjectsProgramsReports()
        {
            _agency.Ledger.SetBalance(500000);
            string msg;
            Project p = _agency.CreateProject("Orbit 1", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Program g = _agency.CreateProgram("Station Alpha", "KerbinStation", "Kerbin", null, out msg);
            _clock.AdvanceDays(p.DesignDays + p.BuildDays + 3);
            _agency.Tick();
            Assert.That(p.Report, Is.Not.Null);

            var store = new MemoryPersistence();
            _agency.Save(store);
            string text = DataNodeText.Write(store.Stored);
            Agency reloaded = Make.Agency(_clock, _log);
            Assert.That(reloaded.Load(new MemoryPersistence { Stored = DataNodeText.ParseAll(text)[0] }), Is.True);

            Assert.That(reloaded.Projects.Count, Is.EqualTo(_agency.Projects.Count));
            Project rp = reloaded.GetProject(p.Id);
            Assert.That(rp.Status, Is.EqualTo(p.Status));
            Assert.That(rp.Report, Is.Not.Null);
            Assert.That(rp.Report.Outcome, Is.EqualTo(p.Report.Outcome));
            Assert.That(rp.Report.Lines, Is.EqualTo(p.Report.Lines));
            Assert.That(rp.Crew, Is.EqualTo(p.Crew));
            Program rg = reloaded.GetProgram(g.Id);
            Assert.That(rg.ModuleProjectIds, Is.EqualTo(g.ModuleProjectIds));
            Assert.That(rg.Status, Is.EqualTo(g.Status));
            Assert.That(reloaded.NextProjectNumber, Is.EqualTo(_agency.NextProjectNumber));
            foreach (CrewProfile c in _agency.Crew.All)
                Assert.That(reloaded.Crew.Get(c.Id).ProjectId, Is.EqualTo(c.ProjectId), c.Id);
            Assert.That(reloaded.Reports.Count, Is.EqualTo(_agency.Reports.Count));
        }
    }
}
