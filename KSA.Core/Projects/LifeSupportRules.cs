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
        /// <summary>Days a stranded crew lasts on what their craft carries (Kerbalism kills on its own clock; this is the simulated equivalent).</summary>
        public int StrandedLostAfterDays { get; set; } = 20;
        /// <summary>While a program is supplied (not overdue), keep the food, water and air of its live vessels topped up: KSA's resupply flights stand in for the cargo.</summary>
        public bool TopUpSuppliedVessels { get; set; } = true;
        /// <summary>A self-sufficient colony builds another greenhouse every this many years (0 = never).</summary>
        public int ColonyGreenhouseEveryYears { get; set; } = 2;

        /// <summary>Days of food and water for a crew, the smaller of the two. Infinite with no crew.</summary>
        public double SupplyDays(double food, double water, int crew)
        {
            if (crew <= 0) return double.PositiveInfinity;
            double f = FoodPerKerbalDay > 0 ? food / (FoodPerKerbalDay * crew) : double.PositiveInfinity;
            double w = WaterPerKerbalDay > 0 ? water / (WaterPerKerbalDay * crew) : double.PositiveInfinity;
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
            r.StrandedLostAfterDays = node.GetInt("strandedLostAfterDays", r.StrandedLostAfterDays);
            r.TopUpSuppliedVessels = node.GetBool("topUpSuppliedVessels", r.TopUpSuppliedVessels);
            r.ColonyGreenhouseEveryYears = node.GetInt("colonyGreenhouseEveryYears", r.ColonyGreenhouseEveryYears);
            return r;
        }
    }
}
