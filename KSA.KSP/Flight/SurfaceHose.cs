using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Projects;
using KSA.KSP.Parts;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// The surface hose (KSA-90, 2026-11-03, T: "I figured 100m would be about the limit of a hose connection"). In the flight scene, a
    /// crewed craft landed within range of other landed craft on the same world draws propellant, monopropellant, charge and, with
    /// Kerbalism, food, water and oxygen from them, at the pump rate, as if a hose had been run across. Donors keep a reserve of
    /// their own (agency.cfg SURFACE_LINK). Nothing runs off screen: a lander waiting out a night keeps its flight on screen.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class SurfaceHose : MonoBehaviour
    {
        private static readonly string[] Always = { "LiquidFuel", "Oxidizer", "MonoPropellant", "ElectricCharge" };
        private static readonly string[] Supplies = { "Food", "Water", "Oxygen" };
        private const double StepSeconds = 1;      // pump once a second
        private const double ReportSeconds = 120;  // and say what moved every two minutes

        private double _last = -1, _lastReport = -1;
        private readonly HashSet<uint> _linked = new HashSet<uint>();
        private readonly Dictionary<string, double> _moved = new Dictionary<string, double>();

        private void FixedUpdate()
        {
            try { Tick(); }
            catch (Exception e) { KsaCore.Log.Warn("Surface hose: " + e.Message); }
        }

        private void Tick()
        {
            Agency agency = KsaCore.Agency;
            if (agency == null) return;
            SurfaceLinkRules rules = agency.SurfaceLink;
            if (rules == null || !rules.Enabled) return;
            double now = Planetarium.GetUniversalTime();
            if (_last < 0) { _last = now; return; }
            double dt = now - _last;
            if (dt < StepSeconds) return;
            _last = now;
            dt = Math.Min(dt, 5 * StepSeconds); // a warp step moves no more than five seconds' worth

            Vessel us = FlightGlobals.ActiveVessel;
            if (us == null || !us.loaded || us.isEVA || us.GetCrewCount() == 0 || !us.LandedOrSplashed) { DropAll(us); return; }
            List<string> resources = new List<string>(Always);
            if (KspLifeSupport.KerbalismInstalled) resources.AddRange(Supplies);

            var inRange = new HashSet<uint>();
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v == us || v.isEVA || !v.loaded || !v.LandedOrSplashed || v.mainBody != us.mainBody) continue;
                // Flags, debris and rocks carry nothing worth a hose (0.8.221: the hose "connected" to the flag Verdin had just planted, 13 m away).
                if (v.vesselType == VesselType.Flag || v.vesselType == VesselType.Debris || v.vesselType == VesselType.SpaceObject) continue;
                double dist = Vector3d.Distance(v.GetWorldPos3D(), us.GetWorldPos3D());
                if (dist > rules.RangeMetres) continue;
                inRange.Add(v.persistentId);
                if (_linked.Add(v.persistentId)) Note(us, "Hose: connected to " + v.vesselName + ", " + Math.Round(dist) + " m.");
                foreach (string r in resources)
                {
                    double reserve = r == "ElectricCharge" ? rules.ChargeReserveFraction : rules.ReserveFraction;
                    double available, capacity, room;
                    Stock(v, r, out available, out capacity);
                    double have, ours; Stock(us, r, out have, out ours);
                    room = ours - have;
                    double units = SurfaceLinkRules.Share(available, capacity, reserve, room, rules.PumpUnitsPerSecond * dt);
                    if (units <= 1e-6) continue;
                    double moved = DepotOps.Move(r, v.parts, us.parts, units);
                    if (moved > 0) _moved[r] = (_moved.ContainsKey(r) ? _moved[r] : 0) + moved;
                }
            }
            // Out of range: say so once each.
            var gone = new List<uint>();
            foreach (uint id in _linked) if (!inRange.Contains(id)) gone.Add(id);
            foreach (uint id in gone) { _linked.Remove(id); Note(us, "Hose: " + NameOf(id) + " is out of range."); }

            if (_moved.Count > 0 && now - _lastReport >= ReportSeconds)
            {
                _lastReport = now;
                var parts = new List<string>();
                foreach (KeyValuePair<string, double> kv in _moved) if (kv.Value >= 0.5) parts.Add(Math.Round(kv.Value) + " " + kv.Key);
                _moved.Clear();
                if (parts.Count > 0) Note(us, "Hose: took on " + string.Join(", ", parts.ToArray()) + ".");
            }
        }

        private void DropAll(Vessel us)
        {
            if (_linked.Count == 0) return;
            _linked.Clear();
            _moved.Clear();
        }

        private static void Stock(Vessel v, string resource, out double amount, out double capacity)
        {
            amount = 0; capacity = 0;
            foreach (Part p in v.parts)
            {
                PartResource r = p.Resources != null ? p.Resources.Get(resource) : null;
                if (r == null) continue;
                amount += r.amount; capacity += r.maxAmount;
            }
        }

        private static string NameOf(uint id)
        {
            foreach (Vessel v in FlightGlobals.Vessels) if (v != null && v.persistentId == id) return v.vesselName;
            return "the other craft";
        }

        /// <summary>Into the live flight's log when this is its vessel, else the KSP log.</summary>
        private static void Note(Vessel us, string text)
        {
            KsaCore.Log.Info(text);
            KsaFlightDirector.LiveFlightState live = KsaFlightDirector.Live;
            if (live == null || KsaCore.Agency == null || us == null || live.VesselId != us.persistentId) return;
            string stamp = "T+" + KSPUtil.PrintTimeCompact(Planetarium.GetUniversalTime() - live.LaunchUT, false) + " ";
            KsaCore.Agency.LogFlightEvent(live.ProjectId, stamp + text, null);
        }
    }
}
