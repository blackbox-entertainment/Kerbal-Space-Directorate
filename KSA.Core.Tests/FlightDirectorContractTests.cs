using System.Collections.Generic;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The agency side of the own flight director (KSA-51/52/53): scope, station docking targets, deliveries.</summary>
    public class FlightDirectorContractTests
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
            a.Ledger.SetBalance(900000);
            a.LiveFlightAvailable = true;
            return a;
        }

        private static Project Ready(Agency a, FakeClock clock, string typeId)
        {
            string msg;
            Project p = a.CreateProject("Live " + typeId, typeId, null, null, null, null, MissionMode.WatchLive, out msg);
            Assert.That(p, Is.Not.Null, msg);
            for (int i = 0; i < 40 && p.Status != ProjectStatus.Ready; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Ready), typeId + " " + p.Status + " " + string.Join(" / ", p.Notes));
            return p;
        }

        [Test]
        public void DeliveredVesselsCountAsSuccess()
        {
            MissionType core = ShippedConfigs.Missions().Get("StationCoreKerbin");
            var crew = new List<string> { "Jeb Kerman" };
            var delivered = new LiveFlightResult { Delivered = true, DaysInSpace = 0.3 };
            delivered.Achievements.Add("Orbit:Kerbin");
            MissionReport r = LiveFlightResolver.Resolve(new Project { Id = "P1", Name = "Core" }, core, crew, delivered, 10, 0.7, 0.6);
            Assert.That(r.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(r.Lines[0], Does.Contain("stays on station"));
        }

        [Test]
        public void ScopeSettingLimitsWhatFliesLive()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            Project mun = Ready(a, clock, "MunFlyby");
            a.Settings.LiveFlightScope = "Kerbin";
            Assert.That(a.LiveBlocker(mun), Does.Contain("liveFlightScope"));
            a.Settings.LiveFlightScope = "Moons";
            Assert.That(a.LiveBlocker(mun), Is.Null);
            a.Settings.LiveFlightScope = "All";
            Project duna = Ready(a, clock, "DunaFlyby");
            Assert.That(a.LiveBlocker(duna), Is.Null);
            a.Settings.LiveFlightScope = "Moons";
            Assert.That(a.LiveBlocker(duna), Does.Contain("liveFlightScope"));
        }

        [Test]
        public void StationModuleNeedsALiveCoreAndCarriesItsVesselId()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Program g = a.CreateProgram("Station Alpha", "KerbinStation", "Kerbin", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            Project core = a.GetProject(g.ModuleProjectIds[0]);
            Assert.That(a.SetProjectMode(core.Id, MissionMode.WatchLive, out msg), Is.True, msg);
            clock.AdvanceDays(core.DesignDays + core.BuildDays + 1);
            a.Tick();
            Assert.That(core.Status, Is.EqualTo(ProjectStatus.Ready));
            LaunchSpec spec = null;
            a.LiveLaunchRequested += s => spec = s;
            Assert.That(a.LaunchLive(core.Id, out msg), Is.True, msg);
            Assert.That(spec.ProgramId, Is.EqualTo(g.Id));
            Assert.That(spec.DockTargetVesselId, Is.Empty, "nothing in orbit yet");

            // The host reports the core delivered to orbit as vessel 4242.
            a.BindProgramVessel(g.Id, "4242");
            a.LogFlightEvent(core.Id, "Orbit.", "Orbit:Kerbin");
            MissionReport r = a.ResolveLive(core.Id, new LiveFlightResult { Delivered = true });
            Assert.That(r.Outcome, Is.EqualTo(MissionOutcome.Success));
            foreach (string id in core.Crew) { Assert.That(a.Crew.Get(id).IsFlying, Is.True, "station crew stay aboard"); Assert.That(a.AvailableFor(CrewRole.Pilot), Has.None.Matches<CrewProfile>(c => c.Id == id), "not assignable while on station"); }
            string aboard = core.Crew[0];
            a.ReturnFromStation(aboard);
            Assert.That(a.Crew.Get(aboard).IsFlying, Is.False);
            Assert.That(a.Crew.Get(aboard).Status, Is.EqualTo(CrewStatus.Available));
            Assert.That(g.VesselId, Is.EqualTo("4242"));

            // Next module: ready it live; the launch spec names the station to dock with.
            a.Tick();
            Project module = null;
            foreach (string id in g.ModuleProjectIds) { Project p = a.GetProject(id); if (p != null && p.ModuleRole != "core" && p.IsActive) module = p; }
            Assert.That(module, Is.Not.Null, string.Join(" | ", g.Notes));
            a.SetProjectMode(module.Id, MissionMode.WatchLive, out msg);
            clock.AdvanceDays(module.DesignDays + module.BuildDays + 1);
            a.Tick();
            Assert.That(module.Status, Is.EqualTo(ProjectStatus.Ready));
            Assert.That(a.LiveBlocker(module), Is.Null);
            Assert.That(a.LaunchLive(module.Id, out msg), Is.True, msg);
            Assert.That(spec.DockTargetVesselId, Is.EqualTo("4242"));

            // A program whose core was simulated has nothing to dock with.
            g.VesselId = string.Empty;
            Assert.That(a.LiveBlocker(module), Does.Contain("nothing in orbit"));

            Agency b = Staffed(new FakeClock());
            g.VesselId = "4242";
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.GetProgram(g.Id).VesselId, Is.EqualTo("4242"));
        }
    }
}
