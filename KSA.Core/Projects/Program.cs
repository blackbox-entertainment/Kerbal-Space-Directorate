using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Projects
{
    public enum ProgramKind { OrbitalStation = 0, SurfaceBase = 1, CloudCity = 2 }

    public enum ProgramStatus { Active = 0, Complete = 1, Stalled = 2, Cancelled = 3 }

    public sealed class ModuleRecipe
    {
        public string Role { get; set; }
        public string MissionTypeId { get; set; }
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

        public ProgramType() { Id = string.Empty; Name = string.Empty; Description = string.Empty; }

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
                var t = new ProgramType { Id = id, Name = node.GetValue("name", id), Description = node.GetValue("description", string.Empty), Kind = kind };
                t.Bodies.AddRange(node.GetList("bodies"));
                foreach (DataNode m in node.GetChildren(ModuleNodeName))
                {
                    var r = new ModuleRecipe { Role = m.GetValue("role", "module"), MissionTypeId = m.GetValue("missionType", string.Empty) };
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
        public List<string> Notes { get; } = new List<string>();

        public Program()
        {
            Id = string.Empty; Name = string.Empty; ProgramTypeId = string.Empty; Body = string.Empty; ProgramManager = string.Empty; Site = string.Empty;
        }

        public override string ToString() { return Name + " [" + Status + "]"; }
    }
}
