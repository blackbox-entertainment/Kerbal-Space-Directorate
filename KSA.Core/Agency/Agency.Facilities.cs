using System;
using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Goals;
using KSA.Core.Parts;
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
            if (p == null || p.Design == null) return null;
            bool plane = p.Design.IsPlane;
            string parts = p.Design.PartCount > 0 ? PartsBlocker(p.Design.PartCount, plane) : null;
            if (parts != null) return parts;
            if (p.Design.TotalMass <= 0) return null;
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

        // ------------------------------------------------------------------ the other buildings' limits
        // 2026-10-30, T: "ai director needs to take building limits into account" (Mission Control, R&D, Astronaut Complex,
        // VAB and Tracking Station at level 1). Each rule names the building whose upgrade lifts it.

        /// <summary>Missions whose engineers' rocket has more parts than the VAB allows: mission id -> part count.</summary>
        private readonly Dictionary<string, int> _tooManyParts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The most parts the VAB (or SPH) lets a craft have; 0 when there is no limit or it is not known.</summary>
        public int PartLimit(bool plane)
        {
            FacilityOption f = Facility(plane ? "SpaceplaneHangar" : "VehicleAssemblyBuilding");
            return f != null ? f.PartLimit : 0;
        }

        /// <summary>Null when the VAB (or SPH) allows a craft of this many parts; else why not and what to upgrade.</summary>
        internal string PartsBlocker(int parts, bool plane)
        {
            int limit = PartLimit(plane);
            if (limit <= 0 || parts <= limit) return null;
            FacilityOption f = Facility(plane ? "SpaceplaneHangar" : "VehicleAssemblyBuilding");
            string title = f != null ? f.Title : (plane ? "Spaceplane Hangar" : "Vehicle Assembly Building");
            return (plane ? "the plane has " : "the rocket has ") + parts + " parts and the " + title + " allows " + limit + ": upgrade the " + title;
        }

        internal void NoteTooManyParts(string missionId, int parts)
        {
            if (string.IsNullOrEmpty(missionId)) return;
            if (parts > 0) _tooManyParts[missionId] = parts; else _tooManyParts.Remove(missionId);
        }

        /// <summary>Kerbals on staff the Astronaut Complex counts (everyone not departed, dead or missing).</summary>
        private int StaffCount()
        {
            int n = 0;
            foreach (CrewProfile c in Crew.Active) if (c.Status != CrewStatus.Dead && c.Status != CrewStatus.Missing) n++;
            return n;
        }

        /// <summary>Null when the Astronaut Complex has room for one more kerbal; else why not.</summary>
        public string CrewRoomBlocker()
        {
            FacilityOption f = Facility("AstronautComplex");
            if (f == null || f.CrewLimit <= 0) return null;
            int n = StaffCount();
            if (n < f.CrewLimit) return null;
            return "the " + f.Title + " holds " + f.CrewLimit + " active kerbals and the agency has " + n + ": upgrade the " + f.Title + " before hiring";
        }

        /// <summary>Null unless the mission needs a spacewalk in flight and the Astronaut Complex does not allow one yet.</summary>
        public string EvaBlocker(MissionType type)
        {
            if (type == null || !type.Achievements.Exists(a => a.StartsWith("EVA:", StringComparison.OrdinalIgnoreCase))) return null;
            FacilityOption f = Facility("AstronautComplex");
            if (f == null || !f.NoEva) return null;
            return "needs a spacewalk, and the " + f.Title + " at level " + f.Level + " allows them only on Kerbin's surface: upgrade the " + f.Title;
        }

        /// <summary>
        /// Null unless a live flight to another world would need flight planning the buildings do not give yet: manoeuvre
        /// nodes (Mission Control) and predicted encounters (Tracking Station). Such flights are simulated until then.
        /// </summary>
        public string PlanningBlocker(MissionType type)
        {
            if (type == null || string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase)) return null;
            FacilityOption mc = Facility("MissionControl"), ts = Facility("TrackingStation");
            var need = new List<string>();
            if (mc != null && mc.NoFlightPlanning) need.Add("manoeuvre nodes (upgrade " + mc.Title + ")");
            if (ts != null && ts.NoPatchedConics) need.Add("predicted encounters (upgrade the " + ts.Title + ")");
            if (need.Count == 0) return null;
            return "Flying live to " + type.Body + " needs " + string.Join(" and ", need.ToArray()) + "; until then it is simulated.";
        }

        /// <summary>Null when R&amp;D can research a technology of this cost; else why not.</summary>
        public string ResearchLimitBlocker(double scienceCost)
        {
            FacilityOption f = Facility("ResearchAndDevelopment");
            if (f == null || f.ScienceLimit <= 0 || scienceCost <= f.ScienceLimit) return null;
            return "costs " + Math.Round(scienceCost) + " Science and " + f.Title + " at level " + f.Level + " researches up to " + Math.Round(f.ScienceLimit) + ": upgrade " + f.Title;
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

            // Too many parts for the VAB: the engineers' rockets, built projects and the Director's own rockets.
            FacilityOption vabF = Facility("VehicleAssemblyBuilding");
            if (vabF != null && vabF.Level < vabF.MaxLevel && vabF.PartLimit > 0)
            {
                var vab = new UpgradeNeed { Facility = vabF };
                foreach (KeyValuePair<string, int> kv in _tooManyParts) { MissionType t = Catalogs.Missions.Get(kv.Key); if (t != null) vab.Unlocks.Add(t.Name + " (" + kv.Value + " parts)"); }
                foreach (Project p in Projects)
                    if (p.IsActive && p.Status != ProjectStatus.InFlight && p.Design != null && !p.Design.IsPlane && PartsBlocker(p.Design.PartCount, false) != null)
                        vab.Unlocks.Add("project " + p.Name + " (" + p.Design.PartCount + " parts, built and waiting)");
                foreach (PlayerDesign d in PlayerDesigns()) if (!d.IsPlane && PartsBlocker(d.PartCount, false) != null) vab.Unlocks.Add("your rocket '" + d.Name + "' (" + d.PartCount + " parts)");
                if (vab.Unlocks.Count > 0) needs.Add(vab);
            }

            // A full Astronaut Complex while a role is short; spacewalks a goal this year needs.
            FacilityOption acF = Facility("AstronautComplex");
            if (acF != null && acF.Level < acF.MaxLevel)
            {
                var ac = new UpgradeNeed { Facility = acF };
                if (CrewRoomBlocker() != null)
                {
                    var shortRoles = new List<string>();
                    foreach (CrewRole r in new[] { CrewRole.Pilot, CrewRole.Engineer, CrewRole.Scientist }) if (HireBlocker(r) == null) shortRoles.Add(r.ToString().ToLowerInvariant() + "s");
                    if (shortRoles.Count > 0) ac.Unlocks.Add("hiring (" + StaffCount() + " of " + acF.CrewLimit + " on staff; short of " + string.Join(", ", shortRoles.ToArray()) + ")");
                }
                if (acF.NoEva)
                    foreach (GoalState gs in ActiveGoals)
                    {
                        GoalDefinition g = Catalogs.Goals.Get(gs.GoalId);
                        if (g != null && gs.Status == GoalStatus.Active && g.Pattern.StartsWith("EVA:", StringComparison.OrdinalIgnoreCase)) ac.Unlocks.Add("goal " + g.Name + " (spacewalks in flight)");
                    }
                if (ac.Unlocks.Count > 0) needs.Add(ac);
            }

            // Research R&D cannot do yet.
            FacilityOption rdF = Facility("ResearchAndDevelopment");
            if (rdF != null && rdF.Level < rdF.MaxLevel && rdF.ScienceLimit > 0 && ResearchHost != null)
            {
                var rd = new UpgradeNeed { Facility = rdF };
                List<TechOption> opts = ResearchHost.Options();
                opts.Sort((x, y) => x.ScienceCost.CompareTo(y.ScienceCost));
                foreach (TechOption o in opts) if (o.ScienceCost > rdF.ScienceLimit && rd.Unlocks.Count < 3) rd.Unlocks.Add("research " + o.Title + " (" + Math.Round(o.ScienceCost) + " Science)");
                if (rd.Unlocks.Count > 0) needs.Add(rd);
            }
            return needs;
        }

        /// <summary>
        /// Money the agency keeps after an upgrade: half a year of salaries, and never less than a quarter of the yearly grant
        /// (2026-10-30: half a year of four kerbals' salaries let one upgrade leave 13,000 funds).
        /// </summary>
        internal double UpgradeReserve()
        {
            double salaries = 0;
            foreach (CrewProfile c in Crew.Active) salaries += c.Salary;
            return Math.Max(salaries / 2, Settings.BaseGrant * 0.25);
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
                if (!UpgradeAffordable(n.Facility) || (DirectorConserving() && n.Facility.UpgradeCost > DirectorSpendable())) continue;
                _upgradeWakeDay = CurrentDay;
                RequestDirectorTurn("the " + n.Facility.Title + " is holding back " + string.Join(", ", n.Unlocks.ToArray()) + " and the agency can afford the upgrade now");
                return;
            }
        }
    }
}
