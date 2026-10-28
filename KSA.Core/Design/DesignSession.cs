using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Data;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    public enum DesignVerdict { Pending = 0, Approved = 1, Short = 2, Failed = 3, Template = 4 }

    public enum DesignSource { Template = 0, Ai = 1 }

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
            n.AddValue("plan", PlanJson);
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
            foreach (string f in Feedback) n.AddValue("feedback", f);
            foreach (string s in StageSummary) n.AddValue("stage", s);
            return n;
        }

        public static DesignRecord FromDataNode(DataNode n)
        {
            if (n == null) return null;
            var r = new DesignRecord
            {
                DesignName = n.GetValue("name", string.Empty),
                Rationale = n.GetValue("rationale", string.Empty),
                PlanJson = n.GetValue("plan", string.Empty),
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
                CraftPath = n.GetValue("craft", string.Empty)
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
            _client.Chat(_endpoint, system, user, 0.4, true, result =>
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
                    foreach (string note in repairs) record.Feedback.Add("Pass " + record.PassesUsed + ": " + note);
                    List<string> problems = StagePlanValidator.Validate(plan, _catalog, allowed);
                    DesignAnalysis analysis = StageAnalyzer.Analyze(plan, _catalog, request.Requirement, _surfaceGravity);
                    foreach (string p in problems) analysis.Problems.Add(p);
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

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int nl = text.IndexOf('\n');
            return nl >= 0 ? text.Substring(0, nl) : text;
        }
    }
}
