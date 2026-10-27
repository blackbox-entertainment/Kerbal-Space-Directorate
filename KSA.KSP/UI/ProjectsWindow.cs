using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Projects office (Jira KSA-35): see active projects, change how they fly, launch or cancel them, and start new ones.
    /// Every action goes through the Agency and the window rebuilds itself to show the new state.
    /// </summary>
    public sealed class ProjectsWindow
    {
        private const string DialogName = "KSA_Projects";
        private const float Width = 720f;
        private const float WindowHeight = 660f;
        private const float ListHeight = 540f;
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float ButtonHeight = 26f;
        private const int MaxEngineers = 4;
        private const int FinishedShown = 10;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        // New-project selection state; survives Refresh() because the window object does.
        private string _selectedTypeId = string.Empty;
        private string _selectedTemplateId = string.Empty;
        private readonly List<string> _selectedCrew = new List<string>();
        private readonly List<string> _selectedEngineers = new List<string>();
        private MissionMode _mode = MissionMode.Simulate;

        // One Design Review child window at a time, same pattern as the Director's Office.
        private DesignReviewWindow _designReview;

        public ProjectsWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            Spawn();
        }

        public void Close()
        {
            CloseChildren();
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Refresh()
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
            KsaCore.Log.Info("Projects window: " + _agency.Projects.Count + " project(s), " + _agency.Catalogs.Missions.Count + " mission type(s)");

            items.Add(Field("Balance", Agency.Money(_agency.Ledger.Balance), textWidth));

            BuildActiveSection(items, textWidth);
            BuildNewProjectSection(items, textWidth);
            BuildFinishedSection(items, textWidth);
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Pick a mission, a design, and a crew, then start the project.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Projects", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        // --- Active projects ---

        private void BuildActiveSection(List<DialogGUIBase> items, float textWidth)
        {
            var active = new List<Project>();
            for (int i = _agency.Projects.Count - 1; i >= 0; i--)
                if (_agency.Projects[i].IsActive) active.Add(_agency.Projects[i]);

            Section(items, "Active projects (" + active.Count + ")", textWidth);
            if (active.Count == 0) Line(items, "No projects under way.", textWidth);
            foreach (Project p in active)
            {
                string id = p.Id;
                MissionType type = _agency.Catalogs.Missions.Get(p.MissionTypeId);
                string typeName = type != null ? type.Name : p.MissionTypeId;
                int daysLeft = p.DaysRemaining(_agency.CurrentDay, type != null ? type.DurationDays : 0);

                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("<b>" + p.Name + "</b>  (" + p.Id + ")", 260f, LineHeight),
                    new DialogGUILabel(typeName + "  |  " + StatusText(p.Status) + "  |  " + daysLeft + " day" + (daysLeft == 1 ? "" : "s") + " left", textWidth - 266f, LineHeight)));
                Line(items, "Mode " + ModeText(p.Mode) + "  |  budget " + Agency.Money(p.Budget) + "  |  design " + _agency.Catalogs.Templates.NameOf(p.TemplateId), textWidth);
                Line(items, "Crew: " + Names(p.Crew) + "  |  lead engineer: " + NameOf(p.LeadEngineer), textWidth);
                Line(items, DesignLine(p), textWidth);

                var buttons = new List<DialogGUIBase>();
                buttons.Add(new DialogGUISpace(16f));
                buttons.Add(new DialogGUIButton("Design", () => OpenDesignReview(id), 90f, ButtonHeight, false));
                if (p.Status != ProjectStatus.InFlight)
                {
                    MissionMode other = p.Mode == MissionMode.Simulate ? MissionMode.WatchLive : MissionMode.Simulate;
                    buttons.Add(new DialogGUIButton("Switch to " + ModeText(other), () => OnSetMode(id, other), 170f, ButtonHeight, false));
                }
                if (p.Status == ProjectStatus.Ready)
                    buttons.Add(new DialogGUIButton("Launch now", () => OnLaunchNow(id), 120f, ButtonHeight, false));
                if (p.Status != ProjectStatus.InFlight)
                    buttons.Add(new DialogGUIButton("Cancel", () => OnCancel(id), 90f, ButtonHeight, false));
                if (buttons.Count > 1)
                    items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, buttons.ToArray()));
            }
        }

        // --- New project ---

        private void BuildNewProjectSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "New project", textWidth);

            List<CrewProfile> anyCrew = _agency.AvailableFor(CrewRole.Unknown);
            var flyers = new List<CrewProfile>();
            foreach (CrewProfile c in anyCrew) if (c.Role != CrewRole.Tourist) flyers.Add(c);
            if (flyers.Count == 0)
            {
                Line(items, "No crew are available. Hire or free up crew before starting a project.", textWidth);
                return;
            }

            // Mission type row(s): non-module types only.
            var types = new List<MissionType>();
            foreach (MissionType t in _agency.Catalogs.Missions.Types)
                if (string.IsNullOrEmpty(t.ModuleRole)) types.Add(t);
            if (types.Count == 0)
            {
                Line(items, "No mission types configured.", textWidth);
                return;
            }
            MissionType selected = _agency.Catalogs.Missions.Get(_selectedTypeId);
            if (selected != null && !string.IsNullOrEmpty(selected.ModuleRole)) selected = null;

            Line(items, "Mission:", textWidth);
            ButtonRows(items, types.Count, textWidth, 170f, i =>
            {
                MissionType t = types[i];
                string tid = t.Id;
                bool isSel = selected != null && string.Equals(selected.Id, tid, StringComparison.OrdinalIgnoreCase);
                return new DialogGUIButton((isSel ? "> " : "") + t.Name, () => OnSelectType(tid), 170f, ButtonHeight, false);
            });
            if (selected == null)
            {
                Line(items, "Choose a mission type.", textWidth);
                return;
            }

            // Selected type facts.
            if (selected.Description.Length > 0) Line(items, selected.Description, textWidth);
            Line(items, "Crew " + selected.CrewSize + "  |  min stars " + selected.MinStars + "  |  " + selected.DurationDays + " day"
                + (selected.DurationDays == 1 ? "" : "s") + "  |  difficulty " + Percent(selected.Difficulty) + "  |  " + selected.Body
                + (selected.IsFirst ? "  |  a First" : ""), textWidth);

            // Templates.
            List<Template> templates = _agency.Catalogs.Templates.For(selected.Id, _agency.InstalledMods);
            Template selectedTemplate = null;
            foreach (Template t in templates)
                if (string.Equals(t.Id, _selectedTemplateId, StringComparison.OrdinalIgnoreCase)) { selectedTemplate = t; break; }
            if (selectedTemplate == null && templates.Count > 0) { selectedTemplate = templates[0]; _selectedTemplateId = selectedTemplate.Id; }

            Line(items, "Design:", textWidth);
            if (templates.Count == 0) Line(items, "No design template can fly " + selected.Name + " with the mods installed.", textWidth);
            MissionType typeForCost = selected;
            ButtonRows(items, templates.Count, textWidth, 220f, i =>
            {
                Template t = templates[i];
                string tid = t.Id;
                bool isSel = selectedTemplate != null && string.Equals(selectedTemplate.Id, tid, StringComparison.OrdinalIgnoreCase);
                string label = (isSel ? "> " : "") + t.Name + " (" + Percent(t.DesignScore) + ", " + Agency.Money(_agency.ProjectCost(typeForCost, t)) + ")";
                return new DialogGUIButton(label, () => OnSelectTemplate(tid), 220f, ButtonHeight, false);
            });

            // Crew pick.
            PruneSelection(_selectedCrew, flyers);
            Line(items, "Crew (" + _selectedCrew.Count + " of " + selected.CrewSize + " picked):", textWidth);
            foreach (CrewProfile c in flyers)
            {
                string cid = c.Id;
                bool picked = _selectedCrew.Contains(cid);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    KsaIcons.Badge(c.Role),
                    new DialogGUIButton((picked ? "[x] " : "") + c.Name, () => OnToggleCrew(cid, selected.CrewSize), 190f, ButtonHeight, false),
                    new DialogGUILabel(RosterView.RoleText(c.Role) + "  |  " + RosterView.StarsText(c.Stars) + "  |  " + _agency.Careers.RankName(c.Track, c.RankId)
                        + (c.Stars < selected.MinStars ? "  |  <color=#ffd27f>below min stars</color>" : ""),
                        textWidth - KsaIcons.BadgeSize - 230f, LineHeight)));
            }

            // Engineer pick.
            List<CrewProfile> engineers = _agency.AvailableFor(CrewRole.Engineer);
            PruneSelection(_selectedEngineers, engineers);
            Line(items, "Engineering team (" + _selectedEngineers.Count + " of up to " + MaxEngineers + "):", textWidth);
            if (engineers.Count == 0) Line(items, "No engineers are available; the project will have no engineering team.", textWidth);
            foreach (CrewProfile c in engineers)
            {
                string cid = c.Id;
                bool picked = _selectedEngineers.Contains(cid);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    KsaIcons.Badge(c.Role),
                    new DialogGUIButton((picked ? "[x] " : "") + c.Name, () => OnToggleEngineer(cid), 190f, ButtonHeight, false),
                    new DialogGUILabel(RosterView.StarsText(c.Stars) + "  |  " + _agency.Careers.RankName(c.Track, c.RankId),
                        textWidth - KsaIcons.BadgeSize - 230f, LineHeight)));
            }

            // Mode and start.
            bool simSel = _mode == MissionMode.Simulate;
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton("Auto-staff", OnAutoStaff, 110f, ButtonHeight, false),
                new DialogGUIButton((simSel ? "> " : "") + "Simulate", () => OnSetNewMode(MissionMode.Simulate), 110f, ButtonHeight, false),
                new DialogGUIButton((!simSel ? "> " : "") + "Watch Live", () => OnSetNewMode(MissionMode.WatchLive), 110f, ButtonHeight, false),
                new DialogGUILabel(_agency.LiveFlightAvailable ? "" : "Live flight arrives in M5; will simulate", textWidth - 370f, LineHeight)));

            double cost = _agency.ProjectCost(selected, selectedTemplate);
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton("Start project (" + Agency.Money(cost) + ")", OnStartProject, 240f, 30f, false),
                new DialogGUILabel(_agency.Ledger.Balance < cost ? "<color=#ffd27f>Not enough funds.</color>" : "Charged to the budget on start.", textWidth - 270f, LineHeight)));
        }

        // --- Finished projects ---

        private void BuildFinishedSection(List<DialogGUIBase> items, float textWidth)
        {
            var finished = new List<Project>();
            for (int i = _agency.Projects.Count - 1; i >= 0 && finished.Count < FinishedShown; i--)
                if (!_agency.Projects[i].IsActive) finished.Add(_agency.Projects[i]);

            Section(items, "Finished projects", textWidth);
            if (finished.Count == 0) Line(items, "Nothing finished yet.", textWidth);
            foreach (Project p in finished)
            {
                string id = p.Id;
                string outcome = p.Report != null ? Agency.OutcomeText(p.Report.Outcome) : StatusText(p.Status);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    new DialogGUIButton("Design", () => OpenDesignReview(id), 90f, ButtonHeight, false),
                    new DialogGUILabel(p.Id + "  " + p.Name + ": " + outcome + (p.EndDay >= 0 ? " (day " + p.EndDay + ")" : ""), textWidth - 118f, LineHeight)));
            }
        }

        // --- Design Review child window (one at a time) ---

        private void OpenDesignReview(string projectId)
        {
            if (_designReview != null && _designReview.IsOpen) _designReview.Close();
            _designReview = new DesignReviewWindow(_agency, projectId, () => { _designReview = null; Refresh(); });
            _designReview.Open();
        }

        private void CloseChildren()
        {
            if (_designReview != null) { DesignReviewWindow w = _designReview; _designReview = null; w.Close(); }
        }

        /// <summary>One line on where the project's design stands: AI record, template, or a session still running.</summary>
        private string DesignLine(Project p)
        {
            if (_agency.DesignInProgress(p.Id)) return "Design: team at work...";
            KSA.Core.Design.DesignRecord d = p.Design;
            if (d == null || d.Source != KSA.Core.Design.DesignSource.Ai) return "Design: template";
            string name = d.DesignName.Length > 0 ? d.DesignName : "(unnamed)";
            return "Design: " + name + " (AI, " + d.Verdict + ", " + d.TotalDeltaV.ToString("0") + " m/s)";
        }

        // --- Director actions: call the Agency, remember its message, rebuild ---

        private void OnSetMode(string projectId, MissionMode mode)
        {
            string message;
            bool ok = _agency.SetProjectMode(projectId, mode, out message);
            Report(ok, message);
        }

        private void OnLaunchNow(string projectId)
        {
            string message;
            bool ok = _agency.LaunchNow(projectId, out message);
            Report(ok, message);
        }

        private void OnCancel(string projectId)
        {
            string message;
            bool ok = _agency.CancelProject(projectId, out message);
            Report(ok, message);
        }

        private void OnSelectType(string typeId)
        {
            if (!string.Equals(_selectedTypeId, typeId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTypeId = typeId;
                _selectedTemplateId = string.Empty;
                _selectedCrew.Clear();
                _mode = _agency.DefaultModeFor(_agency.Catalogs.Missions.Get(typeId));
            }
            Refresh();
        }

        private void OnSelectTemplate(string templateId)
        {
            _selectedTemplateId = templateId;
            Refresh();
        }

        private void OnToggleCrew(string crewId, int crewSize)
        {
            if (_selectedCrew.Contains(crewId)) _selectedCrew.Remove(crewId);
            else if (_selectedCrew.Count < crewSize) _selectedCrew.Add(crewId);
            else _status = "Crew is full (" + crewSize + "). Unpick someone first.";
            Refresh();
        }

        private void OnToggleEngineer(string crewId)
        {
            if (_selectedEngineers.Contains(crewId)) _selectedEngineers.Remove(crewId);
            else if (_selectedEngineers.Count < MaxEngineers) _selectedEngineers.Add(crewId);
            else _status = "Engineering team is full (" + MaxEngineers + "). Unpick someone first.";
            Refresh();
        }

        private void OnAutoStaff()
        {
            MissionType type = _agency.Catalogs.Missions.Get(_selectedTypeId);
            _selectedCrew.Clear();
            _selectedCrew.AddRange(_agency.AutoCrew(type));
            _selectedEngineers.Clear();
            _selectedEngineers.AddRange(_agency.AutoEngineers(2));
            _status = "Auto-staffed: " + _selectedCrew.Count + " crew, " + _selectedEngineers.Count + " engineer" + (_selectedEngineers.Count == 1 ? "" : "s") + ".";
            Refresh();
        }

        private void OnSetNewMode(MissionMode mode)
        {
            _mode = mode;
            Refresh();
        }

        private void OnStartProject()
        {
            MissionType type = _agency.Catalogs.Missions.Get(_selectedTypeId);
            if (type == null) { Report(false, "Choose a mission type first."); return; }
            string message;
            Project p = _agency.CreateProject(type.Name, type.Id, _selectedTemplateId, new List<string>(_selectedCrew), new List<string>(_selectedEngineers), null, _mode, out message);
            if (p != null)
            {
                _selectedTypeId = string.Empty;
                _selectedTemplateId = string.Empty;
                _selectedCrew.Clear();
                _selectedEngineers.Clear();
                _mode = MissionMode.Simulate;
            }
            Report(p != null, message);
        }

        private void Report(bool ok, string message)
        {
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Projects: " + _status);
            else KsaCore.Log.Warn("Projects: " + _status);
            Refresh();
        }

        private void OnCloseClicked()
        {
            CloseChildren();
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private static void PruneSelection(List<string> selected, List<CrewProfile> available)
        {
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                bool found = false;
                foreach (CrewProfile c in available) if (string.Equals(c.Id, selected[i], StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) selected.RemoveAt(i);
            }
        }

        private string NameOf(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return "none";
            CrewProfile p = _agency.Crew.Get(crewId);
            return p != null ? p.Name : crewId;
        }

        private string Names(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "none";
            var names = new List<string>();
            foreach (string id in ids) names.Add(NameOf(id));
            return string.Join(", ", names.ToArray());
        }

        private static string ModeText(MissionMode m)
        {
            return m == MissionMode.WatchLive ? "Watch Live" : "Simulate";
        }

        private static string StatusText(ProjectStatus s)
        {
            switch (s)
            {
                case ProjectStatus.Design: return "in design";
                case ProjectStatus.Build: return "in build";
                case ProjectStatus.Ready: return "ready to launch";
                case ProjectStatus.InFlight: return "in flight";
                case ProjectStatus.Complete: return "complete";
                case ProjectStatus.Failed: return "failed";
                case ProjectStatus.Cancelled: return "cancelled";
                default: return s.ToString();
            }
        }

        private static string Percent(double v)
        {
            return ((int)Math.Round(v * 100.0)) + "%";
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

        /// <summary>Lays out <paramref name="count"/> buttons in rows that fit the text width.</summary>
        private static void ButtonRows(List<DialogGUIBase> items, int count, float textWidth, float buttonWidth, Func<int, DialogGUIBase> make)
        {
            int perRow = Math.Max(1, (int)((textWidth - 16f) / (buttonWidth + 6f)));
            for (int start = 0; start < count; start += perRow)
            {
                var row = new List<DialogGUIBase>();
                row.Add(new DialogGUISpace(16f));
                for (int i = start; i < count && i < start + perRow; i++) row.Add(make(i));
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, row.ToArray()));
            }
        }

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
