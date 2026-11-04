using System;
using KSA.Core.Data;

namespace KSA.Core.Projects
{
    /// <summary>
    /// Life support (KSA-67): the LIFE_SUPPORT block of KSA_AGENCY_SETTINGS. Only applies when the host reports a life
    /// support mod (Kerbalism); without one, colonies need a drill, an ore converter and power, as before.
    /// With it, a self-sufficient place must also grow its own food (greenhouses) and clean its own air and water
    /// (scrubbers and water recyclers, Kerbalism capacity counted in crew), and live flights must carry enough supplies.
    /// Rates are Kerbalism's default profile per 6-hour Kerbin day.
    /// </summary>
    public sealed class LifeSupportRules
    {
        public const string NodeName = "LIFE_SUPPORT";

        /// <summary>Use the life support mod when it is installed. False = ignore it (colonies use the stock rules).</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Residents one greenhouse feeds. Kerbalism's greenhouse at full light grows food for about nine.</summary>
        public int ResidentsPerGreenhouse { get; set; } = 8;
        /// <summary>Also require scrubber and water recycler capacity for every resident.</summary>
        public bool RequireRecyclers { get; set; } = true;
        /// <summary>Food and water a kerbal uses per day (Kerbalism default profile: 2 meals, 4 drinks).</summary>
        public double FoodPerKerbalDay { get; set; } = 0.2624;
        public double WaterPerKerbalDay { get; set; } = 0.1344;
        /// <summary>Extra days of supplies a live flight must carry beyond its planned length.</summary>
        public int MarginDays { get; set; } = 2;
        /// <summary>
        /// Consumption is taken as this many times the nominal rate when counting supply days (0.8.193): a crew drank water at twice
        /// Kerbalism's nominal rate on the way home from Duna and died 73 days short of Kerbin with the sums satisfied.
        /// </summary>
        public double SafetyFactor { get; set; } = 1.5;
        /// <summary>Share of the water a Kerbalism water recycler gives back (its profile: 0.127 of 0.149 an hour).</summary>
        public double WaterRecyclerRecovery { get; set; } = 0.85;
        /// <summary>Days a stranded crew lasts on what their craft carries (Kerbalism kills on its own clock; this is the simulated equivalent).</summary>
        public int StrandedLostAfterDays { get; set; } = 20;
        /// <summary>While a program is supplied (not overdue), keep the food, water and air of its live vessels topped up: KSA's resupply flights stand in for the cargo.</summary>
        public bool TopUpSuppliedVessels { get; set; } = true;
        /// <summary>A self-sufficient colony builds another greenhouse every this many years (0 = never).</summary>
        public int ColonyGreenhouseEveryYears { get; set; } = 2;

        /// <summary>Days of food and water for a crew, the smaller of the two. Infinite with no crew.</summary>
        public double SupplyDays(double food, double water, int crew) { return SupplyDays(food, water, crew, 0); }

        /// <summary>As above, with water recyclers aboard: each covers one crew member's water at the recovery share (0.8.194).</summary>
        public double SupplyDays(double food, double water, int crew, double waterRecyclers)
        {
            if (crew <= 0) return double.PositiveInfinity;
            double factor = SafetyFactor > 0 ? SafetyFactor : 1;
            double cover = Math.Max(0, Math.Min(1, waterRecyclers / crew));
            double waterRate = WaterPerKerbalDay * (1 - Math.Max(0, Math.Min(0.99, WaterRecyclerRecovery)) * cover);
            double f = FoodPerKerbalDay > 0 ? food / (FoodPerKerbalDay * crew * factor) : double.PositiveInfinity;
            double w = waterRate > 0 ? water / (waterRate * crew * factor) : double.PositiveInfinity;
            return Math.Min(f, w);
        }

        public static LifeSupportRules Parse(DataNode node)
        {
            var r = new LifeSupportRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.ResidentsPerGreenhouse = Math.Max(1, node.GetInt("residentsPerGreenhouse", r.ResidentsPerGreenhouse));
            r.RequireRecyclers = node.GetBool("requireRecyclers", r.RequireRecyclers);
            r.FoodPerKerbalDay = node.GetDouble("foodPerKerbalDay", r.FoodPerKerbalDay);
            r.WaterPerKerbalDay = node.GetDouble("waterPerKerbalDay", r.WaterPerKerbalDay);
            r.MarginDays = node.GetInt("marginDays", r.MarginDays);
            r.SafetyFactor = node.GetDouble("supplySafetyFactor", r.SafetyFactor);
            r.WaterRecyclerRecovery = node.GetDouble("waterRecyclerRecovery", r.WaterRecyclerRecovery);
            r.StrandedLostAfterDays = node.GetInt("strandedLostAfterDays", r.StrandedLostAfterDays);
            r.TopUpSuppliedVessels = node.GetBool("topUpSuppliedVessels", r.TopUpSuppliedVessels);
            r.ColonyGreenhouseEveryYears = node.GetInt("colonyGreenhouseEveryYears", r.ColonyGreenhouseEveryYears);
            return r;
        }
    }
}
