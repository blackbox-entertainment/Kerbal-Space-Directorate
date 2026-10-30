using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>2026-10-30: a failed flight names its cause, and the engineers' next design for that mission fixes it.</summary>
    [TestFixture]
    public class LearningTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Val Kerman", CrewRole.Pilot, 2),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.LiveFlightAvailable = true;
            a.Parts = DesignerReachTests.StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            return a;
        }

        private static MissionReport Fly(Agency a, FakeClock clock, string typeId, LiveFlightResult result)
        {
            string msg;
            Project p = a.CreateProject("Live " + typeId, typeId, null, null, null, null, MissionMode.WatchLive, out msg);
            Assert.That(p, Is.Not.Null, msg);
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 1);
            a.Tick();
            Assert.That(a.LaunchLive(p.Id, out msg), Is.True, msg);
            return a.ResolveLive(p.Id, result);
        }

        [Test]
        public void ATumbleIsTheLessonAndTheNextHopGetsUpperStageFins()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            var crash = new LiveFlightResult { Crashed = true, MaxAltitude = 16500, SpaceAltitude = 70000, Cause = FailureCause.Tumbled, CauseDetail = "the upper stage tumbled at 16.2 km, 480 m/s (nose 120 deg off the flight path, spinning 992 deg/s)" };
            crash.Deaths.Add("Jeb Kerman");
            MissionReport r = Fly(a, clock, "SuborbitalHop", crash);

            Assert.That(r.Cause, Is.EqualTo(FailureCause.Tumbled));
            Assert.That(r.Lesson, Does.Contain("tumbled at 16.2 km").And.Contain("fins on the second stage"));
            Assert.That(a.LessonsLearned[a.LessonsLearned.Count - 1], Does.Contain("Cause: the upper stage tumbled"));
            Assert.That(a.FixesFor("SuborbitalHop"), Is.EquivalentTo(new[] { FailureCause.Tumbled }));
            Assert.That(a.LastFlightFailed("SuborbitalHop"), Is.True, "the old design is not reused");

            MissionType hop = a.Catalogs.Missions.Get("SuborbitalHop");
            string why;
            DesignRecord d = a.EngineersDesign(new Project { Id = "x", MissionTypeId = hop.Id, Name = hop.Name }, out why, hop);
            Assert.That(d, Is.Not.Null, why);
            Assert.That(d.Plan.Stages.Count, Is.GreaterThan(1));
            Assert.That(Fins.HasOn(d.Plan, a.Parts, 1), Is.True, "fins on the second stage after a tumble");

            // Saved with the agency.
            Agency b = Staffed(new FakeClock());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.FixesFor("SuborbitalHop"), Is.EquivalentTo(new[] { FailureCause.Tumbled }));
            Assert.That(b.Reports[b.Reports.Count - 1].Cause, Is.EqualTo(FailureCause.Tumbled));
        }

        [Test]
        public void ShortOfSpaceOrFuelMeansMoreMarginAndAHardLandingMoreParachute()
        {
            var req = new DesignRequest();
            Assert.That(req.DeltaVMargin, Is.EqualTo(1.10).Within(1e-9));
            req.Fixes.Add(FailureCause.BelowSpace);
            Assert.That(req.DeltaVMargin, Is.EqualTo(1.25).Within(1e-9));
            req.Fixes.Add(FailureCause.OutOfFuel);
            Assert.That(req.DeltaVMargin, Is.EqualTo(1.40).Within(1e-9));
            Assert.That(req.ChuteFactor, Is.EqualTo(1.0));
            req.Fixes.Add(FailureCause.HardLanding);
            Assert.That(req.ChuteFactor, Is.EqualTo(1.5));

            PartCatalog parts = DesignerReachTests.StockParts();
            var plan = new StagePlan();
            plan.Payload.Command = "mk1pod.v2";
            plan.Payload.Extras.Add("parachuteSingle");
            plan.Payload.Extras.Add("fuelTankSmallFlat");
            plan.Payload.Extras.Add("fuelTankSmallFlat");
            Assert.That(Parachutes.Shortfall(plan, parts), Is.Null, "one Mk16 (2.2 t) lands a 2 t capsule");
            Assert.That(Parachutes.Shortfall(plan, parts, 1.5), Is.Not.Null, "but not with half as much again");
            Parachutes.Fit(plan, parts.All, parts, false, 1.5);
            Assert.That(Parachutes.Shortfall(plan, parts, 1.5), Is.Null);
        }

        [Test]
        public void AnUnflownDesignByOlderRulesIsRedrawnOnLoad()
        {
            Agency a = Staffed(new FakeClock());
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            var p = new Project { Id = "P90", MissionTypeId = orbit.Id, Name = orbit.Name, Status = ProjectStatus.Build, BuildDays = 16, PhaseStartDay = a.CurrentDay };
            p.Design = a.EngineersDesign(p, out why, orbit);
            Assert.That(p.Design, Is.Not.Null, why);
            Assert.That(p.Design.DesignerRevision, Is.EqualTo(ProceduralDesigner.Revision));
            p.Design.DesignerRevision = 0; // drawn by an older build
            a.Projects.Add(p);
            // Load the save into a fresh agency, as the game does.
            Agency b = Make.Agency(new FakeClock(), new ListLogger());
            b.Parts = a.Parts;
            b.Requirements = a.Requirements;
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            var notices = new List<string>();
            b.ResumeInterruptedDesigns(notices);
            Project q = b.GetProject("P90");
            Assert.That(q.Design.DesignerRevision, Is.EqualTo(ProceduralDesigner.Revision), "redrawn by today's rules");
            Assert.That(q.Status, Is.EqualTo(ProjectStatus.Build));
            Assert.That(notices, Has.Some.Contains("redrew its rocket"));
        }

        [Test]
        public void ASimulatedOrbitIsNotKnowHowSoTheNextOrbitalMissionFliesLive()
        {
            Agency a = Staffed(new FakeClock());
            MissionType sounding = a.Catalogs.Missions.Get("OrbitalSounding");
            var paper = new MissionReport { MissionTypeId = "OrbitalSounding", Mode = MissionMode.Simulate, Outcome = MissionOutcome.Success };
            paper.Achievements.Add("Suborbit:Kerbin"); paper.Achievements.Add("Orbit:Kerbin");
            a.Reports.Add(paper);
            Assert.That(a.BreaksNewGround(sounding), Is.True, "only a simulation has reached orbit");
            a.SetDirectorFlights(KSA.Core.Ai.DirectorFlights.Firsts);
            Assert.That(a.DirectorMode(sounding), Is.EqualTo(MissionMode.WatchLive));

            var flown = new MissionReport { MissionTypeId = "KerbinOrbit", Mode = MissionMode.WatchLive, Outcome = MissionOutcome.Success };
            flown.Achievements.Add("Suborbit:Kerbin"); flown.Achievements.Add("Orbit:Kerbin");
            a.Reports.Add(flown);
            Assert.That(a.BreaksNewGround(sounding), Is.False, "a real orbit has been flown");
            Assert.That(a.DirectorMode(sounding), Is.EqualTo(MissionMode.Simulate));
        }

        [Test]
        public void AHopShortOfSpaceIsBelowSpaceAndADirectorFlownCrashChangesNoDesign()
        {
            MissionType hop = ShippedConfigs.Missions().Get("SuborbitalHop");
            var low = new LiveFlightResult { Recovered = true, MaxAltitude = 42000, SpaceAltitude = 70000 };
            MissionReport r = LiveFlightResolver.Resolve(new Project { Id = "P1", Name = "Hop" }, hop, new List<string> { "Jeb Kerman" }, low, 10, 0.7, 0.6);
            Assert.That(r.Cause, Is.EqualTo(FailureCause.BelowSpace));
            Assert.That(r.Lesson, Does.Contain("42.0 km").And.Contain("more delta-v margin"));

            var clock = new FakeClock();
            Agency a = Staffed(clock);
            var mine = new LiveFlightResult { Crashed = true, DirectorFlown = true, Cause = FailureCause.HardLanding, CauseDetail = "the capsule hit the ground at 40 m/s with no parachute open" };
            Fly(a, clock, "SuborbitalHop", mine);
            Assert.That(a.FixesFor("SuborbitalHop"), Is.Empty, "the Director had the stick");
        }
    }
}
