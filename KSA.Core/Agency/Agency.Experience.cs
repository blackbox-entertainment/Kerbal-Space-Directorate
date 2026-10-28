using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>KSA-63: mission experience and the star ladder. Stars are the higher of the stock level and the KSA ladder.</summary>
    public sealed partial class Agency
    {
        private ExperienceRules Experience { get { return Settings.Experience ?? new ExperienceRules(); } }

        /// <summary>Stars a kerbal holds on the KSA ladder alone.</summary>
        public int LadderStars(CrewProfile c) { return c == null ? 0 : Experience.StarsFor(c.Record.Experience); }

        /// <summary>Points still needed for the kerbal's next star on the KSA ladder (0 at five stars).</summary>
        public double ToNextStar(CrewProfile c) { return c == null ? 0 : Experience.ToNextStar(c.Record.Experience); }

        /// <summary>
        /// True when this mission flew a First the player holds and no earlier mission report already carries that
        /// achievement. A repeat hop in the same year is not a First again, even though the claim is from this year.
        /// </summary>
        internal bool ClaimedFirst(Project p, MissionReport r)
        {
            if (r == null) return false;
            foreach (FirstClaim f in Firsts)
            {
                if (f.ClaimedBy != PlayerId) continue;
                string key = f.Achievement;
                if (!r.Achievements.Exists(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase))) continue;
                bool earlier = Projects.Exists(o => o != p && o.Report != null && o.Report != r && o.Report.Achievements.Exists(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)));
                if (!earlier) return true;
            }
            return false;
        }

        /// <summary>Pays every surviving crew member for the mission and promotes stars where the ladder says so.</summary>
        private void AwardExperience(Project p, MissionReport r, List<string> notices)
        {
            ExperienceRules rules = Experience;
            if (!rules.Enabled || r == null) return;
            MissionType type = Catalogs.Missions.Get(r.MissionTypeId ?? (p != null ? p.MissionTypeId : string.Empty));
            double points = rules.PointsFor(type != null ? type.Tier : MissionTier.Early);
            bool first = ClaimedFirst(p, r);
            if (r.IsSuccess) points *= 1 + rules.SuccessBonus; else points *= rules.SurvivedFailureFactor;
            points += Math.Max(0, r.DaysInSpace - 1) * rules.PerExtraDay;
            if (first) points += rules.FirstBonus;
            if (r.DirectorFlown) points *= rules.DirectorFactor;
            points = Math.Round(points, 1);
            if (points <= 0) return;
            foreach (string id in r.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null || r.Deaths.Contains(id)) continue;
                int before = Math.Max(c.Stars, rules.StarsFor(c.Record.Experience));
                c.Record.Experience += points;
                int after = Math.Max(c.Stars, rules.StarsFor(c.Record.Experience));
                if (after > c.Stars) c.Stars = after;
                if (after > before) Push(notices, c.Name + " reaches " + after + " star" + (after == 1 ? "" : "s") + " (" + c.Record.Experience.ToString("0.#") + " experience)");
            }
            if (p != null) p.Notes.Add("Crew experience: +" + points.ToString("0.#") + " each" + (first ? " (includes the First bonus)" : string.Empty) + ".");
        }
    }
}
