using System;
using System.Collections.Generic;
using System.Text;
using KSA.Core.Data;
using KSA.Core.Personnel;

namespace KSA.Core.Flight
{
    /// <summary>
    /// How much the crew's competence shapes a live flight (FLIGHT_CREW block of KSA_AGENCY_SETTINGS). The kerbals fly;
    /// stars buy precision, courage thins the safety margins, stupidity buys mistakes, morale dulls everything.
    /// </summary>
    public sealed class FlightCrewRules
    {
        public const string NodeName = "FLIGHT_CREW";

        public bool Enabled { get; set; } = true;
        /// <summary>Chance per leg of a pilot mistake for a 0-stupidity, 0-star pilot.</summary>
        public double MistakeBase { get; set; } = 0.04;
        /// <summary>Added mistake chance at stupidity 1.0.</summary>
        public double MistakeStupidity { get; set; } = 0.30;
        /// <summary>Mistake chance removed per star.</summary>
        public double MistakePerStar { get; set; } = 0.03;
        /// <summary>Extra mistake chance when morale is under 40.</summary>
        public double MistakeLowMorale { get; set; } = 0.06;
        public double MistakeMax { get; set; } = 0.45;
        /// <summary>Seconds a late ignition can slip (a mistake draws 2..this).</summary>
        public double LateIgnitionMax { get; set; } = 8;
        /// <summary>Burn cutoff tolerance in m/s for a perfect pilot and for a hopeless one.</summary>
        public double BurnToleranceBest { get; set; } = 0.2;
        public double BurnToleranceWorst { get; set; } = 2.5;
        /// <summary>Landing safety margin multiplier at courage 0 and courage 1 (brave pilots cut it finer).</summary>
        public double MarginCautious { get; set; } = 1.5;
        public double MarginBrave { get; set; } = 0.95;
        /// <summary>Docking approach speed multiplier for a hopeless pilot and a perfect one.</summary>
        public double ApproachWorst { get; set; } = 0.6;
        public double ApproachBest { get; set; } = 1.15;

        public static FlightCrewRules Parse(DataNode node)
        {
            var r = new FlightCrewRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.MistakeBase = node.GetDouble("mistakeBase", r.MistakeBase);
            r.MistakeStupidity = node.GetDouble("mistakeStupidity", r.MistakeStupidity);
            r.MistakePerStar = node.GetDouble("mistakePerStar", r.MistakePerStar);
            r.MistakeLowMorale = node.GetDouble("mistakeLowMorale", r.MistakeLowMorale);
            r.MistakeMax = node.GetDouble("mistakeMax", r.MistakeMax);
            r.LateIgnitionMax = node.GetDouble("lateIgnitionMax", r.LateIgnitionMax);
            r.BurnToleranceBest = node.GetDouble("burnToleranceBest", r.BurnToleranceBest);
            r.BurnToleranceWorst = node.GetDouble("burnToleranceWorst", r.BurnToleranceWorst);
            r.MarginCautious = node.GetDouble("marginCautious", r.MarginCautious);
            r.MarginBrave = node.GetDouble("marginBrave", r.MarginBrave);
            r.ApproachWorst = node.GetDouble("approachWorst", r.ApproachWorst);
            r.ApproachBest = node.GetDouble("approachBest", r.ApproachBest);
            return r;
        }
    }

    /// <summary>The flying crew, reduced to the numbers the flight legs use. Built once per launch from the real profiles.</summary>
    public sealed class CrewFlightProfile
    {
        public string PilotName { get; set; }
        public string PilotId { get; set; }
        public int PilotStars { get; set; }
        /// <summary>0..1: how exactly burns, descents and approaches are flown.</summary>
        public double Precision { get; set; }
        /// <summary>0..1: the pilot's courage.</summary>
        public double Nerve { get; set; }
        /// <summary>Chance per leg of a logged pilot mistake.</summary>
        public double MistakeChance { get; set; }
        /// <summary>Multiplier on landing and braking safety margins.</summary>
        public double MarginFactor { get; set; }
        /// <summary>Multiplier on docking approach speeds.</summary>
        public double ApproachFactor { get; set; }
        /// <summary>Remaining delta-v at which a burn is called complete, m/s.</summary>
        public double BurnTolerance { get; set; }
        public double LateIgnitionMax { get; set; }
        public bool Enabled { get; set; }

        public CrewFlightProfile()
        {
            PilotName = "Autopilot"; PilotId = string.Empty; Precision = 0.75; Nerve = 0.5; MistakeChance = 0; MarginFactor = 1.15; ApproachFactor = 1.0; BurnTolerance = 0.3; LateIgnitionMax = 0; Enabled = false;
        }

        /// <summary>A neutral profile for flights with no crew data (or the feature switched off).</summary>
        public static CrewFlightProfile Neutral() { return new CrewFlightProfile(); }

        public static CrewFlightProfile Build(IList<CrewProfile> crew, FlightCrewRules rules)
        {
            if (rules == null) rules = new FlightCrewRules();
            if (!rules.Enabled || crew == null || crew.Count == 0) return Neutral();
            CrewProfile pilot = null;
            foreach (CrewProfile c in crew)
            {
                if (c == null) continue;
                if (pilot == null) { pilot = c; continue; }
                bool better = (c.Role == CrewRole.Pilot && pilot.Role != CrewRole.Pilot) || (c.Role == CrewRole.Pilot) == (pilot.Role == CrewRole.Pilot) && c.Stars > pilot.Stars;
                if (better) pilot = c;
            }
            if (pilot == null) return Neutral();
            var p = new CrewFlightProfile { Enabled = true, PilotName = pilot.Name, PilotId = pilot.Id, PilotStars = pilot.Stars, Nerve = Clamp01(pilot.Courage), LateIgnitionMax = rules.LateIgnitionMax };
            double stars = Math.Max(0, Math.Min(5, pilot.Stars)) / 5.0;
            double morale = Math.Max(0, Math.Min(100, pilot.Morale)) / 100.0;
            double nonPilot = pilot.Role == CrewRole.Pilot ? 0 : 0.2;
            p.Precision = Clamp01(0.35 + 0.45 * stars + 0.2 * morale - nonPilot);
            double mistake = rules.MistakeBase + rules.MistakeStupidity * Clamp01(pilot.Stupidity) - rules.MistakePerStar * pilot.Stars + nonPilot * 0.5;
            if (pilot.Morale < 40) mistake += rules.MistakeLowMorale;
            p.MistakeChance = Math.Max(0, Math.Min(rules.MistakeMax, mistake));
            p.MarginFactor = rules.MarginCautious + (rules.MarginBrave - rules.MarginCautious) * p.Nerve;
            p.ApproachFactor = rules.ApproachWorst + (rules.ApproachBest - rules.ApproachWorst) * p.Precision;
            p.BurnTolerance = rules.BurnToleranceWorst + (rules.BurnToleranceBest - rules.BurnToleranceWorst) * p.Precision;
            return p;
        }

        /// <summary>Rolls for a mistake on this leg.</summary>
        public bool RollMistake(Random rng)
        {
            if (!Enabled || MistakeChance <= 0 || rng == null) return false;
            return rng.NextDouble() < MistakeChance;
        }

        /// <summary>Seconds of late ignition for a mistake: 2 s up to LateIgnitionMax.</summary>
        public double LateIgnition(Random rng)
        {
            if (rng == null || LateIgnitionMax <= 2) return 2;
            return 2 + rng.NextDouble() * (LateIgnitionMax - 2);
        }

        public string Describe()
        {
            if (!Enabled) return "Autopilot profile (crew skill not applied).";
            var sb = new StringBuilder();
            sb.Append(PilotName).Append(" at the controls (").Append(PilotStars).Append(" stars): precision ").Append((int)Math.Round(Precision * 100)).Append("%, ");
            sb.Append(Nerve > 0.66 ? "bold margins" : Nerve < 0.33 ? "cautious margins" : "normal margins");
            sb.Append(", mistake chance ").Append((int)Math.Round(MistakeChance * 100)).Append("% per leg.");
            return sb.ToString();
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    }
}
