using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;

namespace KSA.Core.Projects
{
    public enum ProjectStatus { Design = 0, Build = 1, Ready = 2, InFlight = 3, Complete = 4, Failed = 5, Cancelled = 6 }

    /// <summary>A single rocket or mission effort (GDD 4.6, Jira KSA-35).</summary>
    public sealed class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string MissionTypeId { get; set; }
        public string TemplateId { get; set; }
        public string GoalId { get; set; }
        public string ProgramId { get; set; }
        public string ModuleRole { get; set; }
        public double Budget { get; set; }
        public MissionMode Mode { get; set; }
        public ProjectStatus Status { get; set; }
        public int CreatedDay { get; set; }
        public int DesignDays { get; set; }
        public int BuildDays { get; set; }
        /// <summary>Day the current phase began.</summary>
        public int PhaseStartDay { get; set; }
        public int LaunchDay { get; set; }
        public int EndDay { get; set; }
        public List<string> Engineers { get; } = new List<string>();
        public string LeadEngineer { get; set; }
        public List<string> Crew { get; } = new List<string>();
        public string ProgramManager { get; set; }
        /// <summary>Mission id in the crew records once launched.</summary>
        public string MissionId { get; set; }
        public MissionReport Report { get; set; }
        public List<string> Notes { get; } = new List<string>();

        public Project()
        {
            Id = string.Empty; Name = string.Empty; MissionTypeId = string.Empty; TemplateId = string.Empty; GoalId = string.Empty;
            ProgramId = string.Empty; ModuleRole = string.Empty; LeadEngineer = string.Empty; ProgramManager = string.Empty; MissionId = string.Empty;
            LaunchDay = -1; EndDay = -1;
        }

        public bool IsActive { get { return Status != ProjectStatus.Complete && Status != ProjectStatus.Failed && Status != ProjectStatus.Cancelled; } }

        /// <summary>Days left in the current phase for Design, Build, and InFlight; 0 otherwise.</summary>
        public int DaysRemaining(int currentDay, int durationDays)
        {
            int left;
            switch (Status)
            {
                case ProjectStatus.Design: left = PhaseStartDay + DesignDays - currentDay; break;
                case ProjectStatus.Build: left = PhaseStartDay + BuildDays - currentDay; break;
                case ProjectStatus.InFlight: left = LaunchDay + durationDays - currentDay; break;
                default: return 0;
            }
            return left < 0 ? 0 : left;
        }

        public override string ToString() { return Name + " [" + Status + "]"; }
    }

    /// <summary>What happened on a mission, as the agency records and shows it (Jira KSA-36).</summary>
    public sealed class MissionReport
    {
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string MissionId { get; set; }
        public string MissionTypeId { get; set; }
        public int Day { get; set; }
        public MissionOutcome Outcome { get; set; }
        public MissionMode Mode { get; set; }
        public List<string> Crew { get; } = new List<string>();
        public List<string> Lines { get; } = new List<string>();
        public List<string> Achievements { get; } = new List<string>();
        public List<IncidentKind> Incidents { get; } = new List<IncidentKind>();
        public List<string> Deaths { get; } = new List<string>();
        public double DaysInSpace { get; set; }
        public double DesignScore { get; set; }
        public double CrewScore { get; set; }
        public double SuccessChance { get; set; }
        /// <summary>Short tag for the lessons-learned list that feeds AI prompts in M4.</summary>
        public string Lesson { get; set; }

        public MissionReport()
        {
            ProjectId = string.Empty; ProjectName = string.Empty; MissionId = string.Empty; MissionTypeId = string.Empty; Lesson = string.Empty;
        }

        public bool IsSuccess { get { return Outcome == MissionOutcome.Success || Outcome == MissionOutcome.PartialSuccess; } }
    }
}
