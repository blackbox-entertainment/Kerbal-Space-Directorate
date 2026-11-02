using System;

namespace KSA.Core.Flight.Orbital
{
    /// <summary>
    /// Keeping a long orbit clear of a world's moons (2026-11-01: a 14,747 km Duna orbit ran into Ike on the way down to its first air pass).
    /// The way round turns the velocity about the local vertical: same speed and height, so the low point stays where it was, and at the top of a long orbit it costs little.
    /// </summary>
    public static class MoonDodge
    {
        /// <summary>Stay this many times a moon's sphere of influence away from it.</summary>
        public const double Reach = 1.25;

        private const double MinStep = 30, MoonSpeedAllowance = 2500, DegToRad = Math.PI / 180;
        private const int MaxSteps = 5000;
        private static readonly double[] Tilts = { 3, 6, 10, 15, 20, 30, 40, 50 };

        /// <summary>The first time between from and until the ship comes within reach of a moon, or infinity. The ship's state is at from.</summary>
        public static double FirstMeeting(StateVector ship, double mu, double from, double until, Func<double, Vec3>[] moons, double[] reach)
        {
            double t = from;
            for (int i = 0; i < MaxSteps && t <= until; i++)
            {
                StateVector s = Kepler.Propagate(ship, mu, t - from);
                double gap = double.PositiveInfinity;
                for (int m = 0; m < moons.Length; m++) gap = Math.Min(gap, (s.Position - moons[m](t)).Length - reach[m]);
                if (gap <= 0) return t;
                t += Math.Max(MinStep, 0.8 * gap / (s.Velocity.Length + MoonSpeedAllowance));
            }
            return double.PositiveInfinity;
        }

        /// <summary>The smallest turn of the velocity, within maxCost m/s, that misses every moon up to until. False when none does.</summary>
        public static bool Tilt(StateVector atBurn, double mu, double burnUt, double until, Func<double, Vec3>[] moons, double[] reach, double maxCost, out Vec3 deltaV)
        {
            deltaV = Vec3.Zero;
            Vec3 axis = atBurn.Position.Normalized;
            foreach (double degrees in Tilts)
                foreach (double sign in new double[] { 1, -1 })
                {
                    double a = sign * degrees * DegToRad;
                    // Rodrigues: the velocity turned about the vertical.
                    Vec3 v = atBurn.Velocity;
                    Vec3 turned = v * Math.Cos(a) + Vec3.Cross(axis, v) * Math.Sin(a) + axis * (Vec3.Dot(axis, v) * (1 - Math.Cos(a)));
                    Vec3 dv = turned - v;
                    if (dv.Length > maxCost) continue;
                    if (double.IsPositiveInfinity(FirstMeeting(new StateVector(atBurn.Position, turned), mu, burnUt, until, moons, reach))) { deltaV = dv; return true; }
                }
            return false;
        }
    }
}
