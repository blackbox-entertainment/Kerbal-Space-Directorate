using System;
using System.Collections.Generic;
using System.Text;
using KSA.Core.Ai;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core.Flight
{
    /// <summary>What the host tells the agency a live flight did (Jira M5). Achievements use the "Type:Body" keys.</summary>
    public sealed class LiveFlightResult
    {
        public List<string> Achievements { get; } = new List<string>();
        public List<string> Deaths { get; } = new List<string>();
        public bool Crashed { get; set; }
        public bool Recovered { get; set; }
        /// <summary>The vessel stayed in space on purpose (station core in orbit, module docked, base module down). Counts like a recovery.</summary>
        public bool Delivered { get; set; }
        /// <summary>The Director declared the crew stranded (KSA-65): alive, somewhere they cannot get home from.</summary>
        public bool Stranded { get; set; }
        public string StrandedBody { get; set; } = string.Empty;
        public bool StrandedOnSurface { get; set; }
        public string StrandedVesselId { get; set; } = string.Empty;
        public List<string> Passengers { get; } = new List<string>();
        public bool DirectorFlown { get; set; }
        public double DaysInSpace { get; set; }
        /// <summary>Timeline lines the host logged during the flight (already stored on the project too).</summary>
        public List<string> Events { get; } = new List<string>();
        public double MaxAltitude { get; set; }
        /// <summary>
        /// Where space begins above the home world, metres (the top of its atmosphere; 70 km on Kerbin), when the host knows
        /// it; 0 when not. A suborbital flight that never climbed this high earns no Suborbit credit (2026-10-29).
        /// </summary>
        public double SpaceAltitude { get; set; }
        public double MaxSpeed { get; set; }
        /// <summary>Farthest from the home world during the flight, km (the host measures it).</summary>
        public double MaxDistanceFromHomeKm { get; set; }
        /// <summary>Days spent landed on another world (the host measures it).</summary>
        public double SurfaceDays { get; set; }
        /// <summary>Science missions: points the experiments collected aboard (the host counts them); the primary is earned when > 0.</summary>
        public double SciencePoints { get; set; }
        /// <summary>The first thing that went wrong, as the flight director saw it (2026-10-30); None when nothing did.</summary>
        public FailureCause Cause { get; set; }
        /// <summary>What it measured, in a sentence: "the second stage tumbled at 16 km (spinning 992 deg/s)".</summary>
        public string CauseDetail { get; set; } = string.Empty;
    }

    /// <summary>Turns a live flight's facts into a MissionReport the agency can apply like a simulated one.</summary>
    public static class LiveFlightResolver
    {
        public static MissionReport Resolve(Project project, MissionType type, IList<string> crewIds, LiveFlightResult result, int day, double designScore, double crewScore)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var r = new MissionReport
            {
                ProjectId = project != null ? project.Id : string.Empty,
                ProjectName = project != null ? project.Name : (type != null ? type.Name : "Mission"),
                MissionTypeId = type != null ? type.Id : string.Empty,
                MissionId = project != null ? project.MissionId : string.Empty,
                Day = day,
                Mode = MissionMode.WatchLive,
                DesignScore = designScore,
                CrewScore = crewScore,
                SuccessChance = 0,
                DaysInSpace = Math.Max(0.05, result.DaysInSpace),
                MaxAltitude = result.MaxAltitude,
                MaxSpeed = result.MaxSpeed,
                FarthestKm = result.MaxDistanceFromHomeKm,
                SurfaceDays = result.SurfaceDays,
                DirectorFlown = result.DirectorFlown
            };
            if (crewIds != null) r.Crew.AddRange(crewIds);
            r.Passengers.AddRange(result.Passengers);

            // Achievements: what the flight actually logged, filtered to what the mission asked for plus any firsts.
            var earned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in result.Achievements) if (!string.IsNullOrEmpty(a)) earned.Add(a);
            // A suborbital flight counts only once it reached space: below the top of the air it was a high flight, not a
            // trip to space (2026-10-29).
            bool belowSpace = result.SpaceAltitude > 0 && result.MaxAltitude < result.SpaceAltitude;
            if (belowSpace) earned.RemoveWhere(a => a.StartsWith("Suborbit:", StringComparison.OrdinalIgnoreCase));
            if (type != null && type.IsScience && result.SciencePoints > 0 && (result.Recovered || result.Delivered)) earned.Add("Science:" + type.Body);
            List<string> wanted = type != null ? type.Achievements : new List<string>();
            foreach (string w in wanted) if (Matches(earned, w)) r.Achievements.Add(w);
            foreach (string a in earned) if (!r.Achievements.Exists(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)) && !a.StartsWith("Recover", StringComparison.OrdinalIgnoreCase)) r.Achievements.Add(a);

            string primary = type != null ? type.PrimaryAchievement : string.Empty;
            bool gotPrimary = primary.Length == 0 || Matches(earned, primary);

            if (result.Deaths.Count > 0)
            {
                r.Outcome = MissionOutcome.LossOfCrew;
                r.Deaths.AddRange(result.Deaths);
                r.Incidents.Add(IncidentKind.Crash);
                r.Lesson = "Crew lost on " + r.ProjectName + ". Never again.";
            }
            else if (result.Stranded)
            {
                r.Outcome = result.Crashed ? MissionOutcome.LossOfVehicle : MissionOutcome.Abort;
                if (crewIds != null) r.Stranded.AddRange(crewIds);
                r.StrandedBody = string.IsNullOrEmpty(result.StrandedBody) ? (type != null ? type.Body : "Kerbin") : result.StrandedBody;
                r.StrandedOnSurface = result.StrandedOnSurface;
                r.StrandedVesselId = result.StrandedVesselId ?? string.Empty;
                r.Lesson = "Crew stranded " + (r.StrandedOnSurface ? "on the surface of " : "in orbit of ") + r.StrandedBody + " on " + r.ProjectName + "; a rescue is needed.";
            }
            else if (!result.Recovered && !result.Delivered)
            {
                r.Outcome = result.Crashed ? MissionOutcome.LossOfVehicle : MissionOutcome.Abort;
                if (result.Crashed) r.Incidents.Add(IncidentKind.Crash);
                r.Lesson = result.Crashed ? "Vehicle lost on " + r.ProjectName + "; the design needs more margin." : "Abort on " + r.ProjectName + "; the crew called it early.";
            }
            else if (gotPrimary && !result.Crashed)
            {
                r.Outcome = MissionOutcome.Success;
                r.Lesson = "Nominal flight on " + r.ProjectName + ".";
            }
            else if (gotPrimary)
            {
                r.Outcome = MissionOutcome.PartialSuccess;
                if (result.Crashed) r.Incidents.Add(IncidentKind.Crash); else r.Incidents.Add(IncidentKind.Other);
                r.Lesson = "Objective met on " + r.ProjectName + " but the vehicle took damage.";
            }
            else
            {
                // The main objective was missed: an abort with the crew safe, whatever else was logged on the way
                // (2026-10-28: a Kerbin orbit flight that peaked at 82 km counted as a partial success and as "done").
                r.Outcome = MissionOutcome.Abort;
                r.Incidents.Add(IncidentKind.Other);
                r.Lesson = r.Achievements.Count > 0
                    ? "Main objective (" + primary + ") missed on " + r.ProjectName + "; the crew came home safe."
                    : "Abort on " + r.ProjectName + "; nothing of note was achieved.";
                if (belowSpace && primary.StartsWith("Suborbit:", StringComparison.OrdinalIgnoreCase))
                    r.Lesson = "Space not reached on " + r.ProjectName + ": it peaked at " + FormatKm(result.MaxAltitude) + ", below the " + FormatKm(result.SpaceAltitude) + " where space begins; the crew came home safe.";
            }
            // What went wrong, and what the engineers will change (2026-10-30). A suborbital flight short of space without a
            // better cause was short of delta-v.
            if (r.Outcome != MissionOutcome.Success)
            {
                r.Cause = result.Cause;
                r.CauseDetail = result.CauseDetail ?? string.Empty;
                if (r.Cause == FailureCause.None && belowSpace && primary.StartsWith("Suborbit:", StringComparison.OrdinalIgnoreCase))
                {
                    r.Cause = FailureCause.BelowSpace;
                    r.CauseDetail = "it peaked at " + FormatKm(result.MaxAltitude) + ", below the " + FormatKm(result.SpaceAltitude) + " where space begins";
                }
                if (r.Cause != FailureCause.None)
                    r.Lesson = (r.Deaths.Count > 0 ? "Crew lost. " : string.Empty) + FailureCauses.Lesson(r.Cause, r.CauseDetail, r.ProjectName);
            }
            if (result.DirectorFlown) r.Lesson += " Director-flown.";
            r.Lines.AddRange(CannedLines(r, result));
            return r;
        }

        private static bool Matches(HashSet<string> earned, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (!pattern.Contains("*")) return earned.Contains(pattern);
            string prefix = pattern.Substring(0, pattern.IndexOf('*'));
            foreach (string e in earned) if (e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<string> CannedLines(MissionReport r, LiveFlightResult result)
        {
            var lines = new List<string>();
            lines.Add("Flown live" + (result.DirectorFlown ? " with the Director at the controls" : " by the crew on autopilot") + (result.Delivered ? "; the vessel stays on station" : string.Empty) + ".");
            if (result.MaxAltitude > 0) lines.Add("Peak altitude " + FormatKm(result.MaxAltitude) + ", top speed " + Math.Round(result.MaxSpeed) + " m/s.");
            int shown = 0;
            for (int i = 0; i < result.Events.Count && shown < 6; i++, shown++) lines.Add(result.Events[i]);
            if (result.Events.Count > 6) lines.Add("... " + (result.Events.Count - 6) + " more entries in the flight log.");
            return lines;
        }

        public static string FormatKm(double metres)
        {
            return metres >= 1000 ? (metres / 1000).ToString("0.0") + " km" : Math.Round(metres) + " m";
        }
    }

    /// <summary>Post-mortems: an AI write-up of what happened, or canned text when AI is off (GDD 5.6).</summary>
    public static class PostMortemWriter
    {
        public static string SystemPrompt()
        {
            return "You are the flight safety office of a small space agency. Write a short, plain-language after-action report of a crewed flight: " +
                   "what happened, why, and one concrete lesson for the next design. Four to six sentences, no headings, no bullet points, no markdown. " +
                   "Refer to crew by name. Keep a calm, professional tone; it may be dry or wry but never cruel about deaths.";
        }

        public static string UserPrompt(MissionReport r, MissionType type, IList<CrewProfile> crew, IList<string> flightLog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MISSION: " + r.ProjectName + (type != null ? " (" + type.Name + ", " + type.Body + ")" : string.Empty));
            sb.AppendLine("OUTCOME: " + Agency.OutcomeText(r.Outcome) + (r.DirectorFlown ? " (the Director took manual control)" : string.Empty));
            if (crew != null && crew.Count > 0)
            {
                sb.Append("CREW: ");
                var names = new List<string>();
                foreach (CrewProfile c in crew) names.Add(c.Name + (r.Deaths.Contains(c.Id) ? " (killed)" : string.Empty));
                sb.AppendLine(string.Join(", ", names.ToArray()));
            }
            if (r.Achievements.Count > 0) sb.AppendLine("ACHIEVED: " + string.Join(", ", r.Achievements.ToArray()));
            sb.AppendLine("FLIGHT LOG:");
            if (flightLog != null) foreach (string e in flightLog) sb.AppendLine("- " + e);
            sb.AppendLine();
            sb.AppendLine("Write the after-action report now.");
            return sb.ToString();
        }

        /// <summary>Asks the model; on any failure the report keeps its canned lines. Asynchronous.</summary>
        public static void Run(LlmClient client, LlmEndpoint endpoint, MissionReport r, MissionType type, IList<CrewProfile> crew, IList<string> flightLog, Action<bool> done)
        {
            if (client == null || endpoint == null) { done(false); return; }
            client.Chat(endpoint, SystemPrompt(), UserPrompt(r, type, crew, flightLog), 0.6, false, result =>
            {
                if (!result.Ok || string.IsNullOrEmpty(result.Text)) { done(false); return; }
                string text = result.Text.Trim();
                r.PostMortem = text;
                int dot = text.LastIndexOf('.');
                string lastSentence = text;
                if (dot > 0)
                {
                    int prev = text.LastIndexOf('.', Math.Max(0, dot - 1));
                    lastSentence = text.Substring(prev + 1, dot - prev).Trim();
                }
                // A measured cause beats the write-up's last sentence (2026-10-30).
                if (lastSentence.Length > 10 && lastSentence.Length < 240 && r.Cause == FailureCause.None) r.Lesson = lastSentence;
                done(true);
            });
        }
    }
}
