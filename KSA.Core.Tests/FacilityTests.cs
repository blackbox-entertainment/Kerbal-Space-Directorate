using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The AI Director upgrades space center buildings (2026-10-30, T: "the runway needs to be upgraded").</summary>
    [TestFixture]
    public class FacilityTests
    {
        private sealed class FakeHost : IFacilityHost
        {
            public readonly List<FacilityOption> List = new List<FacilityOption>();
            public bool Refuse;
            public bool CanUpgradeNow { get { return true; } }
            public List<FacilityOption> Facilities() { return List; }
            public bool RaiseLevel(string id, out string message)
            {
                message = Refuse ? "not at the Space Center" : string.Empty;
                if (Refuse) return false;
                foreach (FacilityOption f in List) if (f.Id == id) { f.Level++; f.MassLimit *= 5; }
                return true;
            }
        }

        private static Agency Make1(out FakeHost host)
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            host = new FakeHost();
            host.List.Add(new FacilityOption { Id = "Runway", Title = "Runway", Level = 1, MaxLevel = 3, UpgradeCost = 100000, MassLimit = 10, LimitNow = "craft up to 10 t", NextLevel = "craft up to 50 t" });
            host.List.Add(new FacilityOption { Id = "LaunchPad", Title = "Launch Pad", Level = 3, MaxLevel = 3, MassLimit = 0, LimitNow = "craft up to any mass" });
            a.FacilityHost = host;
            return a;
        }

        [Test]
        public void AnUpgradeIsPaidFromTheLedgerAndRefundedIfTheGameRefuses()
        {
            FakeHost host;
            Agency a = Make1(out host);
            string msg;
            host.Refuse = true;
            Assert.That(a.UpgradeFacility("Runway", true, out msg), Is.False);
            Assert.That(msg, Does.Contain("refunded"));
            Assert.That(a.Ledger.Balance, Is.EqualTo(500000));
            host.Refuse = false;
            Assert.That(a.UpgradeFacility("runway", true, out msg), Is.True, msg);
            Assert.That(a.Ledger.Balance, Is.EqualTo(400000));
            Assert.That(host.List[0].Level, Is.EqualTo(2));
            Assert.That(a.UpgradeFacility("LaunchPad", true, out msg), Is.False);
            Assert.That(msg, Does.Contain("top level"));
        }

        [Test]
        public void APlaneTooHeavyForTheRunwayIsHeldBackAndTheBriefingSaysWhy()
        {
            FakeHost host;
            Agency a = Make1(out host);
            var plane = new PlayerDesign { Name = "Big Plane", CraftPath = "C:/x/SPH/Big.craft", Seats = 1, IsPlane = true, HasJets = true, HasWheels = true, HasParachute = true, Mass = 14 };
            a.PlayerDesignSource = () => new List<PlayerDesign> { plane };
            MissionType first = a.Catalogs.Missions.Get("FirstFlightKerbin");
            Assert.That(a.DesignBlocker(plane, first), Does.Contain("too heavy for the Runway"));
            Assert.That(a.PlayerDesignsFor(first), Is.Empty);
            string brief = a.DirectorBriefing();
            Assert.That(brief, Does.Contain("FACILITIES").And.Contain("UPGRADE NEEDED: Runway").And.Contain("your plane 'Big Plane'").And.Contain("AFFORDABLE NOW"));
            Assert.That(a.DirectorSystemPrompt(), Does.Contain("upgrade {facility}"));
            string msg;
            Assert.That(a.UpgradeFacility("Runway", false, out msg), Is.True, msg);
            Assert.That(a.PlayerDesignsFor(first), Has.Count.EqualTo(1), "50 t runway takes it");
        }

        [Test]
        public void AMissionWhoseRocketIsTooHeavyForThePadStaysLockedUntilTheUpgrade()
        {
            FakeHost host;
            Agency a = Make1(out host);
            host.List[1].Level = 1; host.List[1].MassLimit = 18; host.List[1].UpgradeCost = 75000;
            a.Parts = DesignerReachTests.StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType mun = a.Catalogs.Missions.Get("MunLanding");
            string why = a.MissionBlocker(mun);
            Assert.That(why, Does.Contain("Launch Pad takes 18 t").And.Contain("upgrade the Launch Pad"));
            Assert.That(a.UpgradeNeeds().Exists(n => n.Facility.Id == "LaunchPad" && n.Unlocks.Exists(u => u.StartsWith("Mun landing"))), Is.True);
            string msg;
            Assert.That(a.UpgradeFacility("LaunchPad", true, out msg), Is.True, msg);
            Assert.That(a.MissionBlocker(mun), Is.Null, "a 90 t pad takes it");
        }

        /// <summary>Un Kerballed Start's opening, with KSA's patch (2026-10-30): a probe core and small rockets, no crew pod.</summary>
        private static PartCatalog ProbeStart()
        {
            var c = new PartCatalog();
            c.Add(new PartInfo { Name = "probeCoreSphere.v2", Title = "Stayputnik", Kind = PartKind.Command, Mass = 0.05, Diameter = 0.625, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "solidBooster.sm.v2", Title = "RT-5 Flea", Kind = PartKind.Booster, Mass = 0.45, FuelMass = 1.05, Thrust = 192, IspVacuum = 165, IspSeaLevel = 140, Diameter = 0.625, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "Decoupler.0", Title = "TD-06", Kind = PartKind.Decoupler, Mass = 0.01, Diameter = 0.625, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "parachuteSingle", Title = "Mk16", Kind = PartKind.Parachute, Mass = 0.1, Diameter = 0.625, HasBottomNode = true });
            c.Add(new PartInfo { Name = "basicFin", Title = "Basic Fin", Kind = PartKind.Fin, Mass = 0.01 });
            c.Add(new PartInfo { Name = "sensorThermometer", Title = "2HOT Thermometer", Kind = PartKind.Science, Mass = 0.005 });
            c.Add(new PartInfo { Name = "SurfAntenna", Title = "Communotron 16-S", Kind = PartKind.Antenna, Mass = 0.015 });
            return c;
        }

        [Test]
        public void AProbeStartCanFlySoundingRocketsButNoCrewedMission()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.Parts = ProbeStart();
            a.TemplateCheck = t => "needs research: Mk1 Command Pod"; // as the host says: no template's parts are researched
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType sounding = a.Catalogs.Missions.Get("SoundingRocketKerbin");
            MissionType space = a.Catalogs.Missions.Get("ProbeToSpaceKerbin");
            Assert.That(sounding.IsUncrewed && sounding.Seats == 0, Is.True);
            a.LiveFlightAvailable = true;
            Assert.That(a.MissionBlocker(a.Catalogs.Missions.Get("FleaHop")), Is.Not.Null, "a probe core is no crew pod");
            Assert.That(a.MissionBlocker(sounding), Is.Null, "a Flea and a Stayputnik fly a sounding rocket");
            Assert.That(a.MissionBlocker(space), Does.Contain("climb"), "two Fleas fall short of space: the next research step opens it");

            string msg;
            Project p = a.CreateProject(null, sounding.Id, null, null, null, null, null, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Crew, Is.Empty);
            Assert.That(p.Mode, Is.EqualTo(MissionMode.WatchLive), "a first probe hop flies live (2026-10-30, T: \"I never got to watch the probe\")");
            Assert.That(a.LiveBlocker(p), Is.Null, "probe hops can fly live");
            Assert.That(a.LiveBlocker(new Project { Id = "x", MissionTypeId = "FirstSatelliteKerbin" }), Does.Contain("orbital probe"));
            Assert.That(p.Design.DesignName, Is.Not.Empty);
            Assert.That(p.Design.Plan.Payload.Command, Is.EqualTo("probeCoreSphere.v2"));
            Assert.That(p.Design.Plan.Payload.Extras, Has.Some.EqualTo("SurfAntenna"), "the data goes home by radio");

            // A success counts for the probe goal and pays Science; the next one pays 60% of it.
            var won = new MissionReport { MissionTypeId = sounding.Id, Mode = MissionMode.Simulate, Outcome = MissionOutcome.Success };
            won.Achievements.Add("Probe:Kerbin"); won.Achievements.Add("Science:Kerbin");
            a.Reports.Add(won);
            Assert.That(a.HasAgencyAchievement("Probe:Kerbin"), Is.True);
            Assert.That(a.RepeatScienceFactor(sounding), Is.EqualTo(0.6).Within(1e-9));
            Assert.That(a.RepeatBlocker(sounding), Does.Contain("under way"), "one at a time");
            a.CancelProject(p.Id, out msg);
            Assert.That(a.RepeatBlocker(sounding), Is.Null, "a probe may fly again for more data");
        }

        [Test]
        public void ALiveProbeHopEarnsProbeAchievementsEvenWhenItIsNotRecovered()
        {
            MissionCatalog m = ShippedConfigs.Missions();
            MissionType space = m.Get("ProbeToSpaceKerbin");
            Assert.That(space.PrimaryAchievement, Is.EqualTo("ProbeSpace:Kerbin"));
            var flown = new KSA.Core.Flight.LiveFlightResult { Crashed = true, MaxAltitude = 81000, SpaceAltitude = 70000, SciencePoints = 4 };
            flown.Achievements.Add("Flight:Kerbin"); flown.Achievements.Add("Suborbit:Kerbin");
            MissionReport r = KSA.Core.Flight.LiveFlightResolver.Resolve(new Project { Id = "P3", Name = "Probe to space" }, space, new List<string>(), flown, 30, 0.8, 0.7);
            Assert.That(r.Outcome, Is.EqualTo(MissionOutcome.Success), "a spent sounding rocket is no failure");
            Assert.That(r.Achievements, Does.Contain("Probe:Kerbin").And.Contain("ProbeSpace:Kerbin").And.Contain("Science:Kerbin"));
            Assert.That(r.Achievements, Has.None.StartsWith("Flight:").And.None.StartsWith("Suborbit:"), "probe flights never count as crewed ones");

            var low = new KSA.Core.Flight.LiveFlightResult { Crashed = true, MaxAltitude = 30000, SpaceAltitude = 70000, SciencePoints = 2 };
            low.Achievements.Add("Flight:Kerbin");
            MissionReport r2 = KSA.Core.Flight.LiveFlightResolver.Resolve(new Project { Id = "P4", Name = "Probe to space" }, space, new List<string>(), low, 31, 0.8, 0.7);
            Assert.That(r2.IsSuccess, Is.False, "it never reached space");
            Assert.That(r2.Cause, Is.EqualTo(KSA.Core.Flight.FailureCause.BelowSpace));
        }

        [Test]
        public void ABuiltProjectTooHeavyForThePadWaitsAndCannotBeLaunched()
        {
            FakeHost host;
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.LiveFlightAvailable = true;
            host = new FakeHost();
            host.List.Add(new FacilityOption { Id = "LaunchPad", Title = "Launch Pad", Level = 1, MaxLevel = 3, UpgradeCost = 75000, MassLimit = 18 });
            a.FacilityHost = host;
            var heavy = new PlayerDesign { Name = "K orbiter", CraftPath = "C:/x/VAB/K.craft", Seats = 1, DeltaV = 4000, LiftoffTwr = 1.5, HasParachute = true, Mass = 29.7 };
            a.PlayerDesignSource = () => new List<PlayerDesign> { heavy };
            string msg;
            Assert.That(a.CreateProject(null, "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg, null, heavy.CraftPath), Is.Null, "too heavy to start");
            Assert.That(msg, Does.Contain("too heavy for the Launch Pad"));

            // A project that was already built before the limit was known waits on the pad.
            host.List[0].MassLimit = 100;
            var p = a.CreateProject(null, "KerbinOrbit", null, null, null, null, MissionMode.WatchLive, out msg, null, heavy.CraftPath);
            Assert.That(p, Is.Not.Null, msg);
            host.List[0].MassLimit = 18;
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 2);
            a.Tick();
            Assert.That(p.Status, Is.EqualTo(KSA.Core.Projects.ProjectStatus.Ready));
            Assert.That(p.Notes, Has.Some.StartsWith("Waiting for a building"));
            Assert.That(a.LaunchLive(p.Id, out msg), Is.False);
            Assert.That(msg, Does.Contain("upgrade the Launch Pad"));
            Assert.That(a.UpgradeNeeds()[0].Unlocks, Has.Some.Contains("project"));
        }
    }
}
