using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Design;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Design Review (Jira KSA-44): what the engineering team produced for one project. Figures, stages and parts,
    /// the team's rationale, the validator's review log, and the lessons the prompt carried. Read-only apart from
    /// "Request revision" (another design pass) and copying the plan JSON.
    /// </summary>
    public sealed class DesignReviewWindow
    {
        private const string DialogName = "KSA_DesignReview";
        private const float Width = 720f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 680f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LabelWidth = 130f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const int WrapChars = 100;

        private readonly Agency _agency;
        private readonly string _projectId;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        public DesignReviewWindow(Agency agency, string projectId, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _projectId = projectId ?? string.Empty;
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
            Project p = _agency.GetProject(_projectId);
            DesignRecord d = p != null ? p.Design : null;
            bool inProgress = _agency.DesignInProgress(_projectId);
            KsaCore.Log.Info("Design Review window: project " + _projectId + ", design " + (d != null ? d.Verdict.ToString() : "none") + (inProgress ? " (in progress)" : ""));

            if (p == null)
            {
                Line(items, "No such project: " + _projectId, textWidth);
            }
            else
            {
                BuildHeader(items, p, d, inProgress, textWidth);
                if (d != null)
                {
                    BuildFigures(items, d, textWidth);
                    BuildStages(items, d, textWidth);
                    BuildNotes(items, d, textWidth);
                }
                BuildLessons(items, p, textWidth);
            }
            items.Add(new DialogGUISpace(SectionGap));

            items.Add(new DialogGUISpace(KsaUi.ScrollTail));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var buttons = new List<DialogGUIBase>();
            if (p != null && (p.Status == ProjectStatus.Design || p.Status == ProjectStatus.Build) && !inProgress)
                buttons.Add(new DialogGUIButton("Request revision", OnRequestRevision, 150f, 30f, false));
            if (d != null && !string.IsNullOrEmpty(d.PlanJson))
                buttons.Add(new DialogGUIButton("Copy plan JSON", OnCopyPlan, 140f, 30f, false));
            buttons.Add(new DialogGUIFlexibleSpace());
            buttons.Add(new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "The engineering team's design for this project.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, buttons.ToArray()));

            var dialog = new MultiOptionDialog(DialogName, "", "Design Review", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        // --- Header ---

        private void BuildHeader(List<DialogGUIBase> items, Project p, DesignRecord d, bool inProgress, float textWidth)
        {
            items.Add(new DialogGUILabel("<size=16><b>" + p.Name + "</b></size>  (" + p.Id + ")", textWidth, 28f));
            items.Add(Field("Status", StatusText(p.Status), textWidth));
            items.Add(Field("Lead engineer", NameOf(p.LeadEngineer) + (p.Engineers.Count > 1 ? "  (team of " + p.Engineers.Count + ")" : ""), textWidth));
            items.Add(Field("Template", _agency.Catalogs.Templates.NameOf(p.TemplateId), textWidth));

            if (d == null)
            {
                items.Add(Field("Source", "Template", textWidth));
                if (inProgress)
                    items.Add(Field("Verdict", "<color=#ffe066>Pending: team at work...</color>", textWidth));
                else
                {
                    items.Add(Field("Verdict", "Flies the template design.", textWidth));
                    Line(items, _agency.AiDesignAvailable
                        ? "No AI design session was started for this project."
                        : "AI design did not run: AI is off. Open AI Setup in the Director's Office to enable it.", textWidth);
                }
                return;
            }

            items.Add(Field("Source", d.Source == DesignSource.Ai ? "AI engineering team" : "Template", textWidth));
            items.Add(Field("Design", d.DesignName.Length > 0 ? d.DesignName : "(unnamed)", textWidth));
            items.Add(Field("Verdict", VerdictText(d, inProgress), textWidth));
            if (d.Error.Length > 0) Line(items, "<color=#ff8080>" + d.Error + "</color>", textWidth);
            if (!string.IsNullOrEmpty(d.CraftPath))
                items.Add(Field("Craft", "Saved to VAB: " + System.IO.Path.GetFileName(d.CraftPath), textWidth));
        }

        // --- Figures ---

        private void BuildFigures(List<DialogGUIBase> items, DesignRecord d, float textWidth)
        {
            Section(items, "Figures", textWidth);
            bool dvOk = d.TotalDeltaV >= d.RequiredDeltaV;
            bool twrOk = d.LiftoffTwr >= d.RequiredTwr;
            items.Add(Field("Total Δv", Colour(d.TotalDeltaV.ToString("0") + " m/s", dvOk) + "  (required " + d.RequiredDeltaV.ToString("0") + " m/s)", textWidth));
            items.Add(Field("Liftoff TWR", Colour(d.LiftoffTwr.ToString("0.00"), twrOk) + "  (required " + d.RequiredTwr.ToString("0.00") + ")", textWidth));
            items.Add(Field("Mass", d.TotalMass.ToString("0.0") + " t", textWidth));
            items.Add(Field("Cost", Agency.Money(d.TotalCost), textWidth));
            items.Add(Field("Parts", d.PartCount.ToString(), textWidth));
            items.Add(Field("Design score", Percent(d.DesignScore), textWidth));
            items.Add(Field("Passes", d.PassesUsed + " of " + d.PassesAllowed, textWidth));
            items.Add(Field("Model", d.Model.Length > 0 ? d.Model : "none", textWidth));
        }

        // --- Stages ---

        private void BuildStages(List<DialogGUIBase> items, DesignRecord d, float textWidth)
        {
            Section(items, "Stages", textWidth);
            if (d.StageSummary.Count == 0 && (d.Plan == null || d.Plan.Stages.Count == 0))
            {
                Line(items, "No stage plan recorded.", textWidth);
                return;
            }
            foreach (string s in d.StageSummary) Line(items, s, textWidth);

            if (d.Plan == null) return;
            foreach (PlanStage stage in d.Plan.Stages)
            {
                Line(items, "<b>Stage " + stage.Stage + "</b>" + (stage.Role.Length > 0 ? "  (" + stage.Role + ")" : ""), textWidth);
                foreach (PlanPart part in stage.Core)
                    Line(items, "   core: " + part.Part + " ×" + part.Count, textWidth);
                foreach (PlanPart part in stage.Radial)
                    Line(items, "   radial: " + part.Part + " ×" + part.Count + " (sym " + part.Symmetry + ")", textWidth);
                if (!string.IsNullOrEmpty(stage.Decoupler))
                    Line(items, "   decoupler: " + stage.Decoupler, textWidth);
            }
            if (d.Plan.Payload != null)
            {
                string extras = d.Plan.Payload.Extras.Count > 0 ? string.Join(", ", d.Plan.Payload.Extras.ToArray()) : "none";
                Line(items, "<b>Payload</b>: command " + (d.Plan.Payload.Command.Length > 0 ? d.Plan.Payload.Command : "none") + "  |  extras: " + extras, textWidth);
            }
        }

        // --- Notes and review log ---

        private void BuildNotes(List<DialogGUIBase> items, DesignRecord d, float textWidth)
        {
            Section(items, "Engineering notes", textWidth);
            if (d.Rationale.Length == 0) Line(items, "The team left no notes.", textWidth);
            foreach (string line in Wrap(d.Rationale, WrapChars)) Line(items, line, textWidth);

            Section(items, "Review log (" + d.Feedback.Count + ")", textWidth);
            if (d.Feedback.Count == 0) Line(items, "No review notes.", textWidth);
            foreach (string f in d.Feedback)
                foreach (string line in Wrap(f, WrapChars)) Line(items, line, textWidth);
        }

        private void BuildLessons(List<DialogGUIBase> items, Project p, float textWidth)
        {
            List<string> lessons = new List<string>();
            try { lessons = _agency.BuildDesignRequest(p).Lessons; }
            catch (Exception e) { KsaCore.Log.Warn("Design Review: could not build the design request: " + e.Message); }

            Section(items, "Lessons sent to the team (" + lessons.Count + ")", textWidth);
            if (lessons.Count == 0) Line(items, "No earlier failures on this route to learn from.", textWidth);
            foreach (string l in lessons)
                foreach (string line in Wrap(l, WrapChars)) Line(items, line, textWidth);
        }

        // --- actions ---

        private void OnRequestRevision()
        {
            string message;
            bool ok = _agency.RequestRedesign(_projectId, out message);
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Design Review: " + _status);
            else KsaCore.Log.Warn("Design Review: " + _status);
            Refresh();
        }

        private void OnCopyPlan()
        {
            Project p = _agency.GetProject(_projectId);
            string json = p != null && p.Design != null ? p.Design.PlanJson : string.Empty;
            if (string.IsNullOrEmpty(json)) { _status = "No plan JSON to copy."; Refresh(); return; }
            try
            {
                SetClipboard(json);
                _status = "Plan JSON copied to the clipboard (" + json.Length + " chars).";
            }
            catch (Exception e)
            {
                _status = "<color=#ff8080>Could not copy: " + e.Message + "</color>";
            }
            Refresh();
        }

        /// <summary>
        /// GUIUtility.systemCopyBuffer lives in UnityEngine.IMGUIModule, which this project does not reference,
        /// so the property is reached through reflection at runtime.
        /// </summary>
        private static void SetClipboard(string text)
        {
            Type t = Type.GetType("UnityEngine.GUIUtility, UnityEngine.IMGUIModule") ?? Type.GetType("UnityEngine.GUIUtility, UnityEngine");
            System.Reflection.PropertyInfo prop = t != null
                ? t.GetProperty("systemCopyBuffer", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                : null;
            if (prop == null) throw new InvalidOperationException("clipboard not available");
            prop.SetValue(null, text, null);
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private string NameOf(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return "none";
            CrewProfile c = _agency.Crew.Get(crewId);
            return c != null ? c.Name : crewId;
        }

        private static string VerdictText(DesignRecord d, bool inProgress)
        {
            switch (d.Verdict)
            {
                case DesignVerdict.Approved: return "<color=#8be28b>Approved</color>";
                case DesignVerdict.Short: return "<color=#ffb347>Short: falls short of the mission requirements; the template flies instead</color>";
                case DesignVerdict.Failed: return "<color=#ff8080>Failed: no usable plan; the template flies instead</color>";
                case DesignVerdict.Template: return "Template design";
                default: return "<color=#ffe066>Pending: team at work...</color>" + (inProgress ? "" : "  (no session running)");
            }
        }

        private static string Colour(string text, bool ok)
        {
            return ok ? "<color=#8be28b>" + text + "</color>" : "<color=#ffb347>" + text + "</color>";
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

        /// <summary>Breaks long text into lines of at most <paramref name="maxChars"/> at word boundaries, keeping explicit newlines.</summary>
        private static List<string> Wrap(string text, int maxChars)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (string paragraph in text.Replace("\r", "").Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in paragraph.Split(' '))
                {
                    if (word.Length == 0) continue;
                    if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
                    {
                        lines.Add(current);
                        current = word;
                    }
                    else current = current.Length == 0 ? word : current + " " + word;
                }
                if (current.Length > 0) lines.Add(current);
            }
            return lines;
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

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
