using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Goals;
using KSA.Core.Review;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// The government's year-end report card (Jira KSA-28): the latest review, the history of earlier ones,
    /// and the program goals currently on the table with a live met/open marker.
    /// </summary>
    public sealed class GovernmentReviewWindow
    {
        private const string DialogName = "KSA_GovernmentReview";
        private const float Width = 680f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 640f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 100f; } }
        private const float LabelWidth = 120f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        public GovernmentReviewWindow(Agency agency, Action onClosed)
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
            int daysLeft = _agency.Calendar.DaysLeftInYear(_agency.CurrentDay);
            KsaCore.Log.Info("Government Review window: " + _agency.Reviews.Count + " review(s), " + _agency.ActiveGoals.Count + " active goal(s)");

            // --- Where we are in the fiscal year ---
            items.Add(Field("Fiscal year", _agency.CurrentYear + ", day " + _agency.DayOfYear + " of " + _agency.Calendar.DaysPerYear
                + "  |  " + daysLeft + " day" + (daysLeft == 1 ? "" : "s") + " until the next review", textWidth));
            items.Add(Field("Agency", _agency.State.DirectorTitle + "  |  prestige " + Math.Round(_agency.State.Prestige) + " / 100  |  balance " + Agency.Money(_agency.Ledger.Balance), textWidth));
            string standing = StandingText();
            if (standing.Length > 0) items.Add(Field("Standing", standing, textWidth));

            if (_agency.Reviews.Count == 0)
            {
                Section(items, "No review yet", textWidth);
                Line(items, "The government has not reviewed the agency yet. The first Government Review comes at the end of fiscal year " + _agency.CurrentYear + ".", textWidth);
                Line(items, "Meet the program goals below before then to earn a bigger grant.", textWidth);
            }
            else
            {
                ReviewResult r = _agency.Reviews[_agency.Reviews.Count - 1];
                Section(items, "Report card: fiscal year " + r.Year, textWidth);
                items.Add(Field("Verdict", "<b>" + r.BandText + "</b>", textWidth));
                items.Add(Field("Goals", r.GoalsMet + " of " + r.GoalsIssued + " met", textWidth));
                items.Add(Field("Deaths", r.Deaths == 0 ? "none" : r.Deaths.ToString(), textWidth));
                items.Add(Field("Budget", PercentText(r.BudgetChangePercent), textWidth));
                items.Add(Field("Next grant", Agency.Money(r.NextGrant), textWidth));
                items.Add(Field("Prestige", Math.Round(r.PrestigeBefore) + " -> " + Math.Round(r.PrestigeAfter), textWidth));
                items.Add(Field("Probation", YesNo(r.ProbationBefore) + " -> " + YesNo(r.ProbationAfter), textWidth));
                items.Add(Field("Flags", FlagsText(r), textWidth));

                Section(items, "Goals met (" + r.MetGoals.Count + ")", textWidth);
                if (r.MetGoals.Count == 0) Line(items, "None.", textWidth);
                foreach (string g in r.MetGoals) Line(items, "[Met] " + g, textWidth);

                Section(items, "Goals missed (" + r.MissedGoals.Count + ")", textWidth);
                if (r.MissedGoals.Count == 0) Line(items, "None.", textWidth);
                foreach (string g in r.MissedGoals) Line(items, "[Missed] " + g, textWidth);

                Section(items, "Events", textWidth);
                if (r.Events.Count == 0) Line(items, "A quiet year.", textWidth);
                foreach (string e in r.Events) Line(items, e, textWidth);

                if (_agency.Reviews.Count > 1)
                {
                    Section(items, "History", textWidth);
                    for (int i = _agency.Reviews.Count - 2; i >= 0; i--)
                    {
                        ReviewResult h = _agency.Reviews[i];
                        Line(items, "Year " + h.Year + ": " + h.BandText + ", " + h.GoalsMet + "/" + h.GoalsIssued + " goals, budget " + PercentText(h.BudgetChangePercent), textWidth);
                    }
                }
            }

            // --- Goals on the table this year ---
            Section(items, "Program goals for year " + _agency.CurrentYear + " (" + _agency.ActiveGoals.Count + ")", textWidth);
            if (_agency.ActiveGoals.Count == 0) Line(items, "No goals issued.", textWidth);
            foreach (GoalState gs in _agency.ActiveGoals)
            {
                bool met = _agency.IsGoalCurrentlyMet(gs);
                GoalDefinition def = _agency.Catalogs.Goals.Get(gs.GoalId);
                string name = def != null ? def.Name : gs.GoalId;
                string issued = gs.YearIssued > 0 && gs.YearIssued != _agency.CurrentYear ? "  (carried over from year " + gs.YearIssued + ")" : string.Empty;
                Line(items, (met ? "[Met]  " : "[Open]  ") + name + issued, textWidth);
                if (def != null && def.Description.Length > 0) Line(items, "        " + def.Description, textWidth);
            }
            items.Add(new DialogGUISpace(SectionGap));

            items.Add(new DialogGUISpace(KsaUi.ScrollTail));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Program goals are scored at the end of every fiscal year.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Government Review", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        private string StandingText()
        {
            var parts = new List<string>();
            if (_agency.State.Probation) parts.Add("Director on probation");
            if (_agency.State.HiringFreeze) parts.Add("hiring frozen");
            if (_agency.State.PendingLayoffs > 0) parts.Add(_agency.State.PendingLayoffs + " layoff" + (_agency.State.PendingLayoffs == 1 ? "" : "s") + " required");
            return string.Join(", ", parts.ToArray());
        }

        private static string FlagsText(ReviewResult r)
        {
            var parts = new List<string>();
            if (r.DirectorReplaced) parts.Add("Director replaced");
            if (r.HiringFreeze) parts.Add("hiring freeze");
            if (r.Layoffs > 0) parts.Add(r.Layoffs + " layoff" + (r.Layoffs == 1 ? "" : "s") + " demanded");
            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }

        private static string PercentText(double percent)
        {
            return (percent >= 0 ? "+" : "") + percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        private static string YesNo(bool value)
        {
            return value ? "yes" : "no";
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
