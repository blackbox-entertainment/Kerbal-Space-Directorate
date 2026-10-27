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
        private const float Width = 520f;
        private const float LabelWidth = 110f;

        private readonly Agency _agency;
        private readonly string _crewId;
        private readonly Action _onClosed;
        private PopupDialog _dialog;

        public ServiceRecordWindow(Agency agency, string crewId, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _crewId = crewId;
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
            CrewProfile p = _agency.Crew.Get(_crewId);
            if (p == null)
            {
                KsaCore.Log.Warn("Service Record: no profile for " + _crewId);
                return;
            }

            var items = new List<DialogGUIBase>();

            // --- Header ---
            string rankName = _agency.Careers.RankName(p.Track, p.RankId);
            int age = p.AgeOn(_agency.CurrentDay, _agency.Clock.DaysPerYear);
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 2, 6), TextAnchor.MiddleLeft,
                KsaIcons.PortraitImage(p.Id, 96f),
                KsaIcons.Badge(p.Role),
                new DialogGUILabel("<size=16><b>" + p.Name + "</b></size>\n" + RosterView.RoleText(p.Role) + ", " + rankName, true, false)));
            items.Add(Field("Rank", rankName + (p.Track != CareerTrack.None ? " (" + p.Track + " track)" : string.Empty)));
            items.Add(Field("Role", RosterView.RoleText(p.Role) + ", " + RosterView.StarsText(p.Stars)));
            items.Add(Field("Status", RosterView.StatusText(p.Status) + (p.Departed ? " (left the agency)" : string.Empty)));
            items.Add(Field("Age", age + "  |  hired day " + p.HiredDay));
            items.Add(Field("Morale", p.Morale + " / 100  |  salary " + p.Salary + " / yr"));
            items.Add(Field("Traits", "courage " + Percent(p.Courage) + ", stupidity " + Percent(p.Stupidity)
                + (p.PersonalityTags.Count > 0 ? "  |  " + string.Join(", ", p.PersonalityTags.ToArray()) : string.Empty)));
            items.Add(Field("In space", FormatDays(p.Record.DaysInSpace) + ", " + p.Record.CompletedMissionCount + " missions completed"));

            // --- Director action ---
            RankDefinition pending = _agency.Promotions.PendingSelection(p);
            if (pending != null)
            {
                items.Add(new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 6, 6), TextAnchor.MiddleLeft,
                    new DialogGUIButton("Select as " + pending.Name, OnSelectClicked, 220f, 28f, false),
                    new DialogGUILabel("Director decision: promotes this " + rankName + ".", true, false)));
            }

            // --- Medals ---
            items.Add(Section("Medals (" + p.Record.Medals.Count + ")"));
            if (p.Record.Medals.Count == 0) items.Add(new DialogGUILabel("  None yet.", true, false));
            foreach (MedalAward a in p.Record.Medals)
            {
                string note = string.IsNullOrEmpty(a.Note) ? string.Empty : "  (" + a.Note + ")";
                items.Add(new DialogGUILabel("  " + _agency.Medals.NameOf(a.MedalId) + ", day " + a.Day + note, true, false));
            }

            // --- Missions ---
            items.Add(Section("Missions (" + p.Record.Missions.Count + ")"));
            if (p.Record.Missions.Count == 0) items.Add(new DialogGUILabel("  No missions flown.", true, false));
            for (int i = p.Record.Missions.Count - 1; i >= 0; i--)
            {
                MissionEntry m = p.Record.Missions[i];
                string when = m.EndDay >= 0 ? "days " + m.StartDay + " to " + m.EndDay : "from day " + m.StartDay;
                items.Add(new DialogGUILabel("  " + m.Name + ": " + OutcomeText(m.Outcome) + " (" + when + ")" + (m.DirectorFlown ? " [Director-flown]" : string.Empty), true, false));
            }

            // --- Incidents ---
            items.Add(Section("Incidents (" + p.Record.Incidents.Count + ")"));
            if (p.Record.Incidents.Count == 0) items.Add(new DialogGUILabel("  Clean record.", true, false));
            foreach (IncidentEntry i in p.Record.Incidents)
                items.Add(new DialogGUILabel("  " + i.Kind + ", day " + i.Day + ": " + i.Description, true, false));

            // --- Quotes ---
            if (p.Record.Quotes.Count > 0)
            {
                items.Add(Section("Quotes"));
                foreach (string q in p.Record.Quotes) items.Add(new DialogGUILabel("  \"" + q + "\"", true, false));
            }

            var list = new DialogGUIScrollList(new Vector2(Width - 20f, 360f), false, true,
                new DialogGUIVerticalLayout(true, true, 2f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 80f, 28f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Service Record: " + p.Name, HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, 440f), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private void OnSelectClicked()
        {
            RankDefinition granted = _agency.SelectForRank(_crewId);
            if (granted != null) KsaCore.Log.Info("Director selected " + _crewId + " as " + granted.Name);
            Refresh();
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        private static DialogGUIHorizontalLayout Field(string label, string value)
        {
            return new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>" + label + "</b>", LabelWidth, 20f),
                new DialogGUILabel(value, true, false));
        }

        private static DialogGUILabel Section(string title)
        {
            return new DialogGUILabel("\n<b>" + title + "</b>", true, false);
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
