using System.Collections.Generic;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-64 colonies and KSA-65 rescues.</summary>
    [TestFixture]
    public class ColonyRescueTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            var roster = new List<CrewSnapshot>();
            string[] pilots = { "Jeb Kerman", "Val Kerman", "Ludo Kerman", "Mira Kerman", "Tobb Kerman", "Gene Kerman", "Wern Kerman", "Linus Kerman" };
            foreach (string n in pilots) roster.Add(Make.Snapshot(n, CrewRole.Pilot, 4));
            roster.Add(Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4));
            roster.Add(Make.Snapshot("Bob Kerman", CrewRole.Scientist, 3));
            a.SyncRoster(roster);
            a.Tick();
            a.Ledger.SetBalance(50000000);
            a.LiveFlightAvailable = true;
            a.Rng = new System.Random(5);
            return a;
        }

        private static void FinishProgram(Agency a, FakeClock clock, Program g)
        {
            string msg;
            int guard = 0;
            while (g.Status != ProgramStatus.Complete && guard++ < 400)
            {
                clock.AdvanceDays(10); a.Tick();
                if (g.Status == ProgramStatus.Stalled) a.ResumeProgram(g.Id, out msg);
            }
            Assert.That(g.Status, Is.EqualTo(ProgramStatus.Complete), string.Join(" | ", g.Notes));
        }

        private static bool Resupply(Agency a, FakeClock clock, Program g)
        {
            string msg;
            Project r = a.CreateResupply(g.Id, null, MissionMode.Simulate, out msg);
            Assert.That(r, Is.Not.Null, msg);
            for (int i = 0; i < 200 && r.Report == null; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(r.Report, Is.Not.Null);
            return r.Report.IsSuccess;
        }

        [Test]
        public void EveryBaseRecipeEndsWithAStationedRocket()
        {
            ProgramCatalog programs = ShippedConfigs.ProgramTypes();
            foreach (ProgramType t in programs.Types)
            {
                if (t.Kind == ProgramKind.OrbitalStation) { Assert.That(t.Colonize, Is.False, t.Id); continue; }
                Assert.That(t.Colonize, Is.True, t.Id);
                Assert.That(t.Modules[t.RequiredModuleCount - 1].Role, Is.EqualTo("return"), t.Id + " ends with its rocket (optional modules come after)");
            }
            ProgramType eve = programs.Get("EveSurfaceBase");
            Assert.That(eve, Is.Not.Null);
            Assert.That(eve.RequiresProgram, Is.EqualTo("EveCloudCity"));
            Assert.That(eve.ReturnVia, Is.EqualTo("EveCloudCity"));
            foreach (string b in new[] { "Ike", "Moho", "Dres", "Laythe", "Vall", "Tylo", "Bop", "Eeloo" })
                Assert.That(programs.Get(b + "Base"), Is.Not.Null, b + " has a base program");
        }

        [Test]
        public void ABaseGrowsIntoASelfSufficientColonyThatGrowsItsOwnPeople()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.Settings.Colony.BirthChancePerPair = 1.0; // deterministic births
            // Pilots rotate (2026-10-28), so failed simulated resupplies spread their risk over everyone: give the agency
            // enough pilots for thirty resupplies, so this tests colony growth and not luck.
            var roster = new List<CrewSnapshot>();
            foreach (string n in new[] { "Jeb Kerman", "Val Kerman", "Ludo Kerman", "Mira Kerman", "Tobb Kerman", "Gene Kerman", "Wern Kerman", "Linus Kerman" }) roster.Add(Make.Snapshot(n, CrewRole.Pilot, 4));
            roster.Add(Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4));
            roster.Add(Make.Snapshot("Bob Kerman", CrewRole.Scientist, 3));
            for (int i = 1; i <= 16; i++) roster.Add(Make.Snapshot("Reserve" + i + " Kerman", CrewRole.Pilot, 4));
            a.SyncRoster(roster);
            a.ModuleHardwareCheck = p =>
                p.ModuleRole == "core" ? new ModuleHardware { Seats = 4, Drills = 1, Isru = 1 }
              : p.ModuleRole == "power" ? new ModuleHardware { Power = 2 }
              : new ModuleHardware { Seats = 2 };
            string msg;
            Program g = a.CreateProgram("Mun Base Alpha", "MunBase", "Mun", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            FinishProgram(a, clock, g);
            Assert.That(g.Stage, Is.EqualTo(ColonyStage.Base));
            Assert.That(g.HasRocket, Is.True, "the return module is the stationed rocket");
            Assert.That(g.HardwareSelfSufficient, Is.True);
            Assert.That(g.Seats, Is.EqualTo(4 + 2 + 2 + 2), "core, habitation, lab and return seats; power has none");

            int guard = 0;
            while (g.Stage < ColonyStage.Colony && guard++ < 30) Resupply(a, clock, g);
            Assert.That(g.Stage, Is.EqualTo(ColonyStage.Colony), a.NextStageHint(g) + " | " + string.Join(" | ", g.Notes));
            Assert.That(g.Residents, Is.GreaterThanOrEqualTo(a.Settings.Colony.ColonyResidents));
            Assert.That(a.SupplyStatus(g), Is.EqualTo("no supply needed"), "a colony feeds itself");
            Assert.That(a.HasAgencyAchievement("Colony:Mun"), Is.True);
            Assert.That(a.ColonizedWorlds(), Has.Member("Mun"));

            int before = g.Colonists;
            double science = a.State.ScienceEarned;
            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(g.Colonists, Is.GreaterThan(before), "a man and a woman and room: the colony grows");
            Assert.That(a.State.ScienceEarned, Is.GreaterThan(science), "colonies pay Science at the year end");
        }

        [Test]
        public void NoBirthsWithoutAManAndAWoman()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.Settings.Colony.BirthChancePerPair = 1.0;
            string msg;
            Program g = a.CreateProgram("Minmus Camp", "MinmusBase", "Minmus", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            g.Status = ProgramStatus.Complete; g.Stage = ColonyStage.Colony;
            g.Drills = 1; g.Isru = 1; g.Power = 1; g.Seats = 20; g.HasRocket = true;
            g.ColonistsMale = 6;
            Assert.That(a.HasCouple(g), Is.False);
            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(g.Colonists, Is.EqualTo(6), "six men and no women: nobody is born");
            g.ColonistsFemale = 1;
            Assert.That(a.HasCouple(g), Is.True);
            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(g.Colonists, Is.GreaterThan(7));
        }

        [Test]
        public void SettlementStretchesSuppliesAndSelfSufficiencyEndsThem()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Program g = a.CreateProgram("Duna Base", "DunaBase", "Duna", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            g.Status = ProgramStatus.Complete; g.Stage = ColonyStage.Base;
            int interval = a.SupplyIntervalFor(g);
            Assert.That(interval, Is.EqualTo(420));
            g.Stage = ColonyStage.Settlement;
            Assert.That(a.SupplyIntervalFor(g), Is.EqualTo(840));
            g.Drills = 1; g.Isru = 1; g.Power = 1;
            Assert.That(a.IsSelfSufficient(g), Is.True);
            Assert.That(a.SupplyIntervalFor(g), Is.EqualTo(0));
        }

        [Test]
        public void EveSurfaceNeedsTheCloudCityAndItsCrewGoesUpToIt()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Assert.That(a.CreateProgram("Eve Ground", "EveSurfaceBase", "Eve", null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("needs a finished"));

            Program city = a.CreateProgram("Eve Sky", "EveCloudCity", "Eve", null, out msg);
            Assert.That(city, Is.Not.Null, msg);
            city.Status = ProgramStatus.Complete; city.Stage = ColonyStage.Base;
            Program ground = a.CreateProgram("Eve Ground", "EveSurfaceBase", "Eve", null, out msg);
            Assert.That(ground, Is.Not.Null, msg);

            CrewProfile jeb = a.Crew.Get("Jeb Kerman");
            jeb.InFlightSinceUT = a.Clock.UniversalTime; jeb.Status = CrewStatus.Assigned;
            ground.CrewAboard.Add(jeb.Id);
            Assert.That(a.Evacuate(ground.Id, out msg), Is.False, "no rocket yet");
            ground.HasRocket = true;
            Assert.That(a.Evacuate(ground.Id, out msg), Is.True, msg);
            Assert.That(city.CrewAboard, Has.Member(jeb.Id), "the surface rocket only reaches the cloud city");
            Assert.That(jeb.IsFlying, Is.True, "still on Eve, waiting for the city's rocket home");
            Assert.That(a.Evacuate(city.Id, out msg), Is.False, "the city has no rocket either yet");
            city.HasRocket = true;
            Assert.That(a.Evacuate(city.Id, out msg), Is.True, msg);
            Assert.That(jeb.IsFlying, Is.False, "home");
        }

        // ------------------------------------------------------------------ rescues

        private static Project LiveOrbit(Agency a, string pilot)
        {
            string msg;
            Project p = a.AdoptHangarLaunch("KerbinOrbit", "vessel-" + pilot, "Lost Bird", new List<string> { pilot }, out msg);
            Assert.That(p, Is.Not.Null, msg);
            return p;
        }

        private static Stranding StrandInOrbit(Agency a, string pilot)
        {
            Project p = LiveOrbit(a, pilot);
            var result = new LiveFlightResult { Stranded = true, StrandedBody = "Kerbin", StrandedVesselId = "4242", DaysInSpace = 2 };
            result.Achievements.Add("Flight:Kerbin"); result.Achievements.Add("Orbit:Kerbin");
            MissionReport r = a.ResolveLive(p.Id, result);
            Assert.That(r.Stranded, Has.Member(pilot));
            Assert.That(r.Achievements, Has.None.StartsWith("Recover"));
            Stranding s = a.StrandingFor(pilot);
            Assert.That(s, Is.Not.Null);
            return s;
        }

        [Test]
        public void AStrandedCrewStaysAwayUntilRescued()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            Stranding s = StrandInOrbit(a, "Val Kerman");
            CrewProfile val = a.Crew.Get("Val Kerman");
            Assert.That(val.IsFlying, Is.True);
            Assert.That(a.AvailableFor(CrewRole.Pilot), Has.None.Matches<CrewProfile>(c => c.Id == val.Id));
            Assert.That(s.Where, Is.EqualTo("in orbit of Kerbin"));
            Assert.That(a.WhereaboutsOf(val.Id), Does.StartWith("stranded"));
            Assert.That(val.Record.Incidents, Has.Some.Matches<IncidentEntry>(i => i.Kind == IncidentKind.Stranded));

            string msg;
            Project rescue = null;
            for (int attempt = 0; attempt < 10 && !s.Resolved; attempt++)
            {
                rescue = a.CreateRescue(s.Id, null, MissionMode.Simulate, out msg);
                Assert.That(rescue, Is.Not.Null, msg);
                MissionType t = a.Catalogs.Missions.Get(rescue.MissionTypeId);
                Assert.That(t.IsRescue, Is.True);
                Assert.That(a.CreateRescue(s.Id, null, MissionMode.Simulate, out msg), Is.Null, "one rescue at a time");
                for (int i = 0; i < 60 && rescue.Report == null; i++) { clock.AdvanceDays(5); a.Tick(); }
                Assert.That(rescue.Report, Is.Not.Null);
            }
            Assert.That(s.Resolved, Is.True);
            Assert.That(val.IsFlying, Is.False, "home");
            Assert.That(val.Record.Incidents, Has.Some.Matches<IncidentEntry>(i => i.Kind == IncidentKind.Rescue));
            foreach (string id in rescue.Crew)
                Assert.That(a.Crew.Get(id).Record.Incidents, Has.Some.Matches<IncidentEntry>(i => i.Kind == IncidentKind.Rescue), "the rescuers are on the record too");
        }

        [Test]
        public void ALiveRescueOnlyBringsHomeWhoIsActuallyAboard()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            Stranding s = StrandInOrbit(a, "Val Kerman");
            string msg;
            Project rescue = a.CreateRescue(s.Id, null, MissionMode.WatchLive, out msg);
            Assert.That(rescue, Is.Not.Null, msg);
            Assert.That(a.LiveBlocker(rescue), Is.Null, "orbit stranding with a vessel: flies live");
            for (int i = 0; i < 40 && rescue.Status != ProjectStatus.Ready; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(a.LaunchLive(rescue.Id, out msg), Is.True, msg);

            var empty = new LiveFlightResult { Recovered = true, DaysInSpace = 1 };
            empty.Achievements.Add("Flight:Kerbin"); empty.Achievements.Add("Orbit:Kerbin");
            a.ResolveLive(rescue.Id, empty);
            Assert.That(s.Resolved, Is.False, "came home without her");
            Assert.That(a.Crew.Get("Val Kerman").IsFlying, Is.True);

            Project second = a.CreateRescue(s.Id, null, MissionMode.WatchLive, out msg);
            Assert.That(second, Is.Not.Null, msg);
            for (int i = 0; i < 40 && second.Status != ProjectStatus.Ready; i++) { clock.AdvanceDays(5); a.Tick(); }
            Assert.That(a.LaunchLive(second.Id, out msg), Is.True, msg);
            var full = new LiveFlightResult { Recovered = true, DaysInSpace = 1 };
            full.Achievements.Add("Flight:Kerbin"); full.Achievements.Add("Orbit:Kerbin");
            full.Passengers.Add("Val Kerman");
            a.ResolveLive(second.Id, full);
            Assert.That(s.Resolved, Is.True);
            Assert.That(a.Crew.Get("Val Kerman").IsFlying, Is.False);
        }

        [Test]
        public void SurfaceRescuesFlySimulatedAndTheForgottenAreLost()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.Settings.Rescue.LostAfterDays = 30;
            Project p = LiveOrbit(a, "Gene Kerman");
            var result = new LiveFlightResult { Stranded = true, StrandedBody = "Mun", StrandedOnSurface = true, StrandedVesselId = "99", DaysInSpace = 5 };
            a.ResolveLive(p.Id, result);
            Stranding s = a.StrandingFor("Gene Kerman");
            Assert.That(s.OnSurface, Is.True);
            string msg;
            Project rescue = a.CreateRescue(s.Id, null, MissionMode.WatchLive, out msg);
            Assert.That(rescue, Is.Not.Null, msg);
            Assert.That(a.LiveBlocker(rescue), Does.StartWith("Surface rescues are simulated"));
            a.CancelProject(rescue.Id, out msg);

            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(s.Resolved, Is.True);
            Assert.That(a.Crew.Get("Gene Kerman").Status, Is.EqualTo(CrewStatus.Dead), "given up for lost");
        }

        [Test]
        public void GrowthAndStrandingsSurviveSaveAndLoad()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            Stranding s = StrandInOrbit(a, "Val Kerman"); // before the base auto-staffs its first module
            string msg;
            Program g = a.CreateProgram("Mun Base Alpha", "MunBase", "Mun", null, out msg);
            g.Stage = ColonyStage.Settlement; g.ResuppliesDelivered = 4; g.ColonistsMale = 2; g.ColonistsFemale = 3;
            g.Seats = 12; g.Drills = 1; g.Isru = 2; g.Power = 3; g.HasRocket = true; g.ExtraRoom = 8; g.HardwareCounted.Add("P1");
            a.Crew.Get("Mira Kerman").Female = true;
            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Program h = b.GetProgram(g.Id);
            Assert.That(h.Stage, Is.EqualTo(ColonyStage.Settlement));
            Assert.That(h.ResuppliesDelivered, Is.EqualTo(4));
            Assert.That(h.ColonistsFemale, Is.EqualTo(3));
            Assert.That(h.Room, Is.EqualTo(20));
            Assert.That(h.HasRocket, Is.True);
            Assert.That(h.HardwareCounted, Has.Member("P1"));
            Assert.That(b.Crew.Get("Mira Kerman").Female, Is.True);
            Stranding t = b.GetStranding(s.Id);
            Assert.That(t, Is.Not.Null);
            Assert.That(t.Crew, Has.Member("Val Kerman"));
            Assert.That(t.VesselId, Is.EqualTo("4242"));
        }
    }
}
