using System;
using KSA.Core.Careers;
using KSA.Core.Goals;
using KSA.Core.Medals;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;

namespace KSA.Core
{
    /// <summary>Every data-driven catalog the agency runs on, loaded once by the host from config.</summary>
    public sealed class AgencyCatalogs
    {
        /// <summary>Applicant names (names.cfg, KSA-66). Empty means the host's own generator.</summary>
        public KSA.Core.Recruiting.NameCatalog Names { get; set; } = new KSA.Core.Recruiting.NameCatalog();
        public CareerCatalog Careers { get; set; }
        public MedalCatalog Medals { get; set; }
        public GoalCatalog Goals { get; set; }
        public RecruitCatalog Recruiting { get; set; }
        public MoraleRules Morale { get; set; }
        public AgencySettings Settings { get; set; }
        public KSA.Core.Missions.MissionCatalog Missions { get; set; }
        public KSA.Core.Missions.TemplateCatalog Templates { get; set; }
        public KSA.Core.Projects.ProgramCatalog ProgramTypes { get; set; }
        public System.Collections.Generic.List<KSA.Core.Rivals.RivalDefinition> Rivals { get; set; }

        public AgencyCatalogs()
        {
            Careers = new CareerCatalog();
            Medals = new MedalCatalog();
            Goals = new GoalCatalog();
            Recruiting = new RecruitCatalog();
            Morale = new MoraleRules();
            Settings = new AgencySettings();
            Missions = new KSA.Core.Missions.MissionCatalog();
            Templates = new KSA.Core.Missions.TemplateCatalog();
            ProgramTypes = new KSA.Core.Projects.ProgramCatalog();
            Rivals = new System.Collections.Generic.List<KSA.Core.Rivals.RivalDefinition>();
        }

        public void Validate()
        {
            if (Careers == null) throw new ArgumentNullException(nameof(Careers));
            if (Medals == null) throw new ArgumentNullException(nameof(Medals));
            if (Goals == null) throw new ArgumentNullException(nameof(Goals));
            if (Recruiting == null) throw new ArgumentNullException(nameof(Recruiting));
            if (Morale == null) throw new ArgumentNullException(nameof(Morale));
            if (Settings == null) throw new ArgumentNullException(nameof(Settings));
            if (Missions == null) Missions = new KSA.Core.Missions.MissionCatalog();
            if (Templates == null) Templates = new KSA.Core.Missions.TemplateCatalog();
            if (ProgramTypes == null) ProgramTypes = new KSA.Core.Projects.ProgramCatalog();
            if (Rivals == null) Rivals = new System.Collections.Generic.List<KSA.Core.Rivals.RivalDefinition>();
        }
    }

    /// <summary>Mutable agency-wide state that is not crew: year, prestige, freezes, probation, counters.</summary>
    public sealed class AgencyState
    {
        /// <summary>Player-chosen agency name; empty means the agency.cfg default.</summary>
        public string AgencyName { get; set; } = string.Empty;
        /// <summary>Lifetime Science credited to the game by the agency.</summary>
        public double ScienceEarned { get; set; }
        /// <summary>Of <see cref="ScienceEarned"/>, how much the host has paid into the game's R&D pool (back-pays older saves).</summary>
        public double ScienceCredited { get; set; }
        /// <summary>Day of the most recent launch, or -1: automatic launches wait launchSpacingDays after it (2026-10-28).</summary>
        public int LastLaunchDay { get; set; } = -1;
        /// <summary>Every world that can hold a colony holds one (KSA-64); announced once.</summary>
        public bool SystemColonized { get; set; }
        /// <summary>The AI runs the agency (KSA-68).</summary>
        public bool AiDirector { get; set; }
        public KSA.Core.Ai.DirectorFlights DirectorFlights { get; set; } = KSA.Core.Ai.DirectorFlights.Firsts;
        public int NextStrandingNumber { get; set; } = 1;
        public bool Started { get; set; }
        /// <summary>The fiscal year currently in progress (1-based). 0 before the agency starts.</summary>
        public int CurrentYear { get; set; }
        public double Prestige { get; set; }
        public bool HiringFreeze { get; set; }
        public bool Probation { get; set; }
        public int DirectorNumber { get; set; }
        public int PendingLayoffs { get; set; }
        public double YearStartBalance { get; set; }
        public int DeathsThisYear { get; set; }
        public int MissionsThisYear { get; set; }
        public int LastWalkInQuarter { get; set; }
        public int LastWalkInYear { get; set; }

        public AgencyState()
        {
            DirectorNumber = 1;
            LastWalkInQuarter = -1;
        }

        public string DirectorTitle { get { return DirectorNumber <= 1 ? "Director" : "Director (#" + DirectorNumber + ")"; } }
    }
}
