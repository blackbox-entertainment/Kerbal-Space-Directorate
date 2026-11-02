using System;
using KSA.Core.Flight.Orbital;

namespace KSA.Core.Flight.Sim
{
    /// <summary>The crew's pitch program (KSA-76), used by AscentLeg and flown in the tests.</summary>
    public static class AscentRules
    {
        public const double TurnEnd = 60000, KickSpeed = 100, KickDone = 150;
        public const double TopAheadFull = 45, TopAheadEasy = 75, ClimbFloor = 25, ClimbFloorMax = 75;

        /// <summary>How far the nose may lean off the flight path, degrees: 8 in thick air (25 kPa or more), none in thin air (5 kPa or less).</summary>
        public static double MaxLean(double q)
        {
            if (q >= 25) return 8;
            if (q <= 5) return 90;
            return 8 + (25 - q) / 20.0 * 82;
        }

        /// <summary>The nose pitch that holds a rocket up against what its sideways speed does not carry, degrees.</summary>
        public static double HoldPitch(double accel, double need)
        {
            if (need <= 0) return 0;
            if (accel <= need) return ClimbFloorMax;
            return Math.Min(ClimbFloorMax, Math.Max(ClimbFloor, Math.Asin(need / accel) * 180 / Math.PI + 5));
        }

        /// <summary>The pitch wanted, degrees from the eastern horizon.</summary>
        public static double Pitch(double altitude, double surfaceSpeed, double air, double peA, double timeToAp, double upSpeed, double pathPitch, double q, double hold)
        {
            double alt = Math.Max(0, altitude);
            if (surfaceSpeed < KickSpeed) return 88;
            double program = Math.Max(0, 88 * (1 - Math.Sqrt(Math.Min(1, alt / TurnEnd))));
            if (surfaceSpeed < KickDone) return Math.Max(85, program);
            if (alt > 5000 && alt < air && peA < 0)
            {
                double close = Math.Max(0, Math.Min(1, (TopAheadEasy - timeToAp) / (TopAheadEasy - TopAheadFull)));
                program = Math.Max(program, upSpeed <= 0 ? hold : hold * close);
            }
            double band = MaxLean(q);
            if (alt < 20000) band = Math.Min(band, 10);
            return Math.Max(pathPitch - band, Math.Min(pathPitch + band, program));
        }
    }

    /// <summary>A lander off the ground of another world, as AirlessAscentLeg flies it, on to a circular orbit.</summary>
    public static class LiftoffSim
    {
        private const double Step = 0.05, G0 = 9.80665;

        /// <summary>Why the last flight failed.</summary>
        public static string Why = string.Empty;

        public static double Pitch(double sideSpeed, double circular, double upSpeed)
        {
            double pitch = 5 + 35 * Math.Max(0, 1 - sideSpeed / circular);
            return upSpeed < 5 ? Math.Max(pitch, 45) : pitch;
        }

        /// <summary>The delta-v from the ground to a circular orbit, or NaN when the tanks run dry first.</summary>
        public static double Fly(SimWorld w, double targetAltitude, double thrustNewtons, double isp, double wetKg, double dryKg)
        {
            Vec3 r = new Vec3(w.Radius + 1, 0, 0), v = Vec3.Zero;
            double mass = wetKg, flow = thrustNewtons / (isp * G0), spent = 0;
            bool coasting = false;
            for (double time = 0; time < 6000; time += Step)
            {
                double dist = r.Length, alt = dist - w.Radius;
                if (time > 5 && alt < 0) { Why = "ground at " + Math.Round(time) + " s, side " + Math.Round(Vec3.Dot(v, new Vec3(-r.Y, r.X, 0).Normalized)); return double.NaN; }
                Vec3 up = r.Normalized, east = new Vec3(-up.Y, up.X, 0);
                double vUp = Vec3.Dot(v, up), side = Vec3.Dot(v, east), speed = v.Length, circular = Math.Sqrt(w.Mu / dist);
                var s = new StateVector(r, v);
                double energy = Kepler.SpecificEnergy(s, w.Mu), ap = energy < 0 ? Kepler.Apoapsis(s, w.Mu) - w.Radius : double.PositiveInfinity;
                double accel = thrustNewtons / mass, throttle = 1;
                Vec3 nose;
                if (time < 4) nose = up;
                else if ((ap < targetAltitude || side < 20) && !coasting) { double p = Pitch(side, circular, vUp) * Math.PI / 180; nose = up * Math.Sin(p) + east * Math.Cos(p); }
                else
                {
                    coasting = true;
                    nose = east;
                    double pe = Kepler.Periapsis(s, w.Mu) - w.Radius;
                    if (pe >= w.AirTop + 1000 || pe >= targetAltitude * 0.9) return spent;
                    throttle = vUp > 5 && alt < ap - 500 ? 0 : 1;
                }
                if (mass <= dryKg) { Why = "dry at " + Math.Round(alt) + " m, Ap " + Math.Round(ap) + ", side " + Math.Round(side) + ", up " + Math.Round(vUp) + ", spent " + Math.Round(spent); return double.NaN; }
                Vec3 acc = r * (-w.Mu / (dist * dist * dist)) + nose * (accel * throttle) - v * (w.DragAt(alt) * speed);
                v += acc * Step;
                r += v * Step;
                mass -= flow * throttle * Step;
                spent += accel * throttle * Step;
            }
            return double.NaN;
        }
    }

    /// <summary>When a burn has spent what it was meant to (KSA-78).</summary>
    public static class BurnRules
    {
        public const double OverBurn = 1.03;

        /// <summary>A long burn leaves its node behind and the node's vector never empties: stop on what was really burned.</summary>
        public static bool Spent(double burned, double planned) { return burned >= planned * OverBurn + 1; }
    }

    /// <summary>A stage on the way up: engines, what they burn, what goes when it is empty. Boosters burn alongside the core.</summary>
    public sealed class SimStage
    {
        public double ThrustSea, ThrustVacuum, IspSea, IspVacuum, FuelKg, DropKg;
        public double BoosterSea, BoosterVacuum, BoosterIspSea, BoosterIspVacuum, BoosterFuelKg, BoosterDropKg;
    }

    /// <summary>How a climb ended.</summary>
    public struct AscentResult
    {
        public bool ReachedSpace;
        public double ApA, PeA, Altitude, UpSpeed, SideSpeed, MassKg, MaxPressure, Seconds, BoostersOutAt;
        public int StagesLeft;
    }

    /// <summary>A climb from the pad to engine cutoff on Kerbin, stepped in time, flown by AscentRules.</summary>
    public static class AscentSim
    {
        private const double Step = 0.1, Mu = 3.5316e12, Radius = 600000, Air = 70000, G0 = 9.80665, PadSpeed = 174.9;
        private const double SeaDensity = 1.225, Scale = 5800, SeaKpa = 101.325;

        public static AscentResult Fly(SimStage[] stages, double payloadKg, double dragArea, double targetAp)
        {
            double mass = payloadKg;
            var fuel = new double[stages.Length];
            var boost = new double[stages.Length];
            for (int i = 0; i < stages.Length; i++)
            {
                fuel[i] = stages[i].FuelKg; boost[i] = stages[i].BoosterFuelKg;
                mass += stages[i].FuelKg + stages[i].DropKg + stages[i].BoosterFuelKg + stages[i].BoosterDropKg;
            }
            Vec3 r = new Vec3(Radius, 0, 0), v = new Vec3(0, PadSpeed, 0);
            int s = 0;
            var result = new AscentResult();
            for (double time = 0; time < 1200; time += Step)
            {
                double dist = r.Length, alt = dist - Radius;
                if (alt < -1) return result;
                Vec3 up = r.Normalized, east = new Vec3(-up.Y, up.X, 0);
                Vec3 air = east * (PadSpeed * dist / Radius);
                Vec3 rel = v - air;
                double speed = rel.Length, rho = alt < Air ? SeaDensity * Math.Exp(-alt / Scale) * (1 - alt / Air) : 0, p = alt < Air ? SeaKpa * Math.Exp(-alt / Scale) * (1 - alt / Air) / SeaKpa : 0;
                double q = 0.5 * rho * speed * speed / 1000;
                result.MaxPressure = Math.Max(result.MaxPressure, q);
                double upSpeed = Vec3.Dot(rel, up), eastSpeed = Vec3.Dot(rel, east);
                var state = new StateVector(r, v);
                double energy = Kepler.SpecificEnergy(state, Mu);
                double ap = energy < 0 ? Kepler.Apoapsis(state, Mu) - Radius : double.PositiveInfinity, pe = Kepler.Periapsis(state, Mu) - Radius;
                if (ap >= targetAp && alt > 40000 || s >= stages.Length)
                {
                    result.ApA = ap; result.PeA = pe; result.Altitude = alt; result.UpSpeed = Vec3.Dot(v, up); result.SideSpeed = Vec3.Dot(v, east);
                    result.MassKg = mass; result.Seconds = time; result.StagesLeft = stages.Length - s; result.ReachedSpace = alt > Air || ap > Air;
                    return result;
                }
                SimStage st = stages[s];
                double thrust = 0, flowCore = 0, flowBoost = 0;
                if (fuel[s] > 0) { double isp = st.IspVacuum + (st.IspSea - st.IspVacuum) * p; double t = st.ThrustVacuum * isp / st.IspVacuum; thrust += t; flowCore = t / (isp * G0); }
                if (boost[s] > 0) { double isp = st.BoosterIspVacuum + (st.BoosterIspSea - st.BoosterIspVacuum) * p; double t = st.BoosterVacuum * isp / st.BoosterIspVacuum; thrust += t; flowBoost = t / (isp * G0); }
                double accel = thrust / mass;
                double need = Mu / (dist * dist) - (v.LengthSquared - Math.Pow(Vec3.Dot(v, up), 2)) / dist;
                double pathPitch = Math.Atan2(upSpeed, eastSpeed) * 180 / Math.PI;
                double toTop = need > 0 && Vec3.Dot(v, up) > 0 ? Vec3.Dot(v, up) / need : 0;
                double pitch = AscentRules.Pitch(alt, speed, Air, pe, toTop, Vec3.Dot(v, up), pathPitch, q, AscentRules.HoldPitch(accel, need)) * Math.PI / 180;
                Vec3 nose = up * Math.Sin(pitch) + east * Math.Cos(pitch);
                Vec3 drag = speed > 0 ? rel * (-0.5 * rho * dragArea * speed / mass) : Vec3.Zero;
                v += (r * (-Mu / (dist * dist * dist)) + nose * accel + drag) * Step;
                r += v * Step;
                mass -= (flowCore + flowBoost) * Step;
                fuel[s] -= flowCore * Step; boost[s] -= flowBoost * Step;
                if (boost[s] <= 0 && st.BoosterDropKg > 0 && st.BoosterFuelKg > 0) { mass -= st.BoosterDropKg; boost[s] = 0; st = stages[s]; result.BoostersOutAt = result.BoostersOutAt > 0 ? result.BoostersOutAt : time; stages[s] = Without(st); }
                if (fuel[s] <= 0 && boost[s] <= 0) { mass -= stages[s].DropKg; s++; }
            }
            return result;
        }

        private static SimStage Without(SimStage st)
        {
            return new SimStage { ThrustSea = st.ThrustSea, ThrustVacuum = st.ThrustVacuum, IspSea = st.IspSea, IspVacuum = st.IspVacuum, FuelKg = st.FuelKg, DropKg = st.DropKg };
        }
    }
}
