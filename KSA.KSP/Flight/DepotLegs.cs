using System;
using System.Collections.Generic;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// The fuel depot in Kerbin orbit, live (2026-10-29). Once a ship has docked, the merged vessel holds our parts (launch
    /// id <see cref="FlightContext.OwnLaunchId"/>) and the depot's; propellant is moved between them directly, liquid fuel
    /// and oxidizer each, and what the depot holds afterwards is reported to the agency.
    /// </summary>
    internal static class DepotOps
    {
        public static readonly string[] Propellants = { "LiquidFuel", "Oxidizer" };

        public static bool Own(FlightContext ctx, Part p) { return p != null && p.launchID == ctx.OwnLaunchId; }

        public static List<Part> OwnParts(FlightContext ctx)
        {
            var list = new List<Part>();
            if (ctx.Vessel != null) foreach (Part p in ctx.Vessel.parts) if (Own(ctx, p)) list.Add(p);
            return list;
        }

        public static List<Part> DepotParts(FlightContext ctx)
        {
            var list = new List<Part>();
            if (ctx.Vessel != null) foreach (Part p in ctx.Vessel.parts) if (!Own(ctx, p)) list.Add(p);
            return list;
        }

        /// <summary>Liquid fuel and oxidizer in the parts, tonnes (or their capacity).</summary>
        public static double Tonnes(IEnumerable<Part> parts, bool capacity = false)
        {
            double t = 0;
            foreach (Part p in parts)
                foreach (PartResource r in p.Resources)
                    if (IsPropellant(r)) t += (capacity ? r.maxAmount : r.amount) * r.info.density;
            return t;
        }

        public static bool IsPropellant(PartResource r) { return r != null && Array.IndexOf(Propellants, r.resourceName) >= 0; }

        /// <summary>Moves up to <paramref name="units"/> of a resource from one set of parts to another (into the room there is). Returns units moved.</summary>
        public static double Move(string resource, List<Part> from, List<Part> to, double units)
        {
            double available = 0, room = 0;
            foreach (Part p in from) { PartResource r = p.Resources.Get(resource); if (r != null) available += r.amount; }
            foreach (Part p in to) { PartResource r = p.Resources.Get(resource); if (r != null) room += r.maxAmount - r.amount; }
            double move = Math.Max(0, Math.Min(units, Math.Min(available, room)));
            if (move <= 1e-6) return 0;
            double take = move, put = move;
            foreach (Part p in from)
            {
                PartResource r = p.Resources.Get(resource);
                if (r == null || r.amount <= 0) continue;
                double d = Math.Min(r.amount, take * r.amount / Math.Max(1e-9, available));
                r.amount -= d; take -= d;
            }
            foreach (Part p in to)
            {
                PartResource r = p.Resources.Get(resource);
                if (r == null) continue;
                double space = r.maxAmount - r.amount;
                double d = Math.Min(space, put * space / Math.Max(1e-9, room));
                r.amount += d; put -= d;
            }
            return move;
        }

        /// <summary>Our tanks that went up empty (under 1% full): the ones the depot fills.</summary>
        public static List<Part> EmptyTanks(FlightContext ctx)
        {
            var list = new List<Part>();
            foreach (Part p in OwnParts(ctx))
            {
                PartResource lf = p.Resources.Get("LiquidFuel");
                if (lf != null && lf.maxAmount > 0 && lf.amount <= lf.maxAmount * 0.01) list.Add(p);
            }
            return list;
        }

        public static void Report(FlightContext ctx)
        {
            if (ctx.ReportDepot != null) ctx.ReportDepot(Tonnes(DepotParts(ctx)));
        }

        /// <summary>Docked with the depot: our ship and depot parts share one vessel.</summary>
        public static bool Docked(FlightContext ctx)
        {
            return ctx.Vessel != null && OwnParts(ctx).Count > 0 && DepotParts(ctx).Count > 0;
        }
    }

    /// <summary>Points the rendezvous and docking legs at the depot and remembers our own parts (2026-10-29).</summary>
    public sealed class TargetDepotLeg : IFlightLeg
    {
        public string Name { get { return "To the fuel depot"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            if (ctx.Vessel != null && ctx.Vessel.rootPart != null && ctx.OwnLaunchId == 0) ctx.OwnLaunchId = ctx.Vessel.rootPart.launchID;
            if (ctx.DepotTargetId == 0) { Status = "No live depot to meet."; ctx.Say("No fuel depot in orbit to meet."); State = LegState.Failed; return; }
            ctx.SavedDockTargetId = ctx.DockTargetId;
            ctx.DockTargetId = ctx.DepotTargetId;
            ctx.DockTarget = null;
            Status = "Heading for the fuel depot.";
            State = LegState.Done;
        }
        public string Step(FlightContext ctx) { return null; }
        public void End(FlightContext ctx) { }
    }

    /// <summary>Docked at the depot: fills our tanks that launched empty from the depot's (2026-10-29).</summary>
    public sealed class RefuelLeg : IFlightLeg
    {
        public string Name { get { return "Refuelling"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            if (!DepotOps.Docked(ctx)) { Status = "Not docked with the depot."; ctx.Say("Refuelling failed: not docked with the depot."); State = LegState.Failed; return; }
            List<Part> tanks = DepotOps.EmptyTanks(ctx);
            if (tanks.Count == 0) tanks = DepotOps.OwnParts(ctx); // nothing launched empty: top up whatever has room
            List<Part> depot = DepotOps.DepotParts(ctx);
            double before = DepotOps.Tonnes(depot);
            foreach (string res in DepotOps.Propellants) DepotOps.Move(res, depot, tanks, double.MaxValue);
            double taken = before - DepotOps.Tonnes(depot);
            DepotOps.Report(ctx);
            double room = DepotOps.Tonnes(tanks, true) - DepotOps.Tonnes(tanks);
            string text = "Took on " + taken.ToString("0.0") + " t of propellant at the depot" + (room > 0.5 ? "; the depot ran dry with " + room.ToString("0.0") + " t of room left in our tanks" : "") + ".";
            ctx.Say(text, "Refuel:Kerbin");
            Status = text;
            State = LegState.Done;
        }
        public string Step(FlightContext ctx) { return null; }
        public void End(FlightContext ctx) { }
    }

    /// <summary>A tanker docked at the depot pumps its cargo across, keeping enough to come home (2026-10-29).</summary>
    public sealed class PumpCargoLeg : IFlightLeg
    {
        private const double ReserveDv = 225; // the deorbit burn and a margin
        public string Name { get { return "Pumping cargo"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            if (!DepotOps.Docked(ctx)) { Status = "Not docked with the depot."; ctx.Say("Pumping failed: not docked with the depot."); State = LegState.Failed; return; }
            List<Part> own = DepotOps.OwnParts(ctx), depot = DepotOps.DepotParts(ctx);
            double mass = 0;
            foreach (Part p in own) mass += p.mass + p.GetResourceMass();
            double reserve = mass * (1 - Math.Exp(-ReserveDv / (Math.Max(200, BestIsp(own)) * 9.80665)));
            double mine = DepotOps.Tonnes(own);
            double pump = Math.Max(0, Math.Min(ctx.CargoFuel, mine - reserve));
            double before = DepotOps.Tonnes(depot);
            if (pump > 0)
            {
                // Liquid fuel and oxidizer in the ratio we carry them.
                double lf = 0, ox = 0;
                foreach (Part p in own) { PartResource a = p.Resources.Get("LiquidFuel"); PartResource b = p.Resources.Get("Oxidizer"); if (a != null) lf += a.amount * a.info.density; if (b != null) ox += b.amount * b.info.density; }
                double total = Math.Max(1e-6, lf + ox);
                PartResourceDefinition lfDef = PartResourceLibrary.Instance.GetDefinition("LiquidFuel");
                PartResourceDefinition oxDef = PartResourceLibrary.Instance.GetDefinition("Oxidizer");
                if (lfDef != null && lfDef.density > 0) DepotOps.Move("LiquidFuel", own, depot, pump * lf / total / lfDef.density);
                if (oxDef != null && oxDef.density > 0) DepotOps.Move("Oxidizer", own, depot, pump * ox / total / oxDef.density);
            }
            double moved = DepotOps.Tonnes(depot) - before;
            DepotOps.Report(ctx);
            string text = "Pumped " + moved.ToString("0.0") + " t into the depot" + (moved < ctx.CargoFuel - 0.5 ? " (of " + ctx.CargoFuel.ToString("0") + " t planned: " + (mine - reserve < ctx.CargoFuel ? "kept enough to come home" : "the depot is full") + ")" : "") + ".";
            ctx.Say(text, "Tanker:Kerbin");
            Status = text;
            State = LegState.Done;
        }

        private static double BestIsp(List<Part> parts)
        {
            double best = 0;
            foreach (Part p in parts)
                foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                    if (e.atmosphereCurve != null) best = Math.Max(best, e.atmosphereCurve.Evaluate(0));
            return best;
        }

        public string Step(FlightContext ctx) { return null; }
        public void End(FlightContext ctx) { }
    }

    /// <summary>A tanker relieves the depot keeper: the keeper takes our seat home, our crew stays aboard (2026-10-29).</summary>
    public sealed class KeeperSwapLeg : IFlightLeg
    {
        public string Name { get { return "Crew change"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Done;
            if (ctx.KeeperNames.Count == 0 || !DepotOps.Docked(ctx)) { Status = "No keeper to relieve."; return; }
            int swapped = 0;
            try
            {
                foreach (string name in ctx.KeeperNames)
                {
                    Part theirSeat = null, ourSeat = null;
                    ProtoCrewMember keeper = null, ours = null;
                    foreach (Part p in DepotOps.DepotParts(ctx))
                        foreach (ProtoCrewMember c in p.protoModuleCrew) if (c.name == name) { keeper = c; theirSeat = p; }
                    if (keeper == null) continue;
                    foreach (Part p in DepotOps.OwnParts(ctx))
                        foreach (ProtoCrewMember c in p.protoModuleCrew) if (!ctx.KeeperNames.Contains(c.name) && ours == null) { ours = c; ourSeat = p; }
                    if (ours == null) break;
                    theirSeat.RemoveCrewmember(keeper);
                    ourSeat.RemoveCrewmember(ours);
                    keeper.seat = null; ours.seat = null;
                    ourSeat.AddCrewmember(keeper);
                    theirSeat.AddCrewmember(ours);
                    swapped++;
                }
                if (swapped > 0)
                {
                    ctx.Vessel.SpawnCrew();
                    ctx.Vessel.CrewListSetDirty();
                    GameEvents.onVesselCrewWasModified.Fire(ctx.Vessel);
                }
            }
            catch (Exception e) { KsaCore.Log.Warn("Depot crew change failed: " + e.Message); }
            Status = swapped > 0 ? "Crew changed: " + swapped + " keeper" + (swapped == 1 ? "" : "s") + " flying home." : "Crew change skipped.";
            ctx.Say(Status);
        }
        public string Step(FlightContext ctx) { return null; }
        public void End(FlightContext ctx) { }
    }

    /// <summary>Undocks from the depot and drifts clear before the next burn (2026-10-29).</summary>
    public sealed class UndockLeg : IFlightLeg
    {
        private Part _ours;
        private Vessel _depot;
        private double _since;
        private bool _undocked;

        public string Name { get { return "Undocking"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _undocked = false;
            _since = ctx.Now;
            ModuleDockingNode port = null;
            foreach (Part p in DepotOps.OwnParts(ctx))
                foreach (ModuleDockingNode n in p.FindModulesImplementing<ModuleDockingNode>())
                    if (n.otherNode != null || (!string.IsNullOrEmpty(n.state) && n.state.StartsWith("Docked", StringComparison.Ordinal))) port = n;
            if (port == null) { Status = "Not docked."; Finish(ctx); return; }
            _ours = port.part;
            try
            {
                ctx.Control.ClearInputs();
                ctx.Control.Throttle = 0;
                port.Undock();
                ctx.Say("Undocked from the depot.");
            }
            catch (Exception e) { KsaCore.Log.Warn("Undock failed: " + e.Message); Status = "Undock failed."; State = LegState.Failed; return; }
            Status = "Undocking.";
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            if (_ours == null || _ours.vessel == null) { State = LegState.Failed; Status = "Lost our ship after undocking."; return null; }
            if (!_undocked)
            {
                Vessel ours = _ours.vessel;
                bool clean = true;
                foreach (Part p in ours.parts) if (!DepotOps.Own(ctx, p)) { clean = false; break; }
                if (!clean) { if (ctx.Now - _since > 10) { State = LegState.Failed; Status = "Could not undock."; } return null; }
                _undocked = true;
                if (FlightGlobals.Vessels != null)
                    foreach (Vessel v in FlightGlobals.Vessels)
                        if (v != null && v != ours && v.parts.Count > 0 && v.parts[0].launchID != ctx.OwnLaunchId && StationOps.Distance(v, ours) < 200) { _depot = v; break; }
                if (FlightGlobals.ActiveVessel != ours) FlightGlobals.SetActiveVessel(ours);
                if (ctx.Undocked != null) ctx.Undocked(ours, _depot);
                ctx.Vessel = ours;
                ctx.Control.Vessel = ours;
                _since = ctx.Now;
            }
            // The docking ports push the two apart; wait until clear of the depot before anything fires.
            double d = StationOps.Distance(ctx.Vessel, _depot);
            Status = "Drifting clear of the depot: " + Math.Round(d) + " m.";
            if (d > 60 || ctx.Now - _since > 120 || _depot == null) Finish(ctx);
            return null;
        }

        private void Finish(FlightContext ctx)
        {
            ctx.DockTargetId = ctx.SavedDockTargetId;
            ctx.DockTarget = null;
            State = LegState.Done;
            if (Status == null || Status.StartsWith("Drifting")) Status = "Clear of the depot.";
        }

        public void End(FlightContext ctx) { }
    }
}
