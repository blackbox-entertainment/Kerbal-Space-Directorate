using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core
{
    /// <summary>Tunable numbers for the agency, loaded from the KSA_AGENCY_SETTINGS config node. Every field has a sane default.</summary>
    public sealed class AgencySettings
    {
        public const string NodeName = "KSA_AGENCY_SETTINGS";

        public double StartingBalance { get; set; } = 50000;
        public double BaseGrant { get; set; } = 150000;
        public int GoalsPerYear { get; set; } = 3;
        public double StartingPrestige { get; set; } = 50;
        public int QuitMorale { get; set; } = 20;
        /// <summary>Consecutive year-ends below QuitMorale before a crew member quits.</summary>
        public int QuitYears { get; set; } = 2;
        public int RetireAge { get; set; } = 55;
        public int RetireServiceYears { get; set; } = 25;
        public int WalkInsPerQuarter { get; set; } = 1;
        public int MaxApplicants { get; set; } = 12;
        /// <summary>Fraction of active crew that must be laid off in a Crisis Year (rounded up, at least 1).</summary>
        public double CrisisLayoffFraction { get; set; } = 0.25;
        public List<string> PersonalityTags { get; } = new List<string> { "Cautious", "Reckless", "Perfectionist", "Showboat", "Steady", "Curious" };

        public static AgencySettings Parse(DataNode node)
        {
            var s = new AgencySettings();
            if (node == null) return s;
            s.StartingBalance = node.GetDouble("startingBalance", s.StartingBalance);
            s.BaseGrant = node.GetDouble("baseGrant", s.BaseGrant);
            s.GoalsPerYear = node.GetInt("goalsPerYear", s.GoalsPerYear);
            s.StartingPrestige = node.GetDouble("startingPrestige", s.StartingPrestige);
            s.QuitMorale = node.GetInt("quitMorale", s.QuitMorale);
            s.QuitYears = node.GetInt("quitYears", s.QuitYears);
            s.RetireAge = node.GetInt("retireAge", s.RetireAge);
            s.RetireServiceYears = node.GetInt("retireServiceYears", s.RetireServiceYears);
            s.WalkInsPerQuarter = node.GetInt("walkInsPerQuarter", s.WalkInsPerQuarter);
            s.MaxApplicants = node.GetInt("maxApplicants", s.MaxApplicants);
            s.CrisisLayoffFraction = node.GetDouble("crisisLayoffFraction", s.CrisisLayoffFraction);
            List<string> tags = node.GetList("personalityTags");
            if (tags.Count > 0)
            {
                s.PersonalityTags.Clear();
                s.PersonalityTags.AddRange(tags);
            }
            return s;
        }
    }
}
