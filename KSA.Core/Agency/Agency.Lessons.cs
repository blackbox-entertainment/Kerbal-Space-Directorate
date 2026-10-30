using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// Lessons learned (2026-10-28): every mission that is not a clean success leaves one line with the design, the pilot,
    /// the outcome and the key moments of the flight (fuel gone, how fast it hit, a halt). The newest are kept for the
    /// whole game and given to the AI Director every turn, so a mistake stays known after it drops out of recent results.
    /// </summary>
    public sealed partial class Agency
    {
        public const int MaxLessons = 12;

        /// <summary>Newest last. Saved.</summary>
        public List<string> LessonsLearned { get; } = new List<string>();

        private static readonly string[] KeyMoments =
        {
            "exhausted", "halted", "killed", "too fast", "Splashdown", "Landed on", "crash", "destroyed", "Abort", "abort",
            "stranded", "short", "off retrograde", "no thrust", "Not in orbit", "forced open"
        };

        /// <summary>A save from before lessons were kept: build them once from the reports it has.</summary>
        internal void BackfillLessons()
        {
            if (LessonsLearned.Count > 0) return;
            foreach (MissionReport r in Reports)
            {
                Project p = GetProject(r.ProjectId);
                if (p != null) RecordLesson(p, r);
            }
        }

        private void RecordLesson(Project p, MissionReport r)
        {
            if (p == null || r == null || r.Outcome == MissionOutcome.Success) return;
            string lesson = BuildLesson(p, r);
            if (!string.IsNullOrEmpty(lesson))
            {
                LessonsLearned.Add(lesson);
                if (LessonsLearned.Count > MaxLessons) LessonsLearned.RemoveRange(0, LessonsLearned.Count - MaxLessons);
            }
            NoteDesignFix(r);
        }

        // ------------------------------------------------------------------ design fixes (2026-10-30)

        /// <summary>
        /// The causes of failed flights, per mission type, oldest first (saved). The engineers' next design for that mission
        /// fixes each one (FailureCauses.Fix): fins higher up, more margin, bigger parachutes, a heat shield.
        /// </summary>
        public Dictionary<string, List<Flight.FailureCause>> DesignFixes { get; } = new Dictionary<string, List<Flight.FailureCause>>(StringComparer.OrdinalIgnoreCase);

        private const int MaxFixesPerMission = 6;

        private void NoteDesignFix(MissionReport r)
        {
            if (r == null || r.Cause == Flight.FailureCause.None || string.IsNullOrEmpty(r.MissionTypeId)) return;
            if (r.DirectorFlown) return; // the Director had the stick: the rocket may not be to blame
            List<Flight.FailureCause> list;
            if (!DesignFixes.TryGetValue(r.MissionTypeId, out list)) DesignFixes[r.MissionTypeId] = list = new List<Flight.FailureCause>();
            list.Add(r.Cause);
            if (list.Count > MaxFixesPerMission) list.RemoveRange(0, list.Count - MaxFixesPerMission);
            Log.Info("Design fix noted for " + r.MissionTypeId + ": " + r.Cause + " (" + Flight.FailureCauses.Fix(r.Cause) + ")");
        }

        /// <summary>The fixes the next design for a mission must make (empty when it never failed with a known cause).</summary>
        public List<Flight.FailureCause> FixesFor(string missionTypeId)
        {
            List<Flight.FailureCause> list;
            return !string.IsNullOrEmpty(missionTypeId) && DesignFixes.TryGetValue(missionTypeId, out list) ? new List<Flight.FailureCause>(list) : new List<Flight.FailureCause>();
        }

        /// <summary>True when the newest flight of a mission failed with a known cause: its old design is not reused.</summary>
        public bool LastFlightFailed(string missionTypeId)
        {
            for (int i = Reports.Count - 1; i >= 0; i--)
                if (string.Equals(Reports[i].MissionTypeId, missionTypeId, StringComparison.OrdinalIgnoreCase))
                    return !Reports[i].IsSuccess && Reports[i].Cause != Flight.FailureCause.None;
            return false;
        }

        /// <summary>One line: mission (design, delta-v, pilot): outcome. Key moments.</summary>
        internal string BuildLesson(Project p, MissionReport r)
        {
            var parts = new List<string>();
            if (p.Design != null && !string.IsNullOrEmpty(p.Design.DesignName))
                parts.Add(p.Design.DesignName + (p.Design.TotalDeltaV > 0 ? ", " + Math.Round(p.Design.TotalDeltaV) + (p.Design.RequiredDeltaV > 0 ? " of " + Math.Round(p.Design.RequiredDeltaV) : string.Empty) + " m/s" : string.Empty));
            CrewProfile pilot = r.Crew.Count > 0 ? Crew.Get(r.Crew[0]) : null;
            if (pilot != null) parts.Add("pilot " + pilot.Name + " (" + pilot.Stars + " star" + (pilot.Stars == 1 ? "" : "s") + ")");
            if (r.Mode == MissionMode.Simulate) parts.Add("simulated");
            string head = "Y" + CurrentYear + " " + r.ProjectName + (parts.Count > 0 ? " (" + string.Join(", ", parts.ToArray()) + ")" : string.Empty) + ": " + OutcomeText(r.Outcome) + ".";
            var moments = new List<string>();
            var source = new List<string>(p.FlightLog); // the live flight log has the causes; simulated ones use the report
            source.AddRange(r.Lines);
            foreach (string line in source)
            {
                if (moments.Count >= 3) break;
                foreach (string k in KeyMoments)
                    if (line.IndexOf(k, StringComparison.Ordinal) >= 0) { string m = Trim(line); if (!moments.Contains(m)) moments.Add(m); break; }
            }
            // A known cause says it all, with the fix (2026-10-30); otherwise the key moments of the flight log.
            string text = r.Cause != Flight.FailureCause.None
                ? head + " Cause: " + (string.IsNullOrEmpty(r.CauseDetail) ? Flight.FailureCauses.Describe(r.Cause) : r.CauseDetail.TrimEnd('.')) + "; " + Flight.FailureCauses.Fix(r.Cause) + "."
                : head + (moments.Count > 0 ? " " + string.Join(" ", moments.ToArray()) : (!string.IsNullOrEmpty(r.Lesson) ? " " + r.Lesson : string.Empty));
            return text.Length > 300 ? text.Substring(0, 297) + "..." : text;
        }

        private static string Trim(string line)
        {
            // Flight log lines start with "T+00:01:14 "; keep the words.
            string s = line.Trim();
            if (s.StartsWith("T+", StringComparison.Ordinal)) { int sp = s.IndexOf(' '); if (sp > 0) s = s.Substring(sp + 1); }
            if (!s.EndsWith(".")) s += ".";
            return s;
        }
    }
}
