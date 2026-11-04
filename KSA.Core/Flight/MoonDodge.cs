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

        private static readonly double[] Alongs = { 0, 3, -3, 6, -6, 10, -10, 15, -15, 20, -20, 30, -30, 45, -45, 60, -60, 80, -80 };

        /// <summary>
        /// A moon across an approach (0.8.199, 2026-11-04: Ike two days before a Duna capture bent the path into the ground). A tilt
        /// about the vertical does almost nothing on a path falling nearly straight in, so the dodge is a change of timing: a burn
        /// along the velocity, so the moon has moved on when the ship crosses its orbit, with a horizontal part that keeps the
        /// periapsis radius within [rpLo, rpHi]. The cheapest within maxCost that clears every moon up to until; false when none does.
        /// </summary>
        public static bool Approach(StateVector atBurn, double mu, double burnUt, double until, Func<double, Vec3>[] moons, double[] reach, double rpLo, double rpHi, double maxCost, out Vec3 deltaV)
        {
            deltaV = Vec3.Zero;
            double best = double.PositiveInfinity;
            Vec3 along = atBurn.Velocity.Normalized;
            Vec3 normal = Vec3.Cross(atBurn.Position, atBurn.Velocity);
            if (normal.Length < 1e-6) return false;
            // Square to the velocity in the orbital plane, on the side away from the body: up on a round orbit, horizontal and forward
            // on a steep fall. It adds angular momentum, which is what raises a periapsis.
            Vec3 outward = Vec3.Cross(atBurn.Velocity, normal).Normalized;
            foreach (double p in Alongs)
            {
                if (Math.Abs(p) > maxCost) continue;
                Vec3 v0 = atBurn.Velocity + along * p;
                double rp0 = Kepler.Periapsis(new StateVector(atBurn.Position, v0), mu);
                double lo, hi;
                if (rp0 < rpLo) { lo = 0; hi = maxCost; }
                else if (rp0 > rpHi)
                {
                    // Taking horizontal speed off lowers the periapsis only until the orbit stops going forward.
                    double sideways = Vec3.Cross(atBurn.Position.Normalized, v0).Length;
                    lo = -Math.Min(maxCost, Math.Max(0, sideways - 1)); hi = 0;
                }
                else { lo = 0; hi = 0; }
                double q = 0;
                double rpLoV = Kepler.Periapsis(new StateVector(atBurn.Position, v0 + outward * lo), mu);
                double rpHiV = Kepler.Periapsis(new StateVector(atBurn.Position, v0 + outward * hi), mu);
                if (rpLoV > rpHi || rpHiV < rpLo) continue;
                for (int i = 0; i < 30 && hi - lo > 0.01; i++)
                {
                    q = (lo + hi) / 2;
                    double rp = Kepler.Periapsis(new StateVector(atBurn.Position, v0 + outward * q), mu);
                    if (rp < rpLo) lo = q; else if (rp > rpHi) hi = q; else break;
                }
                Vec3 v1 = v0 + outward * q;
                double rpq = Kepler.Periapsis(new StateVector(atBurn.Position, v1), mu);
                if (rpq < rpLo || rpq > rpHi) continue;
                Vec3 dv = v1 - atBurn.Velocity;
                if (dv.Length > maxCost || dv.Length >= best) continue;
                if (double.IsPositiveInfinity(FirstMeeting(new StateVector(atBurn.Position, v1), mu, burnUt, until, moons, reach))) { best = dv.Length; deltaV = dv; }
            }
            return best < double.PositiveInfinity;
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
