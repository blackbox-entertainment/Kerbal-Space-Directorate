using System;
using System.Collections.Generic;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Read-only roster window (Jira KSA-14), built with KSP's PopupDialog / DialogGUI.
    /// Rebuilds itself whenever the game reports a crew change, so a hire at the
    /// Astronaut Complex shows up without reopening.
    /// </summary>
    public sealed class RosterWindow
    {
        private const string DialogName = "KSA_RosterWindow";
        private const float NameWidth = 150f;
        private const float RoleWidth = 80f;
        private const float StarsWidth = 60f;
        private const float StatusWidth = 80f;

        private readonly IRosterSource _source;
        private readonly Core.ILogger _log;
        private readonly Action _onClosedByUser;
        private PopupDialog _dialog;
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
            _log.Info("Roster window: " + crew.Count + " crew");

            var rows = new List<DialogGUIBase>();
            rows.Add(Row("Name", "Role", "Stars", "Status"));
            if (crew.Count == 0)
            {
                rows.Add(new DialogGUILabel("No crew hired yet. Hire kerbals at the Astronaut Complex.", true, false));
            }
            else
            {
                foreach (CrewRecord c in crew)
                {
                    rows.Add(Row(c.Name, RosterView.RoleText(c.Role), RosterView.StarsText(c.Stars), RosterView.StatusText(c.Status)));
                }
            }

            var list = new DialogGUIScrollList(
                new Vector2(NameWidth + RoleWidth + StarsWidth + StatusWidth + 40f, 260f),
                false, true,
                new DialogGUIVerticalLayout(true, true, 2f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, rows.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 80f, 28f, true)));

            var dialog = new MultiOptionDialog(
                DialogName,
                "",
                "KSA Roster (" + crew.Count + ")",
                HighLogic.UISkin,
                new Rect(0.5f, 0.5f, NameWidth + RoleWidth + StarsWidth + StatusWidth + 70f, 340f),
                content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private static DialogGUIHorizontalLayout Row(string name, string role, string stars, string status)
        {
            return new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel(name, NameWidth, 20f),
                new DialogGUILabel(role, RoleWidth, 20f),
                new DialogGUILabel(stars, StarsWidth, 20f),
                new DialogGUILabel(status, StatusWidth, 20f));
        }

        private void OnCloseClicked()
        {
            // The button dismisses the dialog itself (dismissOnSelect = true).
            _dialog = null;
            Unsubscribe();
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
