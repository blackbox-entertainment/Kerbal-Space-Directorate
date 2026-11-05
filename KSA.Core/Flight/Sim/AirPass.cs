using System;
using KSA.Core.Flight.Orbital;

namespace KSA.Core.Flight.Sim
{
    /// <summary>A world as the off-game simulator sees it (KSA-75).</summary>
    public sealed class SimWorld
    {
        public string Name;
        public double Radius, Mu, AirTop;
        /// <summary>Height (m) and drag per metre per (m/s) of speed squared on the flown craft, highest first in log steps.</summary>
        public double[] Heights, Drag;

        /// <summary>Drag acceleration over speed squared at a height, 1/m: log steps between the measured heights, nothing at the top of the air.</summary>
        public double DragAt(double h)
        {
            if (h >= AirTop || Heights == null) return 0;
            if (h <= Heights[0]) return Drag[0];
            for (int i = 1; i < Heights.Length; i++)
                if (h <= Heights[i])
                {
                    double f = (h - Heights[i - 1]) / (Heights[i] - Heights[i - 1]);
                    return Math.Exp(Math.Log(Drag[i - 1]) * (1 - f) + Math.Log(Drag[i]) * f);
                }
            double last = Heights[Heights.Length - 1], f2 = (h - last) / (AirTop - last);
            return Drag[Drag.Length - 1] * (1 - f2);
        }

        /// <summary>Kerbin as a capsule feels it, fitted to the Minmus return of 2026-11-01 (41 km at 3.2 km/s took two passes), scale height 5.8 km.</summary>
        public static SimWorld KerbinCapsule()
        {
            return new SimWorld
            {
                Name = "Kerbin", Radius = 600000, Mu = 3.5316e12, AirTop = 70000,
                Heights = new double[] { 0, 20000, 30000, 41000, 50000, 60000 },
                Drag = new double[] { 8.3e-4, 2.6e-5, 4.5e-6, 7.7e-7, 1.6e-7, 3e-8 }
            };
        }

        /// <summary>Duna, fitted to the arrivals of 2026-11-01 (Mk I and Mk II landers, nose or engine first).</summary>
        public static SimWorld Duna()
        {
            return new SimWorld
            {
                Name = "Duna", Radius = 320000, Mu = 3.0136321e11, AirTop = 50000,
                Heights = new double[] { 0, 10000, 20000, 26500, 47700 },
                Drag = new double[] { 3.6e-5, 6e-6, 0.9e-6, 0.83e-6, 2.3e-9 }
            };
        }
    }

    /// <summary>What one pass through the air did.</summary>
    public struct PassResult
    {
        public bool Crashed, Trapped;
        public double ApA, PeA, LowestHeight, EnergyLost;
    }

    /// <summary>One coast through a world's air, with no thrust, stepped in time.</summary>
    public static class AirPass
    {
        private const double Step = 0.25;

        public static PassResult Fly(SimWorld w, double apA, double peA)
        {
            double rA = w.Radius + apA, rP = w.Radius + peA, a = (rA + rP) / 2, e = (rA - rP) / (rA + rP), p = a * (1 - e * e);
            return FlyConic(w, p, e, apA, peA);
        }

        /// <summary>
        /// One pass of a hyperbolic arrival (0.8.215): excess speed vInf, periapsis peA. ApA infinite afterwards means it was not captured.
        /// </summary>
        public static PassResult FlyIn(SimWorld w, double peA, double vInf)
        {
            double rP = w.Radius + peA, e = 1 + rP * vInf * vInf / w.Mu, p = rP * (1 + e);
            return FlyConic(w, p, e, double.PositiveInfinity, peA);
        }

        private static PassResult FlyConic(SimWorld w, double p, double e, double apA, double peA)
        {
            double rTop = w.Radius + w.AirTop;
            var result = new PassResult { ApA = apA, PeA = peA, LowestHeight = peA };
            if (peA >= w.AirTop) return result;
            // In at the top of the air, on the way down.
            double cos = (p / rTop - 1) / e, nu = -Math.Acos(Math.Max(-1, Math.Min(1, cos)));
            double h = Math.Sqrt(w.Mu * p);
            Vec3 r = new Vec3(rTop * Math.Cos(nu), rTop * Math.Sin(nu), 0);
            Vec3 v = new Vec3(-Math.Sin(nu), e + Math.Cos(nu), 0) * (w.Mu / h);
            double before = v.LengthSquared / 2 - w.Mu / r.Length;
            for (int i = 0; i < 400000; i++)
            {
                Vec3 acc = Accel(w, r, v);
                Vec3 mid = v + acc * (Step / 2);
                Vec3 rMid = r + v * (Step / 2);
                Vec3 accMid = Accel(w, rMid, mid);
                r += mid * Step;
                v += accMid * Step;
                double height = r.Length - w.Radius;
                if (height < result.LowestHeight) result.LowestHeight = height;
                if (height <= 0) { result.Crashed = true; return Finish(w, r, v, before, ref result); }
                if (height >= w.AirTop && Vec3.Dot(r, v) > 0) return Finish(w, r, v, before, ref result);
            }
            result.Trapped = true;
            return Finish(w, r, v, before, ref result);
        }

        private static Vec3 Accel(SimWorld w, Vec3 r, Vec3 v)
        {
            double d = r.Length, speed = v.Length;
            return r * (-w.Mu / (d * d * d)) - v * (w.DragAt(d - w.Radius) * speed);
        }

        private static PassResult Finish(SimWorld w, Vec3 r, Vec3 v, double before, ref PassResult result)
        {
            var s = new StateVector(r, v);
            double energy = Kepler.SpecificEnergy(s, w.Mu);
            result.EnergyLost = before - energy;
            result.ApA = energy < 0 ? Kepler.Apoapsis(s, w.Mu) - w.Radius : double.PositiveInfinity;
            result.PeA = Kepler.Periapsis(s, w.Mu) - w.Radius;
            // Still inside the air with the top of the orbit under its edge: it will not come out again.
            if (result.ApA < w.AirTop) result.Trapped = true;
            return result;
        }
    }

    /// <summary>What a crew does after a pass through the air.</summary>
    public enum AirbrakeStep { PassAgain, LiftClear, RaisePass, DeepenPass, ClimbOut }

    /// <summary>The aerobraking decisions, out of the flight leg so they can be flown in tests (KSA-75).</summary>
    public static class AirbrakeRules
    {
        public const int MaxPasses = 12;
        /// <summary>Fastest a capsule dives straight in at the top of the air, m/s (0.8.215: 4,187 m/s into a 27 km periapsis burned a KV-3 up at 45 km; Mun and Minmus returns at 3,100 to 3,300 and a plain Duna return at about 3,400 come home).</summary>
        public const double SafeDirectEntry = 3600;
        /// <summary>A skimming pass counts as a capture when the orbit after it tops out under this share of the sphere of influence.</summary>
        public const double CaptureApShare = 0.5;

        /// <summary>Speed at the top of the air for a hyperbolic excess.</summary>
        public static double EntrySpeed(double mu, double rTop, double vInf) { return Math.Sqrt(vInf * vInf + 2 * mu / rTop); }

        /// <summary>
        /// The highest periapsis between floor and ceiling (metres, 500 m steps) at which one pass captures an arrival with excess vInf:
        /// the orbit after it is closed and tops out under CaptureApShare of the sphere, and the pass neither crashes nor stays in the
        /// air. The highest such pass is the coolest. NaN when none captures.
        /// </summary>
        public static double CapturePeriapsis(SimWorld w, double vInf, double soi, double floor, double ceiling)
        {
            if (w == null || vInf <= 0 || ceiling < floor) return double.NaN;
            for (double peA = ceiling; peA >= floor - 1; peA -= 500)
            {
                PassResult r = AirPass.FlyIn(w, peA, vInf);
                if (r.Crashed || r.Trapped || double.IsInfinity(r.ApA) || double.IsNaN(r.ApA)) continue;
                if (r.ApA < CaptureApShare * soi) return peA;
            }
            return double.NaN;
        }
        public const double GoalMargin = 30000;   // metres of apoapsis over the orbit wanted: then stop
        public const double LowestShare = 0.45;   // of the air's depth: a lower periapsis is raised at apoapsis
        public const double PassShare = 0.7;      // of the air's depth: where arrivals aim
        public const double ShallowShare = 0.62;  // of the air's depth: a higher pass takes too many laps (31 km on Duna; 40 km was 12 passes)
        public const double DeepenShare = 0.6;    // of the air's depth: where a shallow pass is brought down to
        public const double PassMargin = 1.5;     // of the last pass's energy loss, allowed for the next

        /// <summary>The step after a pass, with the craft out of the air and on the way up. lastDrop is the energy that pass took, J/kg.</summary>
        /// <summary>Caught in the air, falling and low: the engine goes to landing, not to climbing (2026-11-01: a crew waited for its nose to come round and hit Duna at 346 m/s).</summary>
        public static bool TooLowToClimb(double altitude, double verticalSpeed, double air) { return altitude < ClimbFloorShare * air && verticalSpeed < 0; }

        public const double ClimbFloorShare = 0.5;

        public static AirbrakeStep AfterPass(int passes, double apA, double peA, double air, double wantedOrbit, double radius, double mu, double lastDrop)
        {
            double energy = -mu / (2 * radius + apA + peA);
            double wantedEnergy = -mu / (2 * radius + wantedOrbit + peA);
            if (apA <= wantedOrbit + GoalMargin || passes >= MaxPasses || energy - lastDrop * PassMargin < wantedEnergy) return AirbrakeStep.LiftClear;
            if (peA < LowestShare * air) return AirbrakeStep.RaisePass;
            if (passes >= 1 && peA > ShallowShare * air) return AirbrakeStep.DeepenPass;
            return AirbrakeStep.PassAgain;
        }
    }
}
