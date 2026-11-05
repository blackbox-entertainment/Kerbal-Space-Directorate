using System;
using KSA.Core.Flight.Sim;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-75: Duna arrivals flown without KSP, against what real flights did on 2026-11-01.</summary>
    [TestFixture]
    public class FlightSimTests
    {
        private static readonly SimWorld Duna = SimWorld.Duna();

        [TestCase(14627, 47.7, 14407, 0.15, Description = "Mk II skimming the top of the air")]
        [TestCase(14271, 26.5, 784, 0.35, Description = "Mk I first arrival")]
        [TestCase(7655, 20.0, 97, 0.9, Description = "Mk I third arrival")]
        public void APassMatchesARealFlight(double ap, double pe, double apAfter, double tolerance)
        {
            PassResult r = AirPass.Fly(Duna, ap * 1000, pe * 1000);
            TestContext.WriteLine("Ap " + ap + " x " + pe + " km: " + Math.Round(r.ApA / 1000) + " x " + Math.Round(r.PeA / 1000) + " km, flown " + apAfter + ", lowest " + Math.Round(r.LowestHeight / 1000, 1));
            Assert.That(r.Crashed, Is.False);
            double lost = ap * 1000 - r.ApA, flown = (ap - apAfter) * 1000;
            Assert.That(lost, Is.EqualTo(flown).Within(flown * tolerance));
        }

        /// <summary>Every arrival height, with the crew's rules, ends clear of the air and alive: the crashes of 2026-11-01 as one test.</summary>
        [Test]
        public void EveryDunaArrivalEndsInASafeOrbit()
        {
            const double air = 50000, wanted = 70000;
            for (double pe = 20; pe <= 55; pe += 1) // the last correction has landed from 7 km low to 14 km high of a 35 km aim
                foreach (double ap in new double[] { 7000, 11000, 14400 }) // the capture burn aims for 14,377 km
                {
                    string route = Arrive(ap * 1000, pe * 1000, air, wanted, out bool safe);
                    TestContext.WriteLine(ap + " x " + pe + ": " + route);
                    Assert.That(safe, Is.True, ap + " x " + pe + " km: " + route);
                    Assert.That(route.Split(';').Length, Is.LessThanOrEqualTo(9), "too many laps: " + route);
                }
        }

        /// <summary>A probe does not come home: its budget stops where its job does, and the crewed mission to the same world costs more.</summary>
        [TestCase("ProbeFlybyDuna", "DunaFlyby")]
        [TestCase("ProbeOrbitDuna", "DunaOrbit")]
        [TestCase("ProbeLandDuna", "DunaLanding")]
        public void AProbeGoesOneWay(string probe, string crewed)
        {
            KSA.Core.Missions.MissionCatalog catalog = ShippedConfigs.Missions(new System.Collections.Generic.List<string>());
            var map = new KSA.Core.Design.DeltaVMap();
            KSA.Core.Missions.MissionType p = catalog.Get(probe), c = catalog.Get(crewed);
            Assert.That(p.IsUncrewed, Is.True);
            Assert.That(KSA.Core.Design.FlightBudget.Total(KSA.Core.Design.FlightBudget.For(p, null, map)), Is.LessThan(KSA.Core.Design.FlightBudget.Total(KSA.Core.Design.FlightBudget.For(c, null, map))));
        }

        [Test]
        public void ACrewFallingLowInTheAirLandsInsteadOfClimbing()
        {
            Assert.That(AirbrakeRules.TooLowToClimb(24000, -100, 50000), Is.True);
            Assert.That(AirbrakeRules.TooLowToClimb(30000, -100, 50000), Is.False);
            Assert.That(AirbrakeRules.TooLowToClimb(24000, 50, 50000), Is.False);
        }

        /// <summary>2026-11-01: a 14,747 x 53 km Duna orbit ran into Ike on the way down; the crew burned every drop trying to "ascend".</summary>
        [Test]
        public void ALongOrbitTurnsPastAMoon()
        {
            const double mu = 3.0136e11, radius = 320000, ikeRadius = 3520000, soi = 1049600;
            double rA = radius + 14747000, rP = radius + 30000, a = (rA + rP) / 2;
            var top = new KSA.Core.Flight.Orbital.StateVector(new KSA.Core.Flight.Orbital.Vec3(rA, 0, 0), new KSA.Core.Flight.Orbital.Vec3(0, Math.Sqrt(mu * (2 / rA - 1 / a)), 0));
            double halfLap = Math.PI * Math.Sqrt(a * a * a / mu), crossing = 0;
            for (double t = 0; t < halfLap; t += 60) if (KSA.Core.Flight.Orbital.Kepler.Propagate(top, mu, t).Position.Length > ikeRadius) crossing = t;
            KSA.Core.Flight.Orbital.Vec3 meet = KSA.Core.Flight.Orbital.Kepler.Propagate(top, mu, crossing).Position;
            double ikeRate = Math.Sqrt(mu / (ikeRadius * ikeRadius * ikeRadius)), phase = Math.Atan2(meet.Y, meet.X);
            Func<double, KSA.Core.Flight.Orbital.Vec3> ike = t => new KSA.Core.Flight.Orbital.Vec3(ikeRadius * Math.Cos(phase + ikeRate * (t - crossing)), ikeRadius * Math.Sin(phase + ikeRate * (t - crossing)), 0);
            var moons = new[] { ike };
            var reach = new[] { soi * KSA.Core.Flight.Orbital.MoonDodge.Reach };
            Assert.That(KSA.Core.Flight.Orbital.MoonDodge.FirstMeeting(top, mu, 0, halfLap, moons, reach), Is.LessThan(crossing + 1));
            Assert.That(KSA.Core.Flight.Orbital.MoonDodge.Tilt(top, mu, 0, halfLap, moons, reach, 60, out KSA.Core.Flight.Orbital.Vec3 dv), Is.True);
            TestContext.WriteLine("dodge " + dv.Length + " m/s");
            var after = new KSA.Core.Flight.Orbital.StateVector(top.Position, top.Velocity + dv);
            Assert.That(dv.Length, Is.LessThan(30));
            Assert.That(double.IsPositiveInfinity(KSA.Core.Flight.Orbital.MoonDodge.FirstMeeting(after, mu, 0, halfLap, moons, reach)), Is.True);
            Assert.That(KSA.Core.Flight.Orbital.Kepler.Propagate(after, mu, halfLap).Position.Length, Is.EqualTo(rP).Within(1000), "the low point stays put");
        }

        /// <summary>Mk II, 2026-11-01: from 85 km apoapsis the old rule ended near 188 km, in reach of the radiation.</summary>
        [Test]
        public void AStrongEngineParksLow()
        {
            ParkingResult old = ParkingBurn.Fly(72000, 155, 2110, 70000, 250000, 47000, 350, 85000, true);
            ParkingResult now = ParkingBurn.Fly(72000, 155, 2110, 70000, 250000, 47000, 350, 85000, false);
            TestContext.WriteLine("old " + Math.Round(old.ApA / 1000) + " x " + Math.Round(old.PeA / 1000) + ", " + Math.Round(old.DeltaV) + " m/s; new " + Math.Round(now.ApA / 1000) + " x " + Math.Round(now.PeA / 1000) + ", " + Math.Round(now.DeltaV) + " m/s");
            Assert.That(old.ApA, Is.GreaterThan(120000));
            Assert.That(now.FellBack, Is.False);
            Assert.That(now.ApA, Is.LessThan(120000));
            Assert.That(now.PeA, Is.GreaterThanOrEqualTo(75000));
        }

        /// <summary>A Terrier on the Mun lander: too weak to wait for the top, it still burns from 75 km.</summary>
        [Test]
        public void AWeakEngineStillBurnsEarly()
        {
            ParkingResult now = ParkingBurn.Fly(72000, 155, 2110, 70000, 60000, 32000, 345, 85000, false);
            TestContext.WriteLine("weak " + Math.Round(now.ApA / 1000) + " x " + Math.Round(now.PeA / 1000) + ", " + Math.Round(now.Seconds) + " s");
            Assert.That(now.FellBack, Is.False);
            Assert.That(now.PeA, Is.GreaterThanOrEqualTo(75000));
            Assert.That(now.ApA, Is.LessThan(250000));
        }

        [Test, Explicit]
        public void DescentGrid()
        {
            foreach (double pe in new double[] { 8, 12, 16, 20, 24 })
                foreach (double h in new double[] { 15000, 12000, 10000, 8000, 6000 })
                {
                    DescentResult r = DescentSim.Fly(Duna, 742000, pe * 1000, 215000, 332, 30000, 12000, 0.1, h);
                    TestContext.WriteLine("GRID pe " + pe + " floor " + h + ": " + (r.Landed ? "landed " : "no ") + Math.Round(r.TouchdownSpeed, 1) + " m/s, side " + Math.Round(r.SideSpeed, 1) + ", spent " + Math.Round(r.DeltaV) + ", lit " + Math.Round(r.BrakeHeight));
                }
        }

        private static SimWorld Airless(string name, double radius, double mu) { return new SimWorld { Name = name, Radius = radius, Mu = mu, AirTop = 0 }; }

        /// <summary>Mk II into Duna: deorbited near apoapsis, the engine waits for 15 km, down soft with fuel left for the way home.</summary>
        [TestCase(742, 20)]
        [TestCase(1500, 20)]
        public void ALanderComesDownOnDuna(double ap, double pe)
        {
            DescentResult r = DescentSim.Fly(Duna, ap * 1000, pe * 1000, 215000, 332, 30000, 12000);
            TestContext.WriteLine("Duna from " + ap + " km: " + r.Landed + ", " + Math.Round(r.TouchdownSpeed, 1) + " m/s, side " + Math.Round(r.SideSpeed, 1) + ", " + Math.Round(r.DeltaV) + " m/s spent, lit at " + Math.Round(r.BrakeHeight) + " m. " + r.Log);
            Assert.That(r.Landed, Is.True);
            Assert.That(r.OutOfFuel, Is.False);
            Assert.That(r.TouchdownSpeed, Is.LessThan(5));
            Assert.That(r.SideSpeed, Is.LessThan(3));
            Assert.That(r.BrakeHeight, Is.LessThanOrEqualTo(DescentRules.AirBrakeHeight));
            Assert.That(r.DeltaV, Is.LessThan(1000));
        }

        /// <summary>Laythe: no flight yet and no measured air, so Kerbin's capsule drag scaled to 60% of the pressure. An estimate, not a fit.</summary>
        [Test]
        public void ALanderComesDownOnLaytheByTheEstimate()
        {
            var laythe = new SimWorld { Name = "Laythe", Radius = 500000, Mu = 1.962e12, AirTop = 50000, Heights = new double[] { 0, 14000, 21000, 29000, 36000, 43000 },
                Drag = new double[] { 5e-4, 1.56e-5, 2.7e-6, 4.6e-7, 9.6e-8, 1.8e-8 } };
            DescentResult r = DescentSim.Fly(laythe, 120000, 20000, 250000, 345, 30000, 12000);
            TestContext.WriteLine("Laythe: " + r.Landed + ", " + Math.Round(r.TouchdownSpeed, 1) + " m/s, side " + Math.Round(r.SideSpeed, 1) + ", " + Math.Round(r.DeltaV) + " m/s spent");
            Assert.That(r.Landed, Is.True);
            Assert.That(r.TouchdownSpeed, Is.LessThan(5));
            Assert.That(r.SideSpeed, Is.LessThan(3));
        }

        /// <summary>The Mun and Minmus, where the real landers came down: the same rules with no air.</summary>
        [TestCase("Mun", 200000, 6.5138398e10, 60000, 20000)]
        [TestCase("Minmus", 60000, 1.7658e9, 50000, -6000)]
        public void ALanderComesDownWithNoAir(string name, double radius, double mu, double ap, double pe)
        {
            DescentResult r = DescentSim.Fly(Airless(name, radius, mu), ap, pe, 60000, 345, 13000, 8000);
            TestContext.WriteLine(name + ": " + r.Landed + ", " + Math.Round(r.TouchdownSpeed, 1) + " m/s, side " + Math.Round(r.SideSpeed, 1) + ", " + Math.Round(r.DeltaV) + " m/s spent. " + r.Log);
            Assert.That(r.Landed, Is.True);
            Assert.That(r.TouchdownSpeed, Is.LessThan(5));
            Assert.That(r.SideSpeed, Is.LessThan(3));
        }

        /// <summary>Mk II as built: eight Kickbacks at 75% with a Skipper on three X200-32s, then the Poodle and its tanks.</summary>
        private static SimStage[] MkII(double limiter = 0.75)
        {
            return new[]
            {
                new SimStage { ThrustSea = 568000, ThrustVacuum = 650000, IspSea = 280, IspVacuum = 320, FuelKg = 48000, DropKg = 9000,
                    BoosterSea = 8 * 593000 * limiter, BoosterVacuum = 8 * 670000 * limiter, BoosterIspSea = 195, BoosterIspVacuum = 220, BoosterFuelKg = 156000, BoosterDropKg = 36000 },
                new SimStage { ThrustSea = 64000, ThrustVacuum = 250000, IspSea = 90, IspVacuum = 350, FuelKg = 32000, DropKg = 0 }
            };
        }

        /// <summary>The climb flown in the game: boosters out at T+1:27, the Skipper's fuel gone at T+3:54, 85 km apoapsis.</summary>
        [Test]
        public void MkIIClimbsToSpace()
        {
            AscentResult r = AscentSim.Fly(MkII(), 14000, 14, 85000);
            TestContext.WriteLine("Mk II: Ap " + Math.Round(r.ApA / 1000) + " Pe " + Math.Round(r.PeA / 1000) + " at " + Math.Round(r.Altitude / 1000) + " km, up " + Math.Round(r.UpSpeed) + " side " + Math.Round(r.SideSpeed) + ", T+" + Math.Round(r.Seconds) + " s, boosters out " + Math.Round(r.BoostersOutAt) + " s, peak q " + Math.Round(r.MaxPressure) + " kPa, " + Math.Round(r.MassKg / 1000) + " t");
            Assert.That(r.ReachedSpace, Is.True);
            Assert.That(r.BoostersOutAt, Is.EqualTo(87).Within(15));
            Assert.That(r.ApA, Is.GreaterThanOrEqualTo(85000));
        }

        [Test]
        public void ALongBurnStopsAtWhatItMeantToSpend()
        {
            Assert.That(BurnRules.Spent(1047, 1047), Is.False);
            Assert.That(BurnRules.Spent(1090, 1047), Is.True, "a 1,047 m/s burn once ran on to 1,567");
        }

        /// <summary>Home from Minmus (17,000 km) and from Duna (near escape): the 41 km pass skipped; the 30 km aim must not.</summary>
        [TestCase(17000, 41, false, Description = "the flown skip")]
        [TestCase(17000, 34, true)]
        [TestCase(17000, 30, true)]
        [TestCase(17000, 26, true)]
        [TestCase(80000, 34, true)]
        [TestCase(80000, 30, true)]
        public void ACapsuleComesHome(double ap, double pe, bool firstPass)
        {
            PassResult r = AirPass.Fly(SimWorld.KerbinCapsule(), ap * 1000, pe * 1000);
            TestContext.WriteLine("Home from " + ap + " x " + pe + ": then " + Math.Round(r.ApA / 1000) + " x " + Math.Round(r.PeA / 1000) + (r.Crashed ? " down" : r.Trapped ? " stays in the air" : " out again"));
            Assert.That(r.Crashed || r.Trapped, Is.EqualTo(firstPass));
        }

        /// <summary>0.8.215: an arrival too fast to dive in skims instead; the simulator finds the pass that captures the capsule.</summary>
        [Test]
        public void AHotArrivalSkimsTheAirInsteadOfDiving()
        {
            SimWorld w = SimWorld.KerbinCapsule();
            double soi = 84159286;
            // The mk IIIe's arrival of 2026-11-04: 2,326 m/s excess, 4,187 m/s at the top of the air, which burned it up in a 27 km dive.
            double entry = AirbrakeRules.EntrySpeed(w.Mu, w.Radius + w.AirTop, 2326);
            Assert.That(entry, Is.EqualTo(3994).Within(30), "at the top of the air; the log read 4,187 at 60 km, lower and faster");
            Assert.That(entry, Is.GreaterThan(AirbrakeRules.SafeDirectEntry));
            double pe = AirbrakeRules.CapturePeriapsis(w, 2326, soi, 36000, 56000);
            Assert.That(pe, Is.Not.NaN, "some pass between 36 and 56 km captures");
            Assert.That(pe, Is.GreaterThan(30000).And.LessThan(56000));
            PassResult r = AirPass.FlyIn(w, pe, 2326);
            TestContext.WriteLine("Skim at " + pe / 1000 + " km: lowest " + Math.Round(r.LowestHeight / 1000, 1) + " km, then " + Math.Round(r.ApA / 1000) + " x " + Math.Round(r.PeA / 1000) + " km, " + Math.Round(r.EnergyLost / 1000) + " kJ/kg shed");
            Assert.That(r.Crashed || r.Trapped, Is.False, "the capsule comes back out");
            Assert.That(r.ApA, Is.LessThan(AirbrakeRules.CaptureApShare * soi), "and stays");
            PassResult higher = AirPass.FlyIn(w, pe + 2000, 2326);
            Assert.That(double.IsInfinity(higher.ApA) || higher.ApA >= AirbrakeRules.CaptureApShare * soi, Is.True, "two kilometres higher it is not caught: the chosen pass is the coolest that is");
            // A plain Duna return, 900 m/s excess, dives in as it always has.
            Assert.That(AirbrakeRules.EntrySpeed(w.Mu, w.Radius + w.AirTop, 900), Is.LessThan(AirbrakeRules.SafeDirectEntry));
        }

        /// <summary>Everything Mk II has to do, added up from the simulator's own legs: what a round trip really needs.</summary>
        [Test]
        public void DunaRoundTripBudget()
        {
            double down = DescentSim.Fly(Duna, 742000, 20000, 215000, 332, 30000, 12000).DeltaV;
            double up = LiftoffSim.Fly(Duna, 70000, 215000, 332, 24000, 12000);
            const double transfer = 1100, capture = 330, liftAndDeorbit = 40, home = 700;
            double total = transfer + capture + liftAndDeorbit + down + up + home;
            TestContext.WriteLine("Budget: down " + Math.Round(down) + ", up " + Math.Round(up) + ", total from Kerbin orbit " + Math.Round(total) + " m/s, Mk II has about 4,090. " + LiftoffSim.Why);
            Assert.That(double.IsNaN(up), Is.False, "the lander must reach orbit from Duna");
            Assert.That(up, Is.LessThan(1700));
            Assert.That(total, Is.GreaterThan(3500).And.LessThan(5000), "the plan for a Duna rocket: this figure, not the map");
        }

        // KSA-80: the failures of 2026-10-31, each with the old behaviour shown failing.

        /// <summary>A weak upper stage laid flat by the program topped out in the air (42 km on fire): near the top the nose holds it up.</summary>
        [Test]
        public void AWeakStageIsHeldUp()
        {
            double hold = AscentRules.HoldPitch(1.6, 1.0);
            double held = AscentRules.Pitch(50000, 1700, 70000, -100000, 30, 60, 8, 1, hold);
            double flat = AscentRules.Pitch(50000, 1700, 70000, -100000, 30, 60, 8, 1, 0);
            Assert.That(flat, Is.LessThan(12), "the old program, nearly flat");
            Assert.That(held, Is.GreaterThanOrEqualTo(hold - 1));
            Assert.That(AscentRules.Pitch(50000, 1700, 70000, 72000, 30, 60, 8, 1, hold), Is.LessThan(12), "already in orbit: no hold");
        }

        /// <summary>A burn flat along the horizon, past the top at 69 km, cannot lift the low point out of the air; the nose raised, it can.</summary>
        [Test]
        public void ALowBurnRaisesTheNoseOldFlatBurnFellBack()
        {
            ParkingResult raised = ParkingBurn.Fly(70800, 0, 2100, 70000, 60000, 32000, 345, 85000, false);
            ParkingResult flat = ParkingBurn.Fly(70800, 0, 2100, 70000, 60000, 32000, 345, 85000, false, true);
            TestContext.WriteLine("raised " + Math.Round(raised.ApA / 1000) + " x " + Math.Round(raised.PeA / 1000) + " fell " + raised.FellBack + "; flat " + Math.Round(flat.ApA / 1000) + " x " + Math.Round(flat.PeA / 1000) + " fell " + flat.FellBack);
            Assert.That(raised.FellBack, Is.False);
            Assert.That(flat.FellBack, Is.True);
        }

        /// <summary>The apoapsis limit once sent a full ship home at 400 km over the target; it needs the low point clear of the air first.</summary>
        [Test]
        public void TheApoapsisLimitDoesNotSendAFullShipHome()
        {
            Assert.That(ParkingRules.GiveUp(485000, 60000, 70000, 85000), Is.False, "400 km over with the low point in the air");
            Assert.That(ParkingRules.GiveUp(1500000, 60000, 70000, 85000), Is.False);
            Assert.That(ParkingRules.GiveUp(2200000, 60000, 70000, 85000), Is.True);
            Assert.That(ParkingRules.GiveUp(700000, 72000, 70000, 85000), Is.True);
            Assert.That(ParkingRules.RoundOutNow(130000, 68000, 70000, 76000, 40, 85000, false), Is.True, "the top is running away");
            Assert.That(ParkingRules.RoundOutNow(130000, 68000, 70000, 76000, 40, 85000, true), Is.False, "the round-out burn itself must burn: 0.8.130");
        }

        /// <summary>A transfer burn stops once the path reaches the target, never short of the plan.</summary>
        [Test]
        public void ARunawayTransferBurnStops()
        {
            const double munR = 12000000, soi = 2430000;
            Assert.That(TransferRules.FarEnough(12000000, false, 12000000, munR, soi), Is.False, "short of the plan");
            Assert.That(TransferRules.FarEnough(15000000, false, 12000000, munR, soi), Is.True);
            Assert.That(TransferRules.FarEnough(40000000, true, 12000000, munR, soi), Is.True, "escaping");
        }

        /// <summary>A closed throttle is not empty tanks (crashed at 278 m/s with 2,711 m/s aboard).</summary>
        [Test]
        public void AClosedThrottleIsNotEmptyTanks()
        {
            Assert.That(DescentRules.OutOfFuel(0, true, false), Is.False);
            Assert.That(DescentRules.OutOfFuel(1, true, false), Is.True);
            Assert.That(DescentRules.OutOfFuel(0, false, true), Is.True);
        }

        // KSA-78: a long burn against the same burn all at once.

        [TestCase(250000, 40000, 350, Description = "Poodle")]
        [TestCase(60000, 40000, 345, Description = "Terrier, over 10 minutes")]
        public void ALongEjectionStillGetsThere(double thrust, double mass, double isp)
        {
            LongBurnResult r = LongBurn.Fly(675000, 1047, thrust, mass, isp);
            TestContext.WriteLine("Long burn " + Math.Round(r.Seconds) + " s: excess " + Math.Round(r.Excess) + " m/s, all at once " + Math.Round(r.ExcessAtOnce) + " m/s");
            // A slow burn low over a world wastes much of its speed: 3 minutes costs 3%, 10 minutes nearly half (the warning for KSA-71).
            Assert.That(r.Excess, Is.GreaterThan(r.ExcessAtOnce * (r.Seconds > 300 ? 0.4 : 0.9)));
            Assert.That(BurnRules.Ignition(1000, 120, 0), Is.EqualTo(940));
        }

        // KSA-79: reference rockets, light to heavy, from the pad to a parking orbit under the radiation.

        [TestCase("Engineers' Mun flyby, 27 t", 900000, 1000000, 210, 240, 13000, 5000, 15000, 60000, 85, 345, 4500, 500, 4000, 2.5)]
        [TestCase("Orbiter, 80 t", 1500000, 1700000, 280, 320, 56000, 8000, 215000, 250000, 90, 350, 12000, 1500, 8000, 6)]
        public void AReferenceRocketReachesAParkingOrbit(string name, double sea, double vac, double ispS, double ispV, double fuel, double drop, double sea2, double vac2, double isp2S, double isp2V, double fuel2, double drop2, double payload, double area)
        {
            var stages = new[]
            {
                new SimStage { ThrustSea = sea, ThrustVacuum = vac, IspSea = ispS, IspVacuum = ispV, FuelKg = fuel, DropKg = drop },
                new SimStage { ThrustSea = sea2, ThrustVacuum = vac2, IspSea = isp2S, IspVacuum = isp2V, FuelKg = fuel2, DropKg = drop2 }
            };
            AscentResult r = AscentSim.Fly(stages, payload, area, 85000);
            ParkingResult p = ParkingBurn.Fly(r.Altitude, r.UpSpeed, r.SideSpeed, 70000, vac2, r.MassKg, isp2V, 85000, false);
            TestContext.WriteLine(name + ": Ap " + Math.Round(r.ApA / 1000) + " at " + Math.Round(r.Altitude / 1000) + " km; parked " + Math.Round(p.ApA / 1000) + " x " + Math.Round(p.PeA / 1000));
            Assert.That(r.ReachedSpace, Is.True, name);
            Assert.That(p.FellBack, Is.False, name);
            Assert.That(p.PeA, Is.GreaterThanOrEqualTo(75000), name);
        }

        [Test]
        public void MkIIReachesAParkingOrbitUnderTheRadiation()
        {
            AscentResult r = AscentSim.Fly(MkII(), 14000, 14, 85000);
            ParkingResult p = ParkingBurn.Fly(r.Altitude, r.UpSpeed, r.SideSpeed, 70000, 250000, r.MassKg, 350, 85000, false);
            TestContext.WriteLine("Mk II parked " + Math.Round(p.ApA / 1000) + " x " + Math.Round(p.PeA / 1000) + " for " + Math.Round(p.DeltaV) + " m/s");
            Assert.That(p.FellBack, Is.False);
            Assert.That(p.ApA, Is.LessThan(150000), "the belt starts at 170 km");
        }

        // KSA-75: the model's delta-v against the game's figure for a known stage.

        [Test]
        public void TheModelsDeltaVMatchesTheGame()
        {
            // Mk II's Poodle stage as the VAB showed it: 34.7 t full, 10.7 t empty, 350 s, 4,040 m/s in space.
            double dv = 350 * 9.80665 * Math.Log(34.7 / 10.7);
            Assert.That(dv, Is.EqualTo(4040).Within(40));
            DescentResult r = DescentSim.Fly(new SimWorld { Name = "test", Radius = 200000, Mu = 6.5138398e10, AirTop = 0 }, 30000, 5000, 250000, 350, 34700, 34000);
            Assert.That(r.Landed, Is.True);
            Assert.That(r.OutOfFuel, Is.True, "fuel runs out when the stage does");
        }

        // KSA-81: Moho, the hard end of the airless worlds, with the same rules.

        [Test]
        public void ALanderComesDownOnMoho()
        {
            DescentResult r = DescentSim.Fly(Airless("Moho", 250000, 1.6860938e11), 70000, 20000, 250000, 350, 26000, 12000);
            TestContext.WriteLine("Moho: " + r.Landed + ", " + Math.Round(r.TouchdownSpeed, 1) + " m/s, " + Math.Round(r.DeltaV) + " m/s spent");
            Assert.That(r.Landed, Is.True);
            Assert.That(r.OutOfFuel, Is.False);
            Assert.That(r.TouchdownSpeed, Is.LessThan(5));
        }

        [Test]
        public void ADeorbitOnALongOrbitWaitsForTheTop()
        {
            Assert.That(DescentRules.TooFarFromApoapsis(0.8, 7200, 100, 150), Is.False, "at the top");
            Assert.That(DescentRules.TooFarFromApoapsis(0.8, 7200, 3700, 100), Is.True, "at the bottom");
            Assert.That(DescentRules.TooFarFromApoapsis(0.05, 7200, 3700, 100), Is.False, "a round orbit goes anywhere");
        }

        private static string Arrive(double ap, double pe, double air, double wanted, out bool safe)
        {
            safe = false;
            string route = string.Empty;
            for (int passes = 1; passes <= AirbrakeRules.MaxPasses + 12; passes++)
            {
                PassResult r = AirPass.Fly(Duna, ap, pe);
                route += Math.Round(r.ApA / 1000) + " x " + Math.Round(r.PeA / 1000) + "; ";
                if (r.Crashed) return route + "CRASHED";
                if (r.Trapped) return route + "TRAPPED";
                if (double.IsPositiveInfinity(r.ApA)) return route + "ESCAPED";
                ap = r.ApA; pe = r.PeA;
                switch (AirbrakeRules.AfterPass(passes, ap, pe, air, wanted, Duna.Radius, Duna.Mu, r.EnergyLost))
                {
                    case AirbrakeStep.LiftClear: safe = true; return route + "lift";
                    case AirbrakeStep.RaisePass: pe = AirbrakeRules.PassShare * air; route += "raise; "; break;
                    case AirbrakeStep.DeepenPass: pe = AirbrakeRules.DeepenShare * air; route += "deepen; "; break;
                }
            }
            return route + "NEVER FINISHED";
        }
    }
}
