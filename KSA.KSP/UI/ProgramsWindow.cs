using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Missions;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Programs office (Jira KSA-37): stations, bases and cloud cities built from module projects.
    /// Every action goes through the Agency and the window rebuilds itself to show the new state.
    /// </summary>
    public sealed class ProgramsWindow
    {
        private const string DialogName = "KSA_Programs";
        private const float Width = 720f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 660f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float ButtonHeight = 26f;
        private const int NotesShown = 3;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        private string _selectedTypeId = string.Empty;
        private string _selectedBody = string.Empty;

        public ProgramsWindow(Agency agency, Action onClosed)
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
            KsaCore.Log.Info("Programs window: " + _agency.Programs.Count + " program(s), " + _agency.Catalogs.ProgramTypes.Count + " program type(s)");

            items.Add(Field("Balance", Agency.Money(_agency.Ledger.Balance), textWidth));

            BuildProgramsSection(items, textWidth);
            BuildNewProgramSection(items, textWidth);
            items.Add(new DialogGUISpace(SectionGap));

            items.Add(new DialogGUISpace(KsaUi.ScrollTail));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "A program builds a station or base one module project at a time.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Programs", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
            KsaUi.MakeOpaque(_dialog);
        }

        // --- Programs ---

        private void BuildProgramsSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "Programs (" + _agency.Programs.Count + ")", textWidth);
            if (_agency.Programs.Count == 0) Line(items, "No programs yet.", textWidth);
            for (int i = _agency.Programs.Count - 1; i >= 0; i--)
            {
                Program g = _agency.Programs[i];
                string id = g.Id;
                ProgramType type = _agency.Catalogs.ProgramTypes.Get(g.ProgramTypeId);
                string typeName = type != null ? type.Name : g.ProgramTypeId;
                int total = type != null ? type.Modules.Count : 0;
                int done = 0;
                foreach (string pid in g.ModuleProjectIds)
                {
                    Project mp = _agency.GetProject(pid);
                    if (mp != null && mp.Status == ProjectStatus.Complete) done++;
                }

                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("<b>" + g.Name + "</b>  (" + g.Id + ")", 260f, LineHeight),
                    new DialogGUILabel(typeName + "  |  " + g.Body + "  |  " + StatusText(g.Status) + "  |  modules " + done + " / " + total, textWidth - 266f, LineHeight)));
                if (g.Site.Length > 0) Line(items, "Site: " + g.Site, textWidth);
                foreach (string pid in g.ModuleProjectIds)
                {
                    Project mp = _agency.GetProject(pid);
                    if (mp == null) { Line(items, pid + ": (missing)", textWidth); continue; }
                    string outcome = mp.Report != null ? Agency.OutcomeText(mp.Report.Outcome) : ProjectStatusText(mp.Status);
                    Line(items, mp.Id + "  " + mp.Name + ": " + outcome, textWidth);
                }
                int firstNote = Math.Max(0, g.Notes.Count - NotesShown);
                for (int n = firstNote; n < g.Notes.Count; n++) Line(items, "<i>" + g.Notes[n] + "</i>", textWidth);

                var buttons = new List<DialogGUIBase>();
                buttons.Add(new DialogGUISpace(16f));
                if (g.Status == ProgramStatus.Stalled)
                    buttons.Add(new DialogGUIButton("Resume", () => OnResume(id), 110f, ButtonHeight, false));
                if (g.Status == ProgramStatus.Active || g.Status == ProgramStatus.Stalled)
                    buttons.Add(new DialogGUIButton("Cancel", () => OnCancel(id), 90f, ButtonHeight, false));
                if (buttons.Count > 1)
                    items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, buttons.ToArray()));
            }
        }

        // --- New program ---

        private void BuildNewProgramSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "New program", textWidth);
            IReadOnlyList<ProgramType> types = _agency.Catalogs.ProgramTypes.Types;
            if (types.Count == 0)
            {
                Line(items, "No program types configured.", textWidth);
                return;
            }
            ProgramType selected = _agency.Catalogs.ProgramTypes.Get(_selectedTypeId);

            Line(items, "Program type:", textWidth);
            ButtonRows(items, types.Count, textWidth, 200f, i =>
            {
                ProgramType t = types[i];
                string tid = t.Id;
                bool isSel = selected != null && string.Equals(selected.Id, tid, StringComparison.OrdinalIgnoreCase);
                return new DialogGUIButton((isSel ? "> " : "") + t.Name, () => OnSelectType(tid), 200f, ButtonHeight, false);
            });
            if (selected == null)
            {
                Line(items, "Choose a program type.", textWidth);
                return;
            }
            if (selected.Description.Length > 0) Line(items, selected.Description, textWidth);

            // Recipe and cost.
            double totalCost = 0;
            var recipe = new List<string>();
            foreach (ModuleRecipe m in selected.Modules)
            {
                MissionType mt = _agency.Catalogs.Missions.Get(m.MissionTypeId);
                recipe.Add(m.Role + " (" + (mt != null ? mt.Name : m.MissionTypeId) + ")");
                if (mt != null) totalCost += _agency.ProjectCost(mt, _agency.Catalogs.Templates.Best(mt.Id, _agency.InstalledMods));
            }
            Line(items, "Modules: " + string.Join(", ", recipe.ToArray()), textWidth);
            Line(items, "Estimated total " + Agency.Money(totalCost) + " across " + selected.Modules.Count + " module project" + (selected.Modules.Count == 1 ? "" : "s") + ", each charged when it starts.", textWidth);

            // Body.
            List<string> bodies = selected.Bodies;
            if (bodies.Count == 0)
            {
                Line(items, "This program type lists no bodies; it cannot be started.", textWidth);
                return;
            }
            bool bodyOk = false;
            foreach (string b in bodies) if (string.Equals(b, _selectedBody, StringComparison.OrdinalIgnoreCase)) { bodyOk = true; break; }
            if (!bodyOk) _selectedBody = bodies[0];

            Line(items, "Body:", textWidth);
            ButtonRows(items, bodies.Count, textWidth, 130f, i =>
            {
                string body = bodies[i];
                bool isSel = string.Equals(body, _selectedBody, StringComparison.OrdinalIgnoreCase);
                return new DialogGUIButton((isSel ? "> " : "") + body, () => OnSelectBody(body), 130f, ButtonHeight, false);
            });

            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton("Start program", OnStartProgram, 180f, 30f, false),
                new DialogGUILabel("Starts " + selected.Name + " " + _selectedBody + "; the first module is auto-staffed.", textWidth - 210f, LineHeight)));
        }

        // --- Director actions: call the Agency, remember its message, rebuild ---

        private void OnResume(string programId)
        {
            string message;
            bool ok = _agency.ResumeProgram(programId, out message);
            Report(ok, message);
        }

        private void OnCancel(string programId)
        {
            string message;
            bool ok = _agency.CancelProgram(programId, out message);
            Report(ok, message);
        }

        private void OnSelectType(string typeId)
        {
            if (!string.Equals(_selectedTypeId, typeId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTypeId = typeId;
                _selectedBody = string.Empty;
            }
            Refresh();
        }

        private void OnSelectBody(string body)
        {
            _selectedBody = body;
            Refresh();
        }

        private void OnStartProgram()
        {
            ProgramType type = _agency.Catalogs.ProgramTypes.Get(_selectedTypeId);
            if (type == null) { Report(false, "Choose a program type first."); return; }
            string message;
            Program g = _agency.CreateProgram(type.Name + " " + _selectedBody, type.Id, _selectedBody, null, out message);
            if (g != null)
            {
                _selectedTypeId = string.Empty;
                _selectedBody = string.Empty;
            }
            Report(g != null, message);
        }

        private void Report(bool ok, string message)
        {
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Programs: " + _status);
            else KsaCore.Log.Warn("Programs: " + _status);
            Refresh();
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private static string StatusText(ProgramStatus s)
        {
            switch (s)
            {
                case ProgramStatus.Active: return "active";
                case ProgramStatus.Complete: return "complete";
                case ProgramStatus.Stalled: return "stalled";
                case ProgramStatus.Cancelled: return "cancelled";
                default: return s.ToString();
            }
        }

        private static string ProjectStatusText(ProjectStatus s)
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

        // --- layout helpers: every line has an explicit height so rows never overlap ---

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
                new DialogGUILabel(text, textWidth - 16f, KsaUi.LabelHeight(text, textWidth - 16f, LineHeight))));
        }
    }
}
