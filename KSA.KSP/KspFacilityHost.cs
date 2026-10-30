using System;
using System.Collections.Generic;
using KSA.Core;
using Upgradeables;

namespace KSA.KSP
{
    /// <summary>
    /// The space center's buildings for the agency (2026-10-30, T: "is the AI Director upgrading the facilities at all? the
    /// runway needs to be upgraded"). Levels are read anywhere; prices and the upgrade itself need the buildings loaded,
    /// which they are only in the Space Center scene. Career only. The agency pays; this only raises the level.
    /// </summary>
    public sealed class KspFacilityHost : IFacilityHost
    {
        private static readonly SpaceCenterFacility[] Order =
        {
            SpaceCenterFacility.LaunchPad, SpaceCenterFacility.Runway, SpaceCenterFacility.VehicleAssemblyBuilding, SpaceCenterFacility.SpaceplaneHangar,
            SpaceCenterFacility.AstronautComplex, SpaceCenterFacility.MissionControl, SpaceCenterFacility.TrackingStation,
            SpaceCenterFacility.ResearchAndDevelopment, SpaceCenterFacility.Administration
        };

        private static bool Career { get { return HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER && ScenarioUpgradeableFacilities.Instance != null; } }

        public bool CanUpgradeNow { get { return Career && HighLogic.LoadedScene == GameScenes.SPACECENTER && Live(SpaceCenterFacility.LaunchPad) != null; } }

        public List<FacilityOption> Facilities()
        {
            var list = new List<FacilityOption>();
            if (!Career) return list;
            foreach (SpaceCenterFacility f in Order)
            {
                try
                {
                    int steps = ScenarioUpgradeableFacilities.GetFacilityLevelCount(f);
                    if (steps <= 0) continue;
                    float norm = ScenarioUpgradeableFacilities.GetFacilityLevel(f);
                    int level = (int)Math.Round(norm * steps); // 0-based
                    var o = new FacilityOption { Id = f.ToString(), Title = Title(f), Level = level + 1, MaxLevel = steps + 1 };
                    o.LimitNow = Limits(f, norm);
                    if (f == SpaceCenterFacility.LaunchPad || f == SpaceCenterFacility.Runway) { float m = GameVariables.Instance != null ? GameVariables.Instance.GetCraftMassLimit(norm, f == SpaceCenterFacility.LaunchPad) : 0f; o.MassLimit = m > 0 && m < 1e6f ? m : 0; }
                    if (level < steps) o.NextLevel = Limits(f, (level + 1) / (float)steps);
                    UpgradeableFacility live = Live(f);
                    if (live != null && level < steps) o.UpgradeCost = live.GetUpgradeCost();
                    list.Add(o);
                }
                catch (Exception e) { KsaCore.Log.Warn("Facility " + f + " unreadable: " + e.Message); }
            }
            return list;
        }

        public bool RaiseLevel(string id, out string message)
        {
            message = string.Empty;
            if (!Career) { message = "buildings are upgraded only in career games"; return false; }
            SpaceCenterFacility f;
            try { f = (SpaceCenterFacility)Enum.Parse(typeof(SpaceCenterFacility), id, true); }
            catch { message = "no building called '" + id + "'"; return false; }
            UpgradeableFacility live = Live(f);
            if (live == null) { message = "buildings can be upgraded only while the Space Center is on screen; try again there"; return false; }
            int level = live.FacilityLevel;
            if (level >= live.MaxLevel) { message = Title(f) + " is at its top level"; return false; }
            try
            {
                // Every model of the building (the KSC has more than one copy of some) goes up together.
                foreach (UpgradeableFacility u in Refs(f)) u.SetLevel(level + 1);
                KsaCore.Log.Info("Facility upgrade: " + Title(f) + " to level " + (level + 2));
                return true;
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("Facility upgrade of " + f + " failed: " + e);
                message = e.Message;
                return false;
            }
        }

        private static List<UpgradeableFacility> Refs(SpaceCenterFacility f)
        {
            var refs = new List<UpgradeableFacility>();
            if (ScenarioUpgradeableFacilities.protoUpgradeables == null) return refs;
            foreach (KeyValuePair<string, ScenarioUpgradeableFacilities.ProtoUpgradeable> kv in ScenarioUpgradeableFacilities.protoUpgradeables)
            {
                if (!kv.Key.EndsWith("/" + f, StringComparison.OrdinalIgnoreCase) && !string.Equals(kv.Key, f.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                if (kv.Value != null && kv.Value.facilityRefs != null)
                    foreach (UpgradeableFacility u in kv.Value.facilityRefs) if (u != null) refs.Add(u);
            }
            return refs;
        }

        private static UpgradeableFacility Live(SpaceCenterFacility f)
        {
            List<UpgradeableFacility> refs = Refs(f);
            return refs.Count > 0 ? refs[0] : null;
        }

        private static string Title(SpaceCenterFacility f)
        {
            switch (f)
            {
                case SpaceCenterFacility.LaunchPad: return "Launch Pad";
                case SpaceCenterFacility.VehicleAssemblyBuilding: return "Vehicle Assembly Building";
                case SpaceCenterFacility.SpaceplaneHangar: return "Spaceplane Hangar";
                case SpaceCenterFacility.AstronautComplex: return "Astronaut Complex";
                case SpaceCenterFacility.MissionControl: return "Mission Control";
                case SpaceCenterFacility.TrackingStation: return "Tracking Station";
                case SpaceCenterFacility.ResearchAndDevelopment: return "Research and Development";
                default: return f.ToString();
            }
        }

        /// <summary>What the building allows at a normalised level, from the game's own rules.</summary>
        private static string Limits(SpaceCenterFacility f, float norm)
        {
            GameVariables g = GameVariables.Instance;
            if (g == null) return string.Empty;
            switch (f)
            {
                case SpaceCenterFacility.LaunchPad: return "craft up to " + Mass(g.GetCraftMassLimit(norm, true)) + ", " + Size(g.GetCraftSizeLimit(norm, true));
                case SpaceCenterFacility.Runway: return "craft up to " + Mass(g.GetCraftMassLimit(norm, false)) + ", " + Size(g.GetCraftSizeLimit(norm, false));
                case SpaceCenterFacility.VehicleAssemblyBuilding: return Parts(g.GetPartCountLimit(norm, true)) + " per rocket";
                case SpaceCenterFacility.SpaceplaneHangar: return Parts(g.GetPartCountLimit(norm, false)) + " per plane";
                case SpaceCenterFacility.AstronautComplex: return (g.UnlockedEVA(norm) ? "spacewalks anywhere" : "no spacewalks in flight") + ", " + g.GetActiveCrewLimit(norm) + " crew on staff";
                case SpaceCenterFacility.MissionControl: return g.UnlockedFlightPlanning(norm) ? "flight planning (manoeuvre nodes)" : "no flight planning";
                case SpaceCenterFacility.TrackingStation: return g.GetPatchesAheadLimit(norm) + " orbit patches ahead";
                case SpaceCenterFacility.ResearchAndDevelopment: return "research up to " + Big(g.GetScienceCostLimit(norm)) + " Science per technology";
                case SpaceCenterFacility.Administration: return g.GetActiveStrategyLimit(norm) + " strategies";
                default: return string.Empty;
            }
        }

        private static string Big(float v) { return v >= 1e6f || float.IsInfinity(v) || v == float.MaxValue ? "any" : v.ToString("0"); }
        private static string Mass(float t) { return Big(t) == "any" ? "any mass" : t.ToString("0") + " t"; }
        private static string Parts(int n) { return n >= 10000 || n == int.MaxValue ? "any number of parts" : n + " parts"; }
        private static string Size(UnityEngine.Vector3 s) { return s.x >= 1e5f || float.IsInfinity(s.x) ? "any size" : s.y.ToString("0") + " m tall, " + s.x.ToString("0") + " m wide"; }
    }
}
