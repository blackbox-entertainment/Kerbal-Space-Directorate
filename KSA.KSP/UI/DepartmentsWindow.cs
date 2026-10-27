using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Department heads (M2): the four posts, who holds them, what they do, and the Management-track kerbals who
    /// could take them. Replaces the stock Administration faces in function; the stock strategies are untouched.
    /// </summary>
    public sealed class DepartmentsWindow
    {
        private const string DialogName = "KSA_Departments";
        private const float Width = 700f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 200f, 520f, 820f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LineHeight = 22f;
        private const float ButtonHeight = 26f;
        private const string Green = "#8be28b";
        private const string Grey = "#aaaaaa";
        private const string Amber = "#ffd27f";

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;
        private Department? _choosing;

        public DepartmentsWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open() { if (_dialog == null) Spawn(); }

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
            DepartmentRules rules = _agency.Settings.Departments ?? new DepartmentRules();
            List<CrewProfile> candidates = _agency.DepartmentCandidates();

            Line(items, "Management-track kerbals run the agency's departments. A head keeps their salary, cannot fly or join a project, and gives a bonus that grows with rank and stars.", textWidth);
            if (!rules.Enabled) Line(items, Color("Department effects are disabled in agency.cfg (DEPARTMENTS enabled = false).", Amber), textWidth);
            items.Add(new DialogGUISpace(6f));

            foreach (Department d in new[] { Department.Finances, Department.Science, Department.PublicRelations, Department.Operations })
            {
                CrewProfile head = _agency.HeadOf(d);
                double rung = _agency.DepartmentRung(d);
                string holder = head != null ? "<b>" + Color(head.Name, Green) + "</b>  " + RosterView.StarsText(head.Stars) + "  " + _agency.Careers.RankName(head.Track, head.RankId) : Color("vacant", Grey);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("<b>" + DepartmentRules.Title(d) + "</b>", 200f, LineHeight),
                    new DialogGUILabel(holder, textWidth - 200f - 220f, LineHeight),
                    new DialogGUIButton(_choosing == d ? "Cancel" : (head != null ? "Replace" : "Appoint"), () => OnChoose(d), 90f, ButtonHeight, false),
                    new DialogGUIButton("Vacate", () => OnVacate(d), 80f, ButtonHeight, head != null)));
                Line(items, "    " + Color(DepartmentRules.Describe(d, rules, rung), Grey), textWidth);
                if (_choosing == d)
                {
                    if (candidates.Count == 0) Line(items, "    " + Color("No Management-track kerbal is free. Roster: select a 3-star kerbal for Program Manager first.", Amber), textWidth);
                    foreach (CrewProfile c in candidates)
                    {
                        string cid = c.Id;
                        Department? held = _agency.PostOf(cid);
                        items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                            new DialogGUISpace(24f),
                            KsaIcons.Badge(c.Role),
                            new DialogGUIButton(c.Name, () => OnAppoint(d, cid), 190f, ButtonHeight, false),
                            new DialogGUILabel(RosterView.StarsText(c.Stars) + "  |  " + _agency.Careers.RankName(c.Track, c.RankId) + (held.HasValue ? "  |  currently " + DepartmentRules.Title(held.Value) : string.Empty), textWidth - 260f, LineHeight)));
                    }
                }
                items.Add(new DialogGUISpace(4f));
            }

            items.Add(new DialogGUISpace(8f));
            Line(items, Color("The stock Administration building's strategies still work on stock Funds, Science and Reputation; KSA prestige and budget are separate.", Grey), textWidth);

            items.Add(new DialogGUISpace(KsaUi.ScrollTail));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Who runs what.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Departments", HighLogic.UISkin, new Rect(0.5f, 0.5f, Width, WindowHeight), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private void OnChoose(Department d)
        {
            _choosing = _choosing == d ? (Department?)null : d;
            Refresh();
        }

        private void OnAppoint(Department d, string crewId)
        {
            string msg;
            bool ok = _agency.Appoint(d, crewId, out msg);
            _status = ok ? "<color=" + Green + ">" + msg + "</color>" : "<color=" + Amber + ">" + msg + "</color>";
            _choosing = null;
            KsaCore.Log.Info("Departments: " + msg);
            Refresh();
        }

        private void OnVacate(Department d)
        {
            string msg;
            _agency.Vacate(d, out msg);
            _status = msg;
            Refresh();
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        private static string Color(string text, string hex) { return "<color=" + hex + ">" + text + "</color>"; }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUILabel(text, textWidth, KsaUi.LabelHeight(text, textWidth, LineHeight)));
        }
    }
}
