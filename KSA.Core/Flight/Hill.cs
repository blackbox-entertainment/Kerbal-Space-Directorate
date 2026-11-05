using System;
using KSA.Core.Flight.Orbital;

namespace KSA.Core.Flight
{
    /// <summary>
    /// Hill's (Clohessy-Wiltshire) equations: the motion of a chaser relative to a target on a near-circular orbit, to
    /// first order in their separation. The rendezvous push had aimed straight at the station (0.8.165); over the minutes
    /// a push takes, the orbit's curvature bends that line by kilometres, and on 2026-11-05 three pushes from 9, 10 and
    /// 26 km took the ship from 7 km short to 237 km (0.8.224).
    /// Frame: x radial (outward from the centre), y along-track (the way the target moves), z across (along the orbit's
    /// angular momentum). The target's rate n comes from its state.
    /// </summary>
    public static class Hill
    {
        /// <summary>The target's orbital rate from its state: |r x v| / r².</summary>
        public static double Rate(Vec3 rTarget, Vec3 vTarget)
        {
            double r = rTarget.Length;
            return r > 0 ? Vec3.Cross(rTarget, vTarget).Length / (r * r) : 0;
        }

        /// <summary>
        /// The relative velocity (chaser minus target, inertial frame) the chaser needs now, at relative position
        /// <paramref name="rho0"/>, to be at <paramref name="rhoAim"/> (relative to the target) after
        /// <paramref name="seconds"/>. Both relative vectors are in the inertial frame; the axes come from the target's
        /// state. NaN when the transit is a whole number of orbits (no solution).
        /// </summary>
        public static Vec3 ApproachVelocity(Vec3 rTarget, Vec3 vTarget, Vec3 rho0, Vec3 rhoAim, double seconds)
        {
            Vec3 x = rTarget.Normalized;
            Vec3 z = Vec3.Cross(rTarget, vTarget).Normalized;
            Vec3 y = Vec3.Cross(z, x);
            double n = Rate(rTarget, vTarget), T = seconds;
            double x0 = Vec3.Dot(rho0, x), y0 = Vec3.Dot(rho0, y), z0 = Vec3.Dot(rho0, z);
            double xa = Vec3.Dot(rhoAim, x), ya = Vec3.Dot(rhoAim, y), za = Vec3.Dot(rhoAim, z);
            double s = Math.Sin(n * T), c = Math.Cos(n * T);
            // x(T) = (4 - 3c) x0 + (s/n) xd + (2/n)(1 - c) yd
            // y(T) = 6(s - nT) x0 + y0 - (2/n)(1 - c) xd + ((4s - 3nT)/n) yd
            double a11 = s / n, a12 = 2 * (1 - c) / n, a21 = -2 * (1 - c) / n, a22 = (4 * s - 3 * n * T) / n;
            double b1 = xa - (4 - 3 * c) * x0, b2 = ya - 6 * (s - n * T) * x0 - y0;
            double det = a11 * a22 - a12 * a21;
            if (n <= 0 || Math.Abs(det) < 1e-9 || Math.Abs(s) < 1e-9) return new Vec3(double.NaN, double.NaN, double.NaN);
            double xd = (b1 * a22 - a12 * b2) / det, yd = (a11 * b2 - a21 * b1) / det;
            double zd = (za - c * z0) * n / s;
            Vec3 rhoDot = x * xd + y * yd + z * zd;        // in the rotating frame
            return rhoDot + Vec3.Cross(z * n, rho0);       // plus the frame's own turn: the inertial relative velocity
        }

        /// <summary>
        /// Where the chaser is relative to the target after <paramref name="seconds"/>, in the target's axes (radial,
        /// along-track, across), from its inertial relative velocity now. For checks and tests.
        /// </summary>
        public static Vec3 Propagate(Vec3 rTarget, Vec3 vTarget, Vec3 rho0, Vec3 vRel, double seconds)
        {
            Vec3 x = rTarget.Normalized;
            Vec3 z = Vec3.Cross(rTarget, vTarget).Normalized;
            Vec3 y = Vec3.Cross(z, x);
            double n = Rate(rTarget, vTarget), T = seconds;
            Vec3 rhoDot = vRel - Vec3.Cross(z * n, rho0);
            double x0 = Vec3.Dot(rho0, x), y0 = Vec3.Dot(rho0, y), z0 = Vec3.Dot(rho0, z);
            double xd = Vec3.Dot(rhoDot, x), yd = Vec3.Dot(rhoDot, y), zd = Vec3.Dot(rhoDot, z);
            double s = Math.Sin(n * T), c = Math.Cos(n * T);
            double xT = (4 - 3 * c) * x0 + s / n * xd + 2 * (1 - c) / n * yd;
            double yT = 6 * (s - n * T) * x0 + y0 - 2 * (1 - c) / n * xd + (4 * s - 3 * n * T) / n * yd;
            double zT = c * z0 + s / n * zd;
            return new Vec3(xT, yT, zT);
        }
    }
}
