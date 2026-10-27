using System;
using System.Collections.Generic;
using System.Text;
using KSA.Core.Ai;
using KSA.Core.Data;
using KSA.Core.Personnel;

namespace KSA.Core.Flight
{
    /// <summary>Moments in a live flight where the crew, not the program, makes the call.</summary>
    public enum DecisionKind
    {
        /// <summary>The landing site survey found a slope: commit, or wave off and try the next orbit.</summary>
        LandingSite = 0,
        /// <summary>The flight program halted (no trajectory, no propellant): retry, or hold for Mission Control.</summary>
        ProgramHalt = 1,
        /// <summary>A burn will leave little propellant margin: go, or skip it and come home.</summary>
        ThinMargin = 2
    }

    public enum DecisionChoice { Proceed = 0, Abort = 1, Wait = 2 }

    public sealed class DecisionRequest
    {
        public DecisionKind Kind { get; set; }
        /// <summary>Plain-language situation for the log and the prompt, e.g. "Survey: 24% slope at the predicted touchdown, limit 30%."</summary>
        public string Situation { get; set; }
        /// <summary>0..1 how risky proceeding is, from the program's own numbers (slope / limit, dv short / dv needed...).</summary>
        public double Risk { get; set; }
        public DecisionRequest() { Situation = string.Empty; }
    }

    public sealed class CrewDecision
    {
        public DecisionChoice Choice { get; set; }
        /// <summary>What the kerbal said, for the flight log. Canned when no model answered.</summary>
        public string Line { get; set; }
        /// <summary>"Rule" or "AI".</summary>
        public string Source { get; set; }
        public CrewDecision() { Line = string.Empty; Source = "Rule"; }
    }

    /// <summary>
    /// The crew's judgement: a trait rule always answers; a local model (or the API, when allowed) can answer in
    /// character first. The game never waits: callers get the rule's answer if the model is slow or silent.
    /// </summary>
    public static class CrewDecider
    {
        /// <summary>Trait rule: nerve against risk, with stupidity blurring the line. Deterministic for a given rng.</summary>
        public static CrewDecision Rule(CrewFlightProfile profile, CrewProfile pilot, DecisionRequest request, Random rng)
        {
            double nerve = profile != null ? profile.Nerve : 0.5;
            double stupidity = pilot != null ? pilot.Stupidity : 0.3;
            double risk = Math.Max(0, Math.Min(1, request.Risk));
            // A brave pilot accepts more risk; a dim one misjudges it either way.
            double noise = rng != null ? (rng.NextDouble() - 0.5) * stupidity * 0.6 : 0;
            double tolerance = 0.35 + 0.5 * nerve + noise;
            bool go = risk <= tolerance;
            var d = new CrewDecision { Choice = go ? DecisionChoice.Proceed : DecisionChoice.Abort, Source = "Rule" };
            string name = pilot != null ? pilot.Name : "The pilot";
            switch (request.Kind)
            {
                case DecisionKind.LandingSite:
                    d.Line = go ? name + ": \"Slope's within what I can handle. Committing to the landing.\"" : name + ": \"That ground is too rough. Waving off; we'll come around again.\"";
                    if (!go) d.Choice = DecisionChoice.Wait;
                    break;
                case DecisionKind.ProgramHalt:
                    d.Line = go ? name + ": \"Give me a minute, I'll re-run the numbers and try again.\"" : name + ": \"Holding here. Mission Control, we need a plan.\"";
                    if (!go) d.Choice = DecisionChoice.Wait;
                    break;
                default:
                    d.Line = go ? name + ": \"Margins are thin but it'll do. Going.\"" : name + ": \"Not enough in the tanks for that. Bringing us home instead.\"";
                    break;
            }
            return d;
        }

        public static string SystemPrompt(CrewProfile pilot, CrewFlightProfile profile, string agencyName)
        {
            var sb = new StringBuilder();
            sb.Append("You are ").Append(pilot != null ? pilot.Name : "a kerbal pilot").Append(", a crew member of the ").Append(agencyName).Append(" space agency, in the middle of a flight. ");
            if (pilot != null)
            {
                sb.Append("Rank ").Append(pilot.RankId).Append(", ").Append(pilot.Stars).Append(" stars. ");
                sb.Append("Courage ").Append(Math.Round(pilot.Courage, 2)).Append(" and stupidity ").Append(Math.Round(pilot.Stupidity, 2)).Append(" on a 0 to 1 scale; morale ").Append(pilot.Morale).Append(" of 100. ");
                if (pilot.PersonalityTags.Count > 0) sb.Append("Personality: ").Append(string.Join(", ", pilot.PersonalityTags.ToArray())).Append(". ");
                sb.Append("Missions flown: ").Append(pilot.Record.Missions.Count).Append(". ");
            }
            sb.Append("Decide in character. Brave kerbals accept more risk, cautious ones less, dim ones misjudge. ");
            sb.Append("Answer ONLY with JSON: {\"decision\": \"proceed\" | \"abort\" | \"wait\", \"say\": \"one short radio line in your own voice\"}.");
            return sb.ToString();
        }

        public static string UserPrompt(DecisionRequest request)
        {
            string ask;
            switch (request.Kind)
            {
                case DecisionKind.LandingSite: ask = "Do you commit to this landing (proceed) or wave off and try again next orbit (wait)?"; break;
                case DecisionKind.ProgramHalt: ask = "Do you retry the flight program (proceed) or hold for Mission Control (wait)?"; break;
                default: ask = "Do you go ahead with the burn (proceed) or skip it and head home (abort)?"; break;
            }
            return "SITUATION: " + request.Situation + "\nRISK (0 safe .. 1 reckless): " + Math.Round(request.Risk, 2) + "\n" + ask;
        }

        /// <summary>Parses the model's JSON; null when it is not usable.</summary>
        public static CrewDecision ParseReply(string text)
        {
            string json = Json.ExtractObject(text);
            if (json == null) return null;
            object parsed;
            string err;
            if (!Json.TryParse(json, out parsed, out err)) return null;
            var map = parsed as Dictionary<string, object>;
            if (map == null) return null;
            object dec;
            if (!map.TryGetValue("decision", out dec) || !(dec is string)) return null;
            string s = ((string)dec).Trim().ToLowerInvariant();
            var d = new CrewDecision { Source = "AI" };
            if (s.StartsWith("proceed") || s == "go" || s == "yes") d.Choice = DecisionChoice.Proceed;
            else if (s.StartsWith("abort") || s == "no") d.Choice = DecisionChoice.Abort;
            else if (s.StartsWith("wait") || s.StartsWith("hold")) d.Choice = DecisionChoice.Wait;
            else return null;
            object say;
            if (map.TryGetValue("say", out say) && say is string) d.Line = ((string)say).Trim();
            if (d.Line.Length > 240) d.Line = d.Line.Substring(0, 240);
            return d;
        }

        /// <summary>
        /// Asks the model in character; the callback fires once with the model's answer or, on any failure or after
        /// the timeout, with the rule's answer. Never blocks the caller.
        /// </summary>
        public static void Ask(LlmClient client, LlmEndpoint endpoint, IClock clock, double timeoutSeconds, CrewProfile pilot, CrewFlightProfile profile, string agencyName,
            DecisionRequest request, Random rng, Action<CrewDecision> done)
        {
            CrewDecision fallback = Rule(profile, pilot, request, rng);
            if (client == null || endpoint == null) { done(fallback); return; }
            bool answered = false;
            Action<CrewDecision> once = d => { if (answered) return; answered = true; done(d); };
            client.Chat(endpoint, SystemPrompt(pilot, profile, agencyName), UserPrompt(request), 0.7, true, result =>
            {
                CrewDecision d = result != null && result.Ok ? ParseReply(result.Text) : null;
                if (d != null && d.Line.Length == 0) d.Line = fallback.Line;
                else if (d != null && pilot != null && !d.Line.StartsWith(pilot.Name)) d.Line = pilot.Name + ": \"" + d.Line.Trim('"') + "\"";
                once(d ?? fallback);
            });
            // The host polls this: if the model is slow, the rule answers first and the late reply is dropped.
            Pending.Add(new PendingAsk { Deadline = (clock != null ? clock.UniversalTime : 0) + timeoutSeconds, Fire = () => once(fallback) });
        }

        private sealed class PendingAsk { public double Deadline; public Action Fire; }
        private static readonly List<PendingAsk> Pending = new List<PendingAsk>();

        /// <summary>Called from the agency tick: fires the rule answer for any ask whose model has not replied in time.</summary>
        public static void Tick(double universalTime)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (universalTime >= Pending[i].Deadline) { Action f = Pending[i].Fire; Pending.RemoveAt(i); f(); }
            }
        }

        public static int PendingCount { get { return Pending.Count; } }
    }
}
