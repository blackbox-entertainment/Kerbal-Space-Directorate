using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// Research (M3 decision, built 2026-10-27): the stock tech tree stays, but the agency earns the Science to climb it.
    /// Scientists assigned to the lab produce Science every fiscal year; successful missions produce Science by tier.
    /// The host credits the points to the game. Templates whose parts are not researched cannot be used.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>(points, reason): the host adds them to the game's Science total.</summary>
        public event Action<double, string> ScienceEarned;

        /// <summary>Host check: null when a template's parts are all researched, else why it cannot be built yet.</summary>
        public Func<Template, string> TemplateCheck { get; set; }

        public string TemplateBlocker(Template t)
        {
            Func<Template, string> check = TemplateCheck;
            return t == null || check == null ? null : check(t);
        }

        public List<CrewProfile> LabScientists()
        {
            var list = new List<CrewProfile>();
            foreach (CrewProfile c in Crew.Active) if (c.InLab && c.Status != CrewStatus.Dead) list.Add(c);
            return list;
        }

        /// <summary>Science the lab will produce at the next year end with today's staff.</summary>
        public double ProjectedLabScience()
        {
            double total = 0;
            foreach (CrewProfile c in LabScientists()) total += LabOutput(c);
            return Math.Round(total * (1 + Settings.Departments.SuccessBonusPerRung * 0 + DepartmentRung(Department.Science) * Settings.LabScienceHeadBonus), 1);
        }

        private double LabOutput(CrewProfile c)
        {
            double perStar = c.Role == CrewRole.Scientist ? Settings.LabSciencePerStar : Settings.LabSciencePerStar * 0.5;
            return (Settings.LabScienceBase + perStar * c.Stars) * Personality.ScienceFactor(c);
        }

        /// <summary>Puts a kerbal in (or takes them out of) the research lab. Lab staff are not available for missions.</summary>
        public bool SetLab(string crewId, bool inLab, out string message)
        {
            CrewProfile c = Crew.Get(crewId);
            if (c == null || c.Departed) { message = "No such active crew member."; return false; }
            if (inLab)
            {
                if (c.IsFlying || c.ProjectId.Length > 0) { message = c.Name + " is busy on a project."; return false; }
                if (IsDepartmentHead(c.Id)) { message = c.Name + " runs a department."; return false; }
                if (c.Role != CrewRole.Scientist && c.Track != CareerTrack.Science) { message = c.Name + " is not a scientist; the lab needs Science-track kerbals (others produce half)."; }
            }
            AccrueLab(c, CurrentDay, null); // pay up the days so far before the change
            c.InLab = inLab;
            AccrueLab(c, CurrentDay, null); // joining: the clock starts today
            message = inLab ? c.Name + " joins the research lab: about " + LabOutput(c).ToString("0") + " Science per year." : c.Name + " leaves the lab and is available for missions.";
            Push(null, message);
            return true;
        }

        /// <summary>
        /// Can the agency fly this mission with what is researched today? A buildable template says yes; otherwise the
        /// researched part kinds must cover the profile (a pod, a way up, a way down; engines, tanks and decouplers for
        /// orbit; more engine choice and landing legs beyond Kerbin). Null when feasible, else the reason.
        /// </summary>
        public string MissionBlocker(MissionType type)
        {
            if (type == null) return "Unknown mission.";
            bool anyTemplate = false, anyBuildable = false;
            string firstBlock = null;
            foreach (Template t in TemplatesFor(type))
            {
                anyTemplate = true;
                string why = TemplateBlocker(t);
                if (why == null) { anyBuildable = true; break; }
                if (firstBlock == null) firstBlock = t.Name + " " + why;
            }
            if (anyBuildable) return null;
            // With AI design on, the engineering team designs the rocket: templates are only the AI-off fallback
            // (2026-10-28), so the researched part kinds decide.
            if (AiDesignAvailable)
            {
                int known;
                if (Parts != null && NoDesignAt.TryGetValue(type.Id, out known) && known == Parts.All.Count)
                    return "has no workable design yet: the engineers tried and could not make one with today's parts (research something new)";
                return PartKindsBlocker(type);
            }
            // Every project needs a design it can fall back on (an AI design that comes up short flies the template),
            // so a mission whose templates are all locked is locked, whatever part kinds are researched (fixed 2026-10-28:
            // Kerbin orbit showed as open and every start was refused).
            if (anyTemplate) return "has no design it can build yet (" + (firstBlock ?? "parts not researched") + ")";
            return PartKindsBlocker(type);
        }

        /// <summary>
        /// The looser test: do the researched part kinds cover the profile (a pod, a way up, a way down; engines, tanks and
        /// decouplers for orbit...)? Null when they do or nothing is known. Used for missions with no template and for
        /// goal feasibility, so the government may ask for what is one research step away.
        /// </summary>
        public string PartKindsBlocker(MissionType type)
        {
            if (type == null) return "Unknown mission.";
            if (Parts == null) return null;
            var kinds = new HashSet<KSA.Core.Parts.PartKind>();
            int engines = 0;
            foreach (KSA.Core.Parts.PartInfo p in Parts.All) { kinds.Add(p.Kind); if (p.Kind == KSA.Core.Parts.PartKind.Engine) engines++; }
            var missing = new List<string>();
            if (!kinds.Contains(KSA.Core.Parts.PartKind.Command)) missing.Add("a crew pod");
            if (!kinds.Contains(KSA.Core.Parts.PartKind.Parachute)) missing.Add("a parachute");
            bool beyond = type.Profile != MissionProfile.Suborbital && type.Profile != MissionProfile.Orbit && type.Profile != MissionProfile.Station;
            if (type.Profile == MissionProfile.Suborbital)
            {
                if (!kinds.Contains(KSA.Core.Parts.PartKind.Engine) && !kinds.Contains(KSA.Core.Parts.PartKind.Booster)) missing.Add("an engine or booster");
                // Day-one boosters alone do not reach space (the Flea cluster topped out at 41 km): space needs staging.
                if (!kinds.Contains(KSA.Core.Parts.PartKind.Decoupler)) missing.Add("decouplers");
            }
            else
            {
                if (!kinds.Contains(KSA.Core.Parts.PartKind.Engine)) missing.Add("a liquid engine");
                if (!kinds.Contains(KSA.Core.Parts.PartKind.Tank)) missing.Add("fuel tanks");
                if (!kinds.Contains(KSA.Core.Parts.PartKind.Decoupler)) missing.Add("decouplers");
            }
            if (beyond && engines < 3) missing.Add("more engine options (" + engines + " of 3 researched)");
            if ((type.Profile == MissionProfile.Land || type.Profile == MissionProfile.Base) && !kinds.Contains(KSA.Core.Parts.PartKind.LandingLeg)) missing.Add("landing legs");
            if (type.Profile == MissionProfile.Station && !kinds.Contains(KSA.Core.Parts.PartKind.Docking)) missing.Add("docking ports");
            if (missing.Count == 0) return null;
            return "needs research: " + string.Join(", ", missing.ToArray());
        }

        /// <summary>
        /// Why a program type cannot start yet, or null (KSA-66). Every module must be buildable with what is researched,
        /// so a program never starts and then stalls halfway for lack of parts. A missing prerequisite program counts too.
        /// </summary>
        public string ProgramBlocker(ProgramType t, string body)
        {
            if (t == null) return "Unknown program.";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleRecipe m in t.Modules)
            {
                if (m.Optional) continue; // added later with Add module; its own design is checked then
                if (!seen.Add(m.MissionTypeId)) continue;
                MissionType mt = Catalogs.Missions.Get(m.MissionTypeId);
                if (mt == null) return "the " + m.Role + " module's mission (" + m.MissionTypeId + ") is missing from missions.cfg";
                string why = MissionBlocker(mt);
                if (why != null) return "the " + m.Role + " module " + why;
            }
            if (t.RequiresProgram.Length > 0 && !string.IsNullOrEmpty(body) && ProgramAt(t.RequiresProgram, body, true) == null)
            {
                ProgramType req = Catalogs.ProgramTypes.Get(t.RequiresProgram);
                return "needs a finished " + (req != null ? req.Name : t.RequiresProgram) + " at " + body + " first";
            }
            return null;
        }

        /// <summary>Why a program's resupply cannot fly yet, or null.</summary>
        public string ResupplyBlocker(Program g)
        {
            ProgramType t = g != null ? Catalogs.ProgramTypes.Get(g.ProgramTypeId) : null;
            if (t == null || t.ResupplyMissionType.Length == 0) return "it cannot be resupplied";
            MissionType mt = Catalogs.Missions.Get(t.ResupplyMissionType);
            return mt == null ? "its resupply mission is missing" : MissionBlocker(mt);
        }

        /// <summary>Why a rescue for this stranding cannot fly yet, or null.</summary>
        public string RescueBlocker(Stranding s)
        {
            return s == null ? "nobody to rescue" : MissionBlocker(RescueMissionFor(s, 1));
        }

        private static bool PatternMatches(string achievement, string pattern)
        {
            if (string.IsNullOrEmpty(achievement) || string.IsNullOrEmpty(pattern)) return false;
            int star = pattern.IndexOf('*');
            if (star < 0) return string.Equals(achievement, pattern, StringComparison.OrdinalIgnoreCase);
            return achievement.StartsWith(pattern.Substring(0, star), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Goals the government may issue: achievement goals only when some mission that earns them is feasible now.</summary>
        public bool GoalFeasible(Goals.GoalDefinition g)
        {
            if (g == null) return false;
            if (g.Condition != Goals.GoalConditionType.Achievement || string.IsNullOrEmpty(g.Pattern)) return true;
            foreach (MissionType t in Catalogs.Missions.Types)
            {
                bool earns = false;
                foreach (string a in t.Achievements) if (PatternMatches(a, g.Pattern)) { earns = true; break; }
                if (earns && (MissionBlocker(t) == null || PartKindsBlocker(t) == null)) return true;
            }
            return false;
        }

        private void AwardLabScience(List<string> notices)
        {
            double points = ProjectedLabScience();
            if (points <= 0) return;
            State.ScienceEarned += points;
            Push(notices, "Research lab delivered " + points.ToString("0.#") + " Science (" + LabScientists().Count + " in the lab)");
            Action<double, string> h = ScienceEarned;
            if (h != null) h(points, "Research lab, year " + CurrentYear);
        }

        /// <summary>Science missions pay for who flew them: a scientist aboard doubles the yield, each of their stars adds 20%, anyone else adds 5%.</summary>
        public double ScienceCrewMultiplier(IList<string> crewIds)
        {
            double m = 1.0;
            bool scientist = false;
            int bestStars = 0;
            if (crewIds != null) foreach (string id in crewIds)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null) continue;
                if (c.Role == CrewRole.Scientist) { scientist = true; if (c.Stars > bestStars) bestStars = c.Stars; }
                else m += 0.05;
            }
            if (scientist) m += 1.0 + 0.2 * bestStars;
            double curiosity = 1;
            if (crewIds != null) foreach (string id in crewIds) { CrewProfile c = Crew.Get(id); if (c != null) curiosity *= Personality.ScienceFactor(c); }
            return m * curiosity;
        }

        private void AwardMissionScience(MissionReport r, MissionType type, List<string> notices)
        {
            if (r == null || type == null || !r.IsSuccess) return;
            double points = type.Tier == MissionTier.Late ? Settings.MissionScienceLate : type.Tier == MissionTier.Mid ? Settings.MissionScienceMid : Settings.MissionScienceEarly;
            if (type.IsScience && type.ScienceYield > 0) points = type.ScienceYield * ScienceCrewMultiplier(r.Crew);
            if (r.Outcome == MissionOutcome.PartialSuccess) points *= 0.5;
            points = Math.Round(points * (1 + DepartmentRung(Department.Science) * Settings.LabScienceHeadBonus), 1);
            if (points <= 0) return;
            State.ScienceEarned += points;
            Push(notices, r.ProjectName + " returned " + points.ToString("0.#") + " Science");
            Action<double, string> h = ScienceEarned;
            if (h != null) h(points, r.ProjectName);
        }
    }
}
