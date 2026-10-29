using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Personnel
{
    /// <summary>
    /// KSA experience (KSA-63): every mission pays points into the service record and stars follow a ladder.
    /// The stock game only pays for firsts per body, so repeat missions and simulated flights would otherwise
    /// never grow a kerbal. Numbers live in the EXPERIENCE block of KSA_AGENCY_SETTINGS.
    /// </summary>
    public sealed class ExperienceRules
    {
        public const string NodeName = "EXPERIENCE";

        public bool Enabled { get; set; } = true;
        /// <summary>Points for completing a mission of each tier.</summary>
        public double EarlyPoints { get; set; } = 2;
        public double MidPoints { get; set; } = 4;
        public double LatePoints { get; set; } = 8;
        /// <summary>Multiplier added on a success (0.5 = +50%).</summary>
        public double SuccessBonus { get; set; } = 0.5;
        /// <summary>Flat points when the mission claimed a First for the agency.</summary>
        public double FirstBonus { get; set; } = 4;
        /// <summary>Points per mission day beyond the first.</summary>
        public double PerExtraDay { get; set; } = 0.2;
        /// <summary>Credit multiplier when the Director flew the vehicle.</summary>
        public double DirectorFactor { get; set; } = 0.5;
        /// <summary>Credit multiplier when the mission failed but the crew came home.</summary>
        public double SurvivedFailureFactor { get; set; } = 0.25;
        /// <summary>Ground work (2026-10-28): the lead engineer's share of a mission's design points when the design flies.</summary>
        public double DesignLeadFactor { get; set; } = 1.0;
        /// <summary>Each other engineer on the design team.</summary>
        public double DesignTeamFactor { get; set; } = 0.25;
        /// <summary>Design credit multiplier when the mission failed.</summary>
        public double FailedDesignFactor { get; set; } = 0.25;
        /// <summary>Points a kerbal earns for a full year in the research lab, paid day by day.</summary>
        public double LabPerYear { get; set; } = 3;
        /// <summary>Points needed for one to five stars.</summary>
        public List<double> StarThresholds { get; } = new List<double> { 4, 12, 28, 60, 110 };

        /// <summary>Stars earned by a point total on this ladder (0..5).</summary>
        public int StarsFor(double points)
        {
            int stars = 0;
            foreach (double t in StarThresholds) if (points >= t) stars++; else break;
            return Math.Min(5, stars);
        }

        /// <summary>Points still needed for the next star, or 0 at the top.</summary>
        public double ToNextStar(double points)
        {
            int stars = StarsFor(points);
            return stars >= StarThresholds.Count ? 0 : Math.Max(0, StarThresholds[stars] - points);
        }

        public double PointsFor(KSA.Core.Missions.MissionTier tier)
        {
            switch (tier)
            {
                case KSA.Core.Missions.MissionTier.Late: return LatePoints;
                case KSA.Core.Missions.MissionTier.Mid: return MidPoints;
                default: return EarlyPoints;
            }
        }

        public static ExperienceRules Parse(DataNode node)
        {
            var r = new ExperienceRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.EarlyPoints = node.GetDouble("early", r.EarlyPoints);
            r.MidPoints = node.GetDouble("mid", r.MidPoints);
            r.LatePoints = node.GetDouble("late", r.LatePoints);
            r.SuccessBonus = node.GetDouble("successBonus", r.SuccessBonus);
            r.FirstBonus = node.GetDouble("firstBonus", r.FirstBonus);
            r.PerExtraDay = node.GetDouble("perExtraDay", r.PerExtraDay);
            r.DirectorFactor = node.GetDouble("directorFactor", r.DirectorFactor);
            r.SurvivedFailureFactor = node.GetDouble("survivedFailureFactor", r.SurvivedFailureFactor);
            r.DesignLeadFactor = node.GetDouble("designLead", r.DesignLeadFactor);
            r.DesignTeamFactor = node.GetDouble("designTeam", r.DesignTeamFactor);
            r.FailedDesignFactor = node.GetDouble("failedDesign", r.FailedDesignFactor);
            r.LabPerYear = node.GetDouble("labPerYear", r.LabPerYear);
            string ladder = node.GetValue("stars", string.Empty);
            if (ladder.Length > 0)
            {
                var list = new List<double>();
                foreach (string s in ladder.Split(',')) { double v; if (double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) list.Add(v); }
                if (list.Count == 5) { r.StarThresholds.Clear(); r.StarThresholds.AddRange(list); }
            }
            return r;
        }
    }
}
