using System;
using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// The fuel depot in Kerbin orbit (2026-10-29). A program of kind FuelDepot: its core is a tank farm with a docking
    /// port that arrives full; tanker flights (its resupply) pump cargo into it; flights beyond Kerbin can stop there on the
    /// way out, launching their upper stages empty and filling them at the depot. The agency's DepotFuel is the truth:
    /// simulated flights move it directly, live flights report what was actually pumped, and the host keeps a live depot
    /// vessel's tanks matching it.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>A depot's fuel or capacity changed: the host brings the live depot vessel's tanks into line.</summary>
        public event Action<Program> DepotChanged;

        private void RaiseDepotChanged(Program g)
        {
            Action<Program> h = DepotChanged;
            if (h != null) h(g);
        }

        public bool IsDepot(Program g)
        {
            if (g == null) return false;
            ProgramType t = Catalogs.ProgramTypes.Get(g.ProgramTypeId);
            return t != null && t.Kind == ProgramKind.FuelDepot;
        }

        /// <summary>The working fuel depot in Kerbin orbit (finished, not cancelled), or null.</summary>
        public Program Depot()
        {
            Program best = null;
            foreach (Program g in Programs)
                if (IsDepot(g) && g.Status == ProgramStatus.Complete && g.DepotCapacity > 0 && (best == null || g.DepotFuel > best.DepotFuel)) best = g;
            return best;
        }

        /// <summary>A depot core's mission (purpose = Depot in missions.cfg).</summary>
        public static bool IsDepotCoreMission(MissionType t)
        {
            return t != null && string.Equals(t.Purpose, "Depot", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Docks with the depot: its core (it is the depot), a tanker, or a flight that refuels there.</summary>
        private bool DocksAtDepot(MissionType type, Project p)
        {
            return type != null && (type.IsTanker || (p != null && p.DepotId.Length > 0));
        }

        /// <summary>Fuel other flights have been designed to take from the depot and have not launched yet, tonnes.</summary>
        public double DepotFuelSpoken(Program g, Project except = null)
        {
            double t = 0;
            if (g == null) return 0;
            foreach (Project p in Projects)
                if (p != except && p.DepotId == g.Id && p.IsActive && p.Status != ProjectStatus.InFlight && p.Design != null) t += p.Design.DepotFuel;
            return t;
        }

        /// <summary>Null when a flight's depot stop has its fuel waiting, else why it waits.</summary>
        public string DepotBlocker(Project p)
        {
            if (p == null || p.DepotId.Length == 0) return null;
            Program g = GetProgram(p.DepotId);
            if (g == null || g.Status == ProgramStatus.Cancelled) return "its fuel depot is gone; the engineers must redesign it";
            double need = p.Design != null ? p.Design.DepotFuel : 0;
            if (g.DepotFuel + 1e-3 >= need) return null;
            return "waiting for " + need.ToString("0.0") + " t of propellant at " + g.Name + " (it holds " + g.DepotFuel.ToString("0.0") + " t): send a tanker";
        }

        /// <summary>A simulated flight takes its fuel at launch; a live one when it actually pumps it (<see cref="SetDepotFuel"/>).</summary>
        private void TakeDepotFuel(Project p)
        {
            if (p == null || p.DepotId.Length == 0 || p.Mode != MissionMode.Simulate) return;
            Program g = GetProgram(p.DepotId);
            if (g == null) return;
            double need = p.Design != null ? p.Design.DepotFuel : 0;
            g.DepotFuel = Math.Max(0, g.DepotFuel - need);
            g.Notes.Add(p.Name + " took on " + need.ToString("0.0") + " t on day " + CurrentDay + "; " + g.DepotFuel.ToString("0.0") + " t left.");
            p.Notes.Add("Topped up at " + g.Name + ": " + need.ToString("0.0") + " t.");
            RaiseDepotChanged(g);
        }

        /// <summary>A tanker came back (simulated): its cargo is in the depot. Live tankers are measured by the host instead.</summary>
        private void ApplyTanker(Project p, MissionReport r, Program g)
        {
            if (!IsDepot(g) || r == null || !r.IsSuccess) return;
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            if (r.Mode == MissionMode.Simulate)
            {
                double cargo = type != null ? type.CargoFuel : 0;
                double before = g.DepotFuel;
                g.DepotFuel = Math.Min(g.DepotCapacity, g.DepotFuel + cargo);
                g.Notes.Add(p.Name + " pumped " + (g.DepotFuel - before).ToString("0.0") + " t into the depot on day " + CurrentDay + ".");
                RaiseDepotChanged(g);
            }
            Push(null, g.Name + " now holds " + g.DepotFuel.ToString("0.0") + " of " + g.DepotCapacity.ToString("0") + " t of propellant.");
        }

        /// <summary>The depot core is in orbit: its tank farm sets the capacity, and it arrives full.</summary>
        private void OnDepotCoreDelivered(Project p, Program g)
        {
            if (!IsDepot(g) || g.DepotCapacity > 0) return;
            double cap = 0;
            if (p.Design != null && p.Design.Plan != null && Parts != null) cap = DesignSession.PayloadTankFuel(p.Design.Plan, Parts);
            bool yours = p.Design != null && p.Design.Source == DesignSource.Player;
            if (cap <= 0 && !yours && p.TemplateId.Length > 0) cap = Settings.DepotCapacity; // the stock template is drawn to it
            if (cap <= 0)
            {
                // Your own design (no plan): every tank aboard, read from the craft.
                Func<Project, ModuleHardware> check = ModuleHardwareCheck;
                ModuleHardware h = null;
                try { h = check != null ? check(p) : null; } catch (Exception e) { Log.Warn("Depot check failed for " + p.Name + ": " + e.Message); }
                if (h != null) cap = h.FuelTonnes;
            }
            if (cap <= 0) cap = Settings.DepotCapacity;
            g.DepotCapacity = Math.Round(cap, 2);
            // The engineers' and the template's tank farm goes up empty: tankers fill it. Your own design flies as you built it.
            g.DepotFuel = yours ? g.DepotCapacity : 0;
            g.Notes.Add("Depot in orbit on day " + CurrentDay + ": " + g.DepotCapacity.ToString("0.0") + " t of tanks, " + (g.DepotFuel > 0 ? "full" : "empty: send tankers") + ".");
            RaiseDepotChanged(g);
        }

        /// <summary>
        /// The host measured a live depot (after a tanker pumped in, a flight pumped out, or the core arrived): the figure
        /// becomes the agency's. Capacity 0 keeps the one known.
        /// </summary>
        public void SetDepotFuel(string programId, double fuel, double capacity)
        {
            Program g = GetProgram(programId);
            if (g == null || !IsDepot(g)) return;
            if (capacity > 0) g.DepotCapacity = Math.Round(capacity, 2);
            double before = g.DepotFuel;
            g.DepotFuel = Math.Max(0, Math.Min(g.DepotCapacity > 0 ? g.DepotCapacity : fuel, fuel));
            if (Math.Abs(g.DepotFuel - before) > 0.05)
                g.Notes.Add("Day " + CurrentDay + ": " + g.DepotFuel.ToString("0.0") + " t aboard (was " + before.ToString("0.0") + ").");
        }

        /// <summary>
        /// The engineers' design with a stop at the depot, when there is one and the flight leaves Kerbin. Sets the project's
        /// DepotId on success (and clears it on failure).
        /// </summary>
        private DesignRecord DesignViaDepot(Project p, MissionType type, out string why)
        {
            why = null;
            Program depot = Depot();
            if (depot == null || type == null || type.IsTanker || IsDepotCoreMission(type) || string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase)) return null;
            string before = p.DepotId;
            p.DepotId = depot.Id;
            DesignRecord r = DesignByHand(p, out why, type);
            if (r == null) { p.DepotId = before; return null; }
            // A depot design that needs more than the depot can ever hold is no use.
            if (r.DepotFuel > depot.DepotCapacity + 1e-3)
            {
                why = "it would need " + r.DepotFuel.ToString("0.0") + " t at the depot, which holds at most " + depot.DepotCapacity.ToString("0.0") + " t";
                p.DepotId = before;
                return null;
            }
            r.Rationale += " Tops up at " + depot.Name + ": " + r.DepotFuel.ToString("0.0") + " t.";
            return r;
        }

        /// <summary>
        /// KSA-? (2026-10-29): what a craft that docks with the depot must carry: a docking port on the nose, and so its
        /// parachutes on the sides (a nose parachute would block the port). The depot core also needs its tank farm.
        /// </summary>
        private void AddDockingGear(MissionType type, Project p, DesignRequest req)
        {
            if (Parts == null || type == null) return;
            bool core = IsDepotCoreMission(type);
            if (core) req.DepotTanks = Settings.DepotCapacity;
            if (!core && !DocksAtDepot(type, p)) return;
            var ports = new List<string>();
            foreach (PartInfo part in Parts.All)
            {
                if (part.Kind != PartKind.Docking || !part.HasBottomNode || (part.RequiresDlc && !AllowDlcParts)) continue;
                if (!req.AllowedParts.Exists(x => x.Name == part.Name)) req.AllowedParts.Add(part);
                ports.Add(part.Name);
            }
            if (core)
            {
                foreach (PartInfo part in Parts.All)
                    if (part.Kind == PartKind.Tank && part.HasTopNode && part.HasBottomNode && (AllowDlcParts || !part.RequiresDlc) && !req.AllowedParts.Contains(part)) req.AllowedParts.Add(part);
                req.MustCarry.Add("the depot's tank farm: fuel tanks holding about " + Settings.DepotCapacity.ToString("0") + " t of propellant, as payload extras stacked on top of the crew pod. They stay in orbit as the depot and go up EMPTY (set \"tanks_empty\": true in the payload); tankers fill them.");
            }
            if (!core)
            {
                // Docking is flown on RCS: four thruster blocks round the pod (the pod's own monopropellant feeds them).
                var rcs = new List<string>();
                foreach (PartInfo part in Parts.All)
                {
                    if (part.Kind != PartKind.Rcs || part.IsStackable || (part.RequiresDlc && !AllowDlcParts)) continue;
                    if (!req.AllowedParts.Exists(x => x.Name == part.Name)) req.AllowedParts.Add(part);
                    rcs.Add(part.Name);
                }
                req.MustCarry.Add("four RCS thruster blocks round the pod (four payload extras of the same block): " + (rcs.Count > 0 ? string.Join(", ", rcs.ToArray()) : "none researched yet") + ". The crew docks on RCS.");
                req.MustInclude.Add(new KeyValuePair<string, List<string>>("RCS thrusters for docking", rcs));
            }
            req.MustCarry.Add("a docking port on the very top of the payload (the last stacked extra): " + (ports.Count > 0 ? string.Join(", ", ports.ToArray()) : "none researched yet") + ". The craft docks nose first, so nothing may sit on the port; parachutes go on the sides of the pod.");
            req.MustInclude.Add(new KeyValuePair<string, List<string>>("a docking port", ports));
        }
    }
}
