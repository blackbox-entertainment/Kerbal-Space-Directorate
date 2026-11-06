using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.KSP.Flight;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Launch windows (0.8.231, T: "I should be able to time warp at the space center and wait for the mission window"): the next
    /// transfer window to every planet, its date and the round trip by the sums, with a button that sets a stock alarm a couple of
    /// days before the window and warps the clock there. The warp stops at the alarm; the Director launches from the VAB then and
    /// the crew waits in orbit at most those days. A wait that runs past the year's review is marked: the review counts the goals
    /// met by then. Before this the only place that showed the window was the pad, so T warped 477 days on the pad (2026-11-05).
    /// </summary>
    public sealed class LaunchWindowsWindow
    {
        private const string DialogName = "KSA_LaunchWindows";
        private const float Width = 760f, LineHeight = 24f;
        /// <summary>The warp stops this many days before the window: time to launch, meet a tanker and plan the ejection.</summary>
        public const double LeadDays = 2;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;

        public LaunchWindowsWindow(Agency agency, Action onClosed)
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

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            double now = Planetarium.GetUniversalTime(), day = TransferSupport.Day;
            int daysLeft = _agency.Calendar.DaysLeftInYear(_agency.CurrentDay);
            items.Add(new DialogGUILabel("Today: " + TransferSupport.Date(now) + ".  The government review of year " + _agency.CurrentYear + " is " + daysLeft + " days away.", textWidth, LineHeight));
            items.Add(new DialogGUISpace(6f));
            List<CelestialBody> planets = Planets();
            if (planets.Count == 0) items.Add(new DialogGUILabel("No planets found round the sun.", textWidth, LineHeight));
            foreach (CelestialBody planet in planets)
            {
                string name = planet.bodyName;
                double days = double.NaN, trip = double.NaN;
                try { days = TransferWindows.Days(name); trip = TransferWindows.RoundTrip(name); }
                catch (Exception e) { KsaCore.Log.Warn("Window for " + name + " failed: " + e.Message); }
                if (double.IsNaN(days)) { items.Add(new DialogGUILabel("<b>" + name + "</b>: no window found.", textWidth, LineHeight)); continue; }
                double windowUt = now + days * day;
                bool open = days <= _agency.Settings.WindowWaitDays;
                string line = "<b>" + name + "</b>: window in " + Math.Ceiling(days) + " d, " + TransferSupport.Date(windowUt)
                    + (double.IsNaN(trip) ? string.Empty : "; round trip about " + Math.Round(trip) + " d");
                if (open) line = "<color=#8be28b>" + line + ". Good window: launch now.</color>";
                else if (days > daysLeft) line += " <color=#ffd27f>(past the review)</color>";
                string n = name;
                double target = windowUt - LeadDays * day;
                items.Add(new DialogGUIHorizontalLayout(
                    new DialogGUILabel(line, textWidth - 130f, KsaUi.LabelHeight(line, textWidth - 130f, LineHeight)),
                    new DialogGUIButton(open ? "Now" : "Warp to it", () => WarpTo(n, target), () => !open, 110f, LineHeight, false)));
            }
            items.Add(new DialogGUISpace(6f));
            string note = "<color=#999999>Warp to it sets a stock alarm " + LeadDays + " days before the window and warps the clock there; the alarm stops the warp. "
                + "Launch from the VAB then: the crew waits in orbit a day or two for the exact time. A project already started launches by itself when its window opens. "
                + "A wait past the review counts only the goals met by then: fly the year's goals first.</color>";
            items.Add(new DialogGUILabel(note, textWidth, KsaUi.LabelHeight(note, textWidth, LineHeight)));
            items.Add(new DialogGUIButton("Close", () => { Close(); if (_onClosed != null) _onClosed(); }, 100f, 30f, false));
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(DialogName, string.Empty, "Launch windows", HighLogic.UISkin, Width, items.ToArray()), false, HighLogic.UISkin, false);
        }

        /// <summary>The worlds round the home planet's sun, nearest first; moons take their planet's window.</summary>
        private static List<CelestialBody> Planets()
        {
            var list = new List<CelestialBody>();
            CelestialBody home = FlightGlobals.GetHomeBody();
            if (home == null || home.referenceBody == null || FlightGlobals.Bodies == null) return list;
            CelestialBody sun = home.referenceBody;
            foreach (CelestialBody b in FlightGlobals.Bodies)
                if (b != null && b != home && b != sun && b.referenceBody == sun && b.orbit != null) list.Add(b);
            list.Sort((a, b) => a.orbit.semiMajorAxis.CompareTo(b.orbit.semiMajorAxis));
            return list;
        }

        private void WarpTo(string planet, double ut)
        {
            double now = Planetarium.GetUniversalTime();
            if (ut <= now + 60) { ScreenMessages.PostScreenMessage("[KSA] The " + planet + " window is here: launch now.", 6f, ScreenMessageStyle.UPPER_CENTER); return; }
            SetAlarm(planet, ut);
            try
            {
                if (TimeWarp.fetch != null)
                {
                    if (TimeWarp.WarpMode == TimeWarp.Modes.LOW)
                    {
                        if (TimeWarp.CurrentRate > 1.01f) TimeWarp.SetRate(0, true);
                        TimeWarp.fetch.Mode = TimeWarp.Modes.HIGH;
                    }
                    TimeWarp.fetch.WarpTo(ut);
                }
            }
            catch (Exception e) { KsaCore.Log.Warn("Warp to the " + planet + " window failed: " + e.Message); }
            KsaCore.Log.Info("Warping to the " + planet + " window: " + TransferSupport.Date(ut) + ", " + LeadDays + " days before it.");
            ScreenMessages.PostScreenMessage("[KSA] Warping to the " + planet + " window, " + TransferSupport.Date(ut) + ". The alarm stops the warp " + LeadDays + " days before it.", 8f, ScreenMessageStyle.UPPER_CENTER);
            Close();
            if (_onClosed != null) _onClosed();
        }

        /// <summary>A stock alarm at the time, so the warp stops there whoever runs it. A failure is logged; the warp still aims at the time.</summary>
        private static void SetAlarm(string planet, double ut)
        {
            try
            {
                var alarm = new AlarmTypeRaw
                {
                    title = "KSA: " + planet + " window",
                    description = "Launch for " + planet + " now: the transfer window opens in " + LeadDays + " days.",
                    ut = ut,
                    actions = new AlarmActions { warp = AlarmActions.WarpEnum.KillWarp, message = AlarmActions.MessageEnum.Yes }
                };
                AlarmClockScenario.AddAlarm(alarm);
            }
            catch (Exception e) { KsaCore.Log.Warn("Could not set the " + planet + " window alarm: " + e.Message); }
        }
    }
}
