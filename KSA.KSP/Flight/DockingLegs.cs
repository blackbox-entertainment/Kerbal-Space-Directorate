using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>Shared helpers for the station legs (KSA-53): target lookup and relative-motion readouts.</summary>
    internal static class StationOps
    {
        /// <summary>Finds the vessel a module is being sent to. Null when it is not around in the same orbit.</summary>
        public static Vessel ResolveTarget(FlightContext ctx)
        {
            if (ctx == null || ctx.Vessel == null) return null;
            Vessel t = ctx.DockTarget;
            if (t == null && ctx.DockTargetId != 0 && FlightGlobals.Vessels != null)
            {
                foreach (Vessel v in FlightGlobals.Vessels)
                {
                    if (v == null || v == ctx.Vessel) continue;
                    if (v.persistentId == ctx.DockTargetId) { t = v; break; }
                }
            }
            if (t == null || t == ctx.Vessel) return null;
            if (t.mainBody != ctx.Vessel.mainBody) return null;
            if (t.situation != Vessel.Situations.ORBITING) return null;
            ctx.DockTarget = t;
            return t;
        }

        public static double Distance(Vessel a, Vessel b)
        {
            if (a == null || b == null) return double.PositiveInfinity;
            return Vector3d.Distance(a.GetWorldPos3D(), b.GetWorldPos3D());
        }

        public static Vector3d RelativeVelocity(Vessel a, Vessel b)
        {
            if (a == null || b == null) return Vector3d.zero;
            return a.obt_velocity - b.obt_velocity;
        }

        public static string NameOf(Vessel v) { return v != null ? v.vesselName : "the station"; }
        public static string BodyOf(Vessel v) { return v != null && v.mainBody != null ? v.mainBody.name : "Kerbin"; }
    }

    /// <summary>
    /// Brings a module from a low parking orbit to within a couple of kilometres of the station: a plane match at
    /// the relative node when needed, a Lambert intercept found by <see cref="TransferPlanner"/>, a coast, and a
    /// velocity match at arrival. One retry of the intercept before giving up.
    /// </summary>
    public sealed class RendezvousLeg : IFlightLeg
    {
        private enum Phases { PlanePlan, PlaneBurn, InterceptPlan, InterceptBurn, Coast, MatchPlan, MatchBurn, Check }

        private const double MaxPlaneChangeDv = 1500;
        private const double DoneDistance = 2500;
        private const double DoneSpeed = 10;

        private Phases _phase;
        private Vessel _target;
        private NodeExecutor _burn;
        private double _arrivalUt;
        private double _coastEnd;
        private int _intercepts;

        public string Name { get { return "Rendezvous"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        private void Fail(string why, FlightContext ctx)
        {
            State = LegState.Failed;
            Status = "Rendezvous failed: " + why + ".";
            if (ctx != null) ctx.Say("Rendezvous failed: " + why + ".");
        }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Looking for the station.";
            _burn = null;
            _intercepts = 0;
            try
            {
                Vessel v = ctx.Vessel;
                if (v == null) { Fail("no vessel", ctx); return; }
                _target = StationOps.ResolveTarget(ctx);
                if (_target == null) { Fail("station not found in orbit", ctx); return; }
                bool stable = v.orbit != null && (v.situation == Vessel.Situations.ORBITING || (v.situation == Vessel.Situations.SUB_ORBITAL && v.orbit.PeA > v.mainBody.atmosphereDepth));
                if (!stable) { Fail("not in a stable orbit", ctx); return; }
                NodePlanner.ClearNodes(v);
                ctx.Control.Throttle = 0;
                ctx.Control.ClearInputs();
                if (AtStation(v))
                {
                    Status = "Already alongside " + StationOps.NameOf(_target) + ".";
                    State = LegState.Done;
                    return;
                }
                _phase = Phases.PlanePlan;
                Status = "Checking orbital planes.";
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("RendezvousLeg.Begin: " + e);
                Fail("planning error", ctx);
            }
        }

        private bool AtStation(Vessel v)
        {
            return StationOps.Distance(v, _target) < DoneDistance && StationOps.RelativeVelocity(v, _target).magnitude < DoneSpeed;
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            try { return StepInner(ctx); }
            catch (Exception e)
            {
                KsaCore.Log.Error("RendezvousLeg.Step: " + e);
                ctx.Control.Throttle = 0;
                Fail("flight computer error", ctx);
                return null;
            }
        }

        private string StepInner(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            if (v == null || v.orbit == null) { Fail("vessel lost", ctx); return null; }
            if (_target == null || _target.orbit == null || _target.mainBody != v.mainBody) { Fail("station lost", ctx); return null; }
            string line = null;
            double now = ctx.Now;
            switch (_phase)
            {
                case Phases.PlanePlan:
                    {
                        double relInc = Vec3.AngleBetween(OrbitAdapter.Normal(v.orbit, now), OrbitAdapter.Normal(_target.orbit, now)) * 180.0 / Math.PI;
                        if (relInc <= 0.3)
                        {
                            _phase = Phases.InterceptPlan;
                            Status = "Planes match; planning intercept.";
                            break;
                        }
                        double nodeUt;
                        Vec3 dv;
                        if (!PlanPlaneChange(v, now, out nodeUt, out dv)) { Fail("could not find the relative node", ctx); break; }
                        if (dv.Length > MaxPlaneChangeDv) { Fail("plane change of " + Math.Round(dv.Length) + " m/s is too expensive", ctx); break; }
                        NodePlanner.ClearNodes(v);
                        if (NodePlanner.AddFrameDeltaV(v, nodeUt, dv) == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        _burn = new NodeExecutor();
                        _burn.Begin(ctx);
                        if (_burn.Failed) { Fail(_burn.FailReason, ctx); break; }
                        _phase = Phases.PlaneBurn;
                        Status = "Plane change: " + Math.Round(dv.Length) + " m/s.";
                        line = "Plane change of " + Math.Round(dv.Length) + " m/s planned (" + relInc.ToString("0.0") + " deg off the station's plane).";
                    }
                    break;
                case Phases.PlaneBurn:
                    line = RunBurn(ctx, Phases.InterceptPlan, "Plane matched; planning intercept.");
                    break;
                case Phases.InterceptPlan:
                    {
                        if (AtStation(v)) { _phase = Phases.Check; break; }
                        double period = v.orbit.period;
                        if (double.IsNaN(period) || period <= 0) { Fail("not in a closed orbit", ctx); break; }
                        double mu = v.mainBody.gravParameter;
                        TransferPlan plan = TransferPlanner.Plan(OrbitAdapter.Sampler(v.orbit), OrbitAdapter.Sampler(_target.orbit), mu,
                            now + 120, now + 2.5 * period, 0.3 * period, 1.5 * period, 60, 30, 1.0);
                        if (plan == null || double.IsNaN(plan.DeltaVMagnitude) || plan.DepartureUt <= now) { Fail("no intercept found", ctx); break; }
                        NodePlanner.ClearNodes(v);
                        if (NodePlanner.AddFrameDeltaV(v, plan.DepartureUt, plan.DeltaV) == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        _arrivalUt = plan.ArrivalUt;
                        _intercepts++;
                        _burn = new NodeExecutor();
                        _burn.Begin(ctx);
                        if (_burn.Failed) { Fail(_burn.FailReason, ctx); break; }
                        _phase = Phases.InterceptBurn;
                        Status = "Intercept burn: " + Math.Round(plan.DeltaVMagnitude) + " m/s.";
                        line = "Intercept planned: " + Math.Round(plan.DeltaVMagnitude) + " m/s, arriving in " + Math.Round((plan.ArrivalUt - now) / 60) + " min.";
                    }
                    break;
                case Phases.InterceptBurn:
                    line = RunBurn(ctx, Phases.Coast, "Coasting to the station.");
                    if (_phase == Phases.Coast)
                    {
                        Vec3 matchDv = MatchDeltaV(v);
                        double burnTime = c.BurnTime(matchDv.Length);
                        if (double.IsInfinity(burnTime) || double.IsNaN(burnTime)) burnTime = 60;
                        _coastEnd = _arrivalUt - Math.Max(60, burnTime + 20);
                    }
                    break;
                case Phases.Coast:
                    if (now >= _coastEnd || now >= _arrivalUt)
                    {
                        c.StopWarp();
                        _phase = Phases.MatchPlan;
                        Status = "Arriving; planning velocity match.";
                    }
                    else
                    {
                        Status = "Coasting, " + Math.Round((_arrivalUt - now) / 60) + " min to arrival.";
                        if (_coastEnd - now > 30) c.WarpTo(_coastEnd); else if (c.IsWarping) c.StopWarp();
                    }
                    break;
                case Phases.MatchPlan:
                    {
                        if (now >= _arrivalUt - 5) { _phase = Phases.Check; break; }
                        Vec3 dv = MatchDeltaV(v);
                        if (dv.Length < 0.5) { _phase = Phases.Check; break; }
                        NodePlanner.ClearNodes(v);
                        if (NodePlanner.AddFrameDeltaV(v, _arrivalUt, dv) == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        _burn = new NodeExecutor();
                        _burn.Begin(ctx);
                        if (_burn.Failed) { Fail(_burn.FailReason, ctx); break; }
                        _phase = Phases.MatchBurn;
                        Status = "Matching velocity: " + Math.Round(dv.Length) + " m/s.";
                        line = "Velocity match burn planned: " + Math.Round(dv.Length) + " m/s.";
                    }
                    break;
                case Phases.MatchBurn:
                    line = RunBurn(ctx, Phases.Check, "Checking range.");
                    break;
                case Phases.Check:
                    {
                        c.Throttle = 0;
                        double dist = StationOps.Distance(v, _target);
                        double speed = StationOps.RelativeVelocity(v, _target).magnitude;
                        if (dist < DoneDistance && speed < DoneSpeed)
                        {
                            Status = "Alongside " + StationOps.NameOf(_target) + ".";
                            line = "Rendezvous: " + Math.Round(dist) + " m from " + StationOps.NameOf(_target) + ", closing at " + speed.ToString("0.0") + " m/s.";
                            ctx.Say(line, "Rendezvous:" + StationOps.BodyOf(v));
                            State = LegState.Done;
                        }
                        else if (_intercepts < 2)
                        {
                            _phase = Phases.InterceptPlan;
                            Status = "Missed by " + Math.Round(dist / 1000) + " km; planning a second intercept.";
                            line = "First intercept missed (" + Math.Round(dist / 1000) + " km, " + Math.Round(speed) + " m/s); trying again.";
                        }
                        else Fail("rendezvous missed", ctx);
                    }
                    break;
            }
            return line;
        }

        /// <summary>Drives the current NodeExecutor; moves to the next phase when the burn is done.</summary>
        private string RunBurn(FlightContext ctx, Phases next, string nextStatus)
        {
            if (_burn == null) { _phase = next; Status = nextStatus; return null; }
            string line = _burn.Step(ctx);
            Status = _burn.Status;
            if (_burn.Failed) { Fail(_burn.FailReason ?? "burn failed", ctx); return line; }
            if (_burn.Done) { _burn = null; _phase = next; Status = nextStatus; }
            return line;
        }

        /// <summary>Delta-v that puts our velocity onto the station's at the arrival time.</summary>
        private Vec3 MatchDeltaV(Vessel v)
        {
            return OrbitAdapter.StateAt(_target.orbit, _arrivalUt).Velocity - OrbitAdapter.StateAt(v.orbit, _arrivalUt).Velocity;
        }

        /// <summary>
        /// Finds the next time our position lies in the station's orbital plane and the burn that rotates our velocity
        /// into that plane at the same speed.
        /// </summary>
        private bool PlanPlaneChange(Vessel v, double now, out double nodeUt, out Vec3 deltaV)
        {
            nodeUt = 0; deltaV = Vec3.Zero;
            double period = v.orbit.period;
            if (double.IsNaN(period) || period <= 0) return false;
            Vec3 n = OrbitAdapter.Normal(_target.orbit, now);
            const int samples = 180;
            double t0 = now + 60;
            double step = period / samples;
            double prevT = t0;
            double prevF = Vec3.Dot(OrbitAdapter.StateAt(v.orbit, prevT).Position, n);
            bool found = false;
            double a = 0, b = 0, fa = 0;
            for (int i = 1; i <= samples + 1; i++)
            {
                double t = t0 + i * step;
                double f = Vec3.Dot(OrbitAdapter.StateAt(v.orbit, t).Position, n);
                if ((prevF <= 0 && f >= 0) || (prevF >= 0 && f <= 0)) { a = prevT; b = t; fa = prevF; found = true; break; }
                prevT = t; prevF = f;
            }
            if (!found) return false;
            for (int i = 0; i < 30; i++)
            {
                double m = (a + b) / 2;
                double fm = Vec3.Dot(OrbitAdapter.StateAt(v.orbit, m).Position, n);
                if ((fa <= 0 && fm <= 0) || (fa >= 0 && fm >= 0)) { a = m; fa = fm; } else b = m;
            }
            nodeUt = (a + b) / 2;
            Vec3 vOld = OrbitAdapter.StateAt(v.orbit, nodeUt).Velocity;
            Vec3 inPlane = Vec3.Exclude(n, vOld);
            if (inPlane.Length < 1e-6) return false;
            Vec3 vNew = inPlane.Normalized * vOld.Length;
            deltaV = vNew - vOld;
            return true;
        }

        public void End(FlightContext ctx)
        {
            if (_burn != null) { _burn.End(ctx); _burn = null; }
            ctx.Control.Throttle = 0;
            ctx.Control.ClearInputs();
            ctx.Control.StopWarp();
        }
    }

    /// <summary>
    /// Closes the last 2.5 km on RCS and docks: approach the station, line up 30 m in front of a free port with our
    /// port facing it (SAS Target on the port, control from our port), then creep in along the port axis until the
    /// magnets take over. Sets <see cref="FlightContext.Delivered"/> on a hard dock.
    /// </summary>
    public sealed class DockingLeg : IFlightLeg
    {
        private enum Phases { Approach, Align, Final, Docked }

        private const double TimeoutSeconds = 20 * 60;
        private const double AbortDistance = 3000;
        private const double ApproachRange = 150;
        private const double Standoff = 30;
        private const double Gain = 0.5;
        private const double Deadband = 0.05;

        private Phases _phase;
        private Vessel _target;
        private ModuleDockingNode _ourPort, _targetPort;
        private double _started;
        private bool _targetModeOk;
        private bool _sasOnPort;
        private double _lastSasRefresh;

        public string Name { get { return "Docking"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        private void Fail(string why, FlightContext ctx)
        {
            State = LegState.Failed;
            Status = "Docking failed: " + why + ".";
            if (ctx != null) ctx.Say("Docking failed: " + why + ".");
        }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Preparing to dock.";
            _ourPort = null; _targetPort = null;
            _sasOnPort = false;
            _lastSasRefresh = -1e9;
            try
            {
                Vessel v = ctx.Vessel;
                if (v == null) { Fail("no vessel", ctx); return; }
                _target = StationOps.ResolveTarget(ctx);
                if (_target == null) { Fail("station not found in orbit", ctx); return; }
                if (!ctx.Control.HasRcs()) { Fail("no RCS on this craft; docking needs RCS", ctx); return; }
                _started = ctx.Now;
                ctx.Control.Throttle = 0;
                ctx.Control.ClearInputs();
                ctx.Control.StopWarp();
                ctx.Control.Rcs(true);
                if (SelectPorts(ctx))
                {
                    if (IsDocked(ctx)) { Finish(ctx); return; }
                }
                if (State != LegState.Running) return;
                _phase = Phases.Approach;
                ctx.Control.SasTarget(_target);
                _targetModeOk = ctx.Control.CanSas(VesselAutopilot.AutopilotMode.Target);
                if (!_targetModeOk) ctx.Say("SAS Target mode unavailable; docking on stability assist only.");
                _sasOnPort = false;
                Status = "Approaching " + StationOps.NameOf(_target) + ".";
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("DockingLeg.Begin: " + e);
                Fail("planning error", ctx);
            }
        }

        private static bool IsReady(ModuleDockingNode n) { return n != null && n.part != null && n.state == "Ready"; }

        /// <summary>Picks our port and the station port. False while the station is not loaded yet or has no free port.</summary>
        private bool SelectPorts(FlightContext ctx)
        {
            if (_ourPort != null && _targetPort != null) return true;
            Vessel v = ctx.Vessel;
            if (_target == null || !_target.loaded) return false;
            var ours = new List<ModuleDockingNode>();
            foreach (ModuleDockingNode n in ctx.Control.DockingPorts()) if (IsReady(n)) ours.Add(n);
            var theirs = new List<ModuleDockingNode>();
            foreach (ModuleDockingNode n in _target.FindPartModulesImplementing<ModuleDockingNode>()) if (IsReady(n)) theirs.Add(n);
            if (ours.Count == 0) { Fail("no free docking port on this craft", ctx); return false; }
            if (theirs.Count == 0) { Fail("no free docking port on " + StationOps.NameOf(_target), ctx); return false; }
            ModuleDockingNode bestOurs = null, bestTheirs = null;
            foreach (ModuleDockingNode o in ours)
            {
                foreach (ModuleDockingNode t in theirs)
                {
                    if (SameType(o, t)) { bestOurs = o; bestTheirs = t; break; }
                }
                if (bestOurs != null) break;
            }
            if (bestOurs == null) { bestOurs = ours[0]; bestTheirs = theirs[0]; ctx.Say("No matching port sizes; trying " + bestOurs.part.partInfo.title + " anyway."); }
            _ourPort = bestOurs; _targetPort = bestTheirs;
            ctx.Control.ControlFrom(_ourPort);
            ctx.Say("Docking " + _ourPort.part.partInfo.title + " to " + _targetPort.part.partInfo.title + " on " + StationOps.NameOf(_target) + ".");
            return v != null;
        }

        private static bool SameType(ModuleDockingNode a, ModuleDockingNode b)
        {
            if (a == null || b == null) return false;
            if (!string.IsNullOrEmpty(a.nodeType) && a.nodeType == b.nodeType) return true;
            if (a.nodeTypes != null && b.nodeTypes != null)
                foreach (string t in a.nodeTypes) if (b.nodeTypes.Contains(t)) return true;
            return false;
        }

        private bool IsDocked(FlightContext ctx)
        {
            if (_ourPort == null || _ourPort.part == null) return false;
            if (!string.IsNullOrEmpty(_ourPort.state) && _ourPort.state.StartsWith("Docked", StringComparison.Ordinal)) return true;
            if (_ourPort.otherNode != null) return true;
            if (_targetPort != null && _targetPort.part != null && _targetPort.vessel == _ourPort.vessel) return true;
            if (ctx.Vessel != null && _target == ctx.Vessel) return true;
            return false;
        }

        private void Finish(FlightContext ctx)
        {
            ctx.Control.ClearInputs();
            ctx.Control.Rcs(false);
            ctx.Delivered = true;
            _phase = Phases.Docked;
            Status = "Docked with " + StationOps.NameOf(_target) + ".";
            ctx.Say("Hard dock with " + StationOps.NameOf(_target) + ".", "Dock:" + StationOps.BodyOf(ctx.Vessel ?? _target));
            State = LegState.Done;
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            try { return StepInner(ctx); }
            catch (Exception e)
            {
                KsaCore.Log.Error("DockingLeg.Step: " + e);
                ctx.Control.ClearInputs();
                Fail("flight computer error", ctx);
                return null;
            }
        }

        private string StepInner(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            if (v == null) { Fail("vessel lost", ctx); return null; }
            if (_target == null) { Fail("station lost", ctx); return null; }
            if (IsDocked(ctx)) { Finish(ctx); return null; }
            if (TimeWarp.CurrentRate > 1.01f) { c.StopWarp(); c.ClearInputs(); Status = "Waiting for warp to end."; return null; }
            double now = ctx.Now;
            if (now - _started > TimeoutSeconds) { c.ClearInputs(); Fail("docking timed out", ctx); return null; }
            double vesselDist = StationOps.Distance(v, _target);
            if (vesselDist > AbortDistance) { c.ClearInputs(); Fail("drifted " + Math.Round(vesselDist / 1000.0, 1) + " km from the station", ctx); return null; }

            // Ports become available once the station loads (physics range); until then approach its centre.
            bool havePorts = SelectPorts(ctx);
            if (State != LegState.Running) return null;

            string line = null;
            Vector3d relVel = StationOps.RelativeVelocity(v, _target);
            Vector3d desired;

            if (!havePorts || _phase == Phases.Approach)
            {
                Vector3d aim = havePorts ? StandoffPoint() : _target.GetWorldPos3D();
                Vector3d from = havePorts ? (Vector3d)_ourPort.nodeTransform.position : v.GetWorldPos3D();
                Vector3d toAim = aim - from;
                double dist = toAim.magnitude;
                HoldSas(ctx, false);
                double speed = Clamp(vesselDist / 40.0, 0.5, 5.0) * (ctx.Crew != null ? ctx.Crew.ApproachFactor : 1.0);
                desired = dist > 1e-3 ? toAim / dist * speed : Vector3d.zero;
                Status = "Approach: " + Math.Round(vesselDist) + " m, " + relVel.magnitude.ToString("0.0") + " m/s.";
                if (havePorts && vesselDist < ApproachRange)
                {
                    _phase = Phases.Align;
                    HoldSas(ctx, true);
                    line = "Inside " + ApproachRange + " m; lining up on the port.";
                }
            }
            else
            {
                Vector3d tgtPos = _targetPort.nodeTransform.position;
                Vector3d tgtFwd = ((Vector3d)_targetPort.nodeTransform.forward).normalized;
                Vector3d ourPos = _ourPort.nodeTransform.position;
                Vector3d rel = ourPos - tgtPos;
                double axial = Vector3d.Dot(rel, tgtFwd);
                Vector3d lateral = rel - tgtFwd * axial;
                double latMag = lateral.magnitude;
                double alignDeg = Vector3d.Angle(_ourPort.nodeTransform.forward, -tgtFwd);
                double alignLimit = _targetModeOk ? 5.0 : 20.0;
                HoldSas(ctx, true);

                if (_phase == Phases.Align)
                {
                    // Fly to the corridor point: 30 m out on the port axis, or hold our axial distance while the lateral error is fixed.
                    double holdAxial = Clamp(axial, 10.0, Standoff);
                    Vector3d point = tgtPos + tgtFwd * (axial > Standoff ? Standoff : holdAxial);
                    Vector3d err = point - ourPos;
                    double e = err.magnitude;
                    double speed = Clamp(e / 20.0, 0.2, 2.0);
                    desired = e > 1e-3 ? err / e * speed : Vector3d.zero;
                    Status = "Align: " + Math.Round(axial) + " m out, " + latMag.ToString("0.0") + " m off axis, " + Math.Round(alignDeg) + " deg.";
                    if (axial > 0 && axial <= Standoff + 1.5 && latMag < 1.0 && alignDeg < alignLimit)
                    {
                        _phase = Phases.Final;
                        line = "On the port axis at " + Math.Round(axial) + " m; final approach.";
                    }
                }
                else
                {
                    // Final: creep along the axis, correct lateral drift, hold when the ports are not facing each other.
                    Vector3d latFix = latMag > 1e-3 ? -lateral / latMag * Clamp(latMag * 0.2, 0.0, 0.3) : Vector3d.zero;
                    double closing = (axial > 5 ? 0.4 : 0.2) * (ctx.Crew != null ? ctx.Crew.ApproachFactor : 1.0);
                    if (alignDeg > alignLimit || latMag > 1.0) closing = 0.0;
                    if (axial < 0) closing = -0.2;
                    desired = -tgtFwd * closing + latFix;
                    Status = "Final: " + axial.ToString("0.0") + " m, " + latMag.ToString("0.00") + " m off axis, " + Math.Round(alignDeg) + " deg" + (closing == 0 ? " (holding)" : "") + ".";
                    if (axial > Standoff + 5 || latMag > 3)
                    {
                        _phase = Phases.Align;
                        line = "Drifted off the corridor; realigning.";
                    }
                }
            }

            ApplyVelocityCommand(v, c, desired, relVel);
            return line;
        }

        private Vector3d StandoffPoint()
        {
            return (Vector3d)_targetPort.nodeTransform.position + ((Vector3d)_targetPort.nodeTransform.forward).normalized * Standoff;
        }

        /// <summary>Keeps SAS pointed at the station (far) or the port (near), re-asserting it every few seconds.</summary>
        private void HoldSas(FlightContext ctx, bool onPort)
        {
            double now = ctx.Now;
            if (onPort == _sasOnPort && now - _lastSasRefresh < 5) return;
            _sasOnPort = onPort;
            _lastSasRefresh = now;
            if (onPort && _targetPort != null && _targetPort.part != null) ctx.Control.SasTarget(_targetPort);
            else ctx.Control.SasTarget(_target);
        }

        /// <summary>
        /// Velocity command: RCS inputs proportional to (desired - actual) relative velocity, expressed in the control
        /// frame. FlightCtrlState translation: X along ReferenceTransform.right, Y along ReferenceTransform.forward,
        /// Z along ReferenceTransform.up (the control point's thrust axis).
        /// </summary>
        private static void ApplyVelocityCommand(Vessel v, VesselControl c, Vector3d desired, Vector3d actual)
        {
            Transform rt = v.ReferenceTransform;
            if (rt == null) { c.ClearInputs(); return; }
            Vector3d err = desired - actual;
            if (err.magnitude < Deadband) { c.ClearInputs(); return; }
            double x = Vector3d.Dot(err, rt.right);
            double y = Vector3d.Dot(err, rt.forward);
            double z = Vector3d.Dot(err, rt.up);
            // Stock ModuleRCS accelerates the vessel along -(X*right + Z*up + Y*forward), so the inputs are negated.
            c.TransX = (float)Clamp(-Gain * x, -1, 1);
            c.TransY = (float)Clamp(-Gain * y, -1, 1);
            c.TransZ = (float)Clamp(-Gain * z, -1, 1);
        }

        private static double Clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }

        public void End(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Control.ClearInputs();
            ctx.Control.StopWarp();
        }
    }
}
