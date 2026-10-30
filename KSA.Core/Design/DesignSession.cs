using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Data;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    public enum DesignVerdict { Pending = 0, Approved = 1, Short = 2, Failed = 3, Template = 4 }

    /// <summary>Template, AI engineering team, or the Director in the vehicle editor (Player, 2026-10-28).</summary>
    public enum DesignSource { Template = 0, Ai = 1, Player = 2 }

    /// <summary>What the design phase produced for a project (Jira KSA-44).</summary>
    public sealed class DesignRecord
    {
        public DesignSource Source { get; set; }
        public DesignVerdict Verdict { get; set; }
        public string DesignName { get; set; }
        public string Rationale { get; set; }
        public string PlanJson { get; set; }
        public StagePlan Plan { get; set; }
        public double TotalDeltaV { get; set; }
        public double RequiredDeltaV { get; set; }
        public double LiftoffTwr { get; set; }
        public double RequiredTwr { get; set; }
        public double TotalMass { get; set; }
        public double TotalCost { get; set; }
        public int PartCount { get; set; }
        public double DesignScore { get; set; }
        public int PassesUsed { get; set; }
        public int PassesAllowed { get; set; }
        public string Model { get; set; }
        public List<string> Feedback { get; } = new List<string>();
        public List<string> StageSummary { get; } = new List<string>();
        public string Error { get; set; }
        /// <summary>Host-side craft file path once built (M4 VesselBuilder), or empty.</summary>
        public string CraftPath { get; set; }
        /// <summary>A plane from the spaceplane hangar: rolls out to the runway and the Director flies it (2026-10-28).</summary>
        public bool IsPlane { get; set; }
        /// <summary>How many parts were researched when the design was made; a proven design is reused until this changes (2026-10-28).</summary>
        public int PartsResearched { get; set; }
        /// <summary>ProceduralDesigner.Revision the engineers drew it by (0 = before revisions were kept, or not theirs).</summary>
        public int DesignerRevision { get; set; }
        /// <summary>Propellant this design takes on at the fuel depot, tonnes (0 = it does not stop there).</summary>
        public double DepotFuel { get; set; }

        public DesignRecord()
        {
            DesignName = string.Empty; Rationale = string.Empty; PlanJson = string.Empty; Model = string.Empty; Error = string.Empty; CraftPath = string.Empty;
        }

        public bool IsAi { get { return Source == DesignSource.Ai; } }

        public void ApplyAnalysis(DesignAnalysis a)
        {
            if (a == null) return;
            TotalDeltaV = Math.Round(a.TotalDeltaV);
            RequiredDeltaV = Math.Round(a.RequiredDeltaV);
            LiftoffTwr = Math.Round(a.LiftoffTwr, 2);
            RequiredTwr = a.RequiredTwr;
            TotalMass = Math.Round(a.TotalMass, 2);
            TotalCost = Math.Round(a.TotalCost);
            PartCount = a.PartCount;
            DesignScore = Math.Round(a.DesignScore, 3);
            DepotFuel = Math.Round(a.DepotFuel, 2);
            StageSummary.Clear();
            foreach (StageAnalysis s in a.Stages)
                StageSummary.Add("Stage " + s.Stage + " (" + s.Role + "): " + Math.Round(s.DeltaV) + " m/s, TWR " + s.Twr.ToString("0.00") + ", " + s.StartMass.ToString("0.0") + " t");
        }

        public DataNode ToDataNode()
        {
            var n = new DataNode("DESIGN");
            n.AddValue("source", Source.ToString());
            n.AddValue("verdict", Verdict.ToString());
            n.AddValue("name", DesignName);
            n.AddValue("rationale", Rationale);
            n.AddValue("planB64", PlanJson.Length > 0 ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(PlanJson)) : string.Empty); // JSON braces would break the config format
            n.AddValue("totalDeltaV", TotalDeltaV);
            n.AddValue("requiredDeltaV", RequiredDeltaV);
            n.AddValue("liftoffTwr", LiftoffTwr);
            n.AddValue("requiredTwr", RequiredTwr);
            n.AddValue("totalMass", TotalMass);
            n.AddValue("totalCost", TotalCost);
            n.AddValue("partCount", PartCount);
            n.AddValue("designScore", DesignScore);
            n.AddValue("passesUsed", PassesUsed);
            n.AddValue("passesAllowed", PassesAllowed);
            n.AddValue("model", Model);
            n.AddValue("error", Error);
            n.AddValue("craft", CraftPath);
            n.AddValue("partsResearched", PartsResearched);
            n.AddValue("designerRevision", DesignerRevision);
            if (DepotFuel > 0) n.AddValue("depotFuel", DepotFuel);
            n.AddValue("isPlane", IsPlane);
            foreach (string f in Feedback) n.AddValue("feedback", f);
            foreach (string s in StageSummary) n.AddValue("stage", s);
            return n;
        }

        private static string DecodePlan(DataNode n)
        {
            string b64 = n.GetValue("planB64", string.Empty);
            if (b64.Length > 0) { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch { return string.Empty; } }
            return n.GetValue("plan", string.Empty);
        }

        public static DesignRecord FromDataNode(DataNode n)
        {
            if (n == null) return null;
            var r = new DesignRecord
            {
                DesignName = n.GetValue("name", string.Empty),
                Rationale = n.GetValue("rationale", string.Empty),
                PlanJson = DecodePlan(n),
                TotalDeltaV = n.GetDouble("totalDeltaV", 0),
                RequiredDeltaV = n.GetDouble("requiredDeltaV", 0),
                LiftoffTwr = n.GetDouble("liftoffTwr", 0),
                RequiredTwr = n.GetDouble("requiredTwr", 0),
                TotalMass = n.GetDouble("totalMass", 0),
                TotalCost = n.GetDouble("totalCost", 0),
                PartCount = n.GetInt("partCount", 0),
                DesignScore = n.GetDouble("designScore", 0),
                PassesUsed = n.GetInt("passesUsed", 0),
                PassesAllowed = n.GetInt("passesAllowed", 0),
                Model = n.GetValue("model", string.Empty),
                Error = n.GetValue("error", string.Empty),
                CraftPath = n.GetValue("craft", string.Empty),
                PartsResearched = n.GetInt("partsResearched", 0),
                DesignerRevision = n.GetInt("designerRevision", 0),
                DepotFuel = n.GetDouble("depotFuel", 0),
                IsPlane = n.GetBool("isPlane", false)
            };
            DesignSource src; DesignVerdict v;
            if (Enum.TryParse(n.GetValue("source", "Template"), true, out src)) r.Source = src;
            if (Enum.TryParse(n.GetValue("verdict", "Pending"), true, out v)) r.Verdict = v;
            r.Feedback.AddRange(n.GetValues("feedback"));
            r.StageSummary.AddRange(n.GetValues("stage"));
            if (r.PlanJson.Length > 0)
            {
                StagePlan plan; string err;
                if (StagePlan.TryParse(r.PlanJson, out plan, out err)) r.Plan = plan;
            }
            return r;
        }
    }

    /// <summary>
    /// Runs the request → validate → analyze → feedback → revise loop against an LLM (GDD 5.2). Asynchronous:
    /// each pass goes out through the client and comes back on the host's main thread.
    /// </summary>
    public sealed class DesignSession
    {
        private readonly LlmClient _client;
        private readonly LlmEndpoint _endpoint;
        private readonly IPartCatalog _catalog;
        private readonly ILogger _log;
        private readonly double _surfaceGravity;

        public DesignSession(LlmClient client, LlmEndpoint endpoint, IPartCatalog catalog, ILogger log, double surfaceGravity = 9.81)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (log == null) throw new ArgumentNullException(nameof(log));
            _client = client;
            _endpoint = endpoint;
            _catalog = catalog;
            _log = log;
            _surfaceGravity = surfaceGravity;
        }

        /// <summary>Raised after every pass with the running record, for a progress line in the UI.</summary>
        public event Action<DesignRecord> PassCompleted;

        public void Run(DesignRequest request, int passesAllowed, Action<DesignRecord> done)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var record = new DesignRecord { Source = DesignSource.Ai, Verdict = DesignVerdict.Pending, PassesAllowed = Math.Max(1, passesAllowed), Model = _endpoint.Model };
            string system = PromptBuilder.SystemPrompt(request);
            Pass(request, system, record, null, null, done);
        }

        private void Pass(DesignRequest request, string system, DesignRecord record, StagePlan previous, string feedback, Action<DesignRecord> done)
        {
            string user = PromptBuilder.UserPrompt(request, previous, feedback);
            double temperature = Math.Min(0.8, 0.4 + 0.1 * Math.Min(4, request.PriorDesigns)); // the fifth lander of a type should not be a carbon copy
            _client.Chat(_endpoint, system, user, temperature, true, result =>
            {
                record.PassesUsed++;
                if (!result.Ok)
                {
                    record.Verdict = DesignVerdict.Failed;
                    record.Error = result.Error;
                    record.Feedback.Add("Pass " + record.PassesUsed + ": " + result.Error);
                    done(record);
                    return;
                }

                StagePlan plan; string parseError;
                HashSet<string> allowed = PartFilter.Names(request.AllowedParts);
                string review;
                bool ok = false;
                if (!StagePlan.TryParse(result.Text, out plan, out parseError))
                {
                    review = "The reply could not be read as a stage plan: " + parseError + ". Reply with one JSON object only.";
                    record.Feedback.Add("Pass " + record.PassesUsed + ": unreadable reply (" + parseError + ")");
                    plan = previous;
                }
                else
                {
                    List<string> repairs = StagePlanValidator.Repair(plan, allowed);
                    string fins = Fins.AddIfMissing(plan, request.AllowedParts, _catalog, int.MaxValue, request.Fixes.Contains(Flight.FailureCause.Tumbled));
                    if (fins != null) repairs.Add(fins);
                    string kit = ScienceKit.AddMissing(plan, request);
                    if (kit != null) repairs.Add(kit);
                    string chutes = NeedsChute(request) ? Parachutes.Fit(plan, request.AllowedParts, _catalog, false, request.ChuteFactor) : null;
                    if (chutes != null) repairs.Add(chutes);
                    foreach (string note in repairs) record.Feedback.Add("Pass " + record.PassesUsed + ": " + note);
                    List<string> problems = StagePlanValidator.Validate(plan, _catalog, allowed);
                    DesignAnalysis analysis = StageAnalyzer.Analyze(plan, _catalog, request.Requirement, _surfaceGravity);
                    foreach (string p in problems) analysis.Problems.Add(p);
                    foreach (string p in MissingMustInclude(plan, request)) analysis.Problems.Add(p);
                    string seats = MissingSeats(plan, request, _catalog);
                    if (seats != null) analysis.Problems.Add(seats);
                    string tanks = MissingDepotTanks(plan, request, _catalog);
                    if (tanks != null) analysis.Problems.Add(tanks);
                    string chutesShort = ChutesShort(plan, request, _catalog);
                    if (chutesShort != null) analysis.Problems.Add(chutesShort);
                    record.Plan = plan;
                    record.PlanJson = plan.ToJsonText(false);
                    record.DesignName = plan.DesignName;
                    record.Rationale = plan.Rationale;
                    record.ApplyAnalysis(analysis);
                    review = StageAnalyzer.Feedback(analysis);
                    ok = analysis.Passes;
                    record.Feedback.Add("Pass " + record.PassesUsed + ": " + (ok ? "meets requirements" : FirstLine(review)) + " (" + Math.Round(analysis.TotalDeltaV) + " m/s, TWR " + analysis.LiftoffTwr.ToString("0.00") + ")");
                }

                Action<DesignRecord> progress = PassCompleted;
                if (progress != null) progress(record);

                if (ok)
                {
                    record.Verdict = DesignVerdict.Approved;
                    _log.Info("Design '" + record.DesignName + "' approved after " + record.PassesUsed + " pass(es): " + record.TotalDeltaV + " m/s, TWR " + record.LiftoffTwr);
                    done(record);
                    return;
                }
                if (record.PassesUsed >= record.PassesAllowed)
                {
                    record.Verdict = record.Plan != null ? DesignVerdict.Short : DesignVerdict.Failed;
                    _log.Info("Design '" + record.DesignName + "' out of passes (" + record.PassesUsed + "): " + record.Verdict);
                    done(record);
                    return;
                }
                Pass(request, system, record, plan, review, done);
            });
        }

        /// <summary>The request's MustInclude rules the plan breaks, as review lines.</summary>
        public static List<string> MissingMustInclude(StagePlan plan, DesignRequest request)
        {
            var missing = new List<string>();
            if (plan == null || request == null) return missing;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan.Payload != null)
            {
                if (!string.IsNullOrEmpty(plan.Payload.Command)) used.Add(plan.Payload.Command);
                foreach (string e in plan.Payload.Extras) used.Add(e);
            }
            foreach (PlanStage s in plan.Stages)
            {
                foreach (PlanPart p in s.Core) used.Add(p.Part);
                foreach (PlanPart p in s.Radial) used.Add(p.Part);
                if (!string.IsNullOrEmpty(s.Decoupler)) used.Add(s.Decoupler);
            }
            foreach (KeyValuePair<string, List<string>> rule in request.MustInclude)
            {
                bool ok = false;
                foreach (string part in rule.Value) if (used.Contains(part) || used.Contains(part.Replace('_', '.'))) { ok = true; break; }
                if (!ok) missing.Add("Missing " + rule.Key + ": add one of " + string.Join(", ", rule.Value.ToArray()) + ".");
            }
            return missing;
        }

        /// <summary>Seats in the payload (the command part plus any crewed extras, such as a crew cabin).</summary>
        /// <summary>True when the brief asks for a parachute (the crew lands under it).</summary>
        public static bool NeedsChute(DesignRequest request)
        {
            return request != null && request.MustInclude.Exists(r => r.Key.IndexOf("parachute", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Null when the parachutes land the capsule softly (or none is asked for), else what is wrong (2026-10-29).</summary>
        public static string ChutesShort(StagePlan plan, DesignRequest request, IPartCatalog catalog)
        {
            return NeedsChute(request) ? Parachutes.Shortfall(plan, catalog, request.ChuteFactor) : null;
        }

        public static int SeatsIn(StagePlan plan, IPartCatalog catalog)
        {
            if (plan == null || plan.Payload == null || catalog == null) return 0;
            int seats = 0;
            PartInfo cmd = catalog.Get(plan.Payload.Command);
            if (cmd != null) seats += cmd.CrewCapacity;
            foreach (string e in plan.Payload.Extras) { PartInfo x = catalog.Get(e); if (x != null) seats += x.CrewCapacity; }
            return seats;
        }

        /// <summary>
        /// Null when the payload seats everyone the mission needs (the crew, plus a rescue's stranded kerbals), else the
        /// problem (2026-10-28: rescue craft were sized for the rescuers only).
        /// </summary>
        /// <summary>Propellant in the payload's tanks (extras), tonnes: what a fuel depot core keeps in orbit.</summary>
        public static double PayloadTankFuel(StagePlan plan, IPartCatalog catalog)
        {
            double t = 0;
            if (plan == null || catalog == null) return 0;
            foreach (string e in plan.Payload.Extras) { PartInfo x = catalog.Get(e); if (x != null && x.Kind == PartKind.Tank) t += x.FuelMass; }
            return t;
        }

        /// <summary>Null when a depot core's payload tanks hold what the brief asks, else what is short.</summary>
        public static string MissingDepotTanks(StagePlan plan, DesignRequest request, IPartCatalog catalog)
        {
            if (plan == null || request == null || catalog == null || request.DepotTanks <= 0) return null;
            double have = PayloadTankFuel(plan, catalog);
            if (have >= request.DepotTanks * 0.9) return null;
            return "The depot's tanks (payload extras, stacked above the pod and under the docking port) hold " + have.ToString("0.0") + " t of propellant; the depot needs about " + request.DepotTanks.ToString("0") + " t.";
        }

        public static string MissingSeats(StagePlan plan, DesignRequest request, IPartCatalog catalog)
        {
            if (plan == null || request == null || catalog == null) return null;
            int need = Math.Max(1, request.CrewSize);
            int have = SeatsIn(plan, catalog);
            if (have >= need) return null;
            return "The payload seats " + have + " but " + need + " are needed" + (request.Mission != null && request.Mission.PassengerSeats > 0
                ? " (" + request.Mission.CrewSize + " crew plus " + request.Mission.PassengerSeats + " stranded kerbal" + (request.Mission.PassengerSeats == 1 ? "" : "s") + " to bring home)"
                : string.Empty) + ". Use a command pod with more seats, or add a crew cabin.";
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int nl = text.IndexOf('\n');
            return nl >= 0 ? text.Substring(0, nl) : text;
        }
    }
}
