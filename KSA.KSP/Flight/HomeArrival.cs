using System;
using KSA.Core.Flight.Sim;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// How fast a crew will meet the air coming home, and what to do about it (0.8.215, 2026-11-04: the mk IIIe took a 2,326 m/s
    /// excess arrival to beat its oxygen, dived to a 27 km periapsis at 4,187 m/s and burned up at 45 km; three dead). Over
    /// <see cref="AirbrakeRules.SafeDirectEntry"/> the capsule skims the air at a periapsis the off-game simulator says captures it,
    /// alone, over as many passes as it takes, and the last of the propellant goes into a retro burn first.
    /// </summary>
    public static class HomeArrival
    {
        /// <summary>Half-width of the periapsis band aimed at for a skimming pass, metres.</summary>
        public const double PassBand = 1500;
        /// <summary>Lowest and highest pass periapsis considered on Kerbin, metres.</summary>
        public const double PassFloor = 36000, PassCeiling = 56000;

        /// <summary>The simulator's world for a capsule at this body, or null when it has none.</summary>
        public static SimWorld World(CelestialBody home)
        {
            return home != null && home.bodyName == "Kerbin" ? SimWorld.KerbinCapsule() : null;
        }

        /// <summary>Speed at the top of the air on the vessel's predicted path at home, m/s; NaN when the path never gets there.</summary>
        public static double EntrySpeed(Vessel v, CelestialBody home)
        {
            if (v == null || home == null || !home.atmosphere) return double.NaN;
            Orbit o = v.mainBody == home ? v.orbit : TransferSupport.VesselPatchAround(v, home);
            if (o == null || o.semiMajorAxis == 0) return double.NaN;
            double rTop = home.Radius + home.atmosphereDepth;
            double energy = -home.gravParameter / (2 * o.semiMajorAxis);
            return Math.Sqrt(Math.Max(0, 2 * (energy + home.gravParameter / rTop)));
        }

        /// <summary>Speed at the top of the air for an arrival with this hyperbolic excess.</summary>
        public static double EntrySpeed(CelestialBody home, double vInf)
        {
            if (home == null) return double.NaN;
            return AirbrakeRules.EntrySpeed(home.gravParameter, home.Radius + (home.atmosphere ? home.atmosphereDepth : 0), vInf);
        }

        /// <summary>Hyperbolic excess on the vessel's predicted path at home, m/s; NaN when it is not hyperbolic there.</summary>
        public static double ExcessAt(Vessel v, CelestialBody home)
        {
            if (v == null || home == null) return double.NaN;
            Orbit o = v.mainBody == home ? v.orbit : TransferSupport.VesselPatchAround(v, home);
            if (o == null || o.eccentricity < 1 || o.semiMajorAxis >= 0) return double.NaN;
            return Math.Sqrt(home.gravParameter / -o.semiMajorAxis);
        }

        /// <summary>Too fast for a direct dive: the capsule skims instead.</summary>
        public static bool Hot(Vessel v, CelestialBody home, out double entry)
        {
            entry = EntrySpeed(v, home);
            return !double.IsNaN(entry) && entry > AirbrakeRules.SafeDirectEntry;
        }

        /// <summary>The highest periapsis at which one pass captures a capsule arriving with this excess, metres; NaN when none does or the world is unknown to the simulator.</summary>
        public static double PassPeriapsis(CelestialBody home, double vInf)
        {
            SimWorld w = World(home);
            if (w == null || double.IsNaN(vInf)) return double.NaN;
            return AirbrakeRules.CapturePeriapsis(w, vInf, home.sphereOfInfluence, PassFloor, PassCeiling);
        }

        /// <summary>The most hyperbolic excess a direct entry at this body can take (SafeDirectEntry at the top of the air), m/s; the fallback where there is no air.</summary>
        public static double MaxExcess(CelestialBody home, double fallback)
        {
            if (home == null || !home.atmosphere) return fallback;
            double rTop = home.Radius + home.atmosphereDepth;
            double cap = Math.Sqrt(Math.Max(0, AirbrakeRules.SafeDirectEntry * AirbrakeRules.SafeDirectEntry - 2 * home.gravParameter / rTop));
            return Math.Min(fallback, cap);
        }

        /// <summary>A note for a plan line: what an arrival excess means at the top of the air.</summary>
        public static string EntryNote(CelestialBody home, double vInf)
        {
            if (home == null || !home.atmosphere) return string.Empty;
            double entry = EntrySpeed(home, vInf);
            return " That is " + Math.Round(entry) + " m/s at the top of the air" + (entry > AirbrakeRules.SafeDirectEntry ? ": skimming passes on the capsule, not a dive." : ".");
        }
    }
}
