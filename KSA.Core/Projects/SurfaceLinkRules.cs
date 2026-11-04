using System;
using KSA.Core.Data;

namespace KSA.Core.Projects
{
    /// <summary>
    /// The surface hose (KSA-90, 2026-11-03, T: "I figured 100m would be about the limit of a hose connection"): craft landed within
    /// RangeMetres of each other on the same world share fuel, supplies and charge as if a hose ran between them. The crewed craft
    /// draws, the others give, each keeping a reserve of its own. Also where a lander aims when a base is on the ground (KSA-89).
    /// Loaded from agency.cfg SURFACE_LINK.
    /// </summary>
    public sealed class SurfaceLinkRules
    {
        public const string NodeName = "SURFACE_LINK";

        public bool Enabled { get; set; } = true;
        /// <summary>Metres between two landed craft within which the hose connects.</summary>
        public double RangeMetres { get; set; } = 100;
        /// <summary>Units of each resource moved a second (the refuel leg's rate).</summary>
        public double PumpUnitsPerSecond { get; set; } = 15;
        /// <summary>Share of a donor's capacity of each propellant or supply it keeps for itself.</summary>
        public double ReserveFraction { get; set; } = 0.05;
        /// <summary>Share of a donor's battery it keeps.</summary>
        public double ChargeReserveFraction { get; set; } = 0.10;
        /// <summary>Metres from a base's centre a lander aims to touch down (KSA-89: 80 to 100, within the hose and clear of the base).</summary>
        public double LandingOffset { get; set; } = 90;

        public static SurfaceLinkRules Parse(DataNode node)
        {
            var r = new SurfaceLinkRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.RangeMetres = Math.Max(1, node.GetDouble("range", r.RangeMetres));
            r.PumpUnitsPerSecond = Math.Max(0.1, node.GetDouble("pumpUnitsPerSecond", r.PumpUnitsPerSecond));
            r.ReserveFraction = Math.Max(0, Math.Min(0.9, node.GetDouble("reserveFraction", r.ReserveFraction)));
            r.ChargeReserveFraction = Math.Max(0, Math.Min(0.9, node.GetDouble("chargeReserveFraction", r.ChargeReserveFraction)));
            r.LandingOffset = Math.Max(20, node.GetDouble("landingOffset", r.LandingOffset));
            return r;
        }

        /// <summary>
        /// Units to move this step: never more than the receiver's room, the pump's rate, or what the donor has above its reserve
        /// (reserveFraction of its capacity).
        /// </summary>
        public static double Share(double available, double capacity, double reserveFraction, double room, double maxUnits)
        {
            double spare = available - Math.Max(0, capacity) * Math.Max(0, reserveFraction);
            return Math.Max(0, Math.Min(spare, Math.Min(Math.Max(0, room), Math.Max(0, maxUnits))));
        }
    }
}
