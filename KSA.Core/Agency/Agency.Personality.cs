using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>Personality effects that touch the agency as a whole (prestige, press, simulated-versus-live sulking).</summary>
    public sealed partial class Agency
    {
        private PersonalityRules Personality { get { return Settings.Personality ?? PersonalityRules.Defaults(); } }

        /// <summary>Showboats and the reckless earn prestige on success and firsts; showboats sulk when simulated.</summary>
        private void ApplyPersonalityAfterMission(Project p, MissionReport r, List<string> notices)
        {
            if (r == null) return;
            double prestige = 0;
            bool first = false;
            foreach (FirstClaim f in Firsts)
            {
                if (f.ClaimedBy != PlayerId || f.Year != CurrentYear) continue;
                if (r.Achievements.Exists(a => string.Equals(a, f.Achievement, StringComparison.OrdinalIgnoreCase))) { first = true; break; }
            }
            foreach (string id in r.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null || c.Departed) continue;
                if (r.IsSuccess) prestige += Personality.PrestigeOnSuccess(c);
                if (first) prestige += Personality.PrestigeOnFirst(c);
                if (r.Mode == MissionMode.Simulate)
                {
                    int sulk = Personality.MoraleIfSimulated(c);
                    if (sulk != 0)
                    {
                        c.Morale = Math.Max(0, Math.Min(100, c.Morale + sulk));
                        Push(notices, c.Name + " wanted that one flown live (" + sulk + " morale)");
                    }
                }
            }
            if (prestige > 0)
            {
                State.Prestige = Math.Min(100, State.Prestige + prestige);
                Push(notices, "The press loved the crew: +" + prestige.ToString("0.#") + " prestige");
            }
        }

        /// <summary>Design score bonus from the lead engineer's personality (Perfectionist).</summary>
        private double LeadDesignBonusFor(Project p)
        {
            CrewProfile lead = p != null ? Crew.Get(p.LeadEngineer) : null;
            return lead != null ? Personality.DesignScore(lead) : 0;
        }

        /// <summary>Design and build time multiplier from the lead engineer's personality.</summary>
        private double LeadTimeFactorFor(Project p)
        {
            CrewProfile lead = p != null ? Crew.Get(p.LeadEngineer) : null;
            return lead != null ? Personality.BuildTimeFactor(lead) : 1;
        }
    }
}
