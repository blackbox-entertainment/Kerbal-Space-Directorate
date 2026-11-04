using System;
using KSA.Core.Flight.Orbital;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>0.8.199: a moon across a hyperbolic approach (2026-11-04: the mk IIIe met Ike two days before its Duna capture and was bent into the ground).</summary>
    [TestFixture]
    public class MoonDodgeTests
    {
        private const double Mu = 3.0136321e11, Radius = 320000, MoonRadius = 3200000, MoonSoi = 1049598;

        [Test]
        public void A_moon_across_a_hyperbolic_approach_is_seen_and_a_cheap_timing_change_clears_it_keeping_the_periapsis()
        {
            // The ship: a hyperbola with periapsis 50 km up and 300 m/s of excess speed, taken a day and a half before periapsis.
            double rp = Radius + 50000, vInf = 300;
            double vp = Math.Sqrt(vInf * vInf + 2 * Mu / rp);
            var atPe = new StateVector(new Vec3(rp, 0, 0), new Vec3(0, vp, 0));
            double lead = 1.5 * 21600;
            StateVector now = Kepler.Propagate(atPe, Mu, -lead);
            Assert.That((Kepler.Propagate(now, Mu, lead).Position - atPe.Position).Length, Is.LessThan(100), "propagation back and forth");
            Assert.That(now.Position.Length, Is.LessThan(47900000), "still inside the planet's sphere");

            // The moon: circular at Ike's distance, placed where the ship crosses its orbit.
            double n = Math.Sqrt(Mu / (MoonRadius * MoonRadius * MoonRadius));
            double tCross = -1;
            for (double t = 0; t < lead; t += 10) if (Kepler.Propagate(now, Mu, t).Position.Length <= MoonRadius) { tCross = t; break; }
            Assert.That(tCross, Is.GreaterThan(0));
            Vec3 pCross = Kepler.Propagate(now, Mu, tCross).Position;
            double phase0 = Math.Atan2(pCross.Y, pCross.X) - n * tCross;
            Func<double, Vec3> moon = t => new Vec3(MoonRadius * Math.Cos(phase0 + n * t), MoonRadius * Math.Sin(phase0 + n * t), 0);
            var moons = new[] { moon };
            var reach = new[] { MoonSoi * MoonDodge.Reach };

            double hit = MoonDodge.FirstMeeting(now, Mu, 0, lead + 600, moons, reach);
            Assert.That(hit, Is.LessThan(tCross), "the moon's reach is met before the crossing");

            double burnAt = 60;
            StateVector s = Kepler.Propagate(now, Mu, burnAt);
            Vec3 dv;
            // A tilt about the vertical cannot do it on a path falling nearly straight in: the crossing is only a few degrees round from the burn.
            Assert.That(MoonDodge.Tilt(s, Mu, burnAt, lead + 600, moons, reach, 100, out dv), Is.False, "a tilt is no use on a steep approach");
            Assert.That(MoonDodge.Approach(s, Mu, burnAt, lead + 600, moons, reach, rp * 0.99, rp * 1.01, 100, out dv), Is.True, "a timing change under 100 m/s clears it");
            Assert.That(dv.Length, Is.LessThan(100).And.GreaterThan(1));
            var after = new StateVector(s.Position, s.Velocity + dv);
            Assert.That(MoonDodge.FirstMeeting(after, Mu, burnAt, lead + 600, moons, reach), Is.EqualTo(double.PositiveInfinity), "clear all the way to periapsis");
            Assert.That(Kepler.Periapsis(after, Mu), Is.EqualTo(rp).Within(rp * 0.01), "same periapsis radius");
        }
    }
}
