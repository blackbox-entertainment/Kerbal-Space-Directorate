using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Everyone who has left the agency: the fallen first, then the retired, the resigned and the dismissed.
    /// Clicking a name opens their Service Record, which the agency keeps for good.
    /// </summary>
    public sealed class HallOfFameWindow
    {
        private const string DialogName = "KSA_HallOfFame";
        private const float Width = 680f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 640f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 100f; } }
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float PortraitSize = 40f;
        private const float NameWidth = 160f;
        private const float RankWidth = 140f;
        private const float YearsWidth = 55f;
        private const float MedalsWidth = 70f;
        private const float CauseWidth = 100f;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private ServiceRecordWindow _record;
        private string _status = string.Empty;

        public HallOfFameWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            PortraitRenderer.BatchReady += OnPortraitsReady;
            Spawn();
        }

        public void Close()
        {
            PortraitRenderer.BatchReady -= OnPortraitsReady;
            CloseRecord();
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

            var fallen = new List<CrewProfile>();
            var alumni = new List<CrewProfile>();
            foreach (CrewProfile p in _agency.Crew.All)
            {
                if (!p.Departed) continue;
                if (p.DepartureReason == DepartureReason.Died) fallen.Add(p);
                else alumni.Add(p);
            }
            fallen.Sort(ByDeparture);
            alumni.Sort(ByDeparture);
            KsaCore.Log.Info("Hall of Fame window: " + fallen.Count + " in memoriam, " + alumni.Count + " alumni");

            if (fallen.Count == 0 && alumni.Count == 0)
            {
                items.Add(new DialogGUILabel("Nobody has left the agency yet.", textWidth, LineHeight));
            }
            else
            {
                items.Add(HeaderRow());
                Section(items, "In Memoriam (" + fallen.Count + ")", textWidth);
                if (fallen.Count == 0) Line(items, "No crew lost in the line of duty.", textWidth);
                foreach (CrewProfile p in fallen) items.Add(CrewRow(p));

                Section(items, "Alumni (" + alumni.Count + ")", textWidth);
                if (alumni.Count == 0) Line(items, "No retirements or resignations yet.", textWidth);
                foreach (CrewProfile p in alumni) items.Add(CrewRow(p));
            }
            items.Add(new DialogGUISpace(SectionGap));

            items.Add(new DialogGUISpace(KsaUi.ScrollTail));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Click a name to open the Service Record.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Hall of Fame", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
            KsaUi.MakeOpaque(_dialog);
        }

        private static DialogGUIHorizontalLayout HeaderRow()
        {
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(PortraitSize),
                new DialogGUISpace(KsaIcons.BadgeSize),
                new DialogGUILabel("<b>Name</b>", NameWidth, LineHeight),
                new DialogGUILabel("<b>Final rank</b>", RankWidth, LineHeight),
                new DialogGUILabel("<b>Years</b>", YearsWidth, LineHeight),
                new DialogGUILabel("<b>Medals</b>", MedalsWidth, LineHeight),
                new DialogGUILabel("<b>Left</b>", CauseWidth, LineHeight));
        }

        private DialogGUIHorizontalLayout CrewRow(CrewProfile p)
        {
            string id = p.Id;
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                KsaIcons.PortraitImage(p.Id, PortraitSize),
                KsaIcons.Badge(p.Role),
                new DialogGUIButton(p.Name, () => OpenRecord(id), NameWidth, LineHeight, false),
                new DialogGUILabel(_agency.Careers.RankName(p.Track, p.RankId), RankWidth, LineHeight),
                new DialogGUILabel(YearsOfService(p).ToString(), YearsWidth, LineHeight),
                new DialogGUILabel(p.Record.Medals.Count.ToString(), MedalsWidth, LineHeight),
                new DialogGUILabel(CauseText(p.DepartureReason), CauseWidth, LineHeight));
        }

        private int YearsOfService(CrewProfile p)
        {
            int daysPerYear = _agency.Calendar.DaysPerYear;
            if (daysPerYear <= 0) return 0;
            int end = p.DepartedDay >= 0 ? p.DepartedDay : _agency.CurrentDay;
            int days = end - p.HiredDay;
            return days < 0 ? 0 : days / daysPerYear;
        }

        private static string CauseText(DepartureReason reason)
        {
            switch (reason)
            {
                case DepartureReason.Died: return "Killed in service";
                case DepartureReason.Retired: return "Retired";
                case DepartureReason.Quit: return "Resigned";
                case DepartureReason.Fired: return "Dismissed";
                default: return "Left";
            }
        }

        private static int ByDeparture(CrewProfile a, CrewProfile b)
        {
            int byDay = b.DepartedDay.CompareTo(a.DepartedDay); // most recent first
            return byDay != 0 ? byDay : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        // --- Service Record: one open at a time ---

        private void OpenRecord(string crewId)
        {
            CloseRecord();
            _record = new ServiceRecordWindow(_agency, crewId, OnRecordClosed);
            _record.Open();
        }

        private void CloseRecord()
        {
            if (_record == null) return;
            ServiceRecordWindow r = _record;
            _record = null;
            r.Close();
        }

        private void OnRecordClosed()
        {
            _record = null;
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            PortraitRenderer.BatchReady -= OnPortraitsReady;
            CloseRecord();
            if (_onClosed != null) _onClosed();
        }

        private void OnPortraitsReady()
        {
            Refresh(); // dead crew are still on the game roster, so their faces render too
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

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
