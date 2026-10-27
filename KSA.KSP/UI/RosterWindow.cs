using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Roster window (Jira KSA-14, KSA-32), built with KSP's PopupDialog / DialogGUI. Rebuilds itself whenever
    /// the game reports a crew change. Clicking a name opens the Service Record (KSA-23). The Director can dismiss
    /// crew here with a two-click confirmation.
    /// </summary>
    public sealed class RosterWindow
    {
        private const string DialogName = "KSA_RosterWindow";
        private const float PortraitSize = 56f;
        private const float NameWidth = 170f;
        private const float RankWidth = 160f;
        private const float RoleWidth = 90f;
        private const float StarsWidth = 60f;
        private const float MoraleWidth = 60f;
        private const float StatusWidth = 90f;
        private const float ActionWidth = 90f;
        private const float RowHeight = 22f;

        private readonly IRosterSource _source;
        private readonly Core.ILogger _log;
        private readonly Action _onClosedByUser;
        private PopupDialog _dialog;
        private ServiceRecordWindow _record;
        private bool _subscribed;
        private string _confirmDismiss;
        private string _status = string.Empty;

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
                rows.Add(new DialogGUILabel("No crew hired yet. Use Recruiting, or the Astronaut Complex.", 500f, RowHeight));
            }
            else
            {
                foreach (CrewRecord c in crew)
                {
                    string rank = "-";
                    string morale = "-";
                    bool canDismiss = false;
                    if (agency != null)
                    {
                        CrewProfile p = agency.Crew.Get(c.Name);
                        if (p != null)
                        {
                            rank = agency.Careers.RankName(p.Track, p.RankId);
                            morale = p.Morale.ToString();
                            canDismiss = !p.Departed && !p.IsFlying && p.ProjectId.Length == 0 && p.Status != CrewStatus.Dead && p.Status != CrewStatus.Assigned;
                            if (p.ProjectId.Length > 0) rank += " (on " + p.ProjectId + ")";
                        }
                    }
                    rows.Add(CrewRow(c, rank, morale, canDismiss));
                }
            }

            float width = PortraitSize + KsaIcons.BadgeSize + NameWidth + RankWidth + RoleWidth + StarsWidth + MoraleWidth + StatusWidth + ActionWidth + 40f;
            var list = new DialogGUIScrollList(new Vector2(width + 40f, 380f), false, true,
                new DialogGUIVerticalLayout(false, false, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, rows.ToArray()));

            string hint = "Click a name to open the Service Record.";
            if (agency != null && agency.State.PendingLayoffs > 0) hint += "  Layoffs required: " + agency.State.PendingLayoffs + ".";
            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(hint, width, RowHeight),
                new DialogGUILabel(_status.Length > 0 ? "<color=#ffd27f>" + _status + "</color>" : " ", width, RowHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 80f, 28f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "KSA Roster (" + crew.Count + ")", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, width + 70f, 540f), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private static DialogGUIHorizontalLayout HeaderRow()
        {
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(PortraitSize),
                new DialogGUISpace(KsaIcons.BadgeSize),
                new DialogGUILabel("<b>Name</b>", NameWidth, RowHeight),
                new DialogGUILabel("<b>Rank</b>", RankWidth, RowHeight),
                new DialogGUILabel("<b>Role</b>", RoleWidth, RowHeight),
                new DialogGUILabel("<b>Stars</b>", StarsWidth, RowHeight),
                new DialogGUILabel("<b>Morale</b>", MoraleWidth, RowHeight),
                new DialogGUILabel("<b>Status</b>", StatusWidth, RowHeight),
                new DialogGUISpace(ActionWidth));
        }

        private DialogGUIHorizontalLayout CrewRow(CrewRecord c, string rank, string morale, bool canDismiss)
        {
            string id = c.Name;
            DialogGUIBase action;
            if (!canDismiss) action = new DialogGUISpace(ActionWidth);
            else if (string.Equals(_confirmDismiss, id, StringComparison.OrdinalIgnoreCase))
                action = new DialogGUIButton("Confirm", () => ConfirmDismiss(id), ActionWidth, RowHeight, false);
            else
                action = new DialogGUIButton("Dismiss", () => AskDismiss(id), ActionWidth, RowHeight, false);

            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                KsaIcons.PortraitImage(c.Name, PortraitSize),
                KsaIcons.Badge(c.Role),
                new DialogGUIButton(c.Name, () => OpenRecord(id), NameWidth, RowHeight, false),
                new DialogGUILabel(rank, RankWidth, RowHeight),
                new DialogGUILabel(RosterView.RoleText(c.Role), RoleWidth, RowHeight),
                new DialogGUILabel(RosterView.StarsText(c.Stars), StarsWidth, RowHeight),
                new DialogGUILabel(morale, MoraleWidth, RowHeight),
                new DialogGUILabel(RosterView.StatusText(c.Status), StatusWidth, RowHeight),
                action);
        }

        private void AskDismiss(string crewId)
        {
            _confirmDismiss = crewId;
            _status = "Dismiss " + crewId + "? Click Confirm. Colleagues will lose a little morale.";
            Refresh();
        }

        private void ConfirmDismiss(string crewId)
        {
            _confirmDismiss = null;
            Agency agency = KsaCore.Agency;
            if (agency == null) return;
            string msg;
            agency.Fire(crewId, out msg);
            _status = msg;
            _log.Info("Roster: " + msg);
            Refresh();
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
            Refresh(); // a Director selection or award may have changed a rank or morale
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
