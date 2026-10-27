using System;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// Courage and Stupidity drift with experience (GDD decision 17, Jira KSA-50). The agency owns both traits
    /// after hire and tells the host when they move so the game's kerbal follows.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>A crew member's Courage or Stupidity changed; the host mirrors it onto the real kerbal.</summary>
        public event Action<CrewProfile> TraitsChanged;

        private TraitDrift Drift { get { return Settings.Drift ?? new TraitDrift(); } }

        private void RaiseTraitsChanged(CrewProfile p)
        {
            Action<CrewProfile> handler = TraitsChanged;
            if (handler != null) handler(p);
        }

        /// <summary>After a mission report is applied: survivors learn, and lose nerve if a crewmate died.</summary>
        private void DriftAfterMission(MissionReport r)
        {
            if (r == null) return;
            bool crewmateDied = r.Deaths.Count > 0;
            foreach (string id in r.Crew)
            {
                if (r.Deaths.Contains(id)) continue;
                CrewProfile p = Crew.Get(id);
                if (p == null) continue;
                if (Drift.AfterMission(p, r.Outcome, crewmateDied)) RaiseTraitsChanged(p);
            }
        }

        /// <summary>A mission flown outside the project system (a Director-flown stock vessel) still teaches. Host calls it after EndMission.</summary>
        public bool DriftAfterAdHocMission(string crewId, MissionOutcome outcome)
        {
            CrewProfile p = Crew.Get(crewId);
            if (p == null || !Drift.AfterMission(p, outcome, false)) return false;
            RaiseTraitsChanged(p);
            return true;
        }

        /// <summary>Everyone else loses a little nerve when a colleague dies.</summary>
        private void DriftAfterDeath(CrewProfile dead)
        {
            foreach (CrewProfile other in Crew.Active)
            {
                if (other == dead) continue;
                if (Drift.Apply(other, Drift.ColleagueDeathCourage, 0f)) RaiseTraitsChanged(other);
            }
        }

        /// <summary>A year on the ground dulls the edge.</summary>
        private void DriftAfterIdleYear(CrewProfile p)
        {
            if (Drift.Apply(p, Drift.IdleYearCourage, 0f)) RaiseTraitsChanged(p);
        }
    }
}
