using System;
using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.Core.Careers
{
    /// <summary>A career ladder: which roles enter it and the ordered ranks on it.</summary>
    public sealed class CareerTrackDefinition
    {
        public CareerTrack Track { get; set; }
        public string Name { get; set; }
        /// <summary>Roles that start on this track when hired. Empty means nobody is assigned automatically.</summary>
        public List<CrewRole> EntryRoles { get; } = new List<CrewRole>();
        /// <summary>Ranks sorted by <see cref="RankDefinition.Order"/>.</summary>
        public List<RankDefinition> Ranks { get; } = new List<RankDefinition>();

        public CareerTrackDefinition() { Name = string.Empty; }

        public RankDefinition EntryRank { get { return Ranks.Count > 0 ? Ranks[0] : null; } }

        public RankDefinition GetRank(string rankId)
        {
            for (int i = 0; i < Ranks.Count; i++)
                if (string.Equals(Ranks[i].Id, rankId, StringComparison.OrdinalIgnoreCase)) return Ranks[i];
            return null;
        }

        public int IndexOf(string rankId)
        {
            for (int i = 0; i < Ranks.Count; i++)
                if (string.Equals(Ranks[i].Id, rankId, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public RankDefinition NextRank(string rankId)
        {
            int i = IndexOf(rankId);
            return i >= 0 && i + 1 < Ranks.Count ? Ranks[i + 1] : null;
        }

        public void SortRanks()
        {
            Ranks.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
