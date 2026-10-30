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
    /// <summary>Aircraft missions (2026-10-30): flown in the Director's own planes, with goals and science of their own.</summary>
    [TestFixture]
    public class AircraftTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Val Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.LiveFlightAvailable = true;
            return a;
        }

        private static PlayerDesign Plane(bool chutes = false)
        {
            return new PlayerDesign { Name = "Tim's Plane", CraftPath = "C:/saves/x/Ships/SPH/Plane.craft", Seats = 1, IsPlane = true, HasJets = true, HasWheels = true, HasParachute = chutes, Mass = 4, PartCount = 12 };
        }

        [Test]
        public void ShippedAircraftMissionsAndGoalsLoad()
        {
            MissionCatalog m = ShippedConfigs.Missions();
            MissionType first = m.Get("FirstFlightKerbin");
            MissionType survey = m.Get("AerialSurveyKerbin");
            MissionType high = m.Get("HighAltitudeFlightKerbin");
            Assert.That(first.IsAircraft && survey.IsAircraft && high.IsAircraft, Is.True);
            Assert.That(first.PrimaryAchievement, Is.EqualTo("Aircraft:Kerbin"));
            Assert.That(survey.IsScience && survey.Heading == 270 && survey.RangeKm == 400, Is.True);
            Assert.That(high.CruiseAltitude, Is.GreaterThan(18000));
            Agency a = Staffed(new FakeClock());
            Assert.That(a.Requirements.For(first).DeltaV, Is.EqualTo(0), "no rocket delta-v for a plane in the air");
        }

        [Test]
        public void OnlyAJetPlaneFromTheHangarFliesThem()
        {
            Agency a = Staffed(new FakeClock());
            MissionType first = a.Catalogs.Missions.Get("FirstFlightKerbin");
            MissionRequirements.Entry need = a.Requirements.For(first);
            Assert.That(Plane().Blocker(first, need), Is.Null);
            PlayerDesign rocket = new PlayerDesign { Name = "Rocket", CraftPath = "C:/x/VAB/R.craft", Seats = 1, DeltaV = 4000, LiftoffTwr = 1.5, HasParachute = true };
            Assert.That(rocket.Blocker(first, need), Does.Contain("SPH"));
            PlayerDesign glider = Plane(); glider.HasJets = false;
            Assert.That(glider.Blocker(first, need), Does.Contain("jet"));
            PlayerDesign noGear = Plane(); noGear.HasWheels = false;
            Assert.That(noGear.Blocker(first, need), Does.Contain("could not land"));

            // No plane saved and no parts known: locked.
            Assert.That(a.MissionBlocker(first), Does.Contain("jet engine"));
            string msg;
            Assert.That(a.CreateProject(null, first.Id, null, null, null, null, null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("jet engine"));

            // With one saved, the project starts on it with no pick needed, and the engineers cannot swap in a rocket.
            a.PlayerDesignSource = () => new List<PlayerDesign> { Plane() };
            Assert.That(a.MissionBlocker(first), Is.Null);
            Project p = a.CreateProject(null, first.Id, null, null, null, null, null, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design.Source, Is.EqualTo(DesignSource.Player));
            Assert.That(p.Design.IsPlane, Is.True);
            Assert.That(p.Mode, Is.EqualTo(MissionMode.WatchLive), "a first flies live");
            Assert.That(a.SwapToEngineersDesign(p.Id, out msg), Is.False);
            Assert.That(msg, Does.Contain("do not design planes"));
        }

        [Test]
        public void PartKindsTellWhenAPlaneIsInReach()
        {
            Agency a = Staffed(new FakeClock());
            MissionType first = a.Catalogs.Missions.Get("FirstFlightKerbin");
            var parts = new PartCatalog();
            parts.Add(new PartInfo { Name = "mk1pod", Kind = PartKind.Command, CrewCapacity = 1 });
            parts.Add(new PartInfo { Name = "chute", Kind = PartKind.Parachute });
            a.Parts = parts;
            Assert.That(a.PartKindsBlocker(first), Does.Contain("a jet engine").And.Contain("wings"));
            parts.Add(new PartInfo { Name = "JetEngine", Kind = PartKind.Jet });
            parts.Add(new PartInfo { Name = "wingConnector", Kind = PartKind.Wing });
            Assert.That(a.PartKindsBlocker(first), Is.Null);
            Assert.That(a.MissionBlocker(first), Is.EqualTo(Agency.AircraftHint), "parts researched, no plane saved yet");
        }

        [Test]
        public void TheAiDirectorFliesOnlyAPlaneTickedForItWithParachutes()
        {
            Agency a = Staffed(new FakeClock());
            a.PlayerDesignSource = () => new List<PlayerDesign> { Plane(false) };
            MissionType first = a.Catalogs.Missions.Get("FirstFlightKerbin");
            a.SetPreferredDesign(first.Id, "Tim's Plane");
            Assert.That(a.PreferredDesignFor(first), Is.Null, "no parachutes: a human has to land it");
            a.PlayerDesignSource = () => new List<PlayerDesign> { Plane(true) };
            Assert.That(a.PreferredDesignFor(first), Is.Not.Null);
        }

        [Test]
        public void ALiveFlightEarnsTheAircraftAchievementsAndTheSurveyPaysScience()
        {
            MissionType survey = ShippedConfigs.Missions().Get("AerialSurveyKerbin");
            var flown = new LiveFlightResult { Recovered = true, MaxAltitude = 3200, SpaceAltitude = 70000, SciencePoints = 9 };
            flown.Achievements.Add("Flight:Kerbin"); flown.Achievements.Add("Aircraft:Kerbin"); flown.Achievements.Add("Survey:Kerbin"); flown.Achievements.Add("Recover:-");
            MissionReport r = LiveFlightResolver.Resolve(new Project { Id = "P1", Name = "Survey" }, survey, new List<string> { "Jeb Kerman" }, flown, 10, 0.8, 0.7);
            Assert.That(r.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(r.Achievements, Does.Contain("Survey:Kerbin").And.Contain("Aircraft:Kerbin").And.Contain("Science:Kerbin"));
            Assert.That(r.Cause, Is.EqualTo(FailureCause.None), "a low flight is not 'below space'");
        }
    }
}
