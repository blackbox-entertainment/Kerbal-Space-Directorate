using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Recruitment office (Jira KSA-29): run paid drives, interview applicants to reveal their stats, hire or reject them.
    /// Every action goes through the Agency and the window rebuilds itself to show the new state.
    /// </summary>
    public sealed class RecruitingWindow
    {
        private const string DialogName = "KSA_Recruiting";
        private const float Width = 680f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 640f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 100f; } }
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float ButtonHeight = 26f;
        private const float NameWidth = 190f;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        public RecruitingWindow(Agency agency, Action onClosed)
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
            List<Applicant> applicants = _agency.Applicants.Applicants;
            KsaCore.Log.Info("Recruiting window: " + applicants.Count + " applicant(s), " + _agency.Catalogs.Recruiting.Count + " source(s)");

            // --- Facts ---
            string why;
            bool allowed = _agency.HiringAllowed(out why);
            items.Add(Field("Balance", Agency.Money(_agency.Ledger.Balance), textWidth));
            items.Add(Field("Hiring", allowed ? "Open" : why, textWidth));
            items.Add(Field("Pool", applicants.Count + " of " + _agency.Settings.MaxApplicants + " applicant slots in use", textWidth));
            if (_agency.PanelBonus() > 0)
                items.Add(Field("Senior panel", "A senior crew member sits on interviews: each interview reveals two facts.", textWidth));

            // --- Drives ---
            Section(items, "Recruitment drives", textWidth);
            if (_agency.Catalogs.Recruiting.Count == 0) Line(items, "No recruitment sources configured.", textWidth);
            foreach (RecruitSource src in _agency.Catalogs.Recruiting.Sources)
            {
                string id = src.Id;
                var roles = new List<string>();
                foreach (CrewRole role in src.Roles) roles.Add(RosterView.RoleText(role));
                items.Add(new DialogGUIHorizontalLayout(false, false, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUIButton("Run drive", () => OnRunDrive(id), 110f, ButtonHeight, false),
                    new DialogGUILabel("<b>" + src.Name + "</b>  (" + string.Join(", ", roles.ToArray()) + ")", textWidth - 118f, LineHeight)));
                Line(items, "Drive " + Agency.Money(src.DriveCost) + "  |  hire fee " + Agency.Money(src.HireFee) + "  |  "
                    + src.ApplicantsPerDrive + " applicant" + (src.ApplicantsPerDrive == 1 ? "" : "s") + " per drive", textWidth);
            }

            // --- Applicants ---
            Section(items, "Applicants (" + applicants.Count + ")", textWidth);
            if (applicants.Count == 0) Line(items, "Nobody is waiting. Run a recruitment drive to attract applicants.", textWidth);
            foreach (Applicant a in applicants)
            {
                string id = a.Id;
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    KsaIcons.Badge(a.Role),
                    new DialogGUILabel("<b>" + a.Name + "</b>", NameWidth, LineHeight),
                    new DialogGUILabel(RosterView.RoleText(a.Role) + "  |  " + _agency.Catalogs.Recruiting.NameOf(a.SourceId) + "  |  arrived day " + a.ArrivedDay,
                        textWidth - KsaIcons.BadgeSize - NameWidth - 12f, LineHeight)));
                Line(items, StatsText(a), textWidth);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    InterviewButton(a, id),
                    new DialogGUIButton("Hire (" + Agency.Money(a.HireFee) + ")", () => OnHire(id), 190f, ButtonHeight, false),
                    new DialogGUIButton("Reject", () => OnReject(id), 90f, ButtonHeight, false)));
            }
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Run a drive, pay to interview applicants (or hire blind), then hire the best.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Recruiting", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        // --- Director actions: call the Agency, remember its message, rebuild ---

        private void OnRunDrive(string sourceId)
        {
            string message;
            bool ok = _agency.RunDrive(sourceId, out message);
            Report(ok, message);
        }

        private DialogGUIButton InterviewButton(Applicant a, string id)
        {
            var button = new DialogGUIButton(a.FullyRevealed ? "Interviewed" : "Interview (" + Agency.Money(_agency.InterviewCost(a)) + ")", () => OnInterview(id), 190f, ButtonHeight, false);
            button.OptionInteractableCondition = () => !a.FullyRevealed;
            return button;
        }

        private void OnInterview(string applicantId)
        {
            string message;
            bool ok = _agency.Interview(applicantId, out message);
            Report(ok, message);
        }

        private void OnHire(string applicantId)
        {
            string message;
            bool ok = _agency.Hire(applicantId, out message);
            Report(ok, message);
        }

        private void OnReject(string applicantId)
        {
            string message;
            bool ok = _agency.Reject(applicantId, out message);
            Report(ok, message);
        }

        private void Report(bool ok, string message)
        {
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Recruiting: " + _status);
            else KsaCore.Log.Warn("Recruiting: " + _status);
            Refresh();
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private static string StatsText(Applicant a)
        {
            string stars = a.IsRevealed(Applicant.RevealStars) ? RosterView.StarsText(a.Stars) : "?";
            string courage = a.IsRevealed(Applicant.RevealCourage) ? Percent(a.Courage) : "?";
            string stupidity = a.IsRevealed(Applicant.RevealStupidity) ? Percent(a.Stupidity) : "?";
            string tags = a.IsRevealed(Applicant.RevealTags) ? (a.Tags.Count > 0 ? string.Join(", ", a.Tags.ToArray()) : "none") : "?";
            string interviews = a.FullyRevealed ? "fully interviewed" : a.Interviews + " interview" + (a.Interviews == 1 ? "" : "s");
            return "Stars " + stars + "  |  courage " + courage + "  |  stupidity " + stupidity + "  |  tags: " + tags + "  |  " + interviews;
        }

        private static string Percent(float v)
        {
            return ((int)Math.Round(v * 100f)) + "%";
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
                new DialogGUILabel(text, textWidth - 16f, LineHeight)));
        }
    }
}
