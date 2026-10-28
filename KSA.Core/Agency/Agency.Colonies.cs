using System;
using System.Collections.Generic;
using KSA.Core.Finance;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Recruiting;

namespace KSA.Core
{
    /// <summary>
    /// Bases that grow (KSA-64): outpost, base, settlement, colony. Hardware comes from the delivered craft (the host
    /// reads each module's craft file), people from crews aboard, settlers and births. A rocket stays at every base so
    /// crews can rotate and evacuate. Colonies need no supplies and pay Science, funds and prestige each year.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>Host check: what a project's craft carries (drills, converters, power, seats). Null = nothing known.</summary>
        public Func<Project, ModuleHardware> ModuleHardwareCheck { get; set; }

        private ColonyRules Colony { get { return Settings.Colony ?? new ColonyRules(); } }

        public static string StageText(ColonyStage s)
        {
            switch (s)
            {
                case ColonyStage.Outpost: return "outpost";
                case ColonyStage.Base: return "base";
                case ColonyStage.Settlement: return "settlement";
                case ColonyStage.Colony: return "colony";
                default: return "site";
            }
        }

        /// <summary>Self-sufficient: the hardware is there and the place is at least a settlement.</summary>
        public bool IsSelfSufficient(Program g) { return g != null && g.Stage >= ColonyStage.Settlement && g.HardwareSelfSufficient; }

        public int FreeRoom(Program g) { return g == null ? 0 : Math.Max(0, g.Room - g.Residents); }

        /// <summary>One line on what the next stage needs, for the Programs window.</summary>
        public string NextStageHint(Program g)
        {
            ProgramType t = g != null ? Catalogs.ProgramTypes.Get(g.ProgramTypeId) : null;
            if (t == null || !t.Colonize || !Colony.Enabled) return string.Empty;
            ColonyRules c = Colony;
            var need = new List<string>();
            switch (g.Stage)
            {
                case ColonyStage.None:
                case ColonyStage.Outpost:
                    return "Finish every module to make it a base.";
                case ColonyStage.Base:
                    if (!g.HasRocket) need.Add("a rocket stationed there (the return module)");
                    if (g.ResuppliesDelivered < c.SettlementResupplies) need.Add((c.SettlementResupplies - g.ResuppliesDelivered) + " more resupplies");
                    if (g.Residents < c.SettlementResidents) need.Add((c.SettlementResidents - g.Residents) + " more residents");
                    return need.Count == 0 ? string.Empty : "Settlement needs " + string.Join(", ", need.ToArray()) + ".";
                case ColonyStage.Settlement:
                    if (g.Drills <= 0) need.Add("a drill");
                    if (g.Isru <= 0) need.Add("an ore converter (ISRU)");
                    if (g.Power <= 0) need.Add("power (solar, RTG or fuel cell)");
                    if (g.ResuppliesDelivered < c.ColonyResupplies) need.Add((c.ColonyResupplies - g.ResuppliesDelivered) + " more resupplies");
                    if (g.Residents < c.ColonyResidents) need.Add((c.ColonyResidents - g.Residents) + " more residents");
                    return need.Count == 0 ? string.Empty : "Colony needs " + string.Join(", ", need.ToArray()) + ".";
                default:
                    return "Self-sufficient colony.";
            }
        }

        /// <summary>Adds a delivered craft's hardware to the program, once per project.</summary>
        private void CountHardware(Program g, Project p, bool seatsCount)
        {
            if (g == null || p == null || g.HardwareCounted.Contains(p.Id)) return;
            g.HardwareCounted.Add(p.Id);
            Func<Project, ModuleHardware> check = ModuleHardwareCheck;
            ModuleHardware h = null;
            try { h = check != null ? check(p) : null; }
            catch (Exception e) { Log.Warn("Hardware check failed for " + p.Name + ": " + e.Message); }
            if (h == null) h = ModuleHardware.None();
            g.Drills += h.Drills;
            g.Isru += h.Isru;
            g.Power += h.Power;
            if (seatsCount) g.Seats += Math.Max(0, h.Seats);
            if (string.Equals(p.ModuleRole, "return", StringComparison.OrdinalIgnoreCase)) g.HasRocket = true;
            if (h.Drills + h.Isru + h.Power + h.Seats > 0)
                g.Notes.Add(p.Name + " delivered " + HardwareText(h) + ".");
        }

        private static string HardwareText(ModuleHardware h)
        {
            var parts = new List<string>();
            if (h.Seats > 0) parts.Add(h.Seats + " seat" + (h.Seats == 1 ? "" : "s"));
            if (h.Drills > 0) parts.Add(h.Drills + " drill" + (h.Drills == 1 ? "" : "s"));
            if (h.Isru > 0) parts.Add(h.Isru + " ore converter" + (h.Isru == 1 ? "" : "s"));
            if (h.Power > 0) parts.Add(h.Power + " power source" + (h.Power == 1 ? "" : "s"));
            return parts.Count == 0 ? "nothing of note" : string.Join(", ", parts.ToArray());
        }

        /// <summary>Moves the program up the ladder as far as it now qualifies. Never moves it down.</summary>
        private void UpdateStage(Program g, List<string> notices)
        {
            if (g == null || g.Status == ProgramStatus.Cancelled) return;
            ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
            if (t == null) return;
            ColonyRules c = Colony;
            ColonyStage before = g.Stage;
            if (g.Stage < ColonyStage.Outpost && AnyModuleDelivered(g)) g.Stage = ColonyStage.Outpost;
            if (g.Stage < ColonyStage.Base && g.Status == ProgramStatus.Complete) g.Stage = ColonyStage.Base;
            if (t.Colonize && c.Enabled)
            {
                if (g.Stage == ColonyStage.Base && g.HasRocket && g.ResuppliesDelivered >= c.SettlementResupplies && g.Residents >= c.SettlementResidents)
                    g.Stage = ColonyStage.Settlement;
                if (g.Stage == ColonyStage.Settlement && g.HardwareSelfSufficient && g.ResuppliesDelivered >= c.ColonyResupplies && g.Residents >= c.ColonyResidents)
                {
                    g.Stage = ColonyStage.Colony;
                    g.ColonyDay = CurrentDay;
                }
            }
            if (g.Stage == before) return;
            g.Notes.Add("Now a " + StageText(g.Stage) + " (day " + CurrentDay + ", " + g.Residents + " residents).");
            RequestDirectorTurn(g.Name + " became a " + StageText(g.Stage));
            if (g.Stage == ColonyStage.Settlement) Push(notices, g.Name + " is now a settlement: " + g.Residents + " residents, supplies last twice as long");
            if (g.Stage == ColonyStage.Colony)
            {
                foreach (string id in g.CrewAboard) AddAchievements(id, new[] { "Colony:" + g.Body });
                State.Prestige = Math.Min(100, State.Prestige + 5);
                Push(notices, g.Name + " is a self-sufficient colony: " + g.Residents + " people live on " + g.Body + " (+5 prestige)");
                CheckSystemColonized(notices);
            }
        }

        private bool AnyModuleDelivered(Program g)
        {
            foreach (string pid in g.ModuleProjectIds) { Project mp = GetProject(pid); if (mp != null && mp.Status == ProjectStatus.Complete) return true; }
            return false;
        }

        /// <summary>Worlds that can hold a colony: every body named by a colonizable program type.</summary>
        public List<string> ColonizableWorlds()
        {
            var bodies = new List<string>();
            foreach (ProgramType t in Catalogs.ProgramTypes.Types)
            {
                if (!t.Colonize) continue;
                foreach (string b in t.Bodies) if (!bodies.Exists(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase))) bodies.Add(b);
            }
            return bodies;
        }

        /// <summary>Worlds that hold at least one colony.</summary>
        public List<string> ColonizedWorlds()
        {
            var bodies = new List<string>();
            foreach (Program g in Programs)
                if (g.Stage == ColonyStage.Colony && !bodies.Exists(x => string.Equals(x, g.Body, StringComparison.OrdinalIgnoreCase))) bodies.Add(g.Body);
            return bodies;
        }

        private void CheckSystemColonized(List<string> notices)
        {
            if (State.SystemColonized) return;
            List<string> all = ColonizableWorlds();
            if (all.Count == 0) return;
            List<string> done = ColonizedWorlds();
            foreach (string b in all) if (!done.Exists(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase))) return;
            State.SystemColonized = true;
            State.Prestige = 100;
            foreach (Program g in Programs) if (g.Stage == ColonyStage.Colony) foreach (string id in g.CrewAboard) AddAchievements(id, new[] { "Colony:System" });
            Push(notices, AgencyName + " has colonized the Kerbol system: a colony on every world that can hold one");
        }

        /// <summary>A successful resupply: counts toward growth, adds room and, once a finished base, brings settlers.</summary>
        private void GrowAfterResupply(Program g, Project p, List<string> notices)
        {
            ColonyRules c = Colony;
            g.ResuppliesDelivered++;
            CountHardware(g, p, false);
            ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
            if (t != null && t.Colonize && c.Enabled)
            {
                g.ExtraRoom += c.RoomPerResupply;
                if (g.Stage >= ColonyStage.Base)
                {
                    int settlers = Math.Min(c.SettlersPerResupply, FreeRoom(g));
                    for (int i = 0; i < settlers; i++) { if ((g.Colonists + i) % 2 == 0) g.ColonistsFemale++; else g.ColonistsMale++; }
                    if (settlers > 0) g.Notes.Add(settlers + " settler" + (settlers == 1 ? "" : "s") + " arrived with the resupply.");
                }
            }
            UpdateStage(g, notices);
        }

        /// <summary>Supply interval after growth: a settlement stretches it, a self-sufficient one needs none.</summary>
        private int GrowthAdjustedInterval(Program g, int interval)
        {
            if (interval <= 0 || g == null) return interval;
            if (g.Stage == ColonyStage.Colony || IsSelfSufficient(g)) return 0;
            if (g.Stage == ColonyStage.Settlement) return (int)Math.Round(interval * Colony.SettlementSupplyFactor);
            return interval;
        }

        /// <summary>Is there at least one man and one woman living there?</summary>
        public bool HasCouple(Program g)
        {
            if (g == null) return false;
            int men = g.ColonistsMale, women = g.ColonistsFemale;
            foreach (string id in g.CrewAboard) { CrewProfile c = Crew.Get(id); if (c == null || c.Departed) continue; if (c.Female) women++; else men++; }
            return men > 0 && women > 0;
        }

        private int Pairs(Program g)
        {
            int men = g.ColonistsMale, women = g.ColonistsFemale;
            foreach (string id in g.CrewAboard) { CrewProfile c = Crew.Get(id); if (c == null || c.Departed) continue; if (c.Female) women++; else men++; }
            return Math.Min(men, women);
        }

        /// <summary>Year end: births, colony building, yields, colony-born applicants.</summary>
        private void ColonyYearEnd(List<string> notices)
        {
            ColonyRules c = Colony;
            if (!c.Enabled) return;
            foreach (Program g in Programs)
            {
                if (g.Status == ProgramStatus.Cancelled || g.Stage < ColonyStage.Settlement) continue;

                // A colony builds its own room.
                if (g.Stage == ColonyStage.Colony)
                    g.ExtraRoom += c.ColonyRoomPerYear + (c.ColonyRoomPerResidents > 0 ? g.Residents / c.ColonyRoomPerResidents : 0);

                // Births: self-sufficient, a man and a woman, and room.
                if (IsSelfSufficient(g) && HasCouple(g))
                {
                    int births = 0;
                    int pairs = Pairs(g);
                    for (int i = 0; i < pairs && FreeRoom(g) > 0; i++)
                    {
                        if (Rng.NextDouble() >= c.BirthChancePerPair) continue;
                        if (Rng.Next(2) == 0) g.ColonistsFemale++; else g.ColonistsMale++;
                        births++;
                    }
                    if (births > 0)
                    {
                        g.Notes.Add(births + " born on " + g.Body + " in year " + CurrentYear + ".");
                        Push(notices, g.Name + ": " + births + " new resident" + (births == 1 ? "" : "s") + " born on " + g.Body + " (" + g.Residents + " now)");
                    }
                    else if (FreeRoom(g) <= 0) Push(notices, g.Name + " is full: send a resupply or let the colony build to make room");
                }

                // Yields.
                double factor = g.Stage == ColonyStage.Colony ? 1.0 : 0.5;
                double science = Math.Round(g.Residents * c.SciencePerResident * factor, 1);
                double funds = Math.Round(g.Residents * c.FundsPerResident * factor);
                if (science > 0)
                {
                    State.ScienceEarned += science;
                    Action<double, string> h = ScienceEarned;
                    if (h != null) h(science, g.Name + ", year " + CurrentYear);
                }
                if (funds > 0) Ledger.Deposit(funds, LedgerCategory.Other, g.Name + " output, year " + CurrentYear, CurrentDay, CurrentYear);
                if (g.Stage == ColonyStage.Colony) State.Prestige = Math.Min(100, State.Prestige + c.PrestigePerColony);
                Push(notices, g.Name + " (" + StageText(g.Stage) + ", " + g.Residents + " residents) returned " + science.ToString("0.#") + " Science and " + Money(funds));

                // Colony-born kerbals come of age and apply.
                if (g.Stage == ColonyStage.Colony && g.Colonists >= c.ComeOfAgeMinColonists && Rng.NextDouble() < c.ComeOfAgeChance)
                {
                    RecruitSource src = Catalogs.Recruiting.Get(c.BornSource);
                    if (src != null)
                    {
                        var made = Applicants.Generate(src, 1, State.Prestige, CurrentDay, Rng, Names, Settings.PersonalityTags, TakenNames());
                        foreach (Applicant a in made)
                        {
                            if (g.ColonistsMale >= g.ColonistsFemale && g.ColonistsMale > 0) g.ColonistsMale--; else if (g.ColonistsFemale > 0) g.ColonistsFemale--;
                            Push(notices, a.Name + ", born on " + g.Body + ", applies to join the agency (" + RosterView.RoleText(a.Role) + ")");
                        }
                    }
                }
                UpdateStage(g, notices);
            }
        }

        /// <summary>Everyone aboard flies the stationed rocket home (or to the program it returns via). Colonists stay.</summary>
        public bool Evacuate(string programId, out string message)
        {
            Program g = GetProgram(programId);
            if (g == null) { message = "No such program."; return false; }
            if (!g.HasRocket) { message = g.Name + " has no rocket stationed there; deliver the return module first."; return false; }
            if (g.CrewAboard.Count == 0) { message = "Nobody from the agency is aboard " + g.Name + "."; return false; }
            int n = g.CrewAboard.Count;
            string via = RelieveCrew(g);
            g.Notes.Add("Evacuated on day " + CurrentDay + ": " + n + " crew " + via + ".");
            message = n + " crew evacuated from " + g.Name + " " + via + ".";
            Push(null, message);
            return true;
        }

        /// <summary>Takes the crew aboard off the program: home, or to the program it returns via. Returns where they went.</summary>
        private string RelieveCrew(Program g)
        {
            ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
            Program via = t != null && t.ReturnVia.Length > 0 ? ProgramAt(t.ReturnVia, g.Body, true) : null;
            var leaving = new List<string>(g.CrewAboard);
            g.CrewAboard.Clear();
            foreach (string id in leaving)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null || c.Departed) continue;
                if (via != null) { if (!via.CrewAboard.Contains(id)) via.CrewAboard.Add(id); }
                else ReturnFromStation(id);
            }
            return via != null ? "up to " + via.Name + " to wait for its rocket home" : "home";
        }

        /// <summary>A program of the given type at a body; with <paramref name="complete"/>, only a finished one.</summary>
        public Program ProgramAt(string programTypeId, string body, bool complete)
        {
            foreach (Program g in Programs)
            {
                if (g.Status == ProgramStatus.Cancelled) continue;
                if (!string.Equals(g.ProgramTypeId, programTypeId, StringComparison.OrdinalIgnoreCase) || !string.Equals(g.Body, body, StringComparison.OrdinalIgnoreCase)) continue;
                if (complete && g.Status != ProgramStatus.Complete && g.Stage < ColonyStage.Base) continue;
                return g;
            }
            return null;
        }

        /// <summary>A finished program whose recipe grew (a return module added after the save): start the missing module.</summary>
        public bool CanExtend(Program g)
        {
            ProgramType t = g != null ? Catalogs.ProgramTypes.Get(g.ProgramTypeId) : null;
            return t != null && g.Status == ProgramStatus.Complete && g.NextModuleIndex < t.Modules.Count && !ModuleInFlight(g);
        }

        public string NextModuleRole(Program g)
        {
            ProgramType t = g != null ? Catalogs.ProgramTypes.Get(g.ProgramTypeId) : null;
            return t != null && g.NextModuleIndex < t.Modules.Count ? t.Modules[g.NextModuleIndex].Role : string.Empty;
        }

        private bool ModuleInFlight(Program g)
        {
            foreach (string pid in g.ModuleProjectIds) { Project mp = GetProject(pid); if (mp != null && mp.IsActive) return true; }
            return false;
        }

        public bool ExtendProgram(string programId, out string message)
        {
            Program g = GetProgram(programId);
            if (g == null) { message = "No such program."; return false; }
            if (!CanExtend(g)) { message = g.Name + " has nothing more to build."; return false; }
            Project p = StartNextModule(g, out message);
            if (p == null) return false;
            message = g.Name + ": " + p.Name + " is in design.";
            return true;
        }
    }
}
