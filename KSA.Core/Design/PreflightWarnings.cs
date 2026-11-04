using System;
using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// What a craft may be missing for a mission (KSA-70, KSA-71). Warnings, not refusals (2026-10-31, T: "warn before launch"):
    /// each one is something that has lost a crew on a real flight.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>Days out, waiting and back, by the sums, or NaN for worlds with no window. Set by the host.</summary>
        public Func<string, double> RoundTripDays { get; set; }

        private const double AfterBoostersMin = 0.8, LandingTwr = 1.2, ShadowCharge = 1000, SlowBurnSeconds = 300, RadiationDays = 500, ProbeCharge = 150;

        /// <summary>The length of a mission: what the catalogue says, or the real trip when that is longer (a Duna landing is not 120 days).</summary>
        public int TripDays(MissionType t)
        {
            if (t == null) return 0;
            double real = double.NaN;
            try { real = RoundTripDays != null ? RoundTripDays(t.Body) : double.NaN; }
            catch (Exception e) { Log.Warn("Trip length failed for " + t.Body + ": " + e.Message); }
            return double.IsNaN(real) ? t.DurationDays : Math.Max(t.DurationDays, (int)Math.Ceiling(real));
        }

        /// <summary>Every warning for this craft on this mission.</summary>
        public List<string> PreflightWarnings(PlayerDesign d, MissionType t)
        {
            var list = new List<string>();
            if (d == null || t == null || t.IsAircraft) return list;
            bool lands = t.Profile == MissionProfile.Land || t.Profile == MissionProfile.Base;
            bool leavesKerbin = !string.Equals(t.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            bool windowed = RoundTripDays != null && !double.IsNaN(RoundTripDays(t.Body));
            BodyData world = Requirements != null ? Requirements.Map.Get(t.Body) : null;

            if (lands && world != null && d.LanderMass > 0 && d.LanderThrust > 0)
            {
                double twr = d.LanderThrust / (d.LanderMass * 1000 * world.Gravity);
                if (twr < LandingTwr) list.Add("Weak for " + t.Body + ": the last engine gives " + twr.ToString("0.0") + " x its weight there. It may land but not lift off. Use a stronger engine.");
            }
            if (lands && !d.HasLegs) list.Add("No landing legs.");
            if (d.AfterBoostersTwr > 0 && d.AfterBoostersTwr < AfterBoostersMin) list.Add("Weak second stage: once the first stage goes, the thrust is only " + d.AfterBoostersTwr.ToString("0.0") + " x the weight, short of the " + AfterBoostersMin.ToString("0.0") + " it takes to keep climbing.");
            if (leavesKerbin && t.CrewSize > 0 && !d.HasHeatShield) list.Add("No heat shield for the return from " + t.Body + ": a pod's own ablator does not last.");
            if (LifeSupportOn && t.CrewSize > 0)
            {
                // The crew that will eat: the mission's, or everyone seated when the Director seated more (0.8.197: the window passed a
                // 2-crew mission while 3 sat aboard, and the pad flagged the same rocket red).
                int crew = Math.Max(1, Math.Max(t.CrewSize, d.SeatedCrew)), days = TripDays(t);
                // Oxygen counts with the food and water (0.8.202): the mk IIIe's 120,561 units were 1,080 days for three against a 1,150-day trip.
                double supply = LifeSupport.SupplyDays(d.Food, d.Water, d.Oxygen, crew, d.WaterRecyclers);
                if (supply < days + Math.Max(0, LifeSupport.MarginDays)) list.Add("Food, water and oxygen for " + Math.Floor(supply) + " days with " + crew + " aboard (" + LifeSupport.ShortestSupply(d.Food, d.Water, d.Oxygen, crew, d.WaterRecyclers) + " runs out first" + (d.WaterRecyclers > 0 ? ", water recycled" : string.Empty) + "), and the trip takes about " + days + ". Counted at " + LifeSupport.SafetyFactor.ToString("0.0") + " x the nominal rate.");
                if (windowed && days > RadiationDays) list.Add("About " + days + " days beyond Kerbin's field: check Radiation in Kerbalism's Planner.");
            }
            if (leavesKerbin && d.Charge < ShadowCharge && t.CrewSize > 0) list.Add("Batteries hold " + Math.Round(d.Charge) + " charge: too little for the dark side of an orbit, or for experiments running. Add batteries.");
            if (windowed && d.LanderThrust > 0 && d.LanderMass > 0)
            {
                double seconds = d.LanderMass * 1000 * 1050 / d.LanderThrust;
                if (seconds > SlowBurnSeconds) list.Add("A " + Math.Round(seconds / 60) + " minute departure burn wastes much of its speed. Use a stronger engine.");
            }
            if (t.IsUncrewed && leavesKerbin && d.Charge < ProbeCharge) list.Add("Batteries hold " + Math.Round(d.Charge) + " charge: a probe with no power cannot steer or call home.");
            if (d.StrandedTanks > 0) list.Add(d.StrandedTanks + " side tank" + (d.StrandedTanks == 1 ? "" : "s") + " with no fuel line: no engine can reach that fuel. Add a line from each tank to the core.");
            if (d.PadDecoupler) list.Add("The KV pod's decoupler is in the staging: the crew switches it off, but check it is not the next stage.");
            return list;
        }

    }
}
