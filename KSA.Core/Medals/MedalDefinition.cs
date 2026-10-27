using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.Core.Medals
{
    public enum MedalTrigger
    {
        /// <summary>All listed achievement patterns present in the record.</summary>
        Achievement = 0,
        /// <summary>Cumulative days in space at or above <see cref="MedalDefinition.Threshold"/>.</summary>
        DaysInSpace = 1,
        /// <summary>An incident of <see cref="MedalDefinition.IncidentKind"/> is on the record.</summary>
        Incident = 2,
        /// <summary>Never automatic; granted by the Director (M2).</summary>
        Manual = 3
    }

    /// <summary>A medal and the rule that awards it, loaded from config (Jira KSA-20).</summary>
    public sealed class MedalDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public MedalTrigger Trigger { get; set; }
        public List<string> Achievements { get; } = new List<string>();
        public double Threshold { get; set; }
        public IncidentKind IncidentKind { get; set; }
        /// <summary>Only the first crew member in the agency to qualify receives it.</summary>
        public bool FirstInAgency { get; set; }
        /// <summary>Awarded even when the crew member is dead (e.g. Memorial Star). Otherwise the dead are skipped.</summary>
        public bool Posthumous { get; set; }
        /// <summary>Awarded only while the crew member is alive; Survivor-style medals.</summary>
        public bool RequiresAlive { get; set; }

        public MedalDefinition()
        {
            Id = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
        }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }
}
