using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-67: Kerbalism life support, and the colony industry module every surface base can add.</summary>
    [TestFixture]
    public class KerbalismTests
    {
        private static Agency Staffed(FakeClock clock, bool kerbalism)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            var roster = new List<CrewSnapshot>();
            foreach (string n in new[] { "Jeb Kerman", "Val Kerman", "Ludo Kerman", "Mira Kerman", "Tobb Kerman", "Gene Kerman", "Wern Kerman", "Linus Kerman" })
                roster.Add(Make.Snapshot(n, CrewRole.Pilot, 4));
            roster.Add(Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4));
            roster.Add(Make.Snapshot("Bob Kerman", CrewRole.Scientist, 3));
            a.SyncRoster(roster);
            a.Tick();
            a.Ledger.SetBalance(50000000);
            a.LiveFlightAvailable = true;
            a.Rng = new System.Random(5);
            if (kerbalism) { a.LifeSupportMod = "Kerbalism"; a.InstalledMods.Add("Kerbalism"); }
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

        /// <summary>A finished colony with drill, converter and power, eight residents and room to spare.</summary>
        private static Program Colony(Agency a)
        {
            string msg;
            Program g = a.CreateProgram("Mun Colony", "MunBase", "Mun", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            g.Status = ProgramStatus.Complete; g.Stage = ColonyStage.Colony; g.HasRocket = true;
            g.Drills = 1; g.Isru = 1; g.Power = 2; g.Seats = 30;
            g.ColonistsMale = 4; g.ColonistsFemale = 4;
            g.LastSuppliedDay = a.CurrentDay;
            return g;
        }

        [Test]
        public void ShippedConfigsReadTheLifeSupportBlock()
        {
            LifeSupportRules ls = ShippedConfigs.Settings().LifeSupport;
            Assert.That(ls.Enabled, Is.True);
            Assert.That(ls.ResidentsPerGreenhouse, Is.EqualTo(8));
            Assert.That(ls.RequireRecyclers, Is.True);
            Assert.That(ls.StrandedLostAfterDays, Is.EqualTo(20));
            Assert.That(ls.SupplyDays(1.35, 0.7, 1), Is.EqualTo(5).Within(0.3), "a pod's Kerbalism supplies last about five days");
        }

        [Test]
        public void EverySurfaceBaseHasAnOptionalIndustryModule()
        {
            ProgramCatalog programs = ShippedConfigs.ProgramTypes();
            TemplateCatalog templates = ShippedConfigs.Templates();
            int bases = 0;
            foreach (ProgramType t in programs.Types)
            {
                if (t.Kind == ProgramKind.OrbitalStation) continue;
                Assert.That(t.Modules[t.RequiredModuleCount - 1].Role, Is.EqualTo("return"), t.Id + ": the last required module is the stationed rocket");
                if (t.Kind != ProgramKind.SurfaceBase) { Assert.That(t.RequiredModuleCount, Is.EqualTo(t.Modules.Count), t.Id); continue; }
                bases++;
                ModuleRecipe last = t.Modules[t.Modules.Count - 1];
                Assert.That(last.Role, Is.EqualTo("industry"), t.Id);
                Assert.That(last.Optional, Is.True, t.Id + ": the base is finished without it");
                Assert.That(t.RequiredModuleCount, Is.EqualTo(t.Modules.Count - 1), t.Id);
                foreach (string id in last.Templates)
                {
                    Template tp = templates.Get(id);
                    Assert.That(tp, Is.Not.Null, id);
                    Assert.That(tp.Flies(last.MissionTypeId), Is.True, id + " flies " + last.MissionTypeId);
                    Assert.That(tp.ServesRole("industry"), Is.True);
                    Assert.That(tp.ServesRole(null), Is.False, id + " is never offered for an ordinary module");
                    Assert.That(tp.PlanFile, Does.EndWith(".json"));
                }
            }
            Assert.That(bases, Is.EqualTo(12));
            Assert.That(templates.Get("KerbalismColonyIndustry").RequiredMods, Has.Member("Kerbalism"));
            Assert.That(templates.Get("StockColonyIndustry").StockOnly, Is.True);
        }

        [Test]
        public void ThePlansShippedWithTheTemplatesParse()
        {
            foreach (string file in new[] { "stock_colony_industry.json", "kerbalism_colony_industry.json" })
            {
                string path = System.IO.Path.Combine(ShippedConfigs.ConfigDir, "..", "Templates", "Plans", file);
                StagePlan plan; string error;
                Assert.That(StagePlan.TryParse(System.IO.File.ReadAllText(path), out plan, out error), Is.True, file + ": " + error);
                Assert.That(plan.Stages, Has.Count.EqualTo(3));
                Assert.That(plan.Payload.Command, Is.EqualTo("mk1-3pod"));
                PlanStage lander = plan.Stages[2];
                Assert.That(lander.Core, Has.Some.Matches<PlanPart>(p => p.Part == "MiniISRU"));
                Assert.That(lander.Radial, Has.Some.Matches<PlanPart>(p => p.Part == "MiniDrill"));
                if (file.StartsWith("kerbalism")) Assert.That(lander.Core, Has.Some.Matches<PlanPart>(p => p.Part == "kerbalism-greenhouse"));
            }
        }

        [Test]
        public void TheHammerFleaPlanUsesOnlyEarlyParts()
        {
            string path = System.IO.Path.Combine(ShippedConfigs.ConfigDir, "..", "Templates", "Plans", "stock_hammer_flea.json");
            StagePlan plan; string error;
            Assert.That(StagePlan.TryParse(System.IO.File.ReadAllText(path), out plan, out error), Is.True, error);
            Assert.That(plan.Stages, Has.Count.EqualTo(2));
            // Start, Basic Rocketry and Engineering 101 only: the AI Director's first research reaches space with this.
            var early = new HashSet<string> { "sensorThermometer", "GooExperiment", "solidBooster.v2", "solidBooster.sm.v2", "basicFin", "Decoupler.1", "mk1pod.v2", "parachuteSingle" };
            foreach (PlanStage s in plan.Stages)
            {
                foreach (PlanPart p in s.Core) Assert.That(early, Does.Contain(p.Part));
                foreach (PlanPart p in s.Radial) Assert.That(early, Does.Contain(p.Part));
                Assert.That(early, Does.Contain(s.Decoupler));
            }
            Assert.That(plan.Payload.Command, Is.EqualTo("mk1pod.v2"));
        }

        [Test]
        public void AFinishedBaseAddsItsIndustryModuleWithTheRightDesign()
        {
            foreach (bool kerbalism in new[] { false, true })
            {
                var clock = new FakeClock();
                Agency a = Staffed(clock, kerbalism);
                string msg;
                Program g = a.CreateProgram("Mun Base", "MunBase", "Mun", null, out msg);
                Assert.That(g, Is.Not.Null, msg);
                FinishProgram(a, clock, g);
                Assert.That(g.Stage, Is.EqualTo(ColonyStage.Base), "finished without the optional module");
                Assert.That(a.CanExtend(g), Is.True);
                Assert.That(a.NextModuleRole(g), Is.EqualTo("industry"));
                Assert.That(a.ExtendProgram(g.Id, out msg), Is.True, msg);
                Project p = a.GetProject(g.ModuleProjectIds[g.ModuleProjectIds.Count - 1]);
                Assert.That(p.ModuleRole, Is.EqualTo("industry"));
                Assert.That(p.TemplateId, Is.EqualTo(kerbalism ? "KerbalismColonyIndustry" : "StockColonyIndustry"));
                DesignRequest req = a.BuildDesignRequest(p);
                Assert.That(req.MustCarry, Has.Some.Contains("drill"));
                if (kerbalism) Assert.That(req.MustCarry, Has.Some.Contains("greenhouse"));
                else Assert.That(req.MustCarry, Has.None.Contains("greenhouse"));
                MissionType module = a.Catalogs.Missions.Get("MunBaseModule");
                Assert.That(a.TemplatesFor(module), Has.None.Matches<Template>(t => t.Id.EndsWith("ColonyIndustry")), "ordinary modules never get the industry craft");
            }
        }

        [Test]
        public void WithoutKerbalismTheStockRulesStand()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, false);
            Program g = Colony(a);
            Assert.That(a.LifeSupportOn, Is.False);
            Assert.That(a.IsSelfSufficient(g), Is.True, "drill, converter and power are enough");
            Assert.That(a.LifeSupportShort(g), Is.Empty);
            Assert.That(a.CrewKeptSupplied(), Is.Empty);
        }

        [Test]
        public void WithKerbalismAColonyMustGrowItsOwnFood()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, true);
            Program g = Colony(a);
            Assert.That(a.LifeSupportOn, Is.True);
            Assert.That(a.IsSelfSufficient(g), Is.False, "no greenhouse yet");
            Assert.That(a.SupplyIntervalFor(g), Is.GreaterThan(0), "a colony that cannot feed itself needs supplies");
            Assert.That(a.NextStageHint(g), Does.Contain("greenhouse").And.Contains("scrubbers"));
            g.Greenhouses = 1; g.Scrubbers = 8; g.WaterRecyclers = 8;
            Assert.That(a.IsSelfSufficient(g), Is.True, "one greenhouse feeds eight, and air and water are recycled");
            Assert.That(a.SupplyIntervalFor(g), Is.EqualTo(0));
            Assert.That(a.FoodRoom(g), Is.EqualTo(0), "no room for another mouth");

            a.Settings.Colony.BirthChancePerPair = 1.0;
            a.Settings.LifeSupport.ColonyGreenhouseEveryYears = 0;
            a.Settings.Colony.ColonyRoomPerYear = 0; a.Settings.Colony.ColonyRoomPerResidents = 0;
            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(g.Residents, Is.EqualTo(8), "nobody is born without food for them");
            g.Greenhouses = 2; g.Scrubbers = 16; g.WaterRecyclers = 16;
            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(g.Residents, Is.GreaterThan(8), "a second greenhouse lets the colony grow");
        }

        [Test]
        public void ASelfSufficientColonyBuildsGreenhousesOverTime()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, true);
            Program g = Colony(a);
            g.Greenhouses = 1; g.Scrubbers = 8; g.WaterRecyclers = 8;
            a.Settings.LifeSupport.ColonyGreenhouseEveryYears = 1;
            a.Settings.Colony.BirthChancePerPair = 0;
            clock.AdvanceDays(a.Clock.DaysPerYear); a.Tick();
            Assert.That(g.Greenhouses, Is.EqualTo(2));
            Assert.That(g.Scrubbers, Is.GreaterThan(8), "the room it builds is fitted out");
        }

        [Test]
        public void SuppliedProgramsKeepTheirCrewsStocked()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, true);
            string msg;
            Program g = a.CreateProgram("Minmus Camp", "MinmusBase", "Minmus", null, out msg);
            Assert.That(g, Is.Not.Null, msg);
            g.Status = ProgramStatus.Complete; g.Stage = ColonyStage.Base;
            g.CrewAboard.Add("Jeb Kerman");
            g.LastSuppliedDay = a.CurrentDay;
            Assert.That(a.CrewKeptSupplied(), Has.Member("Jeb Kerman"));
            clock.AdvanceDays(a.SupplyIntervalFor(g) + 5); a.Tick();
            Assert.That(a.CrewKeptSupplied(), Has.No.Member("Jeb Kerman"), "overdue: Kerbalism's clock takes over");
            a.Settings.LifeSupport.TopUpSuppliedVessels = false;
            g.LastSuppliedDay = a.CurrentDay;
            Assert.That(a.CrewKeptSupplied(), Is.Empty);
        }

        [Test]
        public void ALiveFlightMustCarryFoodAndWaterForTheWholeMission()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, true);
            string msg;
            Project p = a.AdoptHangarLaunch("MunLanding", "v-1", "Hungry Bird", new List<string> { "Jeb Kerman" }, out msg);
            Assert.That(p, Is.Not.Null, msg);
            MissionType type = a.Catalogs.Missions.Get("MunLanding");
            double need = type.DurationDays + a.LifeSupport.MarginDays;
            a.ModuleHardwareCheck = x => new ModuleHardware { Food = a.LifeSupport.FoodPerKerbalDay * (need - 1), Water = a.LifeSupport.WaterPerKerbalDay * need };
            Assert.That(a.LifeSupportBlocker(p, type), Does.Contain("simulated"));
            a.ModuleHardwareCheck = x => new ModuleHardware { Food = a.LifeSupport.FoodPerKerbalDay * need, Water = a.LifeSupport.WaterPerKerbalDay * need };
            Assert.That(a.LifeSupportBlocker(p, type), Is.Null);
            a.LifeSupportMod = string.Empty;
            a.ModuleHardwareCheck = x => new ModuleHardware();
            Assert.That(a.LifeSupportBlocker(p, type), Is.Null, "no life support mod: no supply check");
        }

        [Test]
        public void StrandedCrewsLastDaysNotYearsWithKerbalism()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, true);
            string msg;
            Project p = a.AdoptHangarLaunch("KerbinOrbit", "vessel-1", "Lost Bird", new List<string> { "Val Kerman" }, out msg);
            Assert.That(p, Is.Not.Null, msg);
            var result = new LiveFlightResult { Stranded = true, StrandedBody = "Kerbin", StrandedVesselId = "4242", DaysInSpace = 2 };
            result.Achievements.Add("Flight:Kerbin"); result.Achievements.Add("Orbit:Kerbin");
            a.ResolveLive(p.Id, result);
            Assert.That(a.StrandingFor("Val Kerman"), Is.Not.Null);
            Assert.That(a.LostAfterDays(), Is.EqualTo(20));
            clock.AdvanceDays(10); a.Tick();
            Assert.That(a.Crew.Get("Val Kerman").Status, Is.Not.EqualTo(CrewStatus.Dead));
            clock.AdvanceDays(11); a.Tick();
            Assert.That(a.Crew.Get("Val Kerman").Status, Is.EqualTo(CrewStatus.Dead), "the food ran out");
        }

        [Test]
        public void LifeSupportCountsSurviveSaveAndLoad()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, true);
            Program g = Colony(a);
            g.Greenhouses = 3; g.Scrubbers = 12; g.WaterRecyclers = 11;
            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Program h = b.GetProgram(g.Id);
            Assert.That(h.Greenhouses, Is.EqualTo(3));
            Assert.That(h.Scrubbers, Is.EqualTo(12));
            Assert.That(h.WaterRecyclers, Is.EqualTo(11));
        }
    }
}
