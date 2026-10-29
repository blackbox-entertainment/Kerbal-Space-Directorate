using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Projects
{
    public enum ProgramKind { OrbitalStation = 0, SurfaceBase = 1, CloudCity = 2, FuelDepot = 3 }

    public enum ProgramStatus { Active = 0, Complete = 1, Stalled = 2, Cancelled = 3 }

    /// <summary>How far a base has grown (KSA-64). Never goes backwards.</summary>
    public enum ColonyStage { None = 0, Outpost = 1, Base = 2, Settlement = 3, Colony = 4 }

    /// <summary>What a delivered craft carries that matters to a base: the host reads it from the craft file (KSA-64).</summary>
    public sealed class ModuleHardware
    {
        /// <summary>Parts that dig (stock: ModuleResourceHarvester).</summary>
        public int Drills { get; set; }
        /// <summary>Converters that turn ore into something useful (stock: ModuleResourceConverter with Ore input).</summary>
        public int Isru { get; set; }
        /// <summary>Power sources: solar panels, generators (RTG), fuel cells.</summary>
        public int Power { get; set; }
        /// <summary>Crew seats in the delivered craft.</summary>
        public int Seats { get; set; }
        /// <summary>The craft can fly: a command part and an engine.</summary>
        public bool Rocket { get; set; }
        /// <summary>Life support (KSA-67, only counted with Kerbalism): greenhouses that grow food.</summary>
        public int Greenhouses { get; set; }
        /// <summary>Crew whose air the craft's scrubbers can clean (Kerbalism capacity: 1 = one kerbal).</summary>
        public int Scrubbers { get; set; }
        /// <summary>Crew whose water the craft's recyclers can recover.</summary>
        public int WaterRecyclers { get; set; }
        /// <summary>Food and water the craft carries when full (Kerbalism units), for the live-flight supply check.</summary>
        public double Food { get; set; }
        public double Water { get; set; }
        /// <summary>Liquid fuel and oxidizer the craft's tanks hold when full, tonnes (a fuel depot core's capacity, 2026-10-29).</summary>
        public double FuelTonnes { get; set; }

        public static ModuleHardware None() { return new ModuleHardware(); }
    }

    public sealed class ModuleRecipe
    {
        public string Role { get; set; }
        public string MissionTypeId { get; set; }
        /// <summary>Templates to try first for this module, best first (KSA-67: the colony industry module). Empty = the usual choice.</summary>
        public List<string> Templates { get; } = new List<string>();
        /// <summary>Not needed to finish the program: built later with "Add module" (KSA-67: the colony industry module). Optional modules come last.</summary>
        public bool Optional { get; set; }
        public ModuleRecipe() { Role = string.Empty; MissionTypeId = string.Empty; }
    }

    /// <summary>A kind of program (station, base, cloud city) and the modules it is built from (Jira KSA-37).</summary>
    public sealed class ProgramType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ProgramKind Kind { get; set; }
        public List<string> Bodies { get; } = new List<string>();
        public List<ModuleRecipe> Modules { get; } = new List<ModuleRecipe>();

        /// <summary>Modules that finish the program: everything before the first optional one.</summary>
        public int RequiredModuleCount
        {
            get
            {
                for (int i = 0; i < Modules.Count; i++) if (Modules[i].Optional) return i;
                return Modules.Count;
            }
        }
        /// <summary>Days a finished program goes between resupplies before the crew aboard start to suffer (0 = never needs supply).</summary>
        public int SupplyIntervalDays { get; set; }
        /// <summary>Mission type id of a resupply flight to this program (empty = cannot be resupplied).</summary>
        public string ResupplyMissionType { get; set; }
        /// <summary>A program of this type must be complete at the same body first (Eve surface base needs the cloud city).</summary>
        public string RequiresProgram { get; set; }
        /// <summary>Relieved crews go to this program at the same body instead of straight home (Eve surface to the cloud city).</summary>
        public string ReturnVia { get; set; }
        /// <summary>Can grow into a settlement and a colony (bases and cloud cities; stations do not by default).</summary>
        public bool Colonize { get; set; }

        public ProgramType() { Id = string.Empty; Name = string.Empty; Description = string.Empty; ResupplyMissionType = string.Empty; RequiresProgram = string.Empty; ReturnVia = string.Empty; }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    public sealed class ProgramCatalog
    {
        public const string NodeName = "KSA_PROGRAM_TYPE";
        public const string ModuleNodeName = "MODULE";

        private readonly List<ProgramType> _types = new List<ProgramType>();

        public IReadOnlyList<ProgramType> Types { get { return _types; } }
        public int Count { get { return _types.Count; } }

        public void Add(ProgramType t) { if (t == null) throw new ArgumentNullException(nameof(t)); _types.Add(t); }

        public ProgramType Get(string id)
        {
            for (int i = 0; i < _types.Count; i++)
                if (string.Equals(_types[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _types[i];
            return null;
        }

        public static ProgramCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new ProgramCatalog();
            if (nodes == null) return catalog;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0) { if (warnings != null) warnings.Add("KSA_PROGRAM_TYPE without id skipped"); continue; }
                ProgramKind kind;
                if (!Enum.TryParse(node.GetValue("kind", "OrbitalStation"), true, out kind) || !Enum.IsDefined(typeof(ProgramKind), kind))
                {
                    if (warnings != null) warnings.Add("Program type " + id + ": unknown kind"); continue;
                }
                var t = new ProgramType { Id = id, Name = node.GetValue("name", id), Description = node.GetValue("description", string.Empty), Kind = kind, SupplyIntervalDays = node.GetInt("supplyIntervalDays", 0), ResupplyMissionType = node.GetValue("resupplyMissionType", string.Empty) };
                t.RequiresProgram = node.GetValue("requiresProgram", string.Empty);
                t.ReturnVia = node.GetValue("returnVia", string.Empty);
                t.Colonize = node.GetBool("colonize", kind != ProgramKind.OrbitalStation);
                t.Bodies.AddRange(node.GetList("bodies"));
                foreach (DataNode m in node.GetChildren(ModuleNodeName))
                {
                    var r = new ModuleRecipe { Role = m.GetValue("role", "module"), MissionTypeId = m.GetValue("missionType", string.Empty) };
                    r.Templates.AddRange(m.GetList("templates"));
                    r.Optional = m.GetBool("optional", false);
                    if (r.MissionTypeId.Length == 0) { if (warnings != null) warnings.Add("Program type " + id + ": MODULE without missionType"); continue; }
                    t.Modules.Add(r);
                }
                if (t.Modules.Count == 0) { if (warnings != null) warnings.Add("Program type " + id + " has no modules; skipped"); continue; }
                catalog.Add(t);
            }
            return catalog;
        }
    }

    /// <summary>A multi-mission effort: a station, base, or cloud city made of module projects (GDD 4.7).</summary>
    public sealed class Program
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ProgramTypeId { get; set; }
        public string Body { get; set; }
        public string ProgramManager { get; set; }
        public ProgramStatus Status { get; set; }
        public int CreatedDay { get; set; }
        /// <summary>Module project ids in recipe order; empty slots not yet started.</summary>
        public List<string> ModuleProjectIds { get; } = new List<string>();
        /// <summary>Index of the next recipe module to start.</summary>
        public int NextModuleIndex { get; set; }
        /// <summary>Chosen once for bases: a description of the landing site and its slope (GDD 14.1 decision 6).</summary>
        public string Site { get; set; }
        /// <summary>0 (flat) .. 1 (steep). Applied to every module landing.</summary>
        public double SiteSlope { get; set; }
        /// <summary>Host vessel id of the station core (or first base module) once flown live; empty when the program was simulated.</summary>
        public string VesselId { get; set; }
        /// <summary>Last day a resupply arrived (or the program completed). -1 = never.</summary>
        public int LastSuppliedDay { get; set; }
        /// <summary>Crew ids living aboard (delivered module crews, rotated by resupplies).</summary>
        public List<string> CrewAboard { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();

        // --- Growth (KSA-64) ---
        public ColonyStage Stage { get; set; }
        /// <summary>Successful resupplies delivered over the program's life.</summary>
        public int ResuppliesDelivered { get; set; }
        /// <summary>Settlers and colony-born residents (not roster kerbals).</summary>
        public int ColonistsMale { get; set; }
        public int ColonistsFemale { get; set; }
        /// <summary>Hardware delivered so far, summed over every module and resupply craft.</summary>
        public int Seats { get; set; }
        public int Drills { get; set; }
        public int Isru { get; set; }
        public int Power { get; set; }
        /// <summary>Life support delivered (KSA-67, counted with Kerbalism): greenhouses, scrubber and water recycler capacity.</summary>
        public int Greenhouses { get; set; }
        public int Scrubbers { get; set; }
        public int WaterRecyclers { get; set; }
        /// <summary>A rocket stays at the base (the "return" module): crews can rotate and evacuate.</summary>
        public bool HasRocket { get; set; }
        /// <summary>Room the base added itself: supply deliveries and, once a colony, its own building.</summary>
        public int ExtraRoom { get; set; }
        /// <summary>Projects whose hardware is already counted (so a reload never counts twice).</summary>
        public List<string> HardwareCounted { get; } = new List<string>();
        /// <summary>Day the base became a colony, or -1.</summary>
        public int ColonyDay { get; set; }

        // --- Fuel depot (2026-10-29) ---
        /// <summary>Propellant (liquid fuel and oxidizer) held at a fuel depot, tonnes. The agency's figure is the truth; a live depot's tanks are kept matching it.</summary>
        public double DepotFuel { get; set; }
        /// <summary>What the depot's tanks hold when full, tonnes.</summary>
        public double DepotCapacity { get; set; }

        public int Colonists { get { return ColonistsMale + ColonistsFemale; } }
        /// <summary>Everyone who lives there: agency crew aboard plus colonists.</summary>
        public int Residents { get { return CrewAboard.Count + Colonists; } }
        public int Room { get { return Seats + ExtraRoom; } }
        /// <summary>Drill, ore converter and power all present.</summary>
        public bool HardwareSelfSufficient { get { return Drills > 0 && Isru > 0 && Power > 0; } }

        public Program()
        {
            Id = string.Empty; Name = string.Empty; ProgramTypeId = string.Empty; Body = string.Empty; ProgramManager = string.Empty; Site = string.Empty; VesselId = string.Empty; LastSuppliedDay = -1; ColonyDay = -1;
        }

        public override string ToString() { return Name + " [" + Status + "]"; }
    }
}
