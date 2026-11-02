using System;
using System.Collections.Generic;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core.Missions
{
    /// <summary>Everything the simulator needs to know about one mission attempt.</summary>
    public sealed class SimulationInput
    {
        public MissionType Type { get; set; }
        public Template Template { get; set; }
        public List<CrewProfile> Crew { get; } = new List<CrewProfile>();
        /// <summary>Rank rung of the lead engineer (0 = entry), -1 if none.</summary>
        public int LeadEngineerRung { get; set; }
        public bool HasProgramManager { get; set; }
        /// <summary>0 flat .. 1 steep. Only matters for landings. Negative = unknown site, roll one.</summary>
        public double SiteSlope { get; set; }
        /// <summary>0..1 from an approved AI design analysis (M4); null keeps the template's score.</summary>
        public double? DesignOverride { get; set; }
        /// <summary>Absolute success-chance bonus from the Head of Science (department heads).</summary>
        public double SuccessBonus { get; set; }
        /// <summary>Lead engineer personality: added to the design score.</summary>
        public double LeadDesignBonus { get; set; }

        public SimulationInput() { LeadEngineerRung = -1; SiteSlope = -1; }
    }

    /// <summary>Rolls a simulated mission (GDD section 7, Jira KSA-36). Deterministic given the Random.</summary>
    public static class MissionSimulator
    {
        public sealed class Odds
        {
            public double DesignScore;
            public double CrewScore;
            public double SlopeFactor;
            public double Success;
            public double Partial;
            public double Abort;
            public double LossOfVehicle;
            public double LossOfCrew;
        }

        public static double DesignScore(SimulationInput input)
        {
            double score = input.Template != null ? input.Template.DesignScore : 0.4;
            if (input.DesignOverride.HasValue) score = input.DesignOverride.Value; // an approved AI design replaces the template's figure
            if (input.LeadEngineerRung > 0) score += Math.Min(0.15, 0.05 * input.LeadEngineerRung);
            if (input.HasProgramManager) score += 0.03;
            score += input.LeadDesignBonus;
            return Clamp01(score);
        }

        public static double CrewScore(SimulationInput input)
        {
            // A probe has no crew to panic or err: its flight goes by the design (2026-10-30).
            if (input.Crew.Count == 0) return input.Type != null && input.Type.IsUncrewed ? 0.7 : 0.2;
            double sum = 0;
            double best = 0;
            foreach (CrewProfile p in input.Crew)
            {
                double s = 0.4 * (p.Stars / 5.0) + 0.3 * (p.Morale / 100.0) + 0.15 * p.Courage + 0.15 * (1 - p.Stupidity);
                sum += s;
                if (s > best) best = s;
            }
            // The commander matters most: weight the best crew member double.
            return Clamp01((sum + best) / (input.Crew.Count + 1));
        }

        public static Odds Compute(SimulationInput input, Random rng)
        {
            var o = new Odds();
            o.DesignScore = DesignScore(input);
            o.CrewScore = CrewScore(input);
            double difficulty = input.Type != null ? input.Type.Difficulty : 0.3;
            double baseQuality = 0.5 * o.DesignScore + 0.5 * o.CrewScore;
            double success = 0.25 + baseQuality * 0.85 - difficulty * 0.35 + input.SuccessBonus;

            o.SlopeFactor = 1.0;
            if (input.Type != null && input.Type.IsLanding)
            {
                double slope = input.SiteSlope >= 0 ? input.SiteSlope : (rng != null ? rng.NextDouble() * 0.6 : 0.3);
                int pilotStars = 0;
                foreach (CrewProfile p in input.Crew) if (p.Role == CrewRole.Pilot && p.Stars > pilotStars) pilotStars = p.Stars;
                // Steep ground hurts; experienced pilots pick a better spot (GDD 14.1 decision 6).
                o.SlopeFactor = 1.0 - slope * 0.35 * (1.0 - pilotStars / 5.0 * 0.6);
                success *= o.SlopeFactor;
            }
            o.Success = Clamp(success, 0.04, 0.97);

            double rest = 1 - o.Success;
            double crewLossShare = 0.10 * (1.3 - o.CrewScore * 0.6);   // worse crews die more often
            double vehicleLossShare = 0.20 * (1.3 - o.DesignScore * 0.6);
            double abortShare = 0.25;
            double partialShare = Math.Max(0.1, 1 - crewLossShare - vehicleLossShare - abortShare);
            double shares = partialShare + abortShare + vehicleLossShare + crewLossShare;
            o.Partial = rest * partialShare / shares;
            o.Abort = rest * abortShare / shares;
            o.LossOfVehicle = rest * vehicleLossShare / shares;
            o.LossOfCrew = rest * crewLossShare / shares;
            return o;
        }

        public static MissionReport Resolve(Project project, SimulationInput input, int day, Random rng)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            rng = rng ?? new Random();
            Odds o = Compute(input, rng);

            double roll = rng.NextDouble();
            MissionOutcome outcome;
            if (roll < o.Success) outcome = MissionOutcome.Success;
            else if (roll < o.Success + o.Partial) outcome = MissionOutcome.PartialSuccess;
            else if (roll < o.Success + o.Partial + o.Abort) outcome = MissionOutcome.Abort;
            else if (roll < o.Success + o.Partial + o.Abort + o.LossOfVehicle) outcome = MissionOutcome.LossOfVehicle;
            else outcome = input.Type != null && input.Type.IsUncrewed ? MissionOutcome.LossOfVehicle : MissionOutcome.LossOfCrew; // nobody aboard to lose

            var r = new MissionReport
            {
                ProjectId = project != null ? project.Id : string.Empty,
                ProjectName = project != null ? project.Name : (input.Type != null ? input.Type.Name : "Mission"),
                MissionTypeId = input.Type != null ? input.Type.Id : string.Empty,
                Day = day,
                Outcome = outcome,
                Mode = MissionMode.Simulate,
                DesignScore = o.DesignScore,
                CrewScore = o.CrewScore,
                SuccessChance = o.Success
            };
            foreach (CrewProfile p in input.Crew) r.Crew.Add(p.Id);

            int duration = input.Type != null ? input.Type.DurationDays : 1;
            List<string> ach = input.Type != null ? input.Type.Achievements : new List<string>();
            switch (outcome)
            {
                case MissionOutcome.Success:
                    r.Achievements.AddRange(ach);
                    r.DaysInSpace = duration;
                    r.Lesson = "Nominal flight on " + r.ProjectName + ".";
                    break;
                case MissionOutcome.PartialSuccess:
                    {
                        string primary = input.Type != null ? input.Type.PrimaryAchievement : string.Empty;
                        // A partial flight does not count as a completed orbit (2026-10-28): orbit only on a full success.
                        foreach (string a in ach) if (!string.Equals(a, primary, StringComparison.OrdinalIgnoreCase) && !a.StartsWith("Orbit:", StringComparison.OrdinalIgnoreCase)) r.Achievements.Add(a);
                    }
                    r.DaysInSpace = duration;
                    r.Incidents.Add(IncidentKind.Other);
                    r.Lesson = "Primary objective missed on " + r.ProjectName + "; margins were thin.";
                    break;
                case MissionOutcome.Abort:
                    if (ach.Count > 0) r.Achievements.Add(ach[0]);
                    r.DaysInSpace = Math.Max(0.1, duration * 0.3);
                    r.Lesson = "Abort on " + r.ProjectName + "; the crew called it early.";
                    break;
                case MissionOutcome.LossOfVehicle:
                    if (ach.Count > 0) r.Achievements.Add(ach[0]);
                    r.DaysInSpace = Math.Max(0.1, duration * 0.3);
                    r.Incidents.Add(IncidentKind.Crash);
                    r.Lesson = "Vehicle lost on " + r.ProjectName + "; the design needs more margin.";
                    break;
                default:
                    r.DaysInSpace = Math.Max(0.1, duration * 0.3);
                    r.Incidents.Add(IncidentKind.Crash);
                    foreach (CrewProfile p in input.Crew) r.Deaths.Add(p.Id);
                    r.Lesson = "Crew lost on " + r.ProjectName + ". Never again.";
                    break;
            }
            r.Lines.AddRange(MissionNarrator.Lines(input, outcome, rng));
            // Say plainly what the flight reached and what it missed (2026-10-28: a partial science flight was credited
            // with orbit while its report only said "aborted the final phase").
            if (outcome == MissionOutcome.PartialSuccess || outcome == MissionOutcome.Abort || outcome == MissionOutcome.LossOfVehicle)
            {
                var reached = new List<string>();
                foreach (string a in r.Achievements) if (!a.StartsWith("Recover", StringComparison.OrdinalIgnoreCase) && !a.StartsWith("Flight", StringComparison.OrdinalIgnoreCase)) reached.Add(KSA.Core.Rivals.MilestoneLadder.Describe(a));
                string primary = input.Type != null ? input.Type.PrimaryAchievement : string.Empty;
                r.Lines.Add("Reached: " + (reached.Count > 0 ? string.Join(", ", reached.ToArray()) : "nothing beyond the launch") + "."
                    + (primary.Length > 0 && !r.Achievements.Contains(primary) ? " Missed: " + KSA.Core.Rivals.MilestoneLadder.Describe(primary) + "." : string.Empty));
            }
            return r;
        }

        private static double Clamp01(double v) { return Clamp(v, 0, 1); }
        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    /// <summary>Canned mission text (no AI). Two or three lines per report; M6 replaces these with generated narrative.</summary>
    public static class MissionNarrator
    {
        public static List<string> Lines(SimulationInput input, MissionOutcome outcome, Random rng)
        {
            var lines = new List<string>();
            string type = input.Type != null ? input.Type.Name : "the mission";
            string body = input.Type != null ? input.Type.Body : "Kerbin";
            string commander = input.Crew.Count > 0 ? input.Crew[0].Name : "The crew";
            string crewList = JoinNames(input.Crew);
            MissionProfile profile = input.Type != null ? input.Type.Profile : MissionProfile.Orbit;

            lines.Add(crewList + (profile == MissionProfile.Atmospheric ? " took off on " : " launched on ") + type + ".");
            switch (outcome)
            {
                case MissionOutcome.Success:
                    lines.Add(Pick(rng, new[]
                    {
                        "Every objective was met. " + commander + " reported a textbook flight.",
                        "Mission Control called it clean from liftoff to recovery.",
                        commander + " brought the vehicle home with margin to spare."
                    }));
                    if (profile == MissionProfile.Land || profile == MissionProfile.Base)
                        lines.Add("Touchdown on " + body + " was on a flat stretch of ground; the lander stayed upright.");
                    else if (profile == MissionProfile.Flyby)
                        lines.Add("The crew photographed " + body + " on the way past.");
                    break;
                case MissionOutcome.PartialSuccess:
                    lines.Add(Pick(rng, new[]
                    {
                        "The primary objective slipped away, but the crew and vehicle came home.",
                        commander + " aborted the final phase when the numbers stopped adding up.",
                        "Enough went right to call it a partial success. The engineers have notes."
                    }));
                    break;
                case MissionOutcome.Abort:
                    lines.Add(Pick(rng, new[]
                    {
                        "A fault early in the flight forced an abort. The crew is safe.",
                        commander + " called the abort. Nobody argued.",
                        "The vehicle was recovered intact after an early abort."
                    }));
                    break;
                case MissionOutcome.LossOfVehicle:
                    lines.Add(Pick(rng, new[]
                    {
                        "The vehicle broke up; the crew rode the capsule down and walked away.",
                        "A hard landing wrote off the vehicle. The crew is shaken but alive.",
                        commander + " ejected the crew before the vehicle was lost."
                    }));
                    break;
                default:
                    lines.Add(Pick(rng, new[]
                    {
                        "Contact was lost during " + PhaseName(profile) + ". There were no survivors.",
                        "The vehicle was destroyed with all hands.",
                        "The agency mourns " + crewList + "."
                    }));
                    break;
            }
            return lines;
        }

        private static string PhaseName(MissionProfile p)
        {
            switch (p)
            {
                case MissionProfile.Suborbital: return "ascent";
                case MissionProfile.Land:
                case MissionProfile.Base: return "descent";
                case MissionProfile.Station: return "docking";
                default: return "the flight";
            }
        }

        private static string JoinNames(List<CrewProfile> crew)
        {
            if (crew.Count == 0) return "An uncrewed vehicle";
            if (crew.Count == 1) return crew[0].Name;
            var names = new List<string>();
            foreach (CrewProfile p in crew) names.Add(p.Name);
            string last = names[names.Count - 1];
            names.RemoveAt(names.Count - 1);
            return string.Join(", ", names.ToArray()) + " and " + last;
        }

        private static string Pick(Random rng, string[] options)
        {
            return options[(rng ?? new Random()).Next(options.Length)];
        }
    }
}
