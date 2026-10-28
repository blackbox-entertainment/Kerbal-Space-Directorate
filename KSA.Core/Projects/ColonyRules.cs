using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Projects
{
    /// <summary>
    /// How bases grow into settlements and colonies (KSA-64): the COLONY block of KSA_AGENCY_SETTINGS.
    /// Outpost (first module down) > Base (all modules) > Settlement (rocket on site, resupplies, residents)
    /// > Colony (self-sufficient hardware, more resupplies and residents). Stages never go backwards.
    /// </summary>
    public sealed class ColonyRules
    {
        public const string NodeName = "COLONY";

        public bool Enabled { get; set; } = true;
        public int SettlementResupplies { get; set; } = 3;
        public int SettlementResidents { get; set; } = 4;
        public int ColonyResupplies { get; set; } = 6;
        public int ColonyResidents { get; set; } = 8;
        /// <summary>Settlers a successful resupply brings once the program is a finished base (limited by free room).</summary>
        public int SettlersPerResupply { get; set; } = 2;
        /// <summary>Room each successful resupply adds (habitat stores, inflatables).</summary>
        public int RoomPerResupply { get; set; } = 2;
        /// <summary>Settlement supply interval multiplier (a settlement grows some of its own).</summary>
        public double SettlementSupplyFactor { get; set; } = 2.0;
        /// <summary>Chance per male/female pair per year of a new resident, when self-sufficient and there is room.</summary>
        public double BirthChancePerPair { get; set; } = 0.25;
        /// <summary>Room a colony builds for itself each year, plus one per this many residents.</summary>
        public int ColonyRoomPerYear { get; set; } = 2;
        public int ColonyRoomPerResidents { get; set; } = 10;
        /// <summary>Yearly yield per resident of a colony (settlements earn half).</summary>
        public double SciencePerResident { get; set; } = 1.5;
        public double FundsPerResident { get; set; } = 1000;
        public double PrestigePerColony { get; set; } = 1;
        /// <summary>Chance per year that a colony of at least this many colonists sends a colony-born walk-in applicant.</summary>
        public double ComeOfAgeChance { get; set; } = 0.25;
        public int ComeOfAgeMinColonists { get; set; } = 6;
        /// <summary>Recruiting source used for colony-born applicants (recruiting.cfg).</summary>
        public string BornSource { get; set; } = "ColonyBorn";

        public static ColonyRules Parse(DataNode node)
        {
            var r = new ColonyRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.SettlementResupplies = node.GetInt("settlementResupplies", r.SettlementResupplies);
            r.SettlementResidents = node.GetInt("settlementResidents", r.SettlementResidents);
            r.ColonyResupplies = node.GetInt("colonyResupplies", r.ColonyResupplies);
            r.ColonyResidents = node.GetInt("colonyResidents", r.ColonyResidents);
            r.SettlersPerResupply = node.GetInt("settlersPerResupply", r.SettlersPerResupply);
            r.RoomPerResupply = node.GetInt("roomPerResupply", r.RoomPerResupply);
            r.SettlementSupplyFactor = node.GetDouble("settlementSupplyFactor", r.SettlementSupplyFactor);
            r.BirthChancePerPair = node.GetDouble("birthChancePerPair", r.BirthChancePerPair);
            r.ColonyRoomPerYear = node.GetInt("colonyRoomPerYear", r.ColonyRoomPerYear);
            r.ColonyRoomPerResidents = node.GetInt("colonyRoomPerResidents", r.ColonyRoomPerResidents);
            r.SciencePerResident = node.GetDouble("sciencePerResident", r.SciencePerResident);
            r.FundsPerResident = node.GetDouble("fundsPerResident", r.FundsPerResident);
            r.PrestigePerColony = node.GetDouble("prestigePerColony", r.PrestigePerColony);
            r.ComeOfAgeChance = node.GetDouble("comeOfAgeChance", r.ComeOfAgeChance);
            r.ComeOfAgeMinColonists = node.GetInt("comeOfAgeMinColonists", r.ComeOfAgeMinColonists);
            r.BornSource = node.GetValue("bornSource", r.BornSource);
            return r;
        }
    }

    /// <summary>Stranded crews and rescue missions (KSA-65): the RESCUE block of KSA_AGENCY_SETTINGS.</summary>
    public sealed class RescueRules
    {
        public const string NodeName = "RESCUE";

        public bool Enabled { get; set; } = true;
        /// <summary>Chance that a simulated abort or vehicle loss with survivors strands them, by where it happened.</summary>
        public double StrandChanceKerbinOrbit { get; set; } = 0.15;
        public double StrandChanceAway { get; set; } = 0.35;
        /// <summary>Morale lost each year a crew is stranded.</summary>
        public int MoralePerYear { get; set; } = 10;
        /// <summary>Prestige lost each year while any crew is stranded.</summary>
        public double PrestigePerYear { get; set; } = 3;
        /// <summary>Days after which an unrescued crew is declared lost (0 = never).</summary>
        public int LostAfterDays { get; set; } = 852;
        /// <summary>Morale the rescued gain on coming home.</summary>
        public int RescuedMorale { get; set; } = 10;

        public static RescueRules Parse(DataNode node)
        {
            var r = new RescueRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.StrandChanceKerbinOrbit = node.GetDouble("strandChanceKerbinOrbit", r.StrandChanceKerbinOrbit);
            r.StrandChanceAway = node.GetDouble("strandChanceAway", r.StrandChanceAway);
            r.MoralePerYear = node.GetInt("moralePerYear", r.MoralePerYear);
            r.PrestigePerYear = node.GetDouble("prestigePerYear", r.PrestigePerYear);
            r.LostAfterDays = node.GetInt("lostAfterDays", r.LostAfterDays);
            r.RescuedMorale = node.GetInt("rescuedMorale", r.RescuedMorale);
            return r;
        }
    }

    /// <summary>A crew stuck somewhere they cannot get home from on their own (KSA-65).</summary>
    public sealed class Stranding
    {
        public string Id { get; set; }
        public List<string> Crew { get; } = new List<string>();
        public string Body { get; set; }
        /// <summary>On the surface (true) or in orbit of <see cref="Body"/> (false).</summary>
        public bool OnSurface { get; set; }
        /// <summary>Host vessel id when the stranding happened in a live flight (a live rescue can rendezvous with it).</summary>
        public string VesselId { get; set; }
        public int Day { get; set; }
        public string Cause { get; set; }
        /// <summary>Rescue project under way, or empty.</summary>
        public string RescueProjectId { get; set; }
        public bool Resolved { get; set; }

        public Stranding() { Id = string.Empty; Body = "Kerbin"; VesselId = string.Empty; Cause = string.Empty; RescueProjectId = string.Empty; }

        public string Where { get { return (OnSurface ? "on the surface of " : "in orbit of ") + Body; } }
    }
}
