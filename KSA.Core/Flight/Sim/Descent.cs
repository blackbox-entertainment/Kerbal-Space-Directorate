using System;
using KSA.Core.Flight.Orbital;

namespace KSA.Core.Flight.Sim
{
    /// <summary>The crew's powered descent decisions (KSA-77), used by LandingLeg and flown in the tests.</summary>
    public static class DescentRules
    {
        public const double AirBrakeHeight = 10000; // metres above terrain: no engine higher on a world with air (simulated: 8,000 is the edge of a crash, 15,000 costs 300 m/s more)
        public const double DeorbitPeShare = 0.4;   // of the air's depth: where a deorbit aims (20 km on Duna, cheapest in the simulator without skipping out)
        public const double AirFallFloor = 2000;    // metres above terrain: below it the normal descent speeds
        public const double AirFallShare = 0.7;     // of the speed the engine can just stop from

        public const double LongOrbit = 0.2;       // eccentricity above which the deorbit waits for the high end
        public const double NearApoapsis = 0.08;   // of a period, either side of apoapsis

        /// <summary>A long orbit is left from its high end: from the low end the same drop costs ten times the fuel (2026-11-01: 25 m/s against 300 from 742 x 70 km).</summary>
        public static bool TooFarFromApoapsis(double eccentricity, double period, double fromNow, double timeToAp)
        {
            if (eccentricity <= LongOrbit) return false;
            double fromAp = Math.Abs(fromNow - timeToAp) % period;
            return Math.Min(fromAp, period - fromAp) > period * NearApoapsis;
        }

        /// <summary>Empty tanks are only read with the throttle open: closed on purpose, no thrust is not an empty tank (2026-10-31: 2,711 m/s aboard, crashed at 278 m/s).</summary>
        public static bool OutOfFuel(double throttle, bool noThrust, bool noDeltaV) { return (throttle > 0.05 && noThrust) || noDeltaV; }

        /// <summary>The descent speed wanted at a height.</summary>
        public static double TargetSpeed(double above, double accel, double g, bool hasAir)
        {
            double target = DescentMath.TargetDescentSpeed(above);
            if (hasAir && above >= AirFallFloor && accel > g) target = Math.Max(target, AirFallShare * Math.Sqrt(2 * (accel - g) * above));
            return target;
        }

        /// <summary>When a coasting lander lights its engine. Safe is the stopping height from DescentMath.SafeBurnAltitude.</summary>
        public static bool BrakeNow(double above, double safe, bool hasAir, bool inAir, double descentRate, double engineFloor = AirBrakeHeight)
        {
            bool now = above <= safe;
            if (hasAir && above > engineFloor) now = false;
            if (hasAir && inAir && above < 3000 && descentRate > DescentMath.TargetDescentSpeed(above) * 1.1) now = true;
            return now;
        }

        /// <summary>Throttle for a descent: full when out of room, otherwise hover plus a share of the speed error.</summary>
        public static double Throttle(double descentRate, double target, double speed, double above, double accel, double g, double sideSpeed, bool holdingUp)
        {
            if (accel <= 0) return 1;
            double hover = Math.Min(1, g / accel);
            bool behind = descentRate > target && DescentMath.StoppingDistance(speed, accel, g) * 1.1 >= above;
            if (behind && above > 20) return 1;
            double t = hover + 0.5 * (descentRate - target) / accel;
            if (sideSpeed > 3 && !holdingUp) t = Math.Max(t, Math.Min(1, hover + sideSpeed / 20.0));
            return Math.Max(0, Math.Min(1, t));
        }
    }

    /// <summary>How a descent ended.</summary>
    public struct DescentResult
    {
        public bool Landed, OutOfFuel;
        public double TouchdownSpeed, SideSpeed, DeltaV, Seconds, BrakeHeight;
        public string Log;
    }

    /// <summary>A lander from the top of its path to the ground, stepped in time: the same rules, a simple world.</summary>
    public static class DescentSim
    {
        private const double Step = 0.05, G0 = 9.80665;

        public static DescentResult Fly(SimWorld w, double apA, double peA, double thrustNewtons, double isp, double wetKg, double dryKg, double burnMargin = 0.1, double engineFloor = DescentRules.AirBrakeHeight)
        {
            double rA = w.Radius + apA, a = (rA + w.Radius + peA) / 2;
            Vec3 r = new Vec3(rA, 0, 0), v = new Vec3(0, Math.Sqrt(w.Mu * (2 / rA - 1 / a)), 0);
            double mass = wetKg, flow = thrustNewtons / (isp * G0);
            bool hasAir = w.AirTop > 0, braking = false;
            var result = new DescentResult { Log = string.Empty };
            double nextLog = 0;
            for (double time = 0; time < 40000; time += Step)
            {
                double above = r.Length - w.Radius, g = w.Mu / r.LengthSquared;
                Vec3 up = r.Normalized;
                double vUp = Vec3.Dot(v, up), side = (v - up * vUp).Length, speed = v.Length, descent = -vUp, accel = thrustNewtons / mass;
                if (above <= 0)
                {
                    result.Landed = true; result.TouchdownSpeed = descent; result.SideSpeed = side; result.Seconds = time;
                    return result;
                }
                bool inAir = hasAir && above < w.AirTop, holdUp = above < 300 || speed < 10;
                double throttle = 0;
                if (!braking)
                {
                    double safe = DescentMath.SafeBurnAltitude(speed, accel, g, burnMargin);
                    if (double.IsInfinity(safe)) safe = above;
                    if (vUp < 0 && DescentRules.BrakeNow(above, safe, hasAir, inAir, descent, engineFloor)) { braking = true; result.BrakeHeight = above; }
                }
                if (braking)
                {
                    if (mass <= dryKg) { result.OutOfFuel = true; throttle = 0; }
                    else throttle = DescentRules.Throttle(descent, DescentRules.TargetSpeed(above, accel, g, hasAir), speed, above, accel, g, side, holdUp);
                }
                Vec3 thrustDir = holdUp || speed < 1 ? (up * 3 - (v - up * vUp) * 0.1).Normalized : (v * -1).Normalized;
                Vec3 acc = r * (-w.Mu / (r.Length * r.LengthSquared)) + thrustDir * (accel * throttle) - v * (w.DragAt(above) * speed);
                v += acc * Step;
                r += v * Step;
                if (throttle > 0) { mass -= flow * throttle * Step; result.DeltaV += accel * throttle * Step; }
                if (time >= nextLog && braking) { nextLog = time + 20; result.Log += Math.Round(above) + " m " + Math.Round(descent) + " down " + Math.Round(side) + " side, "; }
            }
            return result;
        }
    }
}
