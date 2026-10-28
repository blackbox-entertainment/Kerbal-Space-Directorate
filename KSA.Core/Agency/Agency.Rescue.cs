using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// Stranded crews and rescue missions (KSA-65). A flight that leaves survivors somewhere they cannot get home from
    /// strands them: they stay "away" (not assignable, losing morale each year) until a rescue brings them home or, after
    /// RESCUE lostAfterDays, they are declared lost. A rescue is a project with a mission type built for the spot.
    /// </summary>
    public sealed partial class Agency
    {
        public List<Stranding> Strandings { get; } = new List<Stranding>();

        private RescueRules Rescue { get { return Settings.Rescue ?? new RescueRules(); } }

        public Stranding GetStranding(string id)
        {
            foreach (Stranding s in Strandings) if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>Strandings still waiting for someone.</summary>
        public List<Stranding> ActiveStrandings()
        {
            var list = new List<Stranding>();
            foreach (Stranding s in Strandings) if (!s.Resolved) list.Add(s);
            return list;
        }

        public Stranding StrandingFor(string crewId)
        {
            foreach (Stranding s in Strandings) if (!s.Resolved && s.Crew.Contains(crewId)) return s;
            return null;
        }

        public int DaysStranded(Stranding s) { return s == null ? 0 : Math.Max(0, CurrentDay - s.Day); }

        /// <summary>Where a kerbal is, for the roster and the Service Record: stranded, aboard a program, or empty.</summary>
        public string WhereaboutsOf(string crewId)
        {
            Stranding s = StrandingFor(crewId);
            if (s != null) return "stranded " + s.Where + " since day " + s.Day;
            foreach (Program g in Programs) if (g.Status != ProgramStatus.Cancelled && g.CrewAboard.Contains(crewId)) return "aboard " + g.Name;
            return string.Empty;
        }

        /// <summary>Simulated flights: an abort or a lost vehicle with survivors may leave them stranded.</summary>
        private void MaybeStrand(MissionType type, MissionReport r)
        {
            RescueRules rules = Rescue;
            if (!rules.Enabled || type == null || r == null) return;
            if (r.Outcome != MissionOutcome.Abort && r.Outcome != MissionOutcome.LossOfVehicle) return;
            if (type.Profile == MissionProfile.Suborbital) return;
            bool home = string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            double chance = home ? rules.StrandChanceKerbinOrbit : rules.StrandChanceAway;
            if (Rng.NextDouble() >= chance) return;
            var survivors = new List<string>();
            foreach (string id in r.Crew) if (!r.Deaths.Contains(id)) survivors.Add(id);
            if (survivors.Count == 0) return;
            r.Stranded.AddRange(survivors);
            r.StrandedBody = type.Body;
            bool floating = type.Floating || string.Equals(type.Body, "Jool", StringComparison.OrdinalIgnoreCase);
            r.StrandedOnSurface = !home && !floating && type.IsLanding && Rng.NextDouble() < 0.5;
            r.Lines.Add("The crew could not get home: stranded " + (r.StrandedOnSurface ? "on the surface of " : "in orbit of ") + r.StrandedBody + ". A rescue is needed.");
            r.Lesson = "Crew stranded on " + r.ProjectName + "; bring them home.";
        }

        /// <summary>Called by ApplyReport after the crew records are written: the stranded stay away and wait.</summary>
        private void Strand(Project p, MissionReport r, List<string> notices)
        {
            var s = new Stranding
            {
                Id = "S" + State.NextStrandingNumber++,
                Body = string.IsNullOrEmpty(r.StrandedBody) ? "Kerbin" : r.StrandedBody,
                OnSurface = r.StrandedOnSurface,
                VesselId = r.StrandedVesselId ?? string.Empty,
                Day = CurrentDay,
                Cause = p != null ? p.Name : r.ProjectName
            };
            foreach (string id in r.Stranded)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null || c.Departed || c.Status == CrewStatus.Dead) continue;
                c.InFlightSinceUT = Clock.UniversalTime; // away: not assignable, days keep counting
                c.Status = CrewStatus.Assigned;
                s.Crew.Add(id);
                RecordIncident(id, IncidentKind.Stranded, "Stranded " + s.Where + " after " + s.Cause);
            }
            if (s.Crew.Count == 0) return;
            Strandings.Add(s);
            RequestDirectorTurn(CrewNames(s.Crew) + " stranded " + s.Where);
            State.Prestige = Math.Max(0, State.Prestige - 2);
            Push(notices, CrewNames(s.Crew) + " stranded " + s.Where + ". Launch a rescue from the Projects window.");
        }

        private string CrewNames(IList<string> ids)
        {
            var names = new List<string>();
            foreach (string id in ids) { CrewProfile c = Crew.Get(id); names.Add(c != null ? c.Name : id); }
            return string.Join(", ", names.ToArray());
        }

        /// <summary>The mission a rescue flies: out to the stranding, pick them up, home. Priced like a custom mission.</summary>
        public MissionType RescueMissionFor(Stranding s, int rescuers)
        {
            bool home = string.Equals(s.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            var spec = new CustomMissionSpec
            {
                Name = "Rescue " + CrewNames(s.Crew),
                Body = s.Body,
                Profile = s.OnSurface ? MissionProfile.Land : (home ? MissionProfile.Orbit : MissionProfile.OrbitBody),
                Orbits = 1,
                SurfaceDays = 0.1,
                CrewSize = Math.Max(1, rescuers)
            };
            MissionType t = PreviewCustom(spec);
            t.Description = "Rescue: fly to " + CrewNames(s.Crew) + " " + s.Where + " and bring them home. Leave seats free for them (" + s.Crew.Count + ").";
            t.Purpose = "Rescue";
            t.Difficulty = Math.Min(0.95, t.Difficulty + 0.05);
            t.Achievements.RemoveAll(a => a.StartsWith("EVA:", StringComparison.OrdinalIgnoreCase));
            return t;
        }

        /// <summary>Starts a rescue project for a stranding. Null crew = one auto-picked pilot.</summary>
        public Project CreateRescue(string strandingId, IList<string> crewIds, MissionMode? mode, out string message)
        {
            Stranding s = GetStranding(strandingId);
            if (s == null || s.Resolved) { message = "Nobody is waiting there."; return null; }
            if (s.RescueProjectId.Length > 0) { Project live = GetProject(s.RescueProjectId); if (live != null && live.IsActive) { message = "A rescue is already under way: " + live.Name + "."; return null; } }
            int rescuers = crewIds != null && crewIds.Count > 0 ? crewIds.Count : 1;
            MissionType t = RescueMissionFor(s, rescuers);
            string blocked = MissionBlocker(t);
            if (blocked != null) { message = t.Name + " " + blocked + "."; return null; }
            Catalogs.Missions.Add(t);
            Project p = CreateProject(t.Name, t.Id, null, crewIds, null, null, mode, out message);
            if (p == null) { Catalogs.Missions.Remove(t.Id); return null; }
            p.ModuleRole = "rescue";
            p.RescueId = s.Id;
            s.RescueProjectId = p.Id;
            message = "Rescue " + p.Id + " started for " + CrewNames(s.Crew) + " " + s.Where + ".";
            Template tp = Catalogs.Templates.Get(p.TemplateId);
            int seatsNeeded = p.Crew.Count + s.Crew.Count;
            if (tp != null && tp.Seats > 0 && tp.Seats < seatsNeeded)
            {
                string note = "The " + tp.Name + " has " + tp.Seats + " seat" + (tp.Seats == 1 ? "" : "s") + " but the rescue needs " + seatsNeeded + ": let the engineers design one, or build one in the hangar with room for everyone.";
                p.Notes.Add(note);
                message += " " + note;
            }
            Push(null, message);
            RaiseProjectChanged(p);
            return p;
        }

        /// <summary>A rescue resolved: on success the stranded come home with the rescuers.</summary>
        private void ApplyRescue(Project p, MissionReport r, List<string> notices)
        {
            Stranding s = GetStranding(p.RescueId);
            if (s == null || s.Resolved) return;
            if (string.Equals(s.RescueProjectId, p.Id, StringComparison.OrdinalIgnoreCase)) s.RescueProjectId = string.Empty;
            // Simulated: a successful rescue brings everyone. Live: only those actually aboard at the end come home.
            var saved = new List<string>();
            if (r.IsSuccess && r.Stranded.Count == 0)
                foreach (string id in s.Crew) if (r.Mode != MissionMode.WatchLive || r.Passengers.Contains(id)) saved.Add(id);
            if (saved.Count == 0)
            {
                foreach (string id in s.Crew) { CrewProfile c = Crew.Get(id); if (c != null && !c.Departed) c.Morale = Math.Max(0, c.Morale - 5); }
                Push(notices, "The rescue of " + CrewNames(s.Crew) + " failed (" + (r.IsSuccess ? "nobody was brought aboard" : OutcomeText(r.Outcome)) + "); they are still waiting " + s.Where + ".");
                return;
            }
            foreach (string id in saved) BringHome(s, id, "Rescued by " + p.Name);
            foreach (string id in r.Crew) if (!r.Deaths.Contains(id)) RecordIncident(id, IncidentKind.Rescue, "Flew the rescue of " + CrewNames(saved));
            State.Prestige = Math.Min(100, State.Prestige + 3);
            Push(notices, CrewNames(saved) + " rescued after " + DaysStranded(s) + " days " + s.Where + " (+3 prestige)"
                + (s.Resolved ? string.Empty : "; " + CrewNames(s.Crew) + " still waiting"));
        }

        /// <summary>One stranded kerbal is home: off the stranding, back on the roster, on the record.</summary>
        private void BringHome(Stranding s, string id, string how)
        {
            if (s == null || !s.Crew.Contains(id)) return;
            int days = DaysStranded(s);
            s.Crew.Remove(id);
            if (s.Crew.Count == 0) s.Resolved = true;
            CrewProfile c = Crew.Get(id);
            if (c == null || c.Departed) return;
            ReturnFromStation(id);
            AddAchievements(id, new[] { "Recover:-" });
            RecordIncident(id, IncidentKind.Rescue, how + " after " + days + " days " + s.Where);
            c.Morale = Math.Min(100, c.Morale + Rescue.RescuedMorale);
        }

        /// <summary>The host recovered a stranded kerbal some other way (a craft the Director flew). True if they were stranded.</summary>
        public bool RecoverStranded(string crewId)
        {
            Stranding s = StrandingFor(crewId);
            if (s == null) return false;
            BringHome(s, crewId, "Brought home by the Director");
            Push(null, (Crew.Get(crewId) != null ? Crew.Get(crewId).Name : crewId) + " is home after being stranded " + s.Where);
            return true;
        }

        /// <summary>Year end: the stranded lose heart, the public notices, and after long enough they are given up for lost.</summary>
        private void StrandedYearEnd(List<string> notices)
        {
            RescueRules rules = Rescue;
            if (!rules.Enabled) return;
            foreach (Stranding s in Strandings)
            {
                if (s.Resolved) continue;
                if (rules.LostAfterDays > 0 && DaysStranded(s) >= rules.LostAfterDays)
                {
                    s.Resolved = true;
                    foreach (string id in s.Crew) notices.AddRange(RecordDeath(id, "Lost " + s.Where + " after " + DaysStranded(s) + " days stranded; never recovered"));
                    Push(notices, CrewNames(s.Crew) + " were declared lost " + s.Where + " after " + DaysStranded(s) + " days");
                    continue;
                }
                foreach (string id in s.Crew) { CrewProfile c = Crew.Get(id); if (c != null && !c.Departed) c.Morale = Math.Max(0, c.Morale - rules.MoralePerYear); }
                State.Prestige = Math.Max(0, State.Prestige - rules.PrestigePerYear);
                Push(notices, CrewNames(s.Crew) + " are still stranded " + s.Where + " (" + DaysStranded(s) + " days); the public wants them home");
            }
        }
    }
}
