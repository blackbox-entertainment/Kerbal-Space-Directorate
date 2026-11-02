using System;

namespace KSA.Core.Flight.Orbital
{
    /// <summary>A phasing orbit: one burn now, k revolutions, and the target is overhead.</summary>
    public sealed class PhasingPlan
    {
        /// <summary>Revolutions of the phasing orbit before the gap closes.</summary>
        public int Orbits;
        /// <summary>Period of the phasing orbit, seconds.</summary>
        public double Period;
        /// <summary>Burn at the planning point, central-body frame.</summary>
        public Vec3 DeltaV;
        public double DeltaVMagnitude;
        /// <summary>Periapsis distance of the phasing orbit.</summary>
        public double Periapsis;
        /// <summary>Apoapsis distance of the phasing orbit.</summary>
        public double Apoapsis;
    }

    /// <summary>
    /// Rendezvous phasing (0.8.163): a single Lambert arc cannot catch a target a third of an orbit away without diving
    /// through the planet or spending hundreds of m/s. Instead the chaser changes its period so that after k
    /// revolutions it comes back to this point as the target passes it; the short intercept is planned then.
    /// </summary>
    public static class Phasing
    {
        /// <summary>
        /// Plans the cheapest phasing burn at (r, v) for a target at rTarget on an orbit of targetPeriod, trying 1 to
        /// maxOrbits revolutions and both the shorter and the longer period. The phasing orbit must keep its periapsis
        /// at or above minRadius and its apoapsis at or below maxRadius. Null when nothing fits.
        /// </summary>
        public static PhasingPlan Plan(double mu, Vec3 r, Vec3 v, Vec3 rTarget, double targetPeriod, double minRadius, double maxRadius, int maxOrbits)
        {
            if (mu <= 0 || targetPeriod <= 0 || r.Length < 1 || v.Length < 1) return null;
            Vec3 n = Vec3.Cross(r, v);
            if (n.Length < 1e-9) return null;
            n = n.Normalized;
            double gap = LeadAngle(r, rTarget, n); // target ahead of us, radians in [0, 2 pi)
            PhasingPlan best = null;
            for (int k = 1; k <= Math.Max(1, maxOrbits); k++)
            {
                // After k of our revolutions the target must have gone k turns less the gap (shorter period: we catch up)
                // or k turns plus the rest of the circle (longer period: it catches us).
                double[] periods = { targetPeriod * (k - gap / (2 * Math.PI)) / k, targetPeriod * (k + (2 * Math.PI - gap) / (2 * Math.PI)) / k };
                foreach (double period in periods)
                {
                    if (period <= 0) continue;
                    double a = Math.Pow(mu * period * period / (4 * Math.PI * Math.PI), 1.0 / 3.0);
                    double rl = r.Length;
                    double speed2 = mu * (2 / rl - 1 / a);
                    if (speed2 <= 0) continue;
                    Vec3 v1 = v.Normalized * Math.Sqrt(speed2);
                    double pe = TransferPlanner.Periapsis(r, v1, mu);
                    double ap = 2 * a - pe;
                    if (pe < minRadius || ap > maxRadius) continue;
                    Vec3 dv = v1 - v;
                    if (best != null && dv.Length >= best.DeltaVMagnitude) continue;
                    best = new PhasingPlan { Orbits = k, Period = period, DeltaV = dv, DeltaVMagnitude = dv.Length, Periapsis = pe, Apoapsis = ap };
                }
            }
            return best;
        }

        /// <summary>Angle from r to rTarget measured prograde about the orbit normal, in [0, 2 pi).</summary>
        public static double LeadAngle(Vec3 r, Vec3 rTarget, Vec3 normal)
        {
            Vec3 a = Vec3.Exclude(normal, r), b = Vec3.Exclude(normal, rTarget);
            if (a.Length < 1e-9 || b.Length < 1e-9) return 0;
            a = a.Normalized; b = b.Normalized;
            double angle = Math.Atan2(Vec3.Dot(Vec3.Cross(a, b), normal), Vec3.Dot(a, b));
            if (angle < 0) angle += 2 * Math.PI;
            return angle;
        }
    }
}
