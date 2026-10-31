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
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 666f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 266f; } }
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
        private AiSetupWindow _aiSetup;
        private NewsWindow _news;
        private AnalyticsWindow _analytics;

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
            HelpWindow.CloseIfOpen();
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
            if (_pendingName == null) _pendingName = _agency.AgencyName;
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("Agency", LabelWidth, LineHeight),
                new DialogGUITextInput(_pendingName, false, 48, v => { _pendingName = v ?? string.Empty; return _pendingName; }, LineHeight + 4f),
                new DialogGUIButton("Rename", OnRename, 90f, 26f, false),
                new DialogGUILabel("Your space agency's name: press releases, the league table and rivals use it.", textWidth - LabelWidth - 330f, LineHeight)));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("Fiscal year", LabelWidth, LineHeight),
                new DialogGUILabel(() => _agency.CurrentYear + ", day " + _agency.DayOfYear + " of " + _agency.Calendar.DaysPerYear + "  (" + _agency.Calendar.DaysLeftInYear(_agency.CurrentDay) + " days to the review)", textWidth - LabelWidth - 10f, LineHeight)));
            items.Add(Field("Balance", Agency.Money(_agency.Ledger.Balance)
                + (_agency.Ledger.Mirror != null && _agency.Ledger.Mirror.IsActive ? "  (stock Funds)" : "  (agency account)"), textWidth));
            items.Add(Field("This year", "income " + Agency.Money(_agency.Ledger.IncomeFor(_agency.CurrentYear))
                + ", spent " + Agency.Money(_agency.Ledger.ExpensesFor(_agency.CurrentYear)), textWidth));
            items.Add(Field("Prestige", ((int)Math.Round(s.Prestige)) + " / 100", textWidth));
            items.Add(Field("Staff", active + " active, average morale " + moraleAvg + ", applicants waiting: " + _agency.Applicants.Applicants.Count, textWidth));
            items.Add(Field("Research", _agency.LabScientists().Count + " in the lab, about " + _agency.ProjectedLabScience().ToString("0") + " Science at year end; " + _agency.State.ScienceEarned.ToString("0") + " earned so far (Roster: Lab button on scientists)", textWidth));
            int inFlight = 0, inWork = 0, ready = 0;
            foreach (KSA.Core.Projects.Project pr in _agency.Projects)
            {
                if (pr.Status == KSA.Core.Projects.ProjectStatus.InFlight) inFlight++;
                else if (pr.Status == KSA.Core.Projects.ProjectStatus.Ready) ready++;
                else if (pr.IsActive) inWork++;
            }
            items.Add(Field("Projects", inFlight + " in flight, " + ready + " ready, " + inWork + " in design or build, " + _agency.Programs.Count + " programs", textWidth));
            List<string> ticker = _agency.News.TickerLines(1);
            if (ticker.Count > 0)
            {
                string headline = ticker[0] ?? string.Empty;
                if (headline.Length > 110) headline = headline.Substring(0, 107) + "...";
                items.Add(new DialogGUILabel("<color=#aaaaaa>Latest: " + headline + "</color>", textWidth, LineHeight));
            }

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
                string failed = _agency.GoalFailure(gs);
                string carried = gs.YearIssued < _agency.CurrentYear ? " (carried from year " + gs.YearIssued + ")" : string.Empty;
                string tag = failed != null ? "<color=#ff8080>[failed: " + failed + "]</color> " : met ? "<color=#8be28b>[met]</color> " : "<color=#dddddd>[open]</color> ";
                Line(items, tag + name + carried, textWidth);
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
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var buttons = new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton("Roster", OpenRoster, 95f, 30f, false),
                new DialogGUIButton("Recruiting", OpenRecruiting, 100f, 30f, false),
                new DialogGUIButton("Gov. Review", OpenReview, 100f, 30f, false),
                new DialogGUIButton("Hall of Fame", OpenHallOfFame, 100f, 30f, false),
                new DialogGUIButton("Records", OpenRecords, 80f, 30f, false),
                new DialogGUIFlexibleSpace(),
                new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true));
            var buttons2 = new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton("Projects", OpenProjects, 95f, 30f, false),
                new DialogGUIButton("Programs", OpenPrograms, 100f, 30f, false),
                new DialogGUIButton("Mission Control", OpenMissionControl, 130f, 30f, false),
                new DialogGUIButton("AI Setup", OpenAiSetup, 95f, 30f, false),
                new DialogGUIButton(_agency.News.Unread > 0 ? "Press Room (" + _agency.News.Unread + " new)" : "Press Room", OpenNews, 150f, 30f, false));
            var buttons3 = new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton("Analytics", OpenAnalytics, 95f, 30f, false),
                new DialogGUIButton("Campaign", OpenCampaign, 100f, 30f, false),
                new DialogGUIButton("Departments", OpenDepartments, 110f, 30f, false),
                new DialogGUIButton(_agency.DirectorOn ? "AI Director (on)" : "AI Director", OpenAiDirector, 150f, 30f, false),
                new DialogGUIButton("Help", () => HelpWindow.Show("start"), 80f, 30f, false),
                // Sandbox saves only: the flight test bench (2026-10-30).
                new DialogGUIButton("Flight tests", OpenFlightTests, () => HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX, 110f, 30f, false));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, list, buttons, buttons2, buttons3);

            var dialog = new MultiOptionDialog(DialogName, "", "Kerbal Space Directorate  v" + Core.CoreInfo.Version, KsaUi.Skin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        // --- child windows (one of each at a time) ---

        private Testing.FlightTestWindow _flightTests;

        private void OpenFlightTests()
        {
            if (_flightTests != null && _flightTests.IsOpen) return;
            _flightTests = new Testing.FlightTestWindow(() => { _flightTests = null; });
            _flightTests.Open();
        }

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

        private void OpenAiSetup()
        {
            if (_aiSetup != null && _aiSetup.IsOpen) return;
            _aiSetup = new AiSetupWindow(_agency, () => { _aiSetup = null; Refresh(); });
            _aiSetup.Open();
        }

        private void OpenNews()
        {
            if (_news != null && _news.IsOpen) return;
            _news = new NewsWindow(_agency, () => { _news = null; Refresh(); });
            _news.Open();
            Refresh(); // the Press Room marks everything read: drop the "(N new)" badge at once
        }

        private CampaignWindow _campaign;
        private void OpenCampaign()
        {
            if (_campaign != null && _campaign.IsOpen) return;
            _campaign = new CampaignWindow(_agency, () => { _campaign = null; });
            _campaign.Open();
        }

        private AiDirectorWindow _aiDirector;
        private void OpenAiDirector()
        {
            if (_aiDirector != null && _aiDirector.IsOpen) return;
            _aiDirector = new AiDirectorWindow(_agency, () => { _aiDirector = null; Refresh(); });
            _aiDirector.Open();
        }

        private RecordsWindow _records;
        private void OpenRecords()
        {
            if (_records != null && _records.IsOpen) return;
            _records = new RecordsWindow(_agency, () => { _records = null; });
            _records.Open();
        }

        private DepartmentsWindow _departments;
        private void OpenDepartments()
        {
            if (_departments != null && _departments.IsOpen) return;
            _departments = new DepartmentsWindow(_agency, () => { _departments = null; Refresh(); });
            _departments.Open();
        }

        private string _pendingName;
        private void OnRename()
        {
            string msg;
            _agency.Rename(_pendingName, out msg);
            _pendingName = null;
            ScreenMessages.PostScreenMessage("[KSA] " + msg, 5f, ScreenMessageStyle.UPPER_CENTER);
            Refresh();
        }

        private void OpenAnalytics()
        {
            if (_analytics != null && _analytics.IsOpen) return;
            _analytics = new AnalyticsWindow(_agency, () => { _analytics = null; });
            _analytics.Open();
        }

        private void CloseChildren()
        {
            if (_analytics != null) { AnalyticsWindow w = _analytics; _analytics = null; w.Close(); }
            if (_records != null) { RecordsWindow w = _records; _records = null; w.Close(); }
            if (_departments != null) { DepartmentsWindow w = _departments; _departments = null; w.Close(); }
            if (_aiDirector != null) { AiDirectorWindow w = _aiDirector; _aiDirector = null; w.Close(); }
            if (_campaign != null) { CampaignWindow w = _campaign; _campaign = null; w.Close(); }
            if (_news != null) { NewsWindow w = _news; _news = null; w.Close(); }
            if (_aiSetup != null) { AiSetupWindow w = _aiSetup; _aiSetup = null; w.Close(); }
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
                new DialogGUILabel(text, textWidth - 16f, KsaUi.LabelHeight(text, textWidth - 16f, LineHeight))));
        }
    }
}
