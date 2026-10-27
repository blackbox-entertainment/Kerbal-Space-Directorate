using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Read-only roster window (Jira KSA-14), built with KSP's PopupDialog / DialogGUI.
    /// Rebuilds itself whenever the game reports a crew change, so a hire at the
    /// Astronaut Complex shows up without reopening. Clicking a name opens the Service Record (KSA-23).
    /// </summary>
    public sealed class RosterWindow
    {
        private const string DialogName = "KSA_RosterWindow";
        private const float NameWidth = 150f;
        private const float RankWidth = 140f;
        private const float RoleWidth = 80f;
        private const float StarsWidth = 60f;
        private const float StatusWidth = 80f;

        private readonly IRosterSource _source;
        private readonly Core.ILogger _log;
        private readonly Action _onClosedByUser;
        private PopupDialog _dialog;
        private ServiceRecordWindow _record;
        private bool _subscribed;

        public bool IsOpen { get { return _dialog != null; } }

        public RosterWindow(IRosterSource source, Core.ILogger log, Action onClosedByUser)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (log == null) throw new ArgumentNullException(nameof(log));
            _source = source;
            _log = log;
            _onClosedByUser = onClosedByUser;
        }

        public void Open()
        {
            if (_dialog != null) return;
            Subscribe();
            Spawn();
        }

        public void Close()
        {
            Unsubscribe();
            CloseRecord();
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        /// <summary>Rebuilds the window in place with fresh roster data.</summary>
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
            List<CrewRecord> crew = RosterView.Sort(_source.GetRoster());
            Agency agency = KsaCore.Agency;
            _log.Info("Roster window: " + crew.Count + " crew");

            var rows = new List<DialogGUIBase>();
            rows.Add(HeaderRow());
            if (crew.Count == 0)
            {
                rows.Add(new DialogGUILabel("No crew hired yet. Hire kerbals at the Astronaut Complex.", true, false));
            }
            else
            {
                foreach (CrewRecord c in crew)
                {
                    string rank = "-";
                    if (agency != null)
                    {
                        CrewProfile p = agency.Crew.Get(c.Name);
                        if (p != null) rank = agency.Careers.RankName(p.Track, p.RankId);
                    }
                    rows.Add(CrewRow(c, rank));
                }
            }

            float width = NameWidth + RankWidth + RoleWidth + StarsWidth + StatusWidth;
            var list = new DialogGUIScrollList(new Vector2(width + 40f, 260f), false, true,
                new DialogGUIVerticalLayout(true, true, 2f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, rows.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel("Click a name to open the Service Record.", true, false),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 80f, 28f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "KSA Roster (" + crew.Count + ")", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, width + 70f, 370f), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private static DialogGUIHorizontalLayout HeaderRow()
        {
            return new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>Name</b>", NameWidth, 20f),
                new DialogGUILabel("<b>Rank</b>", RankWidth, 20f),
                new DialogGUILabel("<b>Role</b>", RoleWidth, 20f),
                new DialogGUILabel("<b>Stars</b>", StarsWidth, 20f),
                new DialogGUILabel("<b>Status</b>", StatusWidth, 20f));
        }

        private DialogGUIHorizontalLayout CrewRow(CrewRecord c, string rank)
        {
            string id = c.Name;
            return new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton(c.Name, () => OpenRecord(id), NameWidth, 22f, false),
                new DialogGUILabel(rank, RankWidth, 20f),
                new DialogGUILabel(RosterView.RoleText(c.Role), RoleWidth, 20f),
                new DialogGUILabel(RosterView.StarsText(c.Stars), StarsWidth, 20f),
                new DialogGUILabel(RosterView.StatusText(c.Status), StatusWidth, 20f));
        }

        private void OpenRecord(string crewId)
        {
            Agency agency = KsaCore.Agency;
            if (agency == null)
            {
                _log.Warn("Service Record requested but no agency is loaded");
                return;
            }
            CloseRecord();
            _record = new ServiceRecordWindow(agency, crewId, OnRecordClosed);
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
            Refresh(); // a Director selection may have changed a rank
        }

        private void OnCloseClicked()
        {
            // The button dismisses the dialog itself (dismissOnSelect = true).
            _dialog = null;
            Unsubscribe();
            CloseRecord();
            if (_onClosedByUser != null) _onClosedByUser();
        }

        // --- Live refresh on crew changes ---

        private void Subscribe()
        {
            if (_subscribed) return;
            GameEvents.onKerbalAdded.Add(OnKerbalAddedOrRemoved);
            GameEvents.onKerbalRemoved.Add(OnKerbalAddedOrRemoved);
            GameEvents.onKerbalTypeChange.Add(OnKerbalTypeChange);
            GameEvents.onKerbalStatusChange.Add(OnKerbalStatusChange);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            GameEvents.onKerbalAdded.Remove(OnKerbalAddedOrRemoved);
            GameEvents.onKerbalRemoved.Remove(OnKerbalAddedOrRemoved);
            GameEvents.onKerbalTypeChange.Remove(OnKerbalTypeChange);
            GameEvents.onKerbalStatusChange.Remove(OnKerbalStatusChange);
            _subscribed = false;
        }

        private void OnKerbalAddedOrRemoved(ProtoCrewMember pcm) { Refresh(); }

        private void OnKerbalTypeChange(ProtoCrewMember pcm, ProtoCrewMember.KerbalType from, ProtoCrewMember.KerbalType to) { Refresh(); }

        private void OnKerbalStatusChange(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus from, ProtoCrewMember.RosterStatus to) { Refresh(); }
    }
}
