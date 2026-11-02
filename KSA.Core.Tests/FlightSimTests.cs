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
