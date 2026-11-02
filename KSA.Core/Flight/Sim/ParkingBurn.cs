using System;
using KSA.Core.Flight.Orbital;

namespace KSA.Core.Flight.Sim
{
    /// <summary>What a circularisation burn did.</summary>
    public struct ParkingResult
    {
        public double ApA, PeA, DeltaV, Seconds;
        public bool FellBack;
    }

    /// <summary>The rules for starting the burn that finishes the climb to a parking orbit (KSA-76).</summary>
    public static class ParkingRules
    {
        public const double LowApoapsis = 40000;   // over the air: an apoapsis under this may burn early
        public const double LowBurnStart = 5000;   // over the air: where an early burn starts
        public const double PeMargin = 5000;       // over the air: the burn stops here

        /// <summary>
        /// A low apoapsis burns on the way up (2026-10-31, T), but only an engine that needs that long: a strong one lit early pushes the
        /// apoapsis up instead (2026-11-01: a Poodle at 75 km ended 188 x 75 km, in reach of the radiation belt).
        /// </summary>
        public static bool BurnEarly(double apA, double altitude, double air, double timeToAp, double burnSeconds, bool roundingOut)
        {
            return !roundingOut && apA < air + LowApoapsis && altitude >= air + LowBurnStart && burnSeconds > timeToAp;
        }
    }

    /// <summary>A coast and burn along the horizon at the top of a climb, above the air, stepped in time.</summary>
    public static class ParkingBurn
    {
        private const double Step = 0.1, Mu = 3.5316e12, Radius = 600000, G0 = 9.80665;

        /// <summary>From a height and speed, burn along the horizon once the rule says, until the low point clears the air by the margin.</summary>
        public static ParkingResult Fly(double altitude, double upSpeed, double sideSpeed, double air, double thrustNewtons, double massKg, double isp, double apGoal, bool oldRule)
        {
            Vec3 r = new Vec3(Radius + altitude, 0, 0), v = new Vec3(upSpeed, sideSpeed, 0);
            double mass = massKg, flow = thrustNewtons / (isp * G0), spent = 0, seconds = 0;
            bool burning = false;
            var result = new ParkingResult();
            for (int i = 0; i < 40000; i++)
            {
                var s = new StateVector(r, v);
                double ap = Kepler.Apoapsis(s, Mu) - Radius, pe = Kepler.Periapsis(s, Mu) - Radius, alt = r.Length - Radius;
                double vUp = Vec3.Dot(r.Normalized, v);
                if (!burning)
                {
                    double toTop = TimeToApoapsis(s), side = Math.Sqrt(Math.Max(0, v.LengthSquared - vUp * vUp));
                    double circular = Math.Sqrt(Mu / (Radius + ap)), atTop = Math.Sqrt(Math.Max(0, Mu * (2 / (Radius + ap) - 2 / (2 * Radius + ap + pe))));
                    double burnSeconds = (circular - atTop) * mass / thrustNewtons;
                    bool early = oldRule ? ap < air + ParkingRules.LowApoapsis && alt >= air + ParkingRules.LowBurnStart
                        : ParkingRules.BurnEarly(ap, alt, air, toTop, burnSeconds, false);
                    if (toTop <= Math.Max(5, burnSeconds / 2) || early || vUp < 0) burning = true;
                }
                else
                {
                    if (pe >= air + ParkingRules.PeMargin) { result.ApA = ap; result.PeA = pe; result.DeltaV = spent; result.Seconds = seconds; return result; }
                    if (alt < air) { result.FellBack = true; result.ApA = ap; result.PeA = pe; return result; }
                    seconds += Step;
                }
                Vec3 up = r.Normalized, along = new Vec3(-up.Y, up.X, 0);
                double a = burning ? thrustNewtons / mass : 0;
                // As the crew steers (AscentLeg.CircularizeDirection): nose up when low and no longer climbing.
                double pitch = 0;
                if (burning && alt < air + ParkingRules.PeMargin + 3000 && vUp < 30)
                {
                    double net = Mu / r.LengthSquared - (v.LengthSquared - vUp * vUp) / r.Length;
                    pitch = Math.Max(10 * Math.PI / 180, Math.Asin(Math.Min(0.9, Math.Max(0, net) / Math.Max(a, 0.1))));
                }
                Vec3 acc = r * (-Mu / Math.Pow(r.Length, 3)) + (along * Math.Cos(pitch) + up * Math.Sin(pitch)) * a;
                v += acc * Step;
                r += v * Step;
                if (burning) { mass -= flow * Step; spent += a * Step; }
            }
            result.FellBack = true;
            return result;
        }

        private static double TimeToApoapsis(StateVector s)
        {
            double vUp = Vec3.Dot(s.Position.Normalized, s.Velocity);
            if (vUp <= 0) return 0;
            double energy = Kepler.SpecificEnergy(s, Mu), a = -Mu / (2 * energy), r = s.Position.Length;
            double side = Vec3.Cross(s.Position, s.Velocity).Length;
            double e = Math.Sqrt(Math.Max(0, 1 - side * side / (Mu * a)));
            double ecc = Math.Acos(Math.Max(-1, Math.Min(1, (1 - r / a) / e)));
            double mean = ecc - e * Math.Sin(ecc);
            return (Math.PI - mean) * Math.Sqrt(a * a * a / Mu);
        }
    }
}
