using System;
using System.Collections.Generic;

namespace KSA.Core.Personnel
{
    /// <summary>All crew profiles the agency knows about, past and present, keyed by id.</summary>
    public sealed class CrewRegistry
    {
        private readonly Dictionary<string, CrewProfile> _byId = new Dictionary<string, CrewProfile>(StringComparer.OrdinalIgnoreCase);

        public int Count { get { return _byId.Count; } }

        public IEnumerable<CrewProfile> All { get { return _byId.Values; } }

        public IEnumerable<CrewProfile> Active
        {
            get
            {
                foreach (CrewProfile p in _byId.Values)
                    if (!p.Departed) yield return p;
            }
        }

        public CrewProfile Get(string id)
        {
            CrewProfile p;
            return id != null && _byId.TryGetValue(id, out p) ? p : null;
        }

        public bool Contains(string id)
        {
            return id != null && _byId.ContainsKey(id);
        }

        public void Add(CrewProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _byId[profile.Id] = profile;
        }

        public bool Remove(string id)
        {
            return id != null && _byId.Remove(id);
        }

        public void Clear()
        {
            _byId.Clear();
        }

        public bool AnyHasMedal(string medalId)
        {
            foreach (CrewProfile p in _byId.Values)
                if (p.Record.HasMedal(medalId)) return true;
            return false;
        }

        /// <summary>How many active crew currently hold a given rank (for slot caps).</summary>
        public int CountAtRank(string rankId)
        {
            int n = 0;
            foreach (CrewProfile p in _byId.Values)
                if (!p.Departed && string.Equals(p.RankId, rankId, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }
    }
}
