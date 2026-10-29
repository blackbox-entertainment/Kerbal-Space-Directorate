using System.Collections.Generic;
using System.IO;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The fuel depot in Kerbin orbit, tanker flights and refuelling stops (2026-10-29).</summary>
    public class DepotTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            var roster = new List<CrewSnapshot>();
            foreach (string n in new[] { "Jeb Kerman", "Val Kerman", "Ludo Kerman", "Mira Kerman", "Tobb Kerman", "Gene Kerman", "Wern Kerman", "Linus Kerman" }) roster.Add(Make.Snapshot(n, CrewRole.Pilot, 4));
            roster.Add(Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4));
            roster.Add(Make.Snapshot("Bob Kerman", CrewRole.Engineer, 3));
            a.SyncRoster(roster);
            a.Tick();
            a.Ledger.SetBalance(50000000);
            a.Rng = new System.Random(7);
            a.Parts = DesignerReachTests.StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            return a;
        }

        private static string Shipped(string relative)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string p = Path.Combine(dir, "GameData", relative);
                if (File.Exists(p)) return File.ReadAllText(p);
                dir = Path.GetDirectoryName(dir);
            }
            Assert.Fail("GameData/" + relative + " not found");
            return null;
        }

        [Test]
        public void TheDepotTemplatesFlyTheirLegs()
        {
            var req = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionCatalog missions = ShippedConfigs.Missions();
            foreach (var t in new[] { new[] { "stock_depot_core.json", "DepotCoreKerbin", "core" }, new[] { "stock_depot_tanker.json", "DepotTankerKerbin", "resupply" } })
            {
                StagePlan plan; string err;
                Assert.That(StagePlan.TryParse(Shipped("KSA/Templates/Plans/" + t[0]), out plan, out err), Is.True, err);
                DesignAnalysis a = StageAnalyzer.Analyze(plan, DesignerReachTests.StockParts(), req.For(missions.Get(t[1]), t[2]));
                Assert.That(a.Passes, Is.True, t[0] + ": " + StageAnalyzer.Feedback(a));
                if (t[1] == "DepotTankerKerbin") Assert.That(a.CargoDelivered, Is.EqualTo(16).Within(0.01));
                else Assert.That(plan.Payload.TanksEmpty, Is.True, "the tank farm goes up empty");
            }
        }

        [Test]
        public void DockingCraftCarryAPortOnTheNoseAndSideParachutes()
        {
            Agency a = Staffed(new FakeClock());
            MissionType tanker = a.Catalogs.Missions.Get("DepotTankerKerbin");
            string why;
            DesignRecord r = a.EngineersDesign(new Project { Id = "t", MissionTypeId = tanker.Id, Name = "Tanker", ModuleRole = "resupply" }, out why, tanker);
            Assert.That(r, Is.Not.Null, why);
            List<string> extras = r.Plan.Payload.Extras;
            Assert.That(extras[extras.Count - 1], Is.EqualTo("dockingPort2"), "the port is the top of the stack");
            Assert.That(extras, Has.Member("parachuteRadial"));
            Assert.That(extras, Has.No.Member("parachuteSingle"), "a nose parachute would block the port");
        }

        [Test]
        public void ADepotStopMakesMohoPossibleWithStageLaunchedEmpty()
        {
            Agency a = Staffed(new FakeClock());
            MissionType moho = a.Catalogs.Missions.Get("MohoLanding");
            string why;
            var direct = new Project { Id = "m1", MissionTypeId = moho.Id, Name = "Moho" };
            Assert.That(a.EngineersDesign(direct, out why, moho), Is.Null, "no depot: out of reach");

            a.Programs.Add(new Program { Id = "G1", Name = "Depot One", ProgramTypeId = "KerbinDepot", Body = "Kerbin", Status = ProgramStatus.Complete, DepotCapacity = 300, DepotFuel = 300 });
            var p = new Project { Id = "m2", MissionTypeId = moho.Id, Name = "Moho" };
            DesignRecord r = a.EngineersDesign(p, out why, moho);
            Assert.That(r, Is.Not.Null, why);
            Assert.That(p.DepotId, Is.EqualTo("G1"));
            Assert.That(r.DepotFuel, Is.GreaterThan(0));
            Assert.That(r.Plan.Stages.Exists(s => s.LaunchEmpty), Is.True);
            Assert.That(r.Plan.Stages[0].LaunchEmpty, Is.False, "the launch stage goes up full");
            Assert.That(r.Plan.Payload.Extras[r.Plan.Payload.Extras.Count - 1], Is.EqualTo("dockingPort2"));

            // Round trip through the plan JSON keeps which stages launch empty.
            StagePlan again; string err;
            Assert.That(StagePlan.TryParse(r.Plan.ToJsonText(false), out again, out err), Is.True, err);
            Assert.That(again.Stages.FindAll(s => s.LaunchEmpty).Count, Is.EqualTo(r.Plan.Stages.FindAll(s => s.LaunchEmpty).Count));
        }

        [Test]
        public void SimulatedDepotIsFilledByTankersAndEmptiedByDepartures()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.Settings.DepotForEverything = true; // a Duna landing could fly without; make it stop at the depot
            string msg;
            Program g = a.CreateProgram("Depot One", "KerbinDepot", "Kerbin", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            for (int i = 0; i < 200 && g.Status != ProgramStatus.Complete; i++)
            {
                clock.AdvanceDays(5); a.Tick();
                if (g.Status == ProgramStatus.Stalled) a.ResumeProgram(g.Id, out msg);
            }
            Assert.That(g.Status, Is.EqualTo(ProgramStatus.Complete), string.Join(" | ", g.Notes));
            Assert.That(g.DepotCapacity, Is.GreaterThanOrEqualTo(64 * 0.9), "the tank farm holds about depotCapacity");
            Assert.That(g.DepotFuel, Is.EqualTo(0), "it goes up empty");
            Assert.That(a.Depot(), Is.SameAs(g));

            Project duna = a.CreateProject("Duna via depot", "DunaLanding", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(duna, Is.Not.Null, msg);
            Assert.That(duna.DepotId, Is.EqualTo(g.Id), string.Join(" | ", duna.Notes));
            double need = duna.Design.DepotFuel;
            Assert.That(need, Is.GreaterThan(0).And.LessThanOrEqualTo(g.DepotCapacity));

            // It is built, then waits on the pad for fuel.
            for (int i = 0; i < 100 && duna.Status != ProjectStatus.Ready; i++) { clock.AdvanceDays(2); a.Tick(); }
            Assert.That(duna.Status, Is.EqualTo(ProjectStatus.Ready));
            clock.AdvanceDays(10); a.Tick();
            Assert.That(duna.Status, Is.EqualTo(ProjectStatus.Ready), "no fuel at the depot yet");
            Assert.That(a.DepotBlocker(duna), Does.Contain("send a tanker"));
            Assert.That(duna.Notes.Exists(n => n.StartsWith("Waiting for fuel")), Is.True);

            // Tankers until it holds enough.
            int tankers = 0;
            while (g.DepotFuel < need && tankers++ < 10)
            {
                Project t = a.CreateResupply(g.Id, null, MissionMode.Simulate, out msg);
                Assert.That(t, Is.Not.Null, msg);
                for (int i = 0; i < 200 && t.Report == null; i++) { clock.AdvanceDays(3); a.Tick(); }
                if (duna.Status != ProjectStatus.Ready) break; // it may launch as soon as the fuel is there
            }
            Assert.That(tankers, Is.LessThanOrEqualTo(10));
            for (int i = 0; i < 20 && duna.Status == ProjectStatus.Ready; i++) { clock.AdvanceDays(3); a.Tick(); }
            Assert.That(duna.Status, Is.Not.EqualTo(ProjectStatus.Ready), "it launched once the fuel was there");
            Assert.That(duna.Notes.Exists(n => n.StartsWith("Topped up at")), Is.True);
            Assert.That(g.DepotFuel, Is.LessThan(g.DepotCapacity));

            // Saved and loaded.
            var saved = AgencyStateSerializer.Write(a);
            Agency b = Staffed(new FakeClock());
            AgencyStateSerializer.Read(saved, b);
            Assert.That(b.GetProgram(g.Id).DepotFuel, Is.EqualTo(g.DepotFuel).Within(0.01));
            Assert.That(b.GetProgram(g.Id).DepotCapacity, Is.EqualTo(g.DepotCapacity).Within(0.01));
            Assert.That(b.GetProject(duna.Id).DepotId, Is.EqualTo(g.Id));
            Assert.That(b.GetProject(duna.Id).Design.DepotFuel, Is.EqualTo(need).Within(0.01));
        }
    }
}
