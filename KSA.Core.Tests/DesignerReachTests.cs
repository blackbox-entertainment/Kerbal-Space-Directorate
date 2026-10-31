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
            Add("sensorBarometer", PartKind.Science, 0.005, stack: false);
            Add("GooExperiment", PartKind.Science, 0.05, stack: false);
            Add("kerbalism-geigercounter", PartKind.Science, 0.005, stack: false);
            Add("science.module", PartKind.Science, 0.2);                                            // SC-9001 materials bay: too heavy for the kit
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
                // Fins at the bottom, and on the stage above a solid launch stage (it climbs on through thick air; 2026-10-30).
                List<int> finned = Fins.StagesNeedingFins(r.Plan, a.Parts);
                for (int i = 0; i < r.Plan.Stages.Count; i++)
                    Assert.That(Fins.HasOn(r.Plan, a.Parts, i), Is.EqualTo(finned.Contains(i)), id + ": fins on stage " + i);
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
        public void YourRocketNeedsAHeatShieldOnlyComingHomeFromAnotherWorld()
        {
            var orbit = ShippedConfigs.Missions().Get("KerbinOrbit");
            var mun = ShippedConfigs.Missions().Get("MunOrbit");
            var hop = ShippedConfigs.Missions().Get("SuborbitalHop");
            var mine = new PlayerDesign { Name = "K Orbiter mk I", Seats = 3, DeltaV = 9000, LiftoffTwr = 1.53, HasParachute = true, HasHeatShield = false };
            Assert.That(mine.Blocker(mun, null), Is.Null, "no shield researched yet: not demanded");
            mine.HeatShieldResearched = true;
            Assert.That(mine.Blocker(mun, null), Does.Contain("no heat shield"));
            Assert.That(mine.Blocker(orbit, null), Is.Null, "from Kerbin orbit a shield is only suggested (2026-10-30)");
            Assert.That(mine.Blocker(hop, null), Is.Null, "a hop comes in slowly enough");
            mine.HasHeatShield = true;
            Assert.That(mine.Blocker(mun, null), Is.Null);
        }

        [Test]
        public void TheEngineersTakeTheEarliestPartsThatDoTheJob()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            PartCatalog parts = StockParts();
            foreach (PartInfo p in parts.All) p.TechCost = 100;
            parts.Get("mk1pod.v2").TechCost = 0;
            parts.Get("liquidEngine2.v2").TechCost = 5;
            parts.Get("liquidEngine2.v2").Tags.Add("gimbal");
            parts.Add(new PartInfo { Name = "laterEngine", Title = "Later, better engine", Kind = PartKind.Engine, Mass = 0.5, Thrust = 400, IspVacuum = 450, IspSeaLevel = 400, Diameter = 1.25, HasTopNode = true, HasBottomNode = true, TechCost = 1000 });
            parts.Get("laterEngine").Tags.Add("gimbal");
            a.Parts = parts;
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = orbit.Id, Name = orbit.Name }, out why, orbit);
            Assert.That(r, Is.Not.Null, why);
            Assert.That(r.Plan.Payload.Command, Is.EqualTo("mk1pod.v2"));
            foreach (PlanStage s in r.Plan.Stages) Assert.That(s.Core.Exists(p => p.Part == "laterEngine"), Is.False, "the early engine does the job");
        }

        [Test]
        public void TheEngineersUseOnlyPartsThatStack()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            PartCatalog parts = StockParts();
            parts.Add(new PartInfo { Name = "KKAOSS.Liquid.Fuel.Tank", Title = "Container tank", Kind = PartKind.Tank, Mass = 0.001, FuelMass = 2, HasTopNode = true, Diameter = 1.25 });
            parts.Add(new PartInfo { Name = "oneNodeEngine", Title = "Bottom-only engine", Kind = PartKind.Engine, Mass = 0.01, Thrust = 400, IspVacuum = 400, IspSeaLevel = 380, HasBottomNode = true, Diameter = 1.25 });
            parts.Add(new PartInfo { Name = "oneNodeDecoupler", Title = "One-node decoupler", Kind = PartKind.Decoupler, Mass = 0.0001, HasTopNode = true, Diameter = 1.25 });
            a.Parts = parts;
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = orbit.Id, Name = orbit.Name }, out why, orbit);
            Assert.That(r, Is.Not.Null, why);
            foreach (PlanStage s in r.Plan.Stages)
            {
                Assert.That(s.Decoupler, Is.Not.EqualTo("oneNodeDecoupler"));
                Assert.That(s.Core.Exists(p => p.Part == "KKAOSS.Liquid.Fuel.Tank" || p.Part == "oneNodeEngine"), Is.False, "the VAB could not stack it");
            }
        }

        [Test]
        public void EachKindOfCraftGetsItsOwnParts()
        {
            var parts = new PartCatalog();
            parts.Add(new PartInfo { Name = "mk1pod.v2", Kind = PartKind.Command, CrewCapacity = 1 });
            parts.Add(new PartInfo { Name = "Mark2Cockpit", Kind = PartKind.Command, CrewCapacity = 1 });
            parts.Add(new PartInfo { Name = "landingLeg1", Kind = PartKind.LandingLeg });
            parts.Add(new PartInfo { Name = "turboJet", Kind = PartKind.Jet });
            parts.Add(new PartInfo { Name = "liquidEngine", Kind = PartKind.Engine });
            System.Func<PartJob, string, bool> has = (job, name) => PartFilter.ForJob(parts, job, false).Exists(p => p.Name == name);
            foreach (PartJob rocket in new[] { PartJob.LaunchVehicle, PartJob.LanderReturn, PartJob.StationModule, PartJob.BaseModule })
            {
                Assert.That(has(rocket, "Mark2Cockpit"), Is.False, rocket + ": no plane cockpit");
                Assert.That(has(rocket, "mk1pod.v2"), Is.True, rocket + ": a crew pod");
                Assert.That(has(rocket, "liquidEngine"), Is.True, rocket + ": shared parts");
                Assert.That(has(rocket, "turboJet"), Is.False, rocket + ": no jets");
            }
            parts.Add(new PartInfo { Name = "cupola", Kind = PartKind.Command, CrewCapacity = 1 });
            parts.Add(new PartInfo { Name = "landerCabinSmall", Kind = PartKind.Command, CrewCapacity = 1 });
            parts.Add(new PartInfo { Name = "KKAOSS.Control.g", Kind = PartKind.Command, CrewCapacity = 2 });
            foreach (PartJob rocket in new[] { PartJob.LaunchVehicle, PartJob.LanderReturn })
            {
                Assert.That(has(rocket, "cupola"), Is.False, rocket + ": a dome is no capsule");
                Assert.That(has(rocket, "landerCabinSmall"), Is.False, rocket + ": a lander can does not come home through the air");
                Assert.That(has(rocket, "KKAOSS.Control.g"), Is.False, rocket + ": base parts are for bases");
            }
            Assert.That(has(PartJob.StationModule, "cupola"), Is.True);
            Assert.That(has(PartJob.BaseModule, "KKAOSS.Control.g"), Is.True);
            Assert.That(has(PartJob.Plane, "Mark2Cockpit"), Is.True);
            Assert.That(has(PartJob.Plane, "turboJet"), Is.True);
            Assert.That(has(PartJob.LaunchVehicle, "landingLeg1"), Is.False, "no legs on an orbiter");
            Assert.That(has(PartJob.LanderReturn, "landingLeg1"), Is.True);
            Assert.That(has(PartJob.BaseModule, "landingLeg1"), Is.True);
            MissionCatalog missions = ShippedConfigs.Missions();
            Assert.That(Agency.JobFor(missions.Get("KerbinOrbit")), Is.EqualTo(PartJob.LaunchVehicle));
            Assert.That(Agency.JobFor(missions.Get("MunFlyby")), Is.EqualTo(PartJob.LaunchVehicle));
            Assert.That(Agency.JobFor(missions.Get("MunLanding")), Is.EqualTo(PartJob.LanderReturn));
            Assert.That(Agency.JobFor(missions.Get("StationCoreKerbin")), Is.EqualTo(PartJob.StationModule));
            Assert.That(Agency.JobFor(missions.Get("MunBaseModule")), Is.EqualTo(PartJob.BaseModule));
            Assert.That(Agency.JobFor(missions.Get("FirstFlightKerbin")), Is.EqualTo(PartJob.Plane));
        }

        [Test]
        public void ARocketHasACapsuleAndDecouplersThatFit()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            PartCatalog parts = StockParts();
            parts.Add(new PartInfo { Name = "Mark2Cockpit", Title = "Mk1 Inline Cockpit", Kind = PartKind.Command, Mass = 0.1, CrewCapacity = 1, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            parts.Add(new PartInfo { Name = "Separator.tiny", Title = "Tiny separator", Kind = PartKind.Decoupler, Mass = 0.001, Diameter = 0.625, HasTopNode = true, HasBottomNode = true });
            a.Parts = parts;
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = orbit.Id, Name = orbit.Name }, out why, orbit);
            Assert.That(r, Is.Not.Null, why);
            Assert.That(r.Plan.Payload.Command, Does.Not.Contain("Cockpit"), "a capsule, not a plane's cockpit");
            foreach (PlanStage s in r.Plan.Stages) Assert.That(s.Decoupler, Is.Not.EqualTo("Separator.tiny"), "as wide as its stage");
        }

        [Test]
        public void AnUpperStageThatLightsInTheAirCarriesFins()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            PartCatalog parts = StockParts();
            a.Parts = parts;
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = orbit.Id, Name = orbit.Name }, out why, orbit);
            Assert.That(r, Is.Not.Null, why);
            Assert.That(r.Plan.Stages.Count, Is.GreaterThan(1));
            Assert.That(Fins.HasOn(r.Plan, parts, 0), Is.True, "launch stage");
            if (Fins.LaunchStageDeltaV(r.Plan, parts) < Fins.LowStageDeltaV)
                Assert.That(Fins.HasOn(r.Plan, parts, 1), Is.True, "2026-10-30: a finless second stage tumbled at 17.5 km");
        }

        [Test]
        public void WhatCanItFlyListsTheFitsFirstThenTheNearMissesByDeltaV()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            var mine = new PlayerDesign { Name = "Orbiter mk II", Seats = 1, DeltaV = 3600, LiftoffTwr = 1.6, HasParachute = true, Mass = 12 };
            List<Agency.MissionFit> fits = a.WhatCanItFly(mine);
            Assert.That(fits.Exists(f => f.Mission.Id == "KerbinOrbit" && f.Blocker == null), Is.True, "3,600 m/s makes orbit");
            int firstBlocked = fits.FindIndex(f => f.Blocker != null);
            Assert.That(firstBlocked, Is.GreaterThan(0));
            Assert.That(fits.GetRange(0, firstBlocked).TrueForAll(f => f.Blocker == null), Is.True, "every fit comes first");
            var shorts = fits.GetRange(firstBlocked, fits.Count - firstBlocked).ConvertAll(f => f.DeltaVShort);
            for (int i = 1; i < shorts.Count; i++) Assert.That(shorts[i], Is.GreaterThanOrEqualTo(shorts[i - 1]), "near misses by delta-v short");
            Assert.That(fits.Exists(f => f.Mission.ModuleRole.Length > 0 || f.Mission.IsRescue), Is.False, "no programme modules or rescues");
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
        public void EveryCrewedFlightCarriesTheWholeScienceKit()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Parts = StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType t = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = t.Id, Name = t.Name }, out why, t);
            Assert.That(r, Is.Not.Null, why);
            foreach (string s in new[] { "sensorThermometer", "sensorBarometer", "GooExperiment", "kerbalism-geigercounter" })
                Assert.That(r.Plan.Payload.Extras.FindAll(e => e == s).Count, Is.EqualTo(1), s + " once");
            Assert.That(r.Plan.Payload.Extras, Has.No.Member("science.module"), "heavy science stays home");
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
            Assert.That(plan.Stages[0].Radial.Exists(p => p.Part == "basicFin" && p.Symmetry == 4), Is.True, "a 5 t rocket gets four of the small fin");
            Assert.That(Fins.AddIfMissing(plan, parts.All, parts), Is.Null, "never twice");
        }

        [Test]
        public void TheStageAboveASolidBoosterGetsFinsToo()
        {
            PartCatalog parts = StockParts();
            var plan = new StagePlan();
            plan.Payload.Command = "mk1pod.v2";
            var s0 = new PlanStage { Stage = 0, Role = "launch" };
            s0.Core.Add(new PlanPart { Part = "solidBooster.v2", Count = 1 });
            var s1 = new PlanStage { Stage = 1, Role = "upper" };
            s1.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s1.Core.Add(new PlanPart { Part = "liquidEngine2.v2", Count = 1 });
            plan.Stages.Add(s0); plan.Stages.Add(s1);
            Assert.That(Fins.StagesNeedingFins(plan, parts), Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(Fins.AddIfMissing(plan, parts.All, parts), Does.Contain("stage 2"));
            Assert.That(Fins.HasOn(plan, parts, 0) && Fins.HasOn(plan, parts, 1), Is.True);
        }

        [Test]
        public void AWeakLiquidLaunchStageFinsTheStageAboveToo()
        {
            PartCatalog parts = StockParts();
            var plan = new StagePlan();
            plan.Payload.Command = "mk1pod.v2";
            var s0 = new PlanStage { Stage = 0, Role = "launch" };
            s0.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s0.Core.Add(new PlanPart { Part = "liquidEngine2.v2", Count = 1 });
            var s1 = new PlanStage { Stage = 1, Role = "upper" };
            s1.Core.Add(new PlanPart { Part = "fuelTank", Count = 1 });
            s1.Core.Add(new PlanPart { Part = "liquidEngine2.v2", Count = 1 });
            plan.Stages.Add(s0); plan.Stages.Add(s1);
            Assert.That(Fins.LaunchStageDeltaV(plan, parts), Is.LessThan(Fins.LowStageDeltaV), "one FL-T400 under a full second stage burns out low");
            Assert.That(Fins.StagesNeedingFins(plan, parts), Is.EquivalentTo(new[] { 0, 1 }));
        }

        [Test]
        public void TheEngineersPickEnginesThatSteer()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            PartCatalog parts = StockParts();
            PartInfo swivel = parts.Get("liquidEngine2.v2");
            swivel.Tags.Add("gimbal");
            // The LV-T30 Reliant: lighter and stronger than the Swivel, but it cannot swivel to steer.
            parts.Add(new PartInfo { Name = "liquidEngine.v2", Title = "Reliant", Kind = PartKind.Engine, Mass = 1.25, Thrust = 240, IspVacuum = 310, IspSeaLevel = 265, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            a.Parts = parts;
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = orbit.Id, Name = orbit.Name }, out why, orbit);
            Assert.That(r, Is.Not.Null, why);
            foreach (PlanStage s in r.Plan.Stages)
                Assert.That(s.Core.Exists(p => p.Part == "liquidEngine.v2"), Is.False, "no fixed Reliant while the Swivel is researched");
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
                if (t.IsAircraft) continue; // planes are the Director's own (2026-10-30)
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
        public void ALanderIsDrawnForThreeWhenAPodSeatsThemAndForItsCrewWhenNot()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.Parts = StockParts();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            MissionType t = a.Catalogs.Missions.Get("MunLanding");
            string why;
            DesignRecord r = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "x", MissionTypeId = t.Id, Name = t.Name }, out why, t);
            Assert.That(r, Is.Not.Null, why);
            Assert.That(DesignSession.SeatsIn(r.Plan, a.Parts), Is.GreaterThanOrEqualTo(3));

            var early = new PartCatalog();
            foreach (PartInfo p in StockParts().All) if (p.Name != "mk1-3pod") early.Add(p);
            early.Add(new PartInfo { Name = "kv2Pod", Title = "kv2Pod", Kind = PartKind.Command, Mass = 1.65, Diameter = 1.25, CrewCapacity = 2, HasTopNode = true, HasBottomNode = true });
            a.Parts = early;
            DesignRecord two = a.EngineersDesign(new KSA.Core.Projects.Project { Id = "y", MissionTypeId = t.Id, Name = t.Name }, out why, t);
            Assert.That(two, Is.Not.Null, why);
            Assert.That(DesignSession.SeatsIn(two.Plan, a.Parts), Is.EqualTo(2));
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
