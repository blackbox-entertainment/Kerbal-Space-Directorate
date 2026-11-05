using System;
using KSA.Core.Flight;
using KSA.Core.Flight.Orbital;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>0.8.224: the rendezvous push by Hill's equations, checked against a two-body integration.</summary>
    [TestFixture]
    public class HillTests
    {
        private const double Mu = 3.5316e12, R = 600000 + 85000; // Kerbin, an 85 km orbit

        [Test]
        public void TheSolutionLandsOnTheAimByItsOwnEquations()
        {
            Vec3 rT = new Vec3(R, 0, 0), vT = new Vec3(0, Math.Sqrt(Mu / R), 0);
            Vec3 rho0 = new Vec3(2000, 7000, 500), aim = new Vec3(0, -200, 0);
            Vec3 vRel = Hill.ApproachVelocity(rT, vT, rho0, aim, 500);
            Vec3 at = Hill.Propagate(rT, vT, rho0, vRel, 500);
            Assert.That((at - aim).Length, Is.LessThan(1), "x " + at.X + " y " + at.Y + " z " + at.Z);
        }

        /// <summary>
        /// The flight of 2026-11-05: pushes from 9, 10 and 26 km aimed straight along the line missed by 7, 21 and 237 km.
        /// Hill's answer meets a two-body integration to first order in the separation.
        /// </summary>
        [TestCase(2000, 9000, 300, 600)]
        [TestCase(-5000, -24000, 1000, 660)]
        [TestCase(0, 1000, 0, 120)]
        [TestCase(7000, 0, 0, 400)]
        public void TheSolutionMeetsTheTargetInTwoBodyMotion(double radial, double along, double across, double seconds)
        {
            Vec3 rT = new Vec3(R, 0, 0), vT = new Vec3(0, Math.Sqrt(Mu / R), 0);
            Vec3 rho0 = new Vec3(radial, along, across); // the target's axes coincide with the world axes at the start
            Vec3 vRel = Hill.ApproachVelocity(rT, vT, rho0, Vec3.Zero, seconds);
            Assert.That(double.IsNaN(vRel.X), Is.False);

            Vec3 rC = rT + rho0, vC = vT + vRel;
            Vec3 rS = rT + rho0, vS = vT - rho0.Normalized * (rho0.Length / seconds); // the old straight push, same transit
            Vec3 r = rT, v = vT;
            for (double t = 0; t < seconds - 1e-9; t += 0.5) { Step(ref r, ref v, 0.5); Step(ref rC, ref vC, 0.5); Step(ref rS, ref vS, 0.5); }
            double miss = (rC - r).Length, straight = (rS - r).Length;
            TestContext.WriteLine("from " + Math.Round(rho0.Length / 1000, 1) + " km in " + seconds + " s: Hill misses by " + Math.Round(miss) + " m at " + Math.Round(vRel.Length, 1) + " m/s, the straight push by " + Math.Round(straight / 1000, 1) + " km");
            Assert.That(miss, Is.LessThan(Math.Max(30, 4 * rho0.Length * rho0.Length / R)), "second order in the separation over the orbit's radius: 275 m from 9 km, 2.6 km from 25 km; the leg's next, short push closes that");
            if (rho0.Length > 5000) Assert.That(straight, Is.GreaterThan(10 * miss), "the straight line is what went wrong");
        }

        /// <summary>Two-body motion about the centre, one RK4 step.</summary>
        private static void Step(ref Vec3 r, ref Vec3 v, double dt)
        {
            Vec3 k1v = Accel(r), k1r = v;
            Vec3 k2v = Accel(r + k1r * (dt / 2)), k2r = v + k1v * (dt / 2);
            Vec3 k3v = Accel(r + k2r * (dt / 2)), k3r = v + k2v * (dt / 2);
            Vec3 k4v = Accel(r + k3r * dt), k4r = v + k3v * dt;
            r = r + (k1r + k2r * 2 + k3r * 2 + k4r) * (dt / 6);
            v = v + (k1v + k2v * 2 + k3v * 2 + k4v) * (dt / 6);
        }

        private static Vec3 Accel(Vec3 r)
        {
            double d = r.Length;
            return r * (-Mu / (d * d * d));
        }
    }
}
