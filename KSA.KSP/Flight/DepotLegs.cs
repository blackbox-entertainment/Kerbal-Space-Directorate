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
        /// <summary>Our tanks grouped by stage, top stage first, without a spent booster (under 10% full) that is still attached.</summary>
        public static List<List<Part>> StageGroups(FlightContext ctx)
        {
            var byStage = new SortedDictionary<int, List<Part>>();
            foreach (Part p in OwnParts(ctx))
            {
                bool hasFuel = false;
                foreach (PartResource r in p.Resources) if (IsPropellant(r) && r.maxAmount > 0) hasFuel = true;
                if (!hasFuel) continue;
                List<Part> list;
                if (!byStage.TryGetValue(p.inverseStage, out list)) byStage[p.inverseStage] = list = new List<Part>();
                list.Add(p);
            }
            var groups = new List<List<Part>>(byStage.Values);
            if (groups.Count > 1)
            {
                List<Part> lowest = groups[groups.Count - 1];
                double cap = Tonnes(lowest, true);
                if (cap > 0 && Tonnes(lowest) < 0.1 * cap) groups.RemoveAt(groups.Count - 1);
            }
            return groups;
        }

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

    /// <summary>
    /// A stop at a tanker sent ahead (2026-11-01, T: "give me a tanker ... that I can send before the mk II"): if an empty-crewed craft with a docking port and
    /// fuel waits in orbit here, meet it, dock, fill up and go on. Nothing to meet, or a failure on the way, and the flight simply carries on: the tanker is a bonus, never a halt.
    /// </summary>
    public sealed class TankerStopLeg : IFlightLeg
    {
        private const double MinTonnes = 1;
        private readonly List<IFlightLeg> _inner = new List<IFlightLeg>();
        private int _index;
        private bool _failed;

        public string Name { get { return "Meeting the tanker"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        /// <summary>
        /// The crewless craft in orbit of the world with a docking port and the most fuel (0.8.223: with two stations above Kerbin, one
        /// holding 6 t after the Duna ship took 32.7, the first the game listed was the empty one).
        /// </summary>
        public static Vessel FindTanker(Vessel us, CelestialBody body)
        {
            if (FlightGlobals.Vessels == null || body == null) return null;
            Vessel best = null;
            double most = 0;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v == us || v.mainBody != body || !IsTanker(v)) continue;
                double t = Tonnes(v);
                if (best == null || t > most) { best = v; most = t; }
            }
            return best;
        }

        /// <summary>A flight with a tanker still to meet: a crewless craft with a port and fuel waiting above home, or round the world it is bound for.</summary>
        public static bool TankerAhead(Vessel us)
        {
            if (KsaFlightDirector.Live == null || FlightGlobals.Vessels == null) return false;
            CelestialBody target = MissionPlanner.Body(KsaFlightDirector.Live.TargetBody), home = FlightGlobals.GetHomeBody();
            if (target == null || target == home) return false;
            if (!KsaFlightDirector.Live.Logged.Contains("Refuel:" + home.bodyName) && FindTanker(us, home) != null) return true;
            return !KsaFlightDirector.Live.Logged.Contains("Refuel:" + target.bodyName) && FindTanker(us, target) != null;
        }

        /// <summary>Every fuel station in orbit of anything.</summary>
        public static List<Vessel> AllTankers()
        {
            var list = new List<Vessel>();
            if (FlightGlobals.Vessels == null) return list;
            foreach (Vessel v in FlightGlobals.Vessels) if (IsTanker(v)) list.Add(v);
            return list;
        }

        /// <summary>Propellant aboard, tonnes.</summary>
        public static double Tonnes(Vessel v)
        {
            double t = 0;
            if (v != null && v.protoVessel != null)
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                    foreach (ProtoPartResourceSnapshot r in p.resources)
                        if (Array.IndexOf(DepotOps.Propellants, r.resourceName) >= 0 && r.definition != null) t += r.amount * r.definition.density;
            return t;
        }

        /// <summary>The order to come down: gone at once, with its fuel (T: "the option to deorbit them").</summary>
        public static void Deorbit(Vessel v)
        {
            if (v == null) return;
            if (HighLogic.CurrentGame.flightState != null && v.protoVessel != null) HighLogic.CurrentGame.flightState.protoVessels.Remove(v.protoVessel);
            v.Die();
        }

        private static bool IsTanker(Vessel v)
        {
            if (v == null || v.situation != Vessel.Situations.ORBITING || v.vesselType == VesselType.Debris || v.GetCrewCount() > 0 || v.protoVessel == null) return false;
            bool port = false;
            foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                foreach (ProtoPartModuleSnapshot m in p.modules) if (m.moduleName == "ModuleDockingNode") port = true;
            return port && Tonnes(v) >= MinTonnes;
        }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            if (v != null && v.rootPart != null) ctx.OwnLaunchId = v.rootPart.launchID;
            // Already docked with a tanker (0.8.172: a retry after the hard dock could not find it, it was part of the ship): pump and undock.
            if (v != null && DepotOps.Docked(ctx) && DepotOps.Tonnes(DepotOps.DepotParts(ctx)) >= MinTonnes)
            {
                ctx.Say("Docked with a tanker already: refuelling.");
                _inner.Add(new RefuelLeg());
                _inner.Add(new UndockLeg());
                _index = 0;
                _inner[0].Begin(ctx);
                return;
            }
            Vessel tanker = v != null ? FindTanker(v, v.mainBody) : null;
            if (tanker == null) { Status = "No tanker here."; State = LegState.Done; return; }
            if (!ctx.Control.HasRcs() || ctx.Control.DockingPorts().Count == 0)
            {
                ctx.Say(tanker.vesselName + " waits in orbit, but this craft has no docking port and RCS to meet it.");
                State = LegState.Done;
                return;
            }
            if (v.rootPart != null) ctx.OwnLaunchId = v.rootPart.launchID;
            ctx.DepotTargetId = tanker.persistentId;
            ctx.DockTargetId = tanker.persistentId;
            ctx.DockTarget = null;
            ctx.Say("Tanker " + tanker.vesselName + " found in orbit with " + Tonnes(tanker).ToString("0.0") + " t of propellant: going to meet it.");
            _inner.Add(new RendezvousLeg());
            _inner.Add(new DockingLeg());
            _inner.Add(new RefuelLeg());
            _inner.Add(new UndockLeg());
            _index = 0;
            _inner[0].Begin(ctx);
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            IFlightLeg l = _inner[_index];
            string line = l.State == LegState.Running ? l.Step(ctx) : null;
            Status = l.Status;
            if (l.State == LegState.Failed)
            {
                // Docked with the tanker and failed after: the undock is still owed.
                bool home = ctx.Vessel != null && ctx.Vessel.mainBody == ctx.Home;
                ctx.Say(l.Name + " did not work: " + l.Status + (home ? " Holding in Kerbin orbit: no leaving on empty tanks." : " Going on without the tanker."));
                l.End(ctx);
                if (DepotOps.Docked(ctx) && !(l is UndockLeg)) { _index = _inner.Count - 1; _failed = home; _inner[_index].Begin(ctx); return line; }
                State = home ? LegState.Failed : LegState.Done;
                return line;
            }
            if (l.State == LegState.Done)
            {
                l.End(ctx);
                if (++_index >= _inner.Count) { State = _failed ? LegState.Failed : LegState.Done; return line; }
                _inner[_index].Begin(ctx);
            }
            return line;
        }

        public void End(FlightContext ctx) { if (_index < _inner.Count && _inner.Count > 0) _inner[_index].End(ctx); }
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

    /// <summary>
    /// Docked at the depot: fills our tanks from the depot's (2026-10-29). Since 0.8.172 it waits for the game to merge the
    /// two craft (the hard dock and the refuel came in the same instant and found "not docked"), pumps at a steady rate so a
    /// fill takes minutes rather than a frame (T), empties the tanks that launched empty first and then the top stages, and
    /// leaves the tanker enough to deorbit itself (T): 5% of its dry mass, at least 0.3 t.
    /// </summary>
    public sealed class RefuelLeg : IFlightLeg
    {
        private const double PumpUnitsPerSecond = 15; // of each propellant: 32 t of drop tanks in about three minutes
        private const double SettleSeconds = 60;
        private double _started, _lastStep, _before, _reserveLf, _reserveOx;
        private bool _pumping;
        private List<List<Part>> _targets;

        public string Name { get { return "Refuelling"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _started = ctx.Now; _lastStep = _started; _pumping = false; _targets = null;
            ctx.Control.Throttle = 0;
            ctx.Control.ClearInputs();
            Status = "Waiting for the docking to settle.";
        }

        private void Fail(FlightContext ctx, string why) { Status = why; ctx.Say("Refuelling failed: " + why); State = LegState.Failed; }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            double now = ctx.Now, dt = Math.Max(0, now - _lastStep);
            _lastStep = now;
            if (!DepotOps.Docked(ctx))
            {
                if (_pumping) { Fail(ctx, "the ships came apart while pumping."); return null; }
                if (now - _started > SettleSeconds) { Fail(ctx, "not docked with the depot."); return null; }
                return null;
            }
            List<Part> depot = DepotOps.DepotParts(ctx);
            if (!_pumping)
            {
                _pumping = true;
                _before = DepotOps.Tonnes(depot);
                _targets = new List<List<Part>>();
                List<Part> empty = DepotOps.EmptyTanks(ctx);
                if (empty.Count > 0) _targets.Add(empty);
                // A tanker sent ahead fills everything; a depot's fuel is rationed to the tanks that launched empty.
                if (ctx.ReportDepot == null || empty.Count == 0) foreach (List<Part> g in DepotOps.StageGroups(ctx)) _targets.Add(g);
                double dry = 0;
                foreach (Part p in depot) dry += p.mass;
                double reserve = ctx.ReportDepot == null ? Math.Max(0.3, 0.05 * dry) : 0; // a tanker keeps enough to deorbit itself (T)
                _reserveLf = reserve * 0.45 / 0.005; _reserveOx = reserve * 0.55 / 0.005;
                ctx.Say("Docked and pumping: " + _before.ToString("0.0") + " t aboard the depot" + (reserve > 0 ? ", " + reserve.ToString("0.0") + " t stays with it" : "") + ".");
                return null;
            }
            double moved = 0;
            foreach (List<Part> group in _targets)
            {
                double lf = Pump("LiquidFuel", depot, group, PumpUnitsPerSecond * dt, _reserveLf);
                double ox = Pump("Oxidizer", depot, group, PumpUnitsPerSecond * dt, _reserveOx);
                moved += lf + ox;
                if (lf > 0 || ox > 0) break; // one group at a time, top of the ship first
            }
            double taken = _before - DepotOps.Tonnes(depot);
            if (moved <= 1e-6 && dt > 0)
            {
                DepotOps.Report(ctx);
                double room = 0;
                foreach (List<Part> g in _targets) room += DepotOps.Tonnes(g, true) - DepotOps.Tonnes(g);
                string text = "Took on " + taken.ToString("0.0") + " t of propellant at the depot" + (room > 0.5 ? "; the depot ran down with " + room.ToString("0.0") + " t of room left in our tanks" : "") + ".";
                ctx.Say(text, "Refuel:" + (ctx.Vessel != null && ctx.Vessel.mainBody != null ? ctx.Vessel.mainBody.bodyName : "Kerbin"));
                Status = text;
                State = LegState.Done;
                return null;
            }
            Status = "Refuelling: " + taken.ToString("0.0") + " t taken on.";
            return null;
        }

        /// <summary>Moves up to units of a propellant into the group, never taking the depot below its reserve. Returns what moved.</summary>
        private static double Pump(string resource, List<Part> depot, List<Part> group, double units, double reserveUnits)
        {
            double available = 0;
            foreach (Part p in depot) { PartResource r = p.Resources.Get(resource); if (r != null) available += r.amount; }
            double spare = available - reserveUnits;
            if (spare <= 1e-6) return 0;
            return DepotOps.Move(resource, depot, group, Math.Min(units, spare));
        }

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
