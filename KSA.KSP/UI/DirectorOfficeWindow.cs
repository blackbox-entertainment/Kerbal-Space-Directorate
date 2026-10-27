using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Goals;
using KSA.Core.Personnel;
using KSA.KSP.Roster;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// The hub (Jira KSA-32): fiscal year and day, balance, prestige, active goals, alerts, and buttons to every
    /// other KSA window. Opened by the toolbar button.
    /// </summary>
    public sealed class DirectorOfficeWindow
    {
        private const string DialogName = "KSA_DirectorOffice";
        private const float Width = 620f;
        private const float WindowHeight = 600f;
        private const float ListHeight = 400f;
        private const float LabelWidth = 130f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;

        private RosterWindow _roster;
        private RecruitingWindow _recruiting;
        private GovernmentReviewWindow _review;
        private HallOfFameWindow _hallOfFame;
        private ProjectsWindow _projects;
        private ProgramsWindow _programs;
        private MissionControlWindow _missionControl;

        public DirectorOfficeWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            _agency.YearEnded += OnYearEnded;
            Spawn();
        }

        public void Close()
        {
            _agency.YearEnded -= OnYearEnded;
            CloseChildren();
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        public void Refresh()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
            Spawn();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            AgencyState s = _agency.State;

            // --- Headline ---
            int active = 0, pendingSelection = 0, moraleSum = 0;
            foreach (CrewProfile p in _agency.Crew.Active)
            {
                active++;
                moraleSum += p.Morale;
                if (_agency.Promotions.PendingSelection(p) != null) pendingSelection++;
            }
            int moraleAvg = active > 0 ? moraleSum / active : 0;

            items.Add(new DialogGUILabel("<size=16><b>" + s.DirectorTitle + "'s Office</b></size>", textWidth, 28f));
            items.Add(Field("Fiscal year", _agency.CurrentYear + ", day " + _agency.DayOfYear + " of " + _agency.Calendar.DaysPerYear
                + "  (" + _agency.Calendar.DaysLeftInYear(_agency.CurrentDay) + " days to the review)", textWidth));
            items.Add(Field("Balance", Agency.Money(_agency.Ledger.Balance)
                + (_agency.Ledger.Mirror != null && _agency.Ledger.Mirror.IsActive ? "  (stock Funds)" : "  (agency account)"), textWidth));
            items.Add(Field("This year", "income " + Agency.Money(_agency.Ledger.IncomeFor(_agency.CurrentYear))
                + ", spent " + Agency.Money(_agency.Ledger.ExpensesFor(_agency.CurrentYear)), textWidth));
            items.Add(Field("Prestige", ((int)Math.Round(s.Prestige)) + " / 100", textWidth));
            items.Add(Field("Staff", active + " active, average morale " + moraleAvg + ", applicants waiting: " + _agency.Applicants.Applicants.Count, textWidth));
            int inFlight = 0, inWork = 0, ready = 0;
            foreach (KSA.Core.Projects.Project pr in _agency.Projects)
            {
                if (pr.Status == KSA.Core.Projects.ProjectStatus.InFlight) inFlight++;
                else if (pr.Status == KSA.Core.Projects.ProjectStatus.Ready) ready++;
                else if (pr.IsActive) inWork++;
            }
            items.Add(Field("Projects", inFlight + " in flight, " + ready + " ready, " + inWork + " in design or build, " + _agency.Programs.Count + " programs", textWidth));

            // --- Alerts ---
            var alerts = new List<string>();
            if (s.Probation) alerts.Add("The Director is on probation. Another poor year ends their tenure.");
            if (s.HiringFreeze) alerts.Add("Hiring freeze in effect until the next Government Review.");
            if (s.PendingLayoffs > 0) alerts.Add("The government requires " + s.PendingLayoffs + " layoff" + (s.PendingLayoffs == 1 ? "" : "s") + ". Dismiss crew from the roster.");
            if (pendingSelection > 0) alerts.Add(pendingSelection + " Test Pilot" + (pendingSelection == 1 ? " is" : "s are") + " awaiting astronaut selection. Unselected pilots lose morale at year end.");
            if (alerts.Count > 0)
            {
                Section(items, "Alerts", textWidth);
                foreach (string a in alerts) Line(items, "<color=#ffb347>" + a + "</color>", textWidth);
            }

            // --- Goals ---
            Section(items, "Program goals, year " + _agency.CurrentYear + " (" + _agency.ActiveGoals.Count + ")", textWidth);
            if (_agency.ActiveGoals.Count == 0) Line(items, "No goals issued yet.", textWidth);
            foreach (GoalState gs in _agency.ActiveGoals)
            {
                GoalDefinition def = _agency.Catalogs.Goals.Get(gs.GoalId);
                string name = def != null ? def.Name : gs.GoalId;
                bool met = _agency.IsGoalCurrentlyMet(gs);
                string carried = gs.YearIssued < _agency.CurrentYear ? " (carried from year " + gs.YearIssued + ")" : string.Empty;
                Line(items, (met ? "<color=#8be28b>[met]</color> " : "<color=#dddddd>[open]</color> ") + name + carried, textWidth);
                if (def != null && def.Description.Length > 0) Line(items, "   <i>" + def.Description + "</i>", textWidth);
            }

            // --- Last review ---
            if (_agency.Reviews.Count > 0)
            {
                var r = _agency.Reviews[_agency.Reviews.Count - 1];
                Section(items, "Last Government Review (year " + r.Year + ")", textWidth);
                Line(items, r.BandText + ": " + r.GoalsMet + " of " + r.GoalsIssued + " goals, budget " + (r.BudgetChangePercent >= 0 ? "+" : "") + r.BudgetChangePercent + "%, grant " + Agency.Money(r.NextGrant), textWidth);
            }
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var buttons = new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton("Roster", OpenRoster, 95f, 30f, false),
                new DialogGUIButton("Recruiting", OpenRecruiting, 100f, 30f, false),
                new DialogGUIButton("Gov. Review", OpenReview, 100f, 30f, false),
                new DialogGUIButton("Hall of Fame", OpenHallOfFame, 100f, 30f, false),
                new DialogGUIFlexibleSpace(),
                new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true));
            var buttons2 = new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton("Projects", OpenProjects, 95f, 30f, false),
                new DialogGUIButton("Programs", OpenPrograms, 100f, 30f, false),
                new DialogGUIButton("Mission Control", OpenMissionControl, 130f, 30f, false));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, list, buttons, buttons2);

            var dialog = new MultiOptionDialog(DialogName, "", "Kerbal Space Directorate", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        // --- child windows (one of each at a time) ---

        private void OpenRoster()
        {
            if (_roster != null && _roster.IsOpen) { _roster.Refresh(); return; }
            _roster = new RosterWindow(new KspRosterSource(), KsaCore.Log, () => { _roster = null; Refresh(); });
            _roster.Open();
        }

        private void OpenRecruiting()
        {
            if (_recruiting != null && _recruiting.IsOpen) return;
            _recruiting = new RecruitingWindow(_agency, () => { _recruiting = null; Refresh(); });
            _recruiting.Open();
        }

        private void OpenReview()
        {
            if (_review != null && _review.IsOpen) return;
            _review = new GovernmentReviewWindow(_agency, () => { _review = null; });
            _review.Open();
        }

        private void OpenHallOfFame()
        {
            if (_hallOfFame != null && _hallOfFame.IsOpen) return;
            _hallOfFame = new HallOfFameWindow(_agency, () => { _hallOfFame = null; });
            _hallOfFame.Open();
        }

        private void OpenProjects()
        {
            if (_projects != null && _projects.IsOpen) return;
            _projects = new ProjectsWindow(_agency, () => { _projects = null; Refresh(); });
            _projects.Open();
        }

        private void OpenPrograms()
        {
            if (_programs != null && _programs.IsOpen) return;
            _programs = new ProgramsWindow(_agency, () => { _programs = null; Refresh(); });
            _programs.Open();
        }

        private void OpenMissionControl()
        {
            if (_missionControl != null && _missionControl.IsOpen) return;
            _missionControl = new MissionControlWindow(_agency, () => { _missionControl = null; });
            _missionControl.Open();
        }

        private void CloseChildren()
        {
            if (_projects != null) { ProjectsWindow w = _projects; _projects = null; w.Close(); }
            if (_programs != null) { ProgramsWindow w = _programs; _programs = null; w.Close(); }
            if (_missionControl != null) { MissionControlWindow w = _missionControl; _missionControl = null; w.Close(); }
            if (_roster != null) { RosterWindow w = _roster; _roster = null; w.Close(); }
            if (_recruiting != null) { RecruitingWindow w = _recruiting; _recruiting = null; w.Close(); }
            if (_review != null) { GovernmentReviewWindow w = _review; _review = null; w.Close(); }
            if (_hallOfFame != null) { HallOfFameWindow w = _hallOfFame; _hallOfFame = null; w.Close(); }
        }

        private void OnYearEnded(Core.Review.ReviewResult r) { Refresh(); }

        private void OnCloseClicked()
        {
            _agency.YearEnded -= OnYearEnded;
            CloseChildren();
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- layout helpers ---

        private static DialogGUIHorizontalLayout Field(string label, string value, float textWidth)
        {
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>" + label + "</b>", LabelWidth, LineHeight),
                new DialogGUILabel(value, textWidth - LabelWidth, LineHeight));
        }

        private static void Section(List<DialogGUIBase> items, string title, float textWidth)
        {
            items.Add(new DialogGUISpace(SectionGap));
            items.Add(new DialogGUILabel("<b>" + title + "</b>", textWidth, LineHeight + 2f));
        }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUIHorizontalLayout(false, false, 0f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUILabel(text, textWidth - 16f, LineHeight)));
        }
    }
}
