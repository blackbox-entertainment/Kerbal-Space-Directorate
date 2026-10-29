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
                GrantExperience(c, points, notices);
                r.Rewards.Add(c.Name + ": +" + points.ToString("0.#") + " experience" + (first ? " (incl. First bonus)" : string.Empty) + ", now " + c.Record.Experience.ToString("0.#") + " points, " + Math.Max(c.Stars, rules.StarsFor(c.Record.Experience)) + " stars");
            }
            if (p != null) p.Notes.Add("Crew experience: +" + points.ToString("0.#") + " each" + (first ? " (includes the First bonus)" : string.Empty) + ".");
            AwardDesignExperience(p, r, type, first, notices);
        }

        /// <summary>Adds points to a kerbal's record and raises their stars when the ladder says so.</summary>
        private void GrantExperience(CrewProfile c, double points, List<string> notices)
        {
            if (c == null || points <= 0) return;
            ExperienceRules rules = Experience;
            int before = Math.Max(c.Stars, rules.StarsFor(c.Record.Experience));
            c.Record.Experience += points;
            int after = Math.Max(c.Stars, rules.StarsFor(c.Record.Experience));
            if (after > c.Stars) c.Stars = after;
            if (after > before) Push(notices, c.Name + " reaches " + after + " star" + (after == 1 ? "" : "s") + " (" + c.Record.Experience.ToString("0.#") + " experience)");
        }

        /// <summary>
        /// Ground work (2026-10-28): the engineers whose design flew are paid too, the lead in full and the rest of the team a
        /// share, less if it failed. A flown design counts as a mission toward the lead's Engineering ranks. The Director
        /// flying it does not cut the design credit; engineers who also flew are paid as crew only.
        /// </summary>
        private void AwardDesignExperience(Project p, MissionReport r, MissionType type, bool first, List<string> notices)
        {
            if (p == null || r == null) return;
            ExperienceRules rules = Experience;
            double design = rules.PointsFor(type != null ? type.Tier : MissionTier.Early);
            design *= r.IsSuccess ? 1 + rules.SuccessBonus : rules.FailedDesignFactor;
            if (first && r.IsSuccess) design += rules.FirstBonus;
            var team = new List<string>();
            if (!string.IsNullOrEmpty(p.LeadEngineer)) team.Add(p.LeadEngineer);
            foreach (string e in p.Engineers) if (!team.Contains(e)) team.Add(e);
            foreach (string id in team)
            {
                if (r.Crew.Contains(id)) continue;
                CrewProfile c = Crew.Get(id);
                if (c == null || c.Departed) continue;
                bool lead = id == p.LeadEngineer || (string.IsNullOrEmpty(p.LeadEngineer) && id == team[0]);
                double points = Math.Round(design * (lead ? rules.DesignLeadFactor : rules.DesignTeamFactor), 1);
                GrantExperience(c, points, notices);
                if (points > 0) r.Rewards.Add(c.Name + " (" + (lead ? "lead engineer" : "design team") + "): +" + points.ToString("0.#") + " experience, now " + c.Record.Experience.ToString("0.#") + " points, " + Math.Max(c.Stars, rules.StarsFor(c.Record.Experience)) + " stars");
                if (lead && r.IsSuccess) c.Record.DesignsFlown++;
                if (points > 0) p.Notes.Add(c.Name + " (" + (lead ? "lead engineer" : "design team") + "): +" + points.ToString("0.#") + " experience.");
                EvaluateProgress(c, p.MissionId ?? string.Empty, notices);
            }
        }

        /// <summary>
        /// Lab experience (2026-10-28): labPerYear points for a full year in the lab, paid up day by day, and each full year
        /// counts as a mission toward the Science ranks. Runs every tick, at the year end, and when a kerbal leaves the lab.
        /// </summary>
        private void AccrueLab(CrewProfile c, int day, List<string> notices)
        {
            if (c == null) return;
            if (!c.InLab || c.Departed || c.Status == CrewStatus.Dead) { c.LabSinceDay = -1; return; }
            if (c.LabSinceDay < 0 || c.LabSinceDay > day) { c.LabSinceDay = day; return; }
            int days = day - c.LabSinceDay;
            if (days <= 0) return;
            c.LabSinceDay = day;
            int dpy = Math.Max(1, Calendar.DaysPerYear);
            c.Record.LabDays += days;
            GrantExperience(c, Experience.Enabled ? Experience.LabPerYear * days / dpy : 0, notices);
            int years = (int)(c.Record.LabDays / dpy);
            if (years > c.Record.LabYears)
            {
                c.Record.LabYears = years;
                Push(notices, c.Name + " completes " + years + " year" + (years == 1 ? "" : "s") + " in the research lab");
                EvaluateProgress(c, string.Empty, notices);
            }
        }

        private void TickLab(int day, List<string> notices)
        {
            foreach (CrewProfile c in Crew.Active) if (c.InLab || c.LabSinceDay >= 0) AccrueLab(c, day, notices);
        }
    }
}
