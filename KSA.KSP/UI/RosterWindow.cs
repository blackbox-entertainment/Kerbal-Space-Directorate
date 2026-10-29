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
        // Taller on taller screens, so a bigger roster fits without scrolling (2026-10-28: the 7th kerbal hid below the fold).
        private static float ListHeight { get { return Mathf.Clamp(Screen.height - 420f, 380f, 760f); } }

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
            if (_record != null && _record.IsOpen) _record.BringToFront(); // an open Service Record stays on top
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
                    KSA.Core.Personnel.CrewProfile prof = KsaCore.Agency != null ? KsaCore.Agency.Crew.Get(c.Name) : null;
                    if (prof != null && prof.Departed) continue; // the fallen and the retired live in the Hall of Fame
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

            float width = PortraitSize + KsaIcons.BadgeSize + NameWidth + RankWidth + RoleWidth + StarsWidth + MoraleWidth + StatusWidth + LabWidth + ActionWidth + 44f;
            var list = new DialogGUIScrollList(new Vector2(width + 40f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, KsaUi.Scrollable(rows)));

            string hint = "Click a name to open the Service Record.";
            if (agency != null && agency.State.PendingLayoffs > 0) hint += "  Layoffs required: " + agency.State.PendingLayoffs + ".";
            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(hint + (crew.Count > 6 ? "  Scroll for more." : string.Empty), width, RowHeight),
                new DialogGUILabel(_status.Length > 0 ? "<color=#ffd27f>" + _status + "</color>" : " ", width, RowHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 80f, 28f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "KSA Roster (" + crew.Count + ")", KsaUi.Skin,
                new Rect(0.5f, 0.5f, width + 70f, ListHeight + 160f), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
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

        private const float LabWidth = 60f;

        /// <summary>Scientists (and Science-track kerbals) can be put in the research lab, where they make Science each year instead of flying.</summary>
        /// <summary>
        /// Stars as the agency counts them: the higher of the game's level and KSA's experience ladder (KSA-63). The game's
        /// own figure lags (it recomputes from its flight log on its own events), so a kerbal could show a star in the
        /// Service Record and none here (fixed 2026-10-28).
        /// </summary>
        private static int StarsOf(CrewRecord c)
        {
            CrewProfile p = KsaCore.Agency != null ? KsaCore.Agency.Crew.Get(c.Name) : null;
            return p != null ? Math.Max(c.Stars, p.Stars) : c.Stars;
        }

        private DialogGUIBase LabButton(CrewRecord c)
        {
            Agency a = KsaCore.Agency;
            KSA.Core.Personnel.CrewProfile p = a != null ? a.Crew.Get(c.Name) : null;
            if (p == null || p.Departed || (p.Role != KSA.Core.Personnel.CrewRole.Scientist && p.Track != KSA.Core.Personnel.CareerTrack.Science)) return new DialogGUISpace(LabWidth);
            string id = c.Name;
            bool inLab = p.InLab;
            return new DialogGUIButton(inLab ? "[x] Lab" : "Lab", () => { string msg; a.SetLab(id, !inLab, out msg); ScreenMessages.PostScreenMessage("[KSA] " + msg, 5f, ScreenMessageStyle.UPPER_CENTER); Refresh(); }, LabWidth, RowHeight, false);
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

            DialogGUIBase lab = LabButton(c);
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                KsaIcons.PortraitImage(c.Name, PortraitSize),
                KsaIcons.Badge(c.Role),
                new DialogGUIButton(c.Name, () => OpenRecord(id), NameWidth, RowHeight, false),
                new DialogGUILabel(rank, RankWidth, RowHeight),
                new DialogGUILabel(RosterView.RoleText(c.Role), RoleWidth, RowHeight),
                new DialogGUILabel(RosterView.StarsText(StarsOf(c)), StarsWidth, RowHeight),
                new DialogGUILabel(morale, MoraleWidth, RowHeight),
                new DialogGUILabel(RosterView.StatusText(c.Status), StatusWidth, RowHeight),
                lab,
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
            PortraitRenderer.BatchReady += OnPortraitsReady;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            GameEvents.onKerbalAdded.Remove(OnKerbalAddedOrRemoved);
            GameEvents.onKerbalRemoved.Remove(OnKerbalAddedOrRemoved);
            GameEvents.onKerbalTypeChange.Remove(OnKerbalTypeChange);
            GameEvents.onKerbalStatusChange.Remove(OnKerbalStatusChange);
            PortraitRenderer.BatchReady -= OnPortraitsReady;
            _subscribed = false;
        }

        private void OnPortraitsReady()
        {
            Refresh(); // swap suit sprites for the faces that just finished rendering
        }

        private void OnKerbalAddedOrRemoved(ProtoCrewMember pcm) { Refresh(); }

        private void OnKerbalTypeChange(ProtoCrewMember pcm, ProtoCrewMember.KerbalType from, ProtoCrewMember.KerbalType to) { Refresh(); }

        private void OnKerbalStatusChange(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus from, ProtoCrewMember.RosterStatus to) { Refresh(); }
    }
}
