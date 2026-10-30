using System;
using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// What the space center's buildings hold back, and the upgrades that would free it (2026-10-30, T: "the AI Director
    /// needs to do upgrades": a 29.7 t rocket could not use the 18 t launch pad and the Director never thought of it).
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>A building whose next level would open something, and what.</summary>
        public sealed class UpgradeNeed
        {
            public FacilityOption Facility { get; set; }
            public List<string> Unlocks { get; } = new List<string>();
        }

        /// <summary>The heaviest craft the launch pad (or runway) takes, tonnes; 0 when there is no limit or it is not known.</summary>
        public double LaunchMassLimit(bool runway)
        {
            FacilityOption f = Facility(runway ? "Runway" : "LaunchPad");
            return f != null ? f.MassLimit : 0;
        }

        public FacilityOption Facility(string id)
        {
            if (FacilityHost == null) return null;
            foreach (FacilityOption f in FacilityHost.Facilities()) if (string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /// <summary>Missions whose lightest engineers' rocket is too heavy for the pad: mission id -> rocket mass, tonnes.</summary>
        private readonly Dictionary<string, double> _tooHeavy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Null when the pad takes a rocket of this mass; else the reason, naming the building to upgrade.</summary>
        internal string PadBlocker(double mass)
        {
            double limit = LaunchMassLimit(false);
            if (limit <= 0 || mass <= limit) return null;
            FacilityOption pad = Facility("LaunchPad");
            return "its lightest rocket weighs " + Math.Round(mass, 1) + " t and the " + (pad != null ? pad.Title : "launch pad") + " takes " + Math.Round(limit) + " t: upgrade the " + (pad != null ? pad.Title : "launch pad");
        }

        /// <summary>
        /// Null when the project's craft can leave its pad or runway; else why not and which building to upgrade (2026-10-30,
        /// T: "the AI Director should not be launching rockets or planes that cannot be launched ... it has to do the upgrades first").
        /// </summary>
        public string ProjectSiteBlocker(Project p)
        {
            if (p == null || p.Design == null || p.Design.TotalMass <= 0) return null;
            bool plane = p.Design.IsPlane;
            double limit = LaunchMassLimit(plane);
            if (limit <= 0 || p.Design.TotalMass <= limit) return null;
            FacilityOption f = Facility(plane ? "Runway" : "LaunchPad");
            string title = f != null ? f.Title : (plane ? "Runway" : "Launch Pad");
            return "the craft weighs " + Math.Round(p.Design.TotalMass, 1) + " t and the " + title + " takes " + Math.Round(limit) + " t: upgrade the " + title;
        }

        internal void NoteTooHeavy(string missionId, double mass)
        {
            if (string.IsNullOrEmpty(missionId)) return;
            if (mass > 0) _tooHeavy[missionId] = mass; else _tooHeavy.Remove(missionId);
        }

        /// <summary>
        /// Buildings in the way, with what their next level opens: missions the engineers' rockets are too heavy for, and the
        /// human Director's saved craft too heavy for the pad or runway. Empty when nothing is held back.
        /// </summary>
        public List<UpgradeNeed> UpgradeNeeds()
        {
            var needs = new List<UpgradeNeed>();
            if (FacilityHost == null) return needs;
            UpgradeNeed pad = null, runway = null;
            FacilityOption padF = Facility("LaunchPad"), runwayF = Facility("Runway");
            foreach (KeyValuePair<string, double> kv in _tooHeavy)
            {
                MissionType t = Catalogs.Missions.Get(kv.Key);
                if (t == null || padF == null || padF.Level >= padF.MaxLevel) continue;
                if (pad == null) pad = new UpgradeNeed { Facility = padF };
                pad.Unlocks.Add(t.Name + " (" + Math.Round(kv.Value, 1) + " t)");
            }
            foreach (Project p in Projects)
            {
                if (!p.IsActive || p.Status == ProjectStatus.InFlight || ProjectSiteBlocker(p) == null) continue;
                bool plane = p.Design.IsPlane;
                FacilityOption f = plane ? runwayF : padF;
                if (f == null || f.Level >= f.MaxLevel) continue;
                UpgradeNeed n = plane ? (runway ?? (runway = new UpgradeNeed { Facility = runwayF })) : (pad ?? (pad = new UpgradeNeed { Facility = padF }));
                n.Unlocks.Add("project " + p.Name + " (" + Math.Round(p.Design.TotalMass, 1) + " t, built and waiting)");
            }
            foreach (PlayerDesign d in PlayerDesigns())
            {
                if (SiteBlocker(d) == null) continue;
                if (d.IsPlane)
                {
                    if (runwayF == null || runwayF.Level >= runwayF.MaxLevel) continue;
                    if (runway == null) runway = new UpgradeNeed { Facility = runwayF };
                    runway.Unlocks.Add("your plane '" + d.Name + "' (" + Math.Round(d.Mass, 1) + " t)");
                }
                else
                {
                    if (padF == null || padF.Level >= padF.MaxLevel) continue;
                    if (pad == null) pad = new UpgradeNeed { Facility = padF };
                    pad.Unlocks.Add("your rocket '" + d.Name + "' (" + Math.Round(d.Mass, 1) + " t)");
                }
            }
            if (pad != null) needs.Add(pad);
            if (runway != null) needs.Add(runway);
            return needs;
        }

        /// <summary>Money the agency should keep after an upgrade: half a year of salaries.</summary>
        private double UpgradeReserve()
        {
            double salaries = 0;
            foreach (CrewProfile c in Crew.Active) salaries += c.Salary;
            return salaries / 2;
        }

        /// <summary>True when the building is in the way and paying for it still leaves the reserve.</summary>
        public bool UpgradeAffordable(FacilityOption f)
        {
            return f != null && f.CanUpgrade && Ledger.Balance - f.UpgradeCost >= UpgradeReserve();
        }

        private int _upgradeWakeDay = -1;

        /// <summary>From Tick: an upgrade that is due, affordable and possible right now earns the Director a turn (once a day).</summary>
        private void UpgradeTick()
        {
            if (!State.AiDirector || FacilityHost == null || !FacilityHost.CanUpgradeNow || _upgradeWakeDay == CurrentDay) return;
            foreach (UpgradeNeed n in UpgradeNeeds())
            {
                if (!UpgradeAffordable(n.Facility)) continue;
                _upgradeWakeDay = CurrentDay;
                RequestDirectorTurn("the " + n.Facility.Title + " is holding back " + string.Join(", ", n.Unlocks.ToArray()) + " and the agency can afford the upgrade now");
                return;
            }
        }
    }
}
