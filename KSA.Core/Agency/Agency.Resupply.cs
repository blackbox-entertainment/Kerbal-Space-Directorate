using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// Resupply and crew rotation for finished programs (KSA-61). A resupply is a cheap project aimed at an existing
    /// station, base or cloud city: on success the program is supplied for another interval and the crews swap
    /// (the relief crew stays aboard, the old crew comes home). An unsupplied program wears its crew down.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>Programs that can take a resupply right now (complete or active with something delivered).</summary>
        public List<Program> ResupplyTargets()
        {
            var list = new List<Program>();
            foreach (Program g in Programs)
            {
                if (g.Status == ProgramStatus.Cancelled) continue;
                ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
                if (t == null || t.ResupplyMissionType.Length == 0) continue;
                if (g.Status != ProgramStatus.Complete && g.ModuleProjectIds.Count == 0) continue;
                if (ResupplyInFlight(g.Id)) continue;
                list.Add(g);
            }
            return list;
        }

        public bool ResupplyInFlight(string programId)
        {
            foreach (Project p in Projects)
                if (p.IsActive && p.ProgramId == programId && string.Equals(p.ModuleRole, "resupply", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Days since the program was last supplied (its completion counts as the first delivery).</summary>
        public int DaysSinceSupplied(Program g)
        {
            int since = g.LastSuppliedDay >= 0 ? g.LastSuppliedDay : g.CreatedDay;
            return Math.Max(0, CurrentDay - since);
        }

        public int SupplyIntervalFor(Program g)
        {
            ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
            return t != null ? GrowthAdjustedInterval(g, t.SupplyIntervalDays) : 0;
        }

        /// <summary>Supplied / due soon / overdue, for the UI and the review.</summary>
        public string SupplyStatus(Program g)
        {
            int interval = SupplyIntervalFor(g);
            if (interval <= 0 || g.CrewAboard.Count == 0) return "no supply needed";
            int since = DaysSinceSupplied(g);
            if (since < interval * 0.75) return "supplied (" + (interval - since) + " days of stores left)";
            if (since < interval) return "supplies running low (" + (interval - since) + " days)";
            return "OVERDUE by " + (since - interval) + " days";
        }

        /// <summary>
        /// KSA-67: crews living at a program that is supplied (not overdue) or self-sufficient. With a life support mod the
        /// host keeps the food, water and air of their live vessels topped up: KSA's resupply flights stand in for the cargo.
        /// An overdue program is left to the life support mod. Empty without life support.
        /// </summary>
        public HashSet<string> CrewKeptSupplied()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!LifeSupportOn || !LifeSupport.TopUpSuppliedVessels) return set;
            foreach (Program g in Programs)
            {
                if (g.Status == ProgramStatus.Cancelled || g.CrewAboard.Count == 0) continue;
                int interval = SupplyIntervalFor(g);
                if (interval > 0 && DaysSinceSupplied(g) >= interval) continue; // overdue: on their own
                foreach (string id in g.CrewAboard) set.Add(id);
            }
            return set;
        }

        /// <summary>Starts a resupply project for a program. Null crew = auto-staffed.</summary>
        public Project CreateResupply(string programId, IList<string> crewIds, MissionMode? mode, out string message)
        {
            Program g = GetProgram(programId);
            if (g == null) { message = "No such program."; return null; }
            ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
            if (t == null || t.ResupplyMissionType.Length == 0) { message = g.Name + " cannot be resupplied."; return null; }
            if (ResupplyInFlight(g.Id)) { message = "A resupply for " + g.Name + " is already under way."; return null; }
            MissionType type = Catalogs.Missions.Get(t.ResupplyMissionType);
            if (type == null) { message = "Resupply mission type " + t.ResupplyMissionType + " is missing from missions.cfg."; return null; }
            Project p = CreateProject(g.Name + ": resupply", type.Id, null, crewIds, null, g.ProgramManager, mode, out message);
            if (p == null) return null;
            p.ProgramId = g.Id;
            p.ModuleRole = "resupply";
            g.Notes.Add("Resupply " + p.Id + " started on day " + CurrentDay + ".");
            Push(null, "Resupply to " + g.Name + " started: " + p.Name);
            RaiseProjectChanged(p);
            return p;
        }

        /// <summary>A resupply resolved: on success the program is supplied and the crews rotate.</summary>
        private void ApplyResupply(Project p, MissionReport r, Program g)
        {
            if (!r.IsSuccess)
            {
                g.Notes.Add("Resupply " + p.Id + " " + OutcomeText(r.Outcome) + " on day " + CurrentDay + ".");
                Push(null, "Resupply to " + g.Name + " " + OutcomeText(r.Outcome) + ".");
                return;
            }
            g.LastSuppliedDay = CurrentDay;
            // Crew rotation: the old crew leave in the stationed rocket (home, or up to the program it returns via),
            // the relief crew stay aboard.
            var relieved = new List<string>(g.CrewAboard);
            string via = RelieveCrew(g);
            foreach (string id in p.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null || c.Departed) continue;
                c.InFlightSinceUT = Clock.UniversalTime;
                c.Status = CrewStatus.Assigned;
                g.CrewAboard.Add(id);
            }
            foreach (string id in p.Crew) AddAchievements(id, new[] { "Resupply:" + g.Body });
            g.Notes.Add("Resupplied on day " + CurrentDay + "; crew rotated (" + relieved.Count + " " + via + ", " + p.Crew.Count + " aboard).");
            Push(null, g.Name + " resupplied; " + relieved.Count + " crew going " + via + ", " + p.Crew.Count + " now aboard.");
            GrowAfterResupply(g, p, null);
        }

        /// <summary>A module was delivered (station core, base module...): its crew now live aboard.</summary>
        private void NoteCrewAboard(Project p, Program g)
        {
            foreach (string id in p.Crew) if (!g.CrewAboard.Contains(id)) g.CrewAboard.Add(id);
            if (g.LastSuppliedDay < 0) g.LastSuppliedDay = CurrentDay;
        }

        /// <summary>Year end: overdue programs wear their crews down and the review hears about it.</summary>
        private void SupplyYearNotes(List<string> notices)
        {
            foreach (Program g in Programs)
            {
                if (g.Status == ProgramStatus.Cancelled) continue;
                int interval = SupplyIntervalFor(g);
                if (interval <= 0 || g.CrewAboard.Count == 0) continue;
                int since = DaysSinceSupplied(g);
                if (since < interval) continue;
                int overdue = since - interval;
                int hit = Math.Min(30, 5 + overdue / 30 * 5);
                foreach (string id in g.CrewAboard)
                {
                    CrewProfile c = Crew.Get(id);
                    if (c != null && !c.Departed) c.Morale = Math.Max(0, c.Morale - hit);
                }
                State.Prestige = Math.Max(0, State.Prestige - 2);
                Push(notices, g.Name + " is " + overdue + " days overdue for resupply: the crew aboard lost " + hit + " morale and the government noticed");
            }
        }
    }
}
