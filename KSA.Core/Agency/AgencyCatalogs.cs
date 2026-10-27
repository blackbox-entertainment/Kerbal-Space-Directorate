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
        public CareerCatalog Careers { get; set; }
        public MedalCatalog Medals { get; set; }
        public GoalCatalog Goals { get; set; }
        public RecruitCatalog Recruiting { get; set; }
        public MoraleRules Morale { get; set; }
        public AgencySettings Settings { get; set; }
        public KSA.Core.Missions.MissionCatalog Missions { get; set; }
        public KSA.Core.Missions.TemplateCatalog Templates { get; set; }
        public KSA.Core.Projects.ProgramCatalog ProgramTypes { get; set; }

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
        }
    }

    /// <summary>Mutable agency-wide state that is not crew: year, prestige, freezes, probation, counters.</summary>
    public sealed class AgencyState
    {
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
