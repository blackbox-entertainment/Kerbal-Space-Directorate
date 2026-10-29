using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Careers;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>Read-only dossier for one crew member (Jira KSA-23), plus the Director's "Select as Candidate" action.</summary>
    public sealed class ServiceRecordWindow
    {
        private const string DialogName = "KSA_ServiceRecord";
        private const float Width = 680f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 640f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 100f; } }
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float PortraitSize = 128f;

        private readonly Agency _agency;
        private readonly string _crewId;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        public ServiceRecordWindow(Agency agency, string crewId, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _crewId = crewId;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        /// <summary>Puts the record back in front (the roster rebuilds itself when portraits finish, which drew it on top; 2026-10-28).</summary>
        public void BringToFront() { if (_dialog != null) _dialog.transform.SetAsLastSibling(); }

        public void Open()
        {
            if (_dialog != null) return;
            PortraitRenderer.BatchReady += Refresh;
            Spawn();
        }

        public void Close()
        {
            PortraitRenderer.BatchReady -= Refresh;
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        /// <summary>Rebuilds the window in place (also runs when a portrait finishes rendering).</summary>
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
            CrewProfile p = _agency.Crew.Get(_crewId);
            if (p == null)
            {
                KsaCore.Log.Warn("Service Record: no profile for " + _crewId);
                return;
            }

            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();

            // --- Header: portrait, badge, name ---
            string rankName = _agency.Careers.RankName(p.Track, p.RankId);
            items.Add(new DialogGUIHorizontalLayout(false, false, 8f, new RectOffset(0, 0, 2, 8), TextAnchor.MiddleLeft,
                KsaIcons.PortraitImage(p.Id, PortraitSize),
                new DialogGUIVerticalLayout(false, false, 2f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                        KsaIcons.Badge(p.Role),
                        new DialogGUILabel("<size=18><b>" + p.Name + "</b></size>", textWidth - PortraitSize - 40f, 30f)),
                    new DialogGUILabel(RosterView.RoleText(p.Role) + ", " + rankName, textWidth - PortraitSize - 20f, LineHeight))));

            // --- Facts ---
            int age = p.AgeOn(_agency.CurrentDay, _agency.Clock.DaysPerYear);
            items.Add(Field("Rank", rankName + (p.Track != CareerTrack.None ? " (" + p.Track + " track)" : string.Empty), textWidth));
            items.Add(Field("Role", RosterView.RoleText(p.Role) + ", " + RosterView.StarsText(p.Stars), textWidth));
            double toNext = _agency.ToNextStar(p);
            items.Add(Field("Experience", p.Record.Experience.ToString("0.#") + " points" + (toNext > 0 ? "  |  " + toNext.ToString("0.#") + " to the next star" : "  |  top of the ladder"), textWidth));
            if (p.Record.DesignsFlown > 0 || p.Record.LabDays > 0 || p.InLab)
                items.Add(Field("Ground work", (p.Record.DesignsFlown > 0 ? p.Record.DesignsFlown + " design" + (p.Record.DesignsFlown == 1 ? "" : "s") + " flown" : "no designs flown yet")
                    + "  |  " + (p.Record.LabDays / Math.Max(1, KsaCore.Agency != null ? KsaCore.Agency.Calendar.DaysPerYear : 426)).ToString("0.0") + " years in the lab"
                    + "  |  counts toward rank: " + PromotionEngine.MissionCredit(p) + " missions", textWidth));
            string whereabouts = _agency.WhereaboutsOf(p.Id);
            items.Add(Field("Status", RosterView.StatusText(p.Status) + (p.Departed ? " (left the agency)" : string.Empty) + (whereabouts.Length > 0 ? ", " + whereabouts : string.Empty), textWidth));
            items.Add(Field("Age", age + "  |  hired day " + p.HiredDay, textWidth));
            items.Add(Field("Morale", p.Morale + " / 100  |  salary " + p.Salary + " / yr", textWidth));
            foreach (string tag in p.PersonalityTags) Line(items, "    <color=#aaaaaa>" + _agency.Settings.Personality.Describe(tag) + "</color>", textWidth);
            items.Add(Field("Traits", "courage " + Percent(p.Courage) + ", stupidity " + Percent(p.Stupidity)
                + (p.PersonalityTags.Count > 0 ? "  |  " + string.Join(", ", p.PersonalityTags.ToArray()) : string.Empty), textWidth));
            items.Add(Field("In space", FormatDays(p.Record.DaysInSpace) + ", " + p.Record.CompletedMissionCount + " missions completed", textWidth));
            int worlds = p.Record.WorldsVisited("Kerbin");
            items.Add(Field("Exploration", "farthest " + p.Record.FarthestKm.ToString("#,##0") + " km from Kerbin  |  " + p.Record.DaysOnOtherWorlds.ToString("0.#") + " days on other worlds  |  "
                + worlds + " world" + (worlds == 1 ? "" : "s") + " visited  |  longest mission " + p.Record.LongestMissionDays.ToString("0.#") + " days", textWidth));

            // --- Director action ---
            RankDefinition pending = _agency.Promotions.PendingSelection(p);
            if (pending != null)
            {
                items.Add(new DialogGUISpace(SectionGap));
                items.Add(new DialogGUIHorizontalLayout(false, false, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUIButton("Select as " + pending.Name, OnSelectClicked, 230f, 30f, false),
                    new DialogGUILabel("Director decision: promotes this " + rankName + ".", textWidth - 240f, LineHeight)));
            }

            // --- Director-awarded medals ---
            foreach (KSA.Core.Medals.MedalDefinition md in _agency.Medals.Medals)
            {
                if (md.Trigger != KSA.Core.Medals.MedalTrigger.Manual || p.Departed || p.Record.HasMedal(md.Id)) continue;
                int remaining = _agency.ManualAwardsRemaining(md.Id);
                string quota = remaining < 0 ? "no yearly limit" : remaining + " left this year";
                items.Add(new DialogGUISpace(SectionGap));
                string medalId = md.Id;
                items.Add(new DialogGUIHorizontalLayout(false, false, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUIButton("Award " + md.Name, () => OnAwardClicked(medalId), 260f, 30f, false),
                    new DialogGUILabel(remaining == 0 ? "No awards left this year." : quota + ". " + md.Description, textWidth - 270f, LineHeight)));
            }
            if (_status.Length > 0)
            {
                items.Add(new DialogGUILabel("<color=#ffd27f>" + _status + "</color>", textWidth, LineHeight));
            }

            // --- Medals ---
            Section(items, "Medals (" + p.Record.Medals.Count + ")", textWidth);
            if (p.Record.Medals.Count == 0) Line(items, "None yet.", textWidth);
            foreach (MedalAward a in p.Record.Medals)
            {
                string note = string.IsNullOrEmpty(a.Note) ? string.Empty : "  (" + a.Note + ")";
                Line(items, _agency.Medals.NameOf(a.MedalId) + ", day " + a.Day + note, textWidth);
            }

            // --- Missions ---
            Section(items, "Missions (" + p.Record.Missions.Count + ")", textWidth);
            if (p.Record.Missions.Count == 0) Line(items, "No missions flown.", textWidth);
            for (int i = p.Record.Missions.Count - 1; i >= 0; i--)
            {
                MissionEntry m = p.Record.Missions[i];
                string when = m.EndDay >= 0 ? "days " + m.StartDay + " to " + m.EndDay : "from day " + m.StartDay;
                Line(items, m.MissionId + "  " + m.Name + ": " + OutcomeText(m.Outcome) + " (" + when + ")" + (m.DirectorFlown ? " [Director-flown]" : string.Empty), textWidth);
            }

            // --- Incidents ---
            Section(items, "Incidents (" + p.Record.Incidents.Count + ")", textWidth);
            if (p.Record.Incidents.Count == 0) Line(items, "Clean record.", textWidth);
            foreach (IncidentEntry i in p.Record.Incidents)
                Line(items, i.Kind + ", day " + i.Day + ": " + i.Description, textWidth);

            // --- Quotes ---
            if (p.Record.Quotes.Count > 0)
            {
                Section(items, "Quotes", textWidth);
                foreach (string q in p.Record.Quotes) Line(items, "\"" + q + "\"", textWidth);
            }
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Service Record: " + p.Name, KsaUi.Skin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        private void OnSelectClicked()
        {
            RankDefinition granted = _agency.SelectForRank(_crewId);
            if (granted != null) KsaCore.Log.Info("Director selected " + _crewId + " as " + granted.Name);
            Refresh();
        }

        private void OnAwardClicked(string medalId)
        {
            string msg;
            _agency.AwardManual(_crewId, medalId, "Awarded by the Director", out msg);
            _status = msg;
            KsaCore.Log.Info("Service Record: " + msg);
            Refresh();
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            PortraitRenderer.BatchReady -= Refresh;
            if (_onClosed != null) _onClosed();
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

        private static string FormatDays(double days)
        {
            if (days <= 0) return "none";
            if (days < 1) return (days * 24).ToString("0.0") + " hours";
            return days.ToString("0.0") + " days";
        }

        private static string Percent(float v)
        {
            return ((int)Math.Round(v * 100f)) + "%";
        }

        private static string OutcomeText(MissionOutcome o)
        {
            switch (o)
            {
                case MissionOutcome.InProgress: return "in progress";
                case MissionOutcome.Success: return "success";
                case MissionOutcome.PartialSuccess: return "partial success";
                case MissionOutcome.Abort: return "aborted";
                case MissionOutcome.LossOfVehicle: return "loss of vehicle";
                case MissionOutcome.LossOfCrew: return "loss of crew";
                default: return o.ToString();
            }
        }
    }
}
