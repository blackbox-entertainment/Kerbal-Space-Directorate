using System.Collections.Generic;

namespace KSA.Core.Careers
{
    /// <summary>One rung on a career ladder, loaded from config (Jira KSA-18).</summary>
    public sealed class RankDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>Position on the ladder, 0 = entry rank.</summary>
        public int Order { get; set; }
        public int MinStars { get; set; }
        public int MinMissions { get; set; }
        /// <summary>Achievement patterns that must all be present, e.g. "Orbit:Kerbin" or "Land:*".</summary>
        public List<string> RequiredAchievements { get; } = new List<string>();
        /// <summary>Maximum active holders agency-wide; -1 for unlimited.</summary>
        public int MaxSlots { get; set; }
        /// <summary>True when only the Director can grant this rank (e.g. Astronaut Candidate).</summary>
        public bool DirectorSelected { get; set; }
        public int Salary { get; set; }
        /// <summary>Perk identifiers; interpreted by later milestones. Stored as data for now.</summary>
        public List<string> Perks { get; } = new List<string>();

        public RankDefinition()
        {
            Id = string.Empty;
            Name = string.Empty;
            MaxSlots = -1;
        }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }
}
