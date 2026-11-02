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
            for (double pe = 20; pe <= 49; pe += 1) // the last correction has landed from 7 km low to 14 km high of a 35 km aim
                foreach (double ap in new double[] { 7000, 11000, 14400 }) // the capture burn aims for 14,377 km
                {
                    string route = Arrive(ap * 1000, pe * 1000, air, wanted, out bool safe);
                    TestContext.WriteLine(ap + " x " + pe + ": " + route);
                    Assert.That(safe, Is.True, ap + " x " + pe + " km: " + route);
                    Assert.That(route.Split(';').Length, Is.LessThanOrEqualTo(9), "too many laps: " + route);
                }
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
