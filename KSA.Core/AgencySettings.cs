using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core
{
    /// <summary>Tunable numbers for the agency, loaded from the KSA_AGENCY_SETTINGS config node. Every field has a sane default.</summary>
    public sealed class AgencySettings
    {
        public const string NodeName = "KSA_AGENCY_SETTINGS";

        public string AgencyName { get; set; } = "Kerbal Space Directorate";
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
        /// <summary>First interview of an applicant costs this much; each later one costs InterviewCostStep more.</summary>
        public double InterviewBaseCost { get; set; } = 200;
        public double InterviewCostStep { get; set; } = 100;
        /// <summary>The first interview is at least this fraction of the hire fee, so poaching a veteran costs more to vet.</summary>
        public double InterviewFeeFraction { get; set; } = 0.1;
        public KSA.Core.Personnel.TraitDrift Drift { get; set; } = new KSA.Core.Personnel.TraitDrift();
        /// <summary>Fraction of active crew that must be laid off in a Crisis Year (rounded up, at least 1).</summary>
        public double CrisisLayoffFraction { get; set; } = 0.25;
        public KSA.Core.Missions.DefaultMissionMode DefaultMissionMode { get; set; } = KSA.Core.Missions.DefaultMissionMode.Hybrid;
        public int DesignBaseDays { get; set; } = 10;
        public int BuildBaseDays { get; set; } = 15;
        public double CancelRefundFraction { get; set; } = 0.5;
        /// <summary>Recover landed crewed vessels on the home world automatically (GDD, T 2026-10-26).</summary>
        public bool AutoRecover { get; set; } = true;
        /// <summary>Opt-in: the Director may take the controls during a live flight (decision 2026-10-26).</summary>
        public bool ManualOverride { get; set; } = false;
        /// <summary>Target orbit for live Kerbin flights, metres.</summary>
        public double LiveOrbitAltitude { get; set; } = 85000;
        /// <summary>Orbit altitude above other bodies for capture and ascent, metres (liveTargetOrbitAltitude).</summary>
        public double LiveTargetOrbitAltitude { get; set; } = 50000;
        /// <summary>Kerbin periapsis aimed for on the way home, metres (liveReentryPeriapsis).</summary>
        public double LiveReentryPeriapsis { get; set; } = 35000;
        /// <summary>Which profiles the built-in flight director may fly live: Kerbin, Moons, All (liveFlightScope).</summary>
        public string LiveFlightScope { get; set; } = "All";
        /// <summary>How crew competence shapes live flights (FLIGHT_CREW block).</summary>
        public KSA.Core.Flight.FlightCrewRules FlightCrew { get; set; } = new KSA.Core.Flight.FlightCrewRules();
        /// <summary>Who makes the crew's judgement calls in flight: Rule (traits only), Local (local model, rule fallback), Any (local or API key), Off.</summary>
        public string CrewDecisions { get; set; } = "Local";
        /// <summary>Rival agencies run in the background and claim firsts (decision 19).</summary>
        public bool RivalsEnabled { get; set; } = true;
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
            s.InterviewBaseCost = node.GetDouble("interviewBaseCost", s.InterviewBaseCost);
            s.InterviewCostStep = node.GetDouble("interviewCostStep", s.InterviewCostStep);
            s.InterviewFeeFraction = node.GetDouble("interviewFeeFraction", s.InterviewFeeFraction);
            s.Drift = KSA.Core.Personnel.TraitDrift.Parse(node.GetChild(KSA.Core.Personnel.TraitDrift.NodeName));
            s.FlightCrew = KSA.Core.Flight.FlightCrewRules.Parse(node.GetChild(KSA.Core.Flight.FlightCrewRules.NodeName));
            s.CrewDecisions = node.GetValue("crewDecisions", s.CrewDecisions);
            s.CrisisLayoffFraction = node.GetDouble("crisisLayoffFraction", s.CrisisLayoffFraction);
            KSA.Core.Missions.DefaultMissionMode mode;
            if (System.Enum.TryParse(node.GetValue("defaultMissionMode", "Hybrid"), true, out mode)) s.DefaultMissionMode = mode;
            s.DesignBaseDays = node.GetInt("designBaseDays", s.DesignBaseDays);
            s.BuildBaseDays = node.GetInt("buildBaseDays", s.BuildBaseDays);
            s.CancelRefundFraction = node.GetDouble("cancelRefundFraction", s.CancelRefundFraction);
            s.AutoRecover = node.GetBool("autoRecover", s.AutoRecover);
            s.AgencyName = node.GetValue("agencyName", s.AgencyName);
            s.ManualOverride = node.GetBool("manualOverride", s.ManualOverride);
            s.LiveOrbitAltitude = node.GetDouble("liveOrbitAltitude", s.LiveOrbitAltitude);
            s.LiveTargetOrbitAltitude = node.GetDouble("liveTargetOrbitAltitude", s.LiveTargetOrbitAltitude);
            s.LiveReentryPeriapsis = node.GetDouble("liveReentryPeriapsis", s.LiveReentryPeriapsis);
            s.LiveFlightScope = node.GetValue("liveFlightScope", s.LiveFlightScope);
            s.RivalsEnabled = node.GetBool("rivals", s.RivalsEnabled);
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
