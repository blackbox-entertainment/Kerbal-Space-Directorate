using System;
using System.Collections.Generic;
using KSA.KSP.Flight;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Fuel stations (2026-11-01, T: "both fuel stations I would want the option to deorbit them"): every crewless craft with a docking port and fuel in orbit, what it holds,
    /// and a Deorbit button. A deorbit is an order to the station: it goes at once and is gone from the game, with no flight to watch.
    /// </summary>
    public sealed class FuelStationsWindow
    {
        private const string DialogName = "KSA_FuelStations";
        private const float Width = 620f, LineHeight = 24f;

        private readonly Action _onClosed;
        private PopupDialog _dialog;

        public FuelStationsWindow(Action onClosed) { _onClosed = onClosed; }

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
            Close();
            Spawn();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            List<Vessel> stations = TankerStopLeg.AllTankers();
            if (stations.Count == 0) items.Add(new DialogGUILabel("No fuel stations in orbit.", textWidth, LineHeight));
            foreach (Vessel v in stations)
            {
                Vessel station = v;
                string where = v.mainBody != null ? v.mainBody.bodyName : "?";
                string line = v.vesselName + "  |  " + where + "  |  " + Math.Round(v.orbit.ApA / 1000) + " x " + Math.Round(v.orbit.PeA / 1000) + " km  |  " + TankerStopLeg.Tonnes(v).ToString("0.0") + " t of fuel";
                items.Add(new DialogGUIHorizontalLayout(
                    new DialogGUILabel(line, textWidth - 120f, LineHeight),
                    new DialogGUIButton("Deorbit", () => Confirm(station), 100f, LineHeight, false)));
            }
            items.Add(new DialogGUILabel("<color=#999999>A station deorbits at once and is gone, with its fuel. There is no flight to watch.</color>", textWidth, LineHeight * 2));
            items.Add(new DialogGUIButton("Close", () => { Close(); if (_onClosed != null) _onClosed(); }, 100f, 30f, false));
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(DialogName, string.Empty, "Fuel stations", HighLogic.UISkin, Width, items.ToArray()), false, HighLogic.UISkin, false);
        }

        private void Confirm(Vessel station)
        {
            string name = station.vesselName;
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("KSA_FuelStationDeorbit", "Deorbit " + name + "? It is gone for good, with " + TankerStopLeg.Tonnes(station).ToString("0.0") + " t of fuel.", "Deorbit station", HighLogic.UISkin, 420f,
                    new DialogGUIButton("Deorbit", () => { TankerStopLeg.Deorbit(station); ScreenMessages.PostScreenMessage("[KSA] " + name + " deorbited.", 6f, ScreenMessageStyle.UPPER_CENTER); Refresh(); }, true),
                    new DialogGUIButton("Keep it", () => { }, true)), false, HighLogic.UISkin, true);
        }
    }
}
