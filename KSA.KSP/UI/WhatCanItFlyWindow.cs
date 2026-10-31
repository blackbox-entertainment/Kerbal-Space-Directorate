using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Design;
using KSA.KSP.Parts;
using KSP.UI.Screens;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// "What can this fly?" in the VAB and SPH (2026-10-30, T: "I really like how I can build my own rocket and pick a mission
    /// that it might can complete"; "a What can this fly? in the VAB would be nice"): the craft on the editor floor, saved or
    /// not, against every mission: the ones it can fly, the near misses with the delta-v they lack, and why the rest are out.
    /// </summary>
    public sealed class WhatCanItFlyWindow
    {
        private const string DialogName = "KSA_WhatCanItFly";
        private const float Width = 640f;
        private const float LineHeight = 22f;
        private const string Green = "#8be28b";
        private const string Grey = "#888888";
        private const string Amber = "#ffd27f";

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;

        public WhatCanItFlyWindow(Agency agency, Action onClosed) { _agency = agency; _onClosed = onClosed; }

        public void Open() { if (_dialog == null) Spawn(); }

        public void Close()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Refresh() { Close(); Spawn(); }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            PlayerDesign d = null;
            try { d = KspPlayerDesigns.CurrentEditorCraft(); }
            catch (Exception e) { KsaCore.Log.Warn("What can this fly: the craft could not be read: " + e.Message); }
            if (d == null) Line(items, Color("Build a craft first: nothing is on the floor.", Grey), textWidth);
            else
            {
                Line(items, "<b>" + d.Name + "</b>: " + Math.Round(d.DeltaV).ToString("#,##0") + " m/s, lift-off TWR " + d.LiftoffTwr.ToString("0.00") + ", " + d.Seats + " seat" + (d.Seats == 1 ? "" : "s")
                    + (d.HasParachute ? ", parachutes for " + d.ChuteCapacity.ToString("0.0") + " t" : ", no main parachute") + (d.PartCount > 0 ? ", " + d.PartCount + " parts" : string.Empty)
                    + (d.Mass > 0 ? ", " + d.Mass.ToString("0.0") + " t" : string.Empty), textWidth);
                if (d.ResearchBlocker != null) Line(items, Color("Not every part is researched yet: " + d.ResearchBlocker, Amber), textWidth);
                List<Agency.MissionFit> fits = _agency.WhatCanItFly(d);
                var can = fits.FindAll(f => f.Blocker == null);
                var close = fits.FindAll(f => f.Blocker != null && f.DeltaVShort > 0 && f.DeltaVShort <= 1500);
                var rest = fits.FindAll(f => f.Blocker != null && !(f.DeltaVShort > 0 && f.DeltaVShort <= 1500));

                items.Add(new DialogGUISpace(6f));
                Line(items, "<b>It can fly (" + can.Count + ")</b>", textWidth);
                if (can.Count == 0) Line(items, Color("   nothing yet", Grey), textWidth);
                foreach (Agency.MissionFit f in can)
                    Line(items, "   " + Color(f.Mission.Name, Green) + (f.Locked != null ? Color("  (mission locked: " + f.Locked + ")", Amber) : string.Empty), textWidth);

                if (close.Count > 0)
                {
                    items.Add(new DialogGUISpace(6f));
                    Line(items, "<b>Close: a little more delta-v (" + close.Count + ")</b>", textWidth);
                    foreach (Agency.MissionFit f in close)
                        Line(items, "   " + Color(f.Mission.Name, Amber) + ": " + Math.Round(f.DeltaVShort).ToString("#,##0") + " m/s short" + (f.Blocker.Contains(" m/s") ? string.Empty : Color(" (and " + f.Blocker + ")", Grey)), textWidth);
                }

                if (rest.Count > 0)
                {
                    items.Add(new DialogGUISpace(6f));
                    Line(items, "<b>Out of reach for now (" + rest.Count + ")</b>", textWidth);
                    int shown = 0;
                    foreach (Agency.MissionFit f in rest)
                    {
                        if (shown++ >= 10) { Line(items, Color("   and " + (rest.Count - 10) + " more", Grey), textWidth); break; }
                        Line(items, "   " + Color(f.Mission.Name + ": " + f.Blocker, Grey), textWidth);
                    }
                }
                items.Add(new DialogGUISpace(6f));
                Line(items, Color("Save the craft in this game and it appears under Your designs in Projects, or launch it with a free KSA kerbal aboard to pick one of these missions on the pad.", Grey), textWidth);
            }

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, 420f), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));
            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIButton("Check again", Refresh, 120f, 30f, false),
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));
            var dialog = new MultiOptionDialog(DialogName, "", "What can this fly?", KsaUi.Skin, new Rect(0.5f, 0.5f, Width, 520f), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
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

    /// <summary>The KSA button in the VAB and SPH opens "What can this fly?".</summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public sealed class KsaEditorToolbar : MonoBehaviour
    {
        private ApplicationLauncherButton _button;
        private WhatCanItFlyWindow _window;

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(Remove);
            if (ApplicationLauncher.Ready) OnReady();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(Remove);
            if (_window != null) _window.Close();
            Remove();
        }

        private void OnReady()
        {
            if (_button != null || ApplicationLauncher.Instance == null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(OnOn, OnOff, null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, KsaToolbar.GetIcon());
        }

        private void Remove()
        {
            if (_button == null) return;
            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private void OnOn()
        {
            if (KsaCore.Agency == null) { if (_button != null) _button.SetFalse(false); return; }
            if (_window == null) _window = new WhatCanItFlyWindow(KsaCore.Agency, () => { _window = null; if (_button != null) _button.SetFalse(false); });
            _window.Open();
        }

        private void OnOff() { if (_window != null) _window.Close(); }
    }
}
