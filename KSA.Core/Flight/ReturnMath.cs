using System;

namespace KSA.Core.Flight.Orbital
{
    /// <summary>
    /// The way home from a moon, worked out by the two-body sums alone (2026-10-31: the search through the game's path preview
    /// found no return at all from a 50 km Mun orbit with the fuel aboard, and the Director could not find it by hand).
    /// </summary>
    public static class ReturnMath
    {
        /// <summary>
        /// Periapsis radius around the home world after leaving the moon from the given state (relative to the moon), or NaN when
        /// the path never leaves the moon. moonAt gives the moon's state around home, seconds after the start.
        /// </summary>
        public static double HomePeriapsis(StateVector start, double moonMu, double moonSoi, double homeMu, Func<double, StateVector> moonAt)
        {
            double r0 = start.Position.Length;
            if (r0 >= moonSoi) return double.NaN;
            Vec3 h = Vec3.Cross(start.Position, start.Velocity);
            double hLen = h.Length;
            if (hLen <= 0) return double.NaN;
            Vec3 eVec = Vec3.Cross(start.Velocity, h) / moonMu - start.Position / r0;
            double e = eVec.Length;
            if (e < 1e-6) return double.NaN;
            double p = hLen * hLen / moonMu;
            if (p / (1 + e) >= moonSoi) return double.NaN;
            double cosExit = (p / moonSoi - 1) / e;
            if (cosExit < -1 || cosExit > 1) return double.NaN; // an orbit that stays inside the sphere
            double exit = Math.Acos(cosExit);
            double cosStart = Math.Max(-1, Math.Min(1, (p / r0 - 1) / e));
            double begin = Math.Acos(cosStart);
            if (Vec3.Dot(start.Position, start.Velocity) < 0) begin = -begin;

            double seconds = Seconds(e, p, moonMu, begin, exit);
            if (double.IsNaN(seconds) || seconds < 0) return double.NaN;

            Vec3 pHat = eVec / e;
            Vec3 qHat = Vec3.Cross(h, pHat).Normalized;
            Vec3 pos = (pHat * Math.Cos(exit) + qHat * Math.Sin(exit)) * moonSoi;
            Vec3 vel = (pHat * -Math.Sin(exit) + qHat * (e + Math.Cos(exit))) * (moonMu / hLen);

            StateVector moon = moonAt(seconds);
            Vec3 r = moon.Position + pos;
            Vec3 v = moon.Velocity + vel;
            double energy = v.LengthSquared / 2 - homeMu / r.Length;
            double h2 = Vec3.Cross(r, v).Length;
            double e2 = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h2 * h2 / (homeMu * homeMu)));
            return h2 * h2 / homeMu / (1 + e2);
        }

        /// <summary>Time along an orbit between two true anomalies.</summary>
        private static double Seconds(double e, double p, double mu, double from, double to)
        {
            if (e < 1)
            {
                double a = p / (1 - e * e);
                double n = Math.Sqrt(mu / (a * a * a));
                Func<double, double> mean = f =>
                {
                    double ea = 2 * Math.Atan(Math.Sqrt((1 - e) / (1 + e)) * Math.Tan(f / 2));
                    return ea - e * Math.Sin(ea);
                };
                return (mean(to) - mean(from)) / n;
            }
            double ah = p / (e * e - 1);
            double nh = Math.Sqrt(mu / (ah * ah * ah));
            Func<double, double> meanH = f =>
            {
                double t = Math.Sqrt((e - 1) / (e + 1)) * Math.Tan(f / 2);
                if (Math.Abs(t) >= 1) return double.NaN;
                double fa = Math.Log((1 + t) / (1 - t)); // 2 atanh
                return e * Math.Sinh(fa) - fa;
            };
            return (meanH(to) - meanH(from)) / nh;
        }
    }
}
