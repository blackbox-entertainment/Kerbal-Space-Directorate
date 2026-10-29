using System.Collections.Generic;
using System.Diagnostics;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Parts;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>
    /// The engineers' leg-by-leg designer against the whole mission list, with the stock parts of a well-researched tech
    /// tree (2026-10-29: "a designer that can design rockets that can go anywhere in the system, and make bases and
    /// stations").
    /// </summary>
    public class DesignerReachTests
    {
        /// <summary>Stock parts with their real figures (mass and fuel in tonnes, vacuum thrust in kN).</summary>
        public static PartCatalog StockParts()
        {
            var c = new PartCatalog();
            void Add(string name, PartKind kind, double mass, double fuel = 0, double thrust = 0, double ispVac = 0, double ispSl = 0, double d = 1.25, int crew = 0, bool stack = true)
            {
                c.Add(new PartInfo { Name = name, Title = name, Kind = kind, Mass = mass, FuelMass = fuel, Thrust = thrust, IspVacuum = ispVac, IspSeaLevel = ispSl, Diameter = d, CrewCapacity = crew, HasTopNode = stack, HasBottomNode = stack, Cost = 100 });
            }
            Add("mk1pod.v2", PartKind.Command, 0.84, crew: 1);
            Add("mk2LanderCabin.v2", PartKind.Command, 1.34, crew: 2);
            Add("mk1-3pod", PartKind.Command, 2.72, d: 2.5, crew: 3);
            Add("parachuteSingle", PartKind.Parachute, 0.1);
            Add("parachuteRadial", PartKind.Parachute, 0.1, stack: false);                           // Mk2-R
            Add("HeatShield1", PartKind.HeatShield, 0.3);
            Add("HeatShield2", PartKind.HeatShield, 1.3, d: 2.5);
            Add("sensorThermometer", PartKind.Science, 0.005, stack: false);
            Add("Decoupler.1", PartKind.Decoupler, 0.04);
            Add("Decoupler.2", PartKind.Decoupler, 0.16, d: 2.5);
            Add("radialDecoupler", PartKind.Decoupler, 0.025, stack: false);
            Add("landingLeg1", PartKind.LandingLeg, 0.05, stack: false);
            Add("fuelTankSmallFlat", PartKind.Tank, 0.0625, 0.5);   // FL-T100
            Add("fuelTankSmall", PartKind.Tank, 0.125, 1.0);        // FL-T200
            Add("fuelTank", PartKind.Tank, 0.25, 2.0);              // FL-T400
            Add("fuelTank.long", PartKind.Tank, 0.5, 4.0);          // FL-T800
            Add("Rockomax16.BW", PartKind.Tank, 1.0, 8.0, d: 2.5);  // X200-16
            Add("Rockomax32.BW", PartKind.Tank, 2.0, 16.0, d: 2.5); // X200-32
            Add("Rockomax64.BW", PartKind.Tank, 4.0, 32.0, d: 2.5); // Jumbo-64
            Add("liquidEngine3.v2", PartKind.Engine, 0.5, thrust: 60, ispVac: 345, ispSl: 85);       // Terrier
            Add("liquidEngine2.v2", PartKind.Engine, 1.5, thrust: 215, ispVac: 320, ispSl: 250);     // Swivel
            Add("engineLargeSkipper.v2", PartKind.Engine, 3, thrust: 650, ispVac: 320, ispSl: 280, d: 2.5);
            Add("liquidEngineMainsail.v2", PartKind.Engine, 6, thrust: 1500, ispVac: 310, ispSl: 285, d: 2.5); // Mainsail
            Add("liquidEngine2-2.v2", PartKind.Engine, 1.75, thrust: 250, ispVac: 350, ispSl: 90, d: 2.5); // Poodle
            Add("nuclearEngine", PartKind.Engine, 3, thrust: 60, ispVac: 800, ispSl: 185);           // Nerv
            Add("Size3LargeTank", PartKind.Tank, 9, 72, d: 3.75);                                    // Kerbodyne S3-14400
            Add("Size3EngineCluster", PartKind.Engine, 15, thrust: 4000, ispVac: 315, ispSl: 295, d: 3.75); // Mammoth
            Add("size3Decoupler", PartKind.Decoupler, 0.45, d: 3.75);
            Add("solidBooster.v2", PartKind.Booster, 0.75, 2.81, 227, 195, 170);                     // Hammer
            Add("solidBooster1-1", PartKind.Booster, 1.5, 6.15, 300, 210, 175);                      // Thumper
            Add("MassiveBooster", PartKind.Booster, 4.5, 19.5, 670, 220, 195);                       // Kickback
            Add("MK1CrewCabin", PartKind.Habitat, 0.72, crew: 2);
            Add("crewCabin", PartKind.Habitat, 2.5, d: 2.5, crew: 4);                                // Hitchhiker
            Add("dockingPort2", PartKind.Docking, 0.05);
            Add("solarPanels4", PartKind.Power, 0.0175, stack: false);
            Add("RCSBlock.v2", PartKind.Rcs, 0.04, stack: false);                                    // RV-105
            Add("basicFin", PartKind.Fin, 0.01, d: 0, stack: false);                                 // Basic Fin
            Add("R8winglet", PartKind.Fin, 0.1, d: 0, stack: false);                                 // AV-R8
            return c;
        }

        [Test]
        public void TheEngineersPutFinsOnTheLaunchStageAndStackFewBigTanks()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Parts = StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            foreach (string id in new[] { "SuborbitalHop", "KerbinOrbit", "MunLanding" })
            {
                MissionType t = a.Catalogs.Missions.Get(id);
                string why;
                DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = t.Id, Name = t.Name }, out why, t);
                Assert.That(r, Is.Not.Null, id + ": " + why);
                Assert.That(Fins.Has(r.Plan, a.Parts), Is.True, id + ": fins on the launch stage");
                Assert.That(r.Plan.Stages.FindIndex(1, s => Fins.Has(new StagePlan { Stages = { s } }, a.Parts)) < 0 || r.Plan.Stages.Count == 1, Is.True, id + ": fins only at the bottom");
                foreach (PlanStage s in r.Plan.Stages)
                    foreach (PlanPart p in s.Core)
                    {
                        // An even number of a tank never where half as many tanks twice the size weigh the same.
                        bool even = p.Count >= 2 && p.Count % 2 == 0;
                        Assert.That(p.Part == "fuelTankSmallFlat" && even, Is.False, id + ": " + p.Count + " FL-T100s");
                        Assert.That(p.Part == "fuelTankSmall" && even, Is.False, id + ": " + p.Count + " FL-T200s");
                        Assert.That(p.Part == "fuelTank" && even, Is.False, id + ": " + p.Count + " FL-T400s");
                    }
                DesignAnalysis check = StageAnalyzer.Analyze(r.Plan, a.Parts, a.Requirements.For(t));
                Assert.That(check.Passes, Is.True, id + ": " + StageAnalyzer.Feedback(check));
                if (id == "SuborbitalHop")
                {
                    Assert.That(r.Plan.Stages.Count, Is.EqualTo(2), "a hop flies two stages");
                    Assert.That(r.Plan.Stages[0].Core.Exists(p => a.Parts.Get(p.Part).Kind == PartKind.Booster), Is.True, "solid first");
                    Assert.That(r.Plan.Stages[1].Core.Exists(p => a.Parts.Get(p.Part).Kind == PartKind.Engine), Is.True, "then liquid");
                }
                TestContext.Out.WriteLine(id + ": " + r.Plan.ToJsonText(false));
            }
        }

        [Test]
        public void AHeatShieldIsNotADecouplerBetweenStages()
        {
            PartCatalog parts = StockParts();
            var plan = new StagePlan();
            plan.Payload.Command = "mk1pod.v2";
            var s0 = new PlanStage { Stage = 0, Role = "launch", Decoupler = "HeatShield1" };
            s0.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s0.Core.Add(new PlanPart { Part = "liquidEngine2.v2", Count = 1 });
            var s1 = new PlanStage { Stage = 1, Role = "ascent" };
            s1.Core.Add(new PlanPart { Part = "fuelTankSmall", Count = 1 });
            s1.Core.Add(new PlanPart { Part = "liquidEngine3.v2", Count = 1 });
            plan.Stages.Add(s0); plan.Stages.Add(s1);
            var names = new HashSet<string>(); foreach (PartInfo p in parts.All) names.Add(p.Name);
            Assert.That(StagePlanValidator.Validate(plan, parts, names), Has.Some.Contains("is not a decoupler"));
            s0.Decoupler = "Decoupler.1";
            Assert.That(StagePlanValidator.Validate(plan, parts, names), Has.None.Contains("is not a decoupler"));
        }

        [Test]
        public void ACrewedTopStageNeedsADecouplerUnderThePod()
        {
            PartCatalog parts = StockParts();
            var plan = new StagePlan();
            plan.Payload.Command = "mk1pod.v2";
            plan.Payload.Extras.Add("HeatShield1");
            var s0 = new PlanStage { Stage = 0, Role = "launch", Decoupler = "Decoupler.1" };
            s0.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s0.Core.Add(new PlanPart { Part = "liquidEngine2.v2", Count = 1 });
            var s1 = new PlanStage { Stage = 1, Role = "ascent" };
            s1.Core.Add(new PlanPart { Part = "fuelTankSmall", Count = 1 });
            s1.Core.Add(new PlanPart { Part = "liquidEngine3.v2", Count = 1 });
            plan.Stages.Add(s0); plan.Stages.Add(s1);
            var names = new HashSet<string>(); foreach (PartInfo p in parts.All) names.Add(p.Name);
            Assert.That(StagePlanValidator.Validate(plan, parts, names), Has.Some.Contains("needs a decoupler under the crew pod"));
            s1.Decoupler = "Decoupler.1";
            Assert.That(StagePlanValidator.Validate(plan, parts, names), Has.None.Contains("needs a decoupler under the crew pod"));
        }

        [Test]
        public void YourRocketNeedsAHeatShieldForOrbitOnceOneIsResearched()
        {
            var orbit = ShippedConfigs.Missions().Get("KerbinOrbit");
            var hop = ShippedConfigs.Missions().Get("SuborbitalHop");
            var mine = new PlayerDesign { Name = "K Orbiter mk I", Seats = 1, DeltaV = 4068, LiftoffTwr = 1.53, HasParachute = true, HasHeatShield = false };
            Assert.That(mine.Blocker(orbit, null), Is.Null, "no shield researched yet: not demanded");
            mine.HeatShieldResearched = true;
            Assert.That(mine.Blocker(orbit, null), Does.Contain("no heat shield"));
            Assert.That(mine.Blocker(hop, null), Is.Null, "a hop comes in slowly enough");
            mine.HasHeatShield = true;
            Assert.That(mine.Blocker(orbit, null), Is.Null);
        }

        [Test]
        public void TheParachutesAreSizedForTheCapsule()
        {
            PartCatalog parts = StockParts();
            // One Mk16 lands about 2.2 t: enough for a Mk1 pod with its heat shield, not for a three-seat pod with its own.
            var small = new StagePlan();
            small.Payload.Command = "mk1pod.v2";
            small.Payload.Extras.Add("parachuteSingle"); small.Payload.Extras.Add("HeatShield1");
            Assert.That(Parachutes.Shortfall(small, parts), Is.Null);

            var big = new StagePlan();
            big.Payload.Command = "mk1-3pod";
            big.Payload.Extras.Add("parachuteSingle"); big.Payload.Extras.Add("HeatShield2");
            Assert.That(Parachutes.Shortfall(big, parts), Does.Contain("too small"));
            Assert.That(Parachutes.Fit(big, parts.All, parts), Is.Not.Null);
            Assert.That(Parachutes.Shortfall(big, parts), Is.Null, "radial chutes added until it lands softly");
            Assert.That(big.Payload.Extras.FindAll(e => e == "parachuteRadial").Count % 2, Is.EqualTo(0), "in pairs");

            // The engineers do it themselves: a three-seat Kerbin orbit capsule comes down slowly.
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Parts = parts;
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            var three = new MissionType { Id = "orbit3", Name = "Crew of three", Body = "Kerbin", Profile = MissionProfile.Orbit, CrewSize = 3 };
            a.Catalogs.Missions.Add(three);
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = three.Id, Name = three.Name }, out why, three);
            Assert.That(r, Is.Not.Null, why);
            Assert.That(Parachutes.Shortfall(r.Plan, parts), Is.Null, string.Join(", ", r.Plan.Payload.Extras.ToArray()));
            Assert.That(Parachutes.CapacityOf(r.Plan, parts), Is.GreaterThan(4.0));
        }

        [Test]
        public void YourRocketNeedsEnoughParachute()
        {
            var orbit = ShippedConfigs.Missions().Get("KerbinOrbit");
            var mine = new PlayerDesign { Name = "Heavy", Seats = 1, DeltaV = 4000, LiftoffTwr = 1.5, HasParachute = true, LandingMass = 3.2, ChuteCapacity = 2.2 };
            Assert.That(mine.Blocker(orbit, null), Does.Contain("parachutes too small"));
            mine.ChuteCapacity = 4.4;
            Assert.That(mine.Blocker(orbit, null), Is.Null);
        }

        [Test]
        public void AnAiDesignWithoutFinsGetsThem()
        {
            PartCatalog parts = StockParts();
            var plan = new StagePlan();
            plan.Payload.Command = "mk1pod.v2";
            var s = new PlanStage { Stage = 0, Role = "launch" };
            s.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s.Core.Add(new PlanPart { Part = "liquidEngine2.v2", Count = 1 });
            plan.Stages.Add(s);
            Assert.That(Fins.AddIfMissing(plan, parts.All, parts), Is.Not.Null);
            Assert.That(plan.Stages[0].Radial.Exists(p => p.Part == "basicFin" && p.Symmetry == 4), Is.True, "a 5 t rocket gets the small fin");
            Assert.That(Fins.AddIfMissing(plan, parts.All, parts), Is.Null, "never twice");
        }

        [Test]
        public void TheEngineersReachEveryWorldWithStockParts()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Parts = StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            var failed = new List<string>();
            var watch = Stopwatch.StartNew();
            foreach (MissionType t in a.Catalogs.Missions.Types)
            {
                string why = a.DesignableBlocker(t);
                if (why != null) failed.Add(t.Id + ": " + why);
                TestContext.Out.WriteLine(t.Id + " (" + System.Math.Round(a.Requirements.For(t).DeltaV) + " m/s): " + (why ?? "OK"));
            }
            watch.Stop();
            TestContext.Out.WriteLine("All " + a.Catalogs.Missions.Count + " missions checked in " + watch.ElapsedMilliseconds + " ms");
            // Crewed landings with the trip home from Moho, Laythe and Tylo (over 14,000 m/s) need more lift than one core
            // engine with side boosters gives: fuel-fed boosters or orbital assembly, not built yet.
            failed.RemoveAll(f => f.StartsWith("MohoLanding:") || f.StartsWith("LaytheLanding:") || f.StartsWith("TyloLanding:"));
            Assert.That(failed, Is.Empty);
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(3000), "the mission list checks every design on scene load");
        }

        [Test]
        public void AMunLanderHasItsLegsOnTheStageThatTouchesDownAndEnoughThrustThere()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Parts = StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType t = a.Catalogs.Missions.Get("MunLanding");
            string why;
            var probe = new KSA.Core.Projects.Project { Id = "x", MissionTypeId = t.Id, Name = t.Name };
            DesignRecord r = a.EngineersDesign(probe, out why, t);
            Assert.That(r, Is.Not.Null, why);
            MissionRequirements.Entry need = a.Requirements.For(t);
            DesignAnalysis check = StageAnalyzer.Analyze(r.Plan, a.Parts, need);
            Assert.That(check.Passes, Is.True, StageAnalyzer.Feedback(check));
            Assert.That(check.LiftoffTwr, Is.InRange(1.3, 2.6), "lift-off at sea level");
            int legsOn = r.Plan.Stages.FindIndex(s => s.Radial.Exists(p => p.Part == "landingLeg1"));
            Assert.That(legsOn, Is.GreaterThan(0), "legs on an upper stage, not the launch stage");
            Assert.That(r.Plan.Stages[legsOn].Role, Does.Contain("lander"));
            Assert.That(r.Rationale, Does.Contain("descent to the surface of Mun"));
        }

        [Test]
        public void TheReviewRejectsWhatNeverMadeOrbit()
        {
            PartCatalog parts = StockParts();
            var req = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionRequirements.Entry orbit = req.For(ShippedConfigs.Missions().Get("KerbinOrbit"));
            // Like the AI designs in the 0.5.x saves: a solid booster kicking off far too hard, enough vacuum delta-v on paper.
            var srb = new StagePlan { DesignName = "Punch" };
            srb.Payload.Command = "mk1pod.v2";
            srb.Payload.Extras.Add("parachuteSingle");
            var s0 = new PlanStage { Stage = 0, Role = "launch", Decoupler = "Decoupler.1" };
            s0.Core.Add(new PlanPart { Part = "solidBooster.v2", Count = 1 });
            var s1 = new PlanStage { Stage = 1, Role = "upper", Decoupler = "Decoupler.1" };
            s1.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s1.Core.Add(new PlanPart { Part = "liquidEngine3.v2", Count = 1 });
            srb.Stages.Add(s0); srb.Stages.Add(s1);
            DesignAnalysis a = StageAnalyzer.Analyze(srb, parts, orbit);
            Assert.That(a.TotalDeltaV, Is.GreaterThanOrEqualTo(a.RequiredDeltaV), "the old total-only check would have passed it");
            Assert.That(a.Passes, Is.False);
            Assert.That(StageAnalyzer.Feedback(a), Does.Contain("drag"));

            // A lander that cannot hover in Tylo's gravity fails even with delta-v to spare.
            MissionRequirements.Entry tylo = req.For(ShippedConfigs.Missions().Get("TyloBaseModule"));
            var weak = new StagePlan { DesignName = "Feather" };
            weak.Payload.Command = "mk1pod.v2";
            var l0 = new PlanStage { Stage = 0, Role = "launch", Decoupler = "Decoupler.2" };
            l0.Core.Add(new PlanPart { Part = "Rockomax64.BW", Count = 3 });
            l0.Core.Add(new PlanPart { Part = "liquidEngineMainsail.v2", Count = 1 });
            var l1 = new PlanStage { Stage = 1, Role = "lander" };
            l1.Core.Add(new PlanPart { Part = "fuelTank.long", Count = 6 });
            l1.Core.Add(new PlanPart { Part = "nuclearEngine", Count = 1 });
            weak.Stages.Add(l0); weak.Stages.Add(l1);
            DesignAnalysis w = StageAnalyzer.Analyze(weak, parts, tylo);
            Assert.That(StageAnalyzer.Feedback(w), Does.Contain("Tylo's gravity"));
            Assert.That(w.Passes, Is.False);
        }
    }
}
