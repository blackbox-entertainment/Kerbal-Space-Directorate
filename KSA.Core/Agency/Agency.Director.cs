using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KSA.Core.Ai;
using KSA.Core.Design;
using KSA.Core.Data;
using KSA.Core.Goals;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Recruiting;

namespace KSA.Core
{
    /// <summary>
    /// The AI Director (KSA-68): an optional model that runs the agency. Each turn it reads a briefing of the whole
    /// agency (money, goals, people, applicants, projects, programs, research, stranded crews) and answers with a few
    /// actions. Every action goes through the same checks as the player's clicks, so a bad idea simply fails and the
    /// reason goes into the next briefing. The player keeps time warp and can take over at any moment.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>Host research tree (KSP R&amp;D); null in sandbox and tests that do not need it.</summary>
        public IResearchHost ResearchHost { get; set; }

        public List<DirectorLogEntry> DirectorLog { get; } = new List<DirectorLogEntry>();
        /// <summary>The Director's standing strategy, in its own words, carried from turn to turn.</summary>
        public string DirectorPlan { get; set; } = string.Empty;
        public int DirectorLastTurnDay { get; set; } = -1;
        public bool DirectorThinking { get { return _directorThinking; } }

        private bool _directorThinking;
        private int _directorThinkingSince;
        private readonly List<string> _directorReasons = new List<string>();
        private readonly List<string> _directorLastResults = new List<string>();

        private DirectorRules DirectorRules { get { return Settings.Director ?? new DirectorRules(); } }

        public bool DirectorOn { get { return State.AiDirector; } }

        /// <summary>Switches the AI Director on or off. Off hands the agency straight back to the player.</summary>
        public void SetDirector(bool on, out string message)
        {
            State.AiDirector = on;
            if (on)
            {
                LlmEndpoint e = Ai != null ? Ai.DirectorEndpoint() : null;
                message = e == null
                    ? "AI Director on, but no model is set up: add an OpenRouter key (or a local model) in AI Setup."
                    : "AI Director on (" + e.Model + "). It takes its first turn now; you keep time warp and can take over at any time.";
                DirectorNote(message);
                RequestDirectorTurn("the Director's office was handed to the AI");
            }
            else
            {
                message = "AI Director off: you are the Director again.";
                DirectorNote(message);
            }
        }

        public void SetDirectorFlights(DirectorFlights flights)
        {
            State.DirectorFlights = flights;
            DirectorNote("Flights: " + FlightsText(flights) + ".");
        }

        public static string FlightsText(DirectorFlights f)
        {
            switch (f)
            {
                case DirectorFlights.All: return "every flight that can fly live is flown live";
                case DirectorFlights.Firsts: return "Firsts fly live, the rest are simulated";
                default: return "everything is simulated";
            }
        }

        /// <summary>Something worth a fresh look happened; the Director takes a turn soon (not sooner than minGapDays).</summary>
        public void RequestDirectorTurn(string reason)
        {
            if (!State.AiDirector || string.IsNullOrEmpty(reason)) return;
            if (!_directorReasons.Contains(reason)) _directorReasons.Add(reason);
            if (_directorReasons.Count > 8) _directorReasons.RemoveAt(0);
        }

        /// <summary>Days until the Director's next scheduled turn (0 = due).</summary>
        public int DaysToDirectorTurn()
        {
            if (DirectorLastTurnDay < 0) return 0;
            int gap = _directorReasons.Count > 0 ? DirectorRules.MinGapDays : DirectorRules.TurnIntervalDays;
            return Math.Max(0, DirectorLastTurnDay + gap - CurrentDay);
        }

        /// <summary>Called from Tick: takes a turn when one is due and nothing is in flight.</summary>
        private void DirectorTick()
        {
            if (!State.AiDirector || !State.Started) return;
            if (_directorThinking)
            {
                // A reply that never came (the host lost it): give up after two turn intervals and try again.
                if (CurrentDay - _directorThinkingSince > DirectorRules.TurnIntervalDays * 2) { _directorThinking = false; DirectorNote("No answer from the model; trying again."); }
                return;
            }
            if (DaysToDirectorTurn() > 0) return;
            TakeDirectorTurn();
        }

        /// <summary>Takes a turn now (the Think now button, and the scheduler).</summary>
        public bool TakeDirectorTurn()
        {
            if (_directorThinking) return false;
            LlmEndpoint endpoint = Ai != null ? Ai.DirectorEndpoint() : null;
            if (endpoint == null || Transport == null)
            {
                if (DirectorLastTurnDay != CurrentDay) DirectorNote("No model to think with: add an OpenRouter key or a local model in AI Setup.");
                DirectorLastTurnDay = CurrentDay;
                return false;
            }
            string why;
            if (!Costs.Allows(Ai, endpoint.IsLocal, CurrentDay, out why))
            {
                if (DirectorLastTurnDay != CurrentDay) DirectorNote("Turn skipped: " + why);
                DirectorLastTurnDay = CurrentDay;
                return false;
            }
            string system = DirectorSystemPrompt();
            string user = DirectorBriefing();
            _directorThinking = true;
            _directorThinkingSince = CurrentDay;
            var reasons = new List<string>(_directorReasons);
            _directorReasons.Clear();
            int day = CurrentDay;
            new LlmClient(Transport, Log).Chat(endpoint, system, user, 0.3, true, result =>
            {
                _directorThinking = false;
                DirectorLastTurnDay = day;
                if (!endpoint.IsLocal) Costs.Record(CurrentDay, DirectorCost(endpoint, result));
                if (result == null || !result.Ok)
                {
                    DirectorNote("The model did not answer: " + (result != null ? result.Error : "no reply") + ". Next try in " + DirectorRules.MinGapDays + " days.");
                    DirectorLastTurnDay = CurrentDay - DirectorRules.TurnIntervalDays + DirectorRules.MinGapDays;
                    return;
                }
                var notices = new List<string>();
                ApplyDirectorReply(result.Text, notices);
                foreach (string n in notices) Push(null, n);
            });
            Log.Info("AI Director turn on day " + day + (reasons.Count > 0 ? " (" + string.Join("; ", reasons.ToArray()) + ")" : string.Empty) + " using " + endpoint.Model);
            return true;
        }

        private double DirectorCost(LlmEndpoint endpoint, LlmResult result)
        {
            double per1k;
            if (ModelRates != null && result != null && ModelRates.TryGetValue(endpoint.Model, out per1k)) return per1k * result.EstimatedTokens / 1000.0;
            return DirectorRules.CostPerTurnUsd;
        }

        // ------------------------------------------------------------------ the reply

        /// <summary>Parses the model's JSON and carries out its actions. Public so tests can feed replies directly.</summary>
        public void ApplyDirectorReply(string text, List<string> notices)
        {
            _directorLastResults.Clear();
            string json = Json.ExtractObject(text ?? string.Empty);
            object parsed; string error;
            if (json == null || !Json.TryParse(json, out parsed, out error) || Json.AsObject(parsed) == null)
            {
                DirectorAdd("Failed", "The reply was not the JSON asked for; nothing was done.");
                _directorLastResults.Add("Your last reply was not valid JSON. Answer with the JSON object only.");
                return;
            }
            Dictionary<string, object> obj = Json.AsObject(parsed);
            string thoughts = Json.GetString(obj, "thoughts", string.Empty);
            if (thoughts.Length > 0) DirectorAdd("Thought", thoughts);
            string plan = Json.GetString(obj, "plan", string.Empty);
            if (plan.Length > 0) DirectorPlan = Clip(plan, 600);
            List<object> actions = Json.GetArray(obj, "actions") ?? new List<object>();
            int done = 0;
            foreach (object ao in actions)
            {
                if (done >= DirectorRules.MaxActionsPerTurn) { _directorLastResults.Add("Only " + DirectorRules.MaxActionsPerTurn + " actions per turn; the rest were ignored."); break; }
                Dictionary<string, object> a = Json.AsObject(ao);
                if (a == null) continue;
                string verb = Json.GetString(a, "do", string.Empty).Trim().ToLowerInvariant();
                if (verb.Length == 0 || verb == "wait") continue;
                done++;
                string why = Json.GetString(a, "why", string.Empty);
                string message;
                bool ok;
                try { ok = DirectorDo(verb, a, out message); }
                catch (Exception e) { ok = false; message = "error: " + e.Message; }
                string line = Describe(verb, a) + (ok ? "" : " failed") + ": " + message + (why.Length > 0 ? "  (why: " + why + ")" : string.Empty);
                DirectorAdd(ok ? "Action" : "Failed", line);
                _directorLastResults.Add((ok ? "OK " : "FAILED ") + Describe(verb, a) + ": " + message);
                if (ok) Push(notices, "AI Director: " + message);
            }
            if (done == 0) DirectorAdd("Thought", "No action this turn.");
        }

        private static string Describe(string verb, Dictionary<string, object> a)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, object> kv in a)
            {
                if (kv.Key == "do" || kv.Key == "why") continue;
                List<object> arr = kv.Value as List<object>;
                string v = arr != null ? string.Join("/", arr.ConvertAll(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToArray()) : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                parts.Add(kv.Key + "=" + v);
            }
            return verb + (parts.Count > 0 ? " " + string.Join(" ", parts.ToArray()) : string.Empty);
        }

        private bool DirectorDo(string verb, Dictionary<string, object> a, out string message)
        {
            switch (verb)
            {
                case "hire": { Applicant ap = FindApplicant(Json.GetString(a, "applicant", "")); if (ap == null) { message = "no such applicant"; return false; } return Hire(ap.Id, out message); }
                case "interview": { Applicant ap = FindApplicant(Json.GetString(a, "applicant", "")); if (ap == null) { message = "no such applicant"; return false; } return Interview(ap.Id, out message); }
                case "reject": { Applicant ap = FindApplicant(Json.GetString(a, "applicant", "")); if (ap == null) { message = "no such applicant"; return false; } return Reject(ap.Id, out message); }
                case "recruit_drive":
                    {
                        RecruitSource src = FindSource(Json.GetString(a, "source", ""));
                        if (src == null || src.DriveCost <= 0) { message = "no such recruiting source"; return false; }
                        return RunDrive(src.Id, out message);
                    }
                case "dismiss": { CrewProfile c = FindCrew(Json.GetString(a, "kerbal", "")); if (c == null) { message = "no such kerbal"; return false; } return Fire(c.Id, out message); }
                case "lab": { CrewProfile c = FindCrew(Json.GetString(a, "kerbal", "")); if (c == null) { message = "no such kerbal"; return false; } return SetLab(c.Id, Json.GetBool(a, "on", true), out message); }
                case "appoint":
                    {
                        Department d; CrewProfile c = FindCrew(Json.GetString(a, "kerbal", ""));
                        if (!TryDepartment(Json.GetString(a, "department", ""), out d)) { message = "department must be Finances, Science, PublicRelations or Operations"; return false; }
                        if (c == null) { message = "no such kerbal"; return false; }
                        return Appoint(d, c.Id, out message);
                    }
                case "vacate":
                    {
                        Department d;
                        if (!TryDepartment(Json.GetString(a, "department", ""), out d)) { message = "unknown department"; return false; }
                        return Vacate(d, out message);
                    }
                case "start_project": return DirectorStartProject(a, out message);
                case "start_program":
                    {
                        ProgramType t = FindProgramType(Json.GetString(a, "program_type", ""));
                        if (t == null) { message = "no such program type"; return false; }
                        string body = Json.GetString(a, "body", t.Bodies.Count > 0 ? t.Bodies[0] : string.Empty);
                        Program g = CreateProgram(null, t.Id, body, null, out message);
                        return g != null;
                    }
                case "resupply":
                    {
                        Program g = FindProgram(Json.GetString(a, "program", ""));
                        if (g == null) { message = "no such program"; return false; }
                        ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
                        MissionType mt = t != null ? Catalogs.Missions.Get(t.ResupplyMissionType) : null;
                        return CreateResupply(g.Id, null, mt != null ? DirectorMode(mt) : MissionMode.Simulate, out message) != null;
                    }
                case "rescue":
                    {
                        Stranding s = GetStranding(Json.GetString(a, "stranding", ""));
                        if (s == null) { message = "no such stranding"; return false; }
                        MissionMode mode = State.DirectorFlights == DirectorFlights.Simulate ? MissionMode.Simulate : MissionMode.WatchLive;
                        return CreateRescue(s.Id, null, mode, out message) != null;
                    }
                case "extend_program": { Program g = FindProgram(Json.GetString(a, "program", "")); if (g == null) { message = "no such program"; return false; } return ExtendProgram(g.Id, out message); }
                case "resume_program": { Program g = FindProgram(Json.GetString(a, "program", "")); if (g == null) { message = "no such program"; return false; } return ResumeProgram(g.Id, out message); }
                case "cancel_program": { Program g = FindProgram(Json.GetString(a, "program", "")); if (g == null) { message = "no such program"; return false; } return CancelProgram(g.Id, out message); }
                case "evacuate": { Program g = FindProgram(Json.GetString(a, "program", "")); if (g == null) { message = "no such program"; return false; } return Evacuate(g.Id, out message); }
                case "cancel_project": { Project p = GetProject(Json.GetString(a, "project", "")); if (p == null) { message = "no such project"; return false; } return CancelProject(p.Id, out message); }
                case "research":
                    {
                        if (ResearchHost == null) { message = "research is not available in this game mode"; return false; }
                        string id = Json.GetString(a, "tech", "");
                        foreach (TechOption o in ResearchHost.Options()) if (string.Equals(o.Title, id, StringComparison.OrdinalIgnoreCase)) { id = o.Id; break; }
                        return ResearchHost.Research(id, out message);
                    }
                default:
                    message = "unknown action '" + verb + "'";
                    return false;
            }
        }

        /// <summary>For an open achievement goal: the missions that earn it, startable first, so the Director can aim at it.</summary>
        private string GoalRoutes(Goals.GoalDefinition g, Goals.GoalState gs)
        {
            if (g == null || g.Condition != Goals.GoalConditionType.Achievement || string.IsNullOrEmpty(g.Pattern) || IsGoalCurrentlyMet(gs)) return string.Empty;
            var open = new List<string>();
            var locked = new List<string>();
            int failures = 0;
            bool underWay = false;
            foreach (MissionType t in Catalogs.Missions.Types)
            {
                if (t.ModuleRole.Length > 0 || t.IsRescue) continue;
                bool earns = false;
                foreach (string a in t.Achievements) if (PatternMatches(a, g.Pattern)) { earns = true; break; }
                if (!earns) continue;
                foreach (Project p in Projects) if (p.IsActive && p.MissionTypeId == t.Id) underWay = true;
                // This year's attempts at it, so a failure reads as "try again", not as a dead end.
                int tries = 0, failed = 0;
                string lastFailure = null;
                foreach (MissionReport r in Reports)
                {
                    if (r.MissionTypeId != t.Id || Calendar.YearOf(r.Day) != CurrentYear) continue;
                    tries++;
                    if (!r.IsSuccess) { failed++; lastFailure = OutcomeText(r.Outcome); }
                }
                failures += failed;
                string entry = t.Id + (tries > 0 ? " (tried " + tries + (failed > 0 ? ", last failure: " + lastFailure : string.Empty) + ")" : string.Empty);
                if (MissionBlocker(t) == null) open.Add(entry); else locked.Add(entry);
            }
            if (open.Count == 0 && locked.Count == 0) return string.Empty;
            string s = " | met by: " + (open.Count > 0 ? string.Join(", ", open.ToArray()) : "nothing you can start yet");
            if (open.Count == 0 && locked.Count > 0) s += " (research to unlock " + string.Join(", ", locked.GetRange(0, Math.Min(3, locked.Count)).ToArray()) + ")";
            if (underWay) s += " | an attempt is under way";
            else if (failures > 0 && open.Count > 0) s += " | STILL OPEN after a failed attempt: try again (fix the cause: a better design, a more experienced pilot, or the other route)";
            return s;
        }

        private bool DirectorStartProject(Dictionary<string, object> a, out string message)
        {
            MissionType type = FindMission(Json.GetString(a, "mission", ""));
            if (type == null) { message = "no such mission"; return false; }
            if (type.ModuleRole.Length > 0) { message = type.Name + " is part of a program; use start_program or resupply"; return false; }
            Template template = null;
            foreach (Template t in TemplatesFor(type)) if (TemplateBlocker(t) == null && t.IsAvailable(InstalledMods)) { template = t; break; }
            List<string> crew = CrewIdsFrom(Json.GetArray(a, "crew"));
            if (crew != null && crew.Count != type.CrewSize) crew = null;           // wrong count: let the agency pick
            List<string> engineers = CrewIdsFrom(Json.GetArray(a, "engineers"));
            if (engineers != null && engineers.Count == 0) engineers = null;
            // The human Director's own design: as asked, or the first that fits when nothing else can fly the mission.
            string design = Json.GetString(a, "design", string.Empty);
            if (design.Length > 0 && FindPlayerDesign(design) == null) design = string.Empty;
            if (design.Length == 0 && MissionBlocker(type) != null) { List<PlayerDesign> fit = PlayerDesignsFor(type); if (fit.Count > 0) design = fit[0].CraftPath; }
            string pd = design.Length > 0 ? design : null;
            Project p = CreateProject(null, type.Id, pd != null ? null : (template != null ? template.Id : null), crew, engineers, null, DirectorMode(type), out message, null, pd);
            if (p == null && (crew != null || engineers != null)) p = CreateProject(null, type.Id, pd != null ? null : (template != null ? template.Id : null), null, null, null, DirectorMode(type), out message, null, pd);
            if (p == null) return false;
            message = p.Name + " started (" + (p.Mode == MissionMode.WatchLive ? "to fly live" : "simulated") + ", " + Money(p.Budget) + ")";
            return true;
        }

        /// <summary>Live or simulated, by the Director's flights setting.</summary>
        public MissionMode DirectorMode(MissionType t)
        {
            if (t == null || !LiveFlightAvailable) return MissionMode.Simulate;
            switch (State.DirectorFlights)
            {
                case DirectorFlights.All: return MissionMode.WatchLive;
                case DirectorFlights.Firsts: return t.IsFirst && !HasAgencyAchievement(t.PrimaryAchievement) ? MissionMode.WatchLive : MissionMode.Simulate;
                default: return MissionMode.Simulate;
            }
        }

        private List<string> CrewIdsFrom(List<object> arr)
        {
            if (arr == null) return null;
            var ids = new List<string>();
            foreach (object o in arr) { CrewProfile c = FindCrew(Convert.ToString(o, CultureInfo.InvariantCulture)); if (c != null && !ids.Contains(c.Id)) ids.Add(c.Id); }
            return ids;
        }

        private CrewProfile FindCrew(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            CrewProfile c = Crew.Get(name);
            if (c != null && !c.Departed) return c;
            foreach (CrewProfile p in Crew.Active) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || p.Name.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private Applicant FindApplicant(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            foreach (Applicant ap in Applicants.Applicants)
                if (string.Equals(ap.Id, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(ap.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return ap;
            return null;
        }

        private RecruitSource FindSource(string idOrName)
        {
            foreach (RecruitSource s in Catalogs.Recruiting.Sources)
                if (string.Equals(s.Id, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        private MissionType FindMission(string idOrName)
        {
            MissionType t = Catalogs.Missions.Get(idOrName);
            if (t != null) return t;
            foreach (MissionType m in Catalogs.Missions.Types) if (string.Equals(m.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        private ProgramType FindProgramType(string idOrName)
        {
            ProgramType t = Catalogs.ProgramTypes.Get(idOrName);
            if (t != null) return t;
            foreach (ProgramType p in Catalogs.ProgramTypes.Types) if (string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private Program FindProgram(string idOrName)
        {
            Program g = GetProgram(idOrName);
            if (g != null) return g;
            foreach (Program p in Programs) if (string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private static bool TryDepartment(string s, out Department d)
        {
            string t = (s ?? string.Empty).Replace(" ", string.Empty);
            if (string.Equals(t, "PR", StringComparison.OrdinalIgnoreCase)) t = "PublicRelations";
            return Enum.TryParse(t, true, out d) && Enum.IsDefined(typeof(Department), d);
        }

        // ------------------------------------------------------------------ the log

        private void DirectorNote(string text) { DirectorAdd("Note", text); }

        private void DirectorAdd(string kind, string text)
        {
            DirectorLog.Add(new DirectorLogEntry { Day = CurrentDay, Year = CurrentYear, Kind = kind, Text = Clean(Clip(text, 500)) });
            int keep = Math.Max(20, DirectorRules.LogKept);
            if (DirectorLog.Count > keep) DirectorLog.RemoveRange(0, DirectorLog.Count - keep);
        }

        private static string Clip(string s, int max) { s = s ?? string.Empty; return s.Length <= max ? s : s.Substring(0, max - 3) + "..."; }

        /// <summary>Saves are config files: no braces, no line breaks, and "//" would start a comment.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace('{', '(').Replace('}', ')').Replace("\r", " ").Replace("\n", " ").Replace("//", "/ /").Trim();
        }

        // ------------------------------------------------------------------ the prompts

        public string DirectorSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the Director of " + AgencyName + ", a space agency in the Kerbol system (a small solar system: home world Kerbin, moons Mun and Minmus, planets Moho, Eve, Duna, Dres, Jool, Eeloo).");
            sb.AppendLine("You run the agency on behalf of the player, who is watching. Each turn you read a briefing and answer with up to " + DirectorRules.MaxActionsPerTurn + " actions.");
            sb.AppendLine("What good looks like: meet the government's goals (they set next year's budget), never run out of money (salaries are paid at the year end), keep kerbals alive and morale up,");
            sb.AppendLine("research the parts that unlock the next missions, and climb the ladder: suborbital, orbit, Mun and Minmus, planets, then bases, settlements and colonies. Rescue anyone stranded.");
            sb.AppendLine("Rules: use only ids and names that appear in the briefing. Locked missions and programs cannot start until their parts are researched. Do not start what you cannot afford; keep a reserve.");
            sb.AppendLine("Crews come from available kerbals; leave crew and engineers out and the agency staffs the project itself, rotating pilots so whoever has flown least goes next. Pilots fly better with more stars, and stars come from flying: spread flights across your pilots instead of sending the same one every time. Scientists in the lab earn Science each year.");
            sb.AppendLine("Flights are how the agency meets its goals and earns prestige and Science: unless money is short or nobody can fly, keep at least one project in design, build or flight.");
            sb.AppendLine("A failure is a lesson, not a reason to stop: fly the cheapest safe mission that moves a goal forward. Do not spend turn after turn only on interviews and drives.");
            sb.AppendLine("A goal is not lost when a flight toward it fails: it stays open until the year-end review. Try again, after fixing what went wrong if you can.");
            sb.AppendLine("Waiting is right only when projects are already under way and nothing needs deciding. Learn from the results of your last turn.");
            sb.AppendLine("Answer with ONE JSON object and nothing else:");
            sb.AppendLine("{\"thoughts\": \"one or two sentences, in character\", \"plan\": \"your standing strategy in under 60 words (kept for next turn)\", \"actions\": [ {\"do\": \"...\", ...params, \"why\": \"short reason\"} ]}");
            sb.AppendLine("Actions:");
            sb.AppendLine("  hire {applicant} | interview {applicant} | reject {applicant} | recruit_drive {source}");
            sb.AppendLine("  dismiss {kerbal} | lab {kerbal, on: true|false} | appoint {department: Finances|Science|PublicRelations|Operations, kerbal} | vacate {department}");
            sb.AppendLine("  start_project {mission, crew?: [names], engineers?: [names], design?: name of one of the human Director's own designs listed with the mission} | cancel_project {project}");
            sb.AppendLine("Prefer the human Director's own designs when one fits a mission: they built them for the agency.");
            sb.AppendLine("  start_program {program_type, body} | resupply {program} | extend_program {program} | resume_program {program} | evacuate {program} | cancel_program {program}");
            sb.AppendLine("  rescue {stranding} | research {tech} | wait {}");
            return sb.ToString();
        }

        /// <summary>The state of the agency, compact, for one turn. Public for the Director window's "show briefing" and tests.</summary>
        public string DirectorBriefing()
        {
            var sb = new StringBuilder();
            int dpy = Clock.DaysPerYear;
            sb.AppendLine("DATE: year " + CurrentYear + ", day " + DayOfYear + " of " + dpy + " (" + Math.Max(0, dpy - DayOfYear) + " days to the budget review).");
            double salaries = 0; foreach (CrewProfile c in Crew.Active) salaries += c.Salary;
            sb.AppendLine("MONEY: " + Money(Ledger.Balance) + "; yearly salaries " + Money(salaries) + "; base grant about " + Money(Settings.BaseGrant) + " a year. Prestige " + State.Prestige.ToString("0") + "/100."
                + (ResearchHost != null ? " Science to spend: " + ResearchHost.Science.ToString("0") + "." : string.Empty)
                + (State.HiringFreeze ? " HIRING FREEZE this year." : string.Empty) + (State.Probation ? " The Director is ON PROBATION: a second bad year replaces you." : string.Empty));
            if (DirectorPlan.Length > 0) sb.AppendLine("YOUR PLAN (from last turn): " + DirectorPlan);
            if (_directorLastResults.Count > 0) { sb.AppendLine("RESULTS OF YOUR LAST TURN:"); foreach (string s in _directorLastResults) sb.AppendLine("  " + s); }
            if (_directorReasons.Count > 0) sb.AppendLine("WHY NOW: " + string.Join("; ", _directorReasons.ToArray()) + ".");

            sb.AppendLine("GOALS THIS YEAR:");
            if (ActiveGoals.Count == 0) sb.AppendLine("  none yet (set at the next review)");
            foreach (GoalState gs in ActiveGoals)
            {
                GoalDefinition g = Catalogs.Goals.Get(gs.GoalId);
                if (g == null) continue;
                sb.AppendLine("  " + GoalStatusText(gs) + " " + g.Name + ": " + g.Description + GoalRoutes(g, gs));
            }

            sb.AppendLine("KERBALS (name | role | stars | morale | status | traits):");
            foreach (CrewProfile c in Crew.Active)
            {
                if (c.Status == CrewStatus.Dead) continue;
                string status = c.IsFlying ? (WhereaboutsOf(c.Id).Length > 0 ? WhereaboutsOf(c.Id) : "flying") : c.ProjectId.Length > 0 ? "on " + c.ProjectId : c.InLab ? "in the lab" : IsDepartmentHead(c.Id) ? "head of " + PostOf(c.Id) : "available";
                sb.AppendLine("  " + c.Name + " | " + c.Role + " | " + c.Stars + " | " + c.Morale + " | " + status + " | courage " + c.Courage.ToString("0.0") + ", stupidity " + c.Stupidity.ToString("0.0")
                    + (c.PersonalityTags.Count > 0 ? ", " + string.Join("/", c.PersonalityTags.ToArray()) : string.Empty));
            }
            var heads = new List<string>();
            foreach (Department d in Enum.GetValues(typeof(Department))) { CrewProfile h = HeadOf(d); heads.Add(d + ": " + (h != null ? h.Name : "vacant")); }
            sb.AppendLine("DEPARTMENTS: " + string.Join(", ", heads.ToArray()) + " (heads come from Management-track or senior kerbals; each gives a bonus).");

            sb.AppendLine("APPLICANTS (id | name | role | what the interviews showed | hire fee | next interview):");
            if (Applicants.Applicants.Count == 0) sb.AppendLine("  none");
            foreach (Applicant ap in Applicants.Applicants)
            {
                var facts = new List<string>();
                if (ap.IsRevealed(Applicant.RevealStars)) facts.Add(ap.Stars + " stars");
                if (ap.IsRevealed(Applicant.RevealCourage)) facts.Add("courage " + ap.Courage.ToString("0.0"));
                if (ap.IsRevealed(Applicant.RevealStupidity)) facts.Add("stupidity " + ap.Stupidity.ToString("0.0"));
                if (ap.IsRevealed(Applicant.RevealTags) && ap.Tags.Count > 0) facts.Add(string.Join("/", ap.Tags.ToArray()));
                sb.AppendLine("  " + ap.Id + " | " + ap.Name + " | " + ap.Role + " | " + (facts.Count > 0 ? string.Join(", ", facts.ToArray()) : "not interviewed") + " | " + Money(ap.HireFee) + " | " + (ap.FullyRevealed ? "nothing left to learn" : Money(InterviewCost(ap))));
            }
            var drives = new List<string>();
            foreach (RecruitSource s in Catalogs.Recruiting.Sources) if (s.DriveCost > 0) drives.Add(s.Id + " (" + s.Name + ", " + Money(s.DriveCost) + ")");
            sb.AppendLine("RECRUITING DRIVES: " + string.Join("; ", drives.ToArray()));

            sb.AppendLine("ACTIVE PROJECTS:");
            int active = 0;
            foreach (Project p in Projects)
            {
                if (!p.IsActive) continue;
                active++;
                MissionType t = Catalogs.Missions.Get(p.MissionTypeId);
                sb.AppendLine("  " + p.Id + " | " + p.Name + " | " + p.Status + (t != null ? ", " + p.DaysRemaining(CurrentDay, t.DurationDays) + " days left" : string.Empty) + " | " + (p.Mode == MissionMode.WatchLive ? "live" : "simulated"));
            }
            if (active == 0) sb.AppendLine("  none: the agency is flying nothing. Start a mission this turn unless you cannot afford one or have no crew.");

            sb.AppendLine("MISSIONS YOU CAN START (id | name | cost | crew | min stars | days | done):");
            var locked = new List<string>();
            foreach (MissionType t in Catalogs.Missions.Types)
            {
                if (t.ModuleRole.Length > 0 || t.IsRescue) continue;
                string block = MissionBlocker(t);
                List<PlayerDesign> mine = PlayerDesignsFor(t);
                if (block != null && mine.Count == 0) { if (locked.Count < 10) locked.Add(t.Name + " (" + block + ")"); continue; }
                Template best = null;
                foreach (Template tp in TemplatesFor(t)) if (TemplateBlocker(tp) == null) { best = tp; break; }
                var names = new List<string>();
                foreach (PlayerDesign d in mine) if (names.Count < 3) names.Add(d.Name);
                sb.AppendLine("  " + t.Id + " | " + t.Name + " | " + Money(ProjectCost(t, best)) + " | " + t.CrewSize + " | " + t.MinStars + " | " + t.DurationDays + " | " + MissionsCompleted(t.Id) + (t.IsFirst && !HasAgencyAchievement(t.PrimaryAchievement) ? " | a First" : string.Empty)
                    + (names.Count > 0 ? " | the human Director's own designs that fit: " + string.Join(", ", names.ToArray()) : string.Empty));
            }
            if (locked.Count > 0) sb.AppendLine("NEXT LOCKED MISSIONS: " + string.Join("; ", locked.ToArray()));

            sb.AppendLine("LIFE SUPPORT: " + LifeSupportText() + ". A base becomes a colony only once its optional industry module (extend_program) delivers a drill, an ore converter and power" + (LifeSupportOn ? " and a greenhouse" : string.Empty) + ".");
            sb.AppendLine("PROGRAMS (stations, bases, cloud cities):");
            foreach (Program g in Programs)
            {
                if (g.Status == ProgramStatus.Cancelled) continue;
                sb.AppendLine("  " + g.Id + " | " + g.Name + " | " + g.Body + " | " + g.Status + ", " + StageText(g.Stage) + " | residents " + g.Residents + " | supplies: " + SupplyStatus(g)
                    + (CanExtend(g) ? " | can add the " + NextModuleRole(g) + " module" : string.Empty) + (NextStageHint(g).Length > 0 ? " | " + NextStageHint(g) : string.Empty));
            }
            var startable = new List<string>();
            foreach (ProgramType t in Catalogs.ProgramTypes.Types)
            {
                string body = t.Bodies.Count > 0 ? t.Bodies[0] : string.Empty;
                if (ProgramBlocker(t, body) == null) startable.Add(t.Id + " at " + body);
            }
            sb.AppendLine("PROGRAM TYPES YOU CAN START: " + (startable.Count > 0 ? string.Join("; ", startable.ToArray()) : "none yet (research first)"));

            List<Stranding> waiting = ActiveStrandings();
            if (waiting.Count > 0)
            {
                sb.AppendLine("STRANDED CREWS (rescue them):");
                foreach (Stranding s in waiting) sb.AppendLine("  " + s.Id + " | " + CrewNames(s.Crew) + " | " + s.Where + " | " + DaysStranded(s) + " days | " + (s.RescueProjectId.Length > 0 ? "rescue " + s.RescueProjectId + " under way" : (RescueBlocker(s) ?? "rescue possible")));
            }

            if (ResearchHost != null)
            {
                List<TechOption> options = ResearchHost.Options();
                options.Sort((x, y) => x.ScienceCost.CompareTo(y.ScienceCost));
                sb.AppendLine("RESEARCH YOU CAN BUY NOW (tech id | title | Science | unlocks):");
                if (options.Count == 0) sb.AppendLine("  nothing available");
                for (int i = 0; i < options.Count && i < 12; i++) sb.AppendLine("  " + options[i].Id + " | " + options[i].Title + " | " + options[i].ScienceCost.ToString("0") + " | " + options[i].Unlocks);
            }

            sb.AppendLine("RECENT RESULTS:");
            int shown = 0;
            for (int i = Reports.Count - 1; i >= 0 && shown < 5; i--, shown++)
                sb.AppendLine("  day " + Reports[i].Day + ": " + Reports[i].ProjectName + ", " + OutcomeText(Reports[i].Outcome) + (Reports[i].Lesson.Length > 0 ? " (" + Reports[i].Lesson + ")" : string.Empty));
            if (shown == 0) sb.AppendLine("  none yet");
            return sb.ToString();
        }
    }
}
