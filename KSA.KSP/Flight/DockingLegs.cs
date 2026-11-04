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
            // A cloud city floats (KSA-91): anchored it reads as landed, free it reads as flying.
            bool floating = ctx.CloudAltitude > 0 && (t.situation == Vessel.Situations.FLYING || t.situation == Vessel.Situations.LANDED);
            if (t.situation != Vessel.Situations.ORBITING && !floating) return null;
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
        private enum Phases { RaisePlan, RaiseBurn, PlanePlan, PlaneBurn, InterceptPlan, PhasePlan, PhaseBurn, PhaseCoast, ClosePlan, CloseBurn, InterceptBurn, Coast, MatchPlan, MatchBurn, Check }

        private const double MaxPlaneChangeDv = 1500;
        /// <summary>A direct intercept dearer than this gets a phasing orbit first (0.8.163).</summary>
        private const double MaxDirectIntercept = 120;
        private const int MaxPhasingOrbits = 8;
        /// <summary>Inside this range the crew closes with a straight push, never a new intercept or phasing orbit (0.8.165).</summary>
        private const double CloseRange = 25000;
        private const int MaxCloses = 6;
        // The main engine brings the ship to a couple of hundred metres (0.8.166, T: "rcs should be the final 100m or 200m");
        // the thrusters only do the last stretch. It had stopped at 2.5 km and 10 m/s and the RCS ran dry on the approach.
        private const double DoneDistance = 300;
        private const double DoneSpeed = 2;
        private const double CloseStandoff = 200;
        /// <summary>
        /// Inside this range the push takes about two minutes, never five (0.8.198): at 85 km the orbit turns 0.2 deg/s, so a
        /// 2 m/s push over five minutes bent a kilometre off its straight line, and a 0.6 s main-engine burn cannot hold 2 m/s
        /// to the tenth; four pushes of 2 to 5 m/s each ended "1 km, not a meeting" (2026-11-04). A 5 m/s push with a 5 m/s
        /// match is short, straight and sharp, and the crew turned round for a 2 m/s burn in 60 s on the same flight.
        /// </summary>
        private const double ShortRange = 3000;
        private const double ShortTransit = 120;
        private bool _shortClose;

        private Phases _phase;
        private Vessel _target;
        private NodeExecutor _burn;
        private double _arrivalUt;
        private double _coastEnd;
        private int _intercepts;
        private bool _phased;
        private double _phaseEnd;
        private int _closes;
        private int _raises;
        private double _raiseTo;

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
            _closes = 0;
            _phased = false;
            try
            {
                Vessel v = ctx.Vessel;
                if (v == null) { Fail("no vessel", ctx); return; }
                _target = StationOps.ResolveTarget(ctx);
                if (_target == null) { Fail("station not found in orbit", ctx); return; }
                double air = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
                bool closed = v.orbit != null && v.orbit.eccentricity < 1 && (v.situation == Vessel.Situations.ORBITING || v.situation == Vessel.Situations.SUB_ORBITAL);
                if (!closed || v.orbit.ApA < air + 5000) { Fail("not in a stable orbit", ctx); return; } // 0.8.170: 85 x 75 km had failed a 20 km test
                NodePlanner.ClearNodes(v);
                ctx.Control.Throttle = 0;
                ctx.Control.ClearInputs();
                if (AtStation(v))
                {
                    Status = "Already alongside " + StationOps.NameOf(_target) + ".";
                    State = LegState.Done;
                    return;
                }
                if (v.orbit.PeA < air + 5000)
                {
                    // A periapsis in the air (0.8.165: the Director's burn left 299 x 64 km): raise it at apoapsis first.
                    _raiseTo = air + 15000;
                    _phase = Phases.RaisePlan;
                    Status = "Periapsis in the air; raising it first.";
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
                case Phases.RaisePlan:
                    {
                        double mu = v.mainBody.gravParameter;
                        double apUt = now + Math.Max(30, v.orbit.timeToAp);
                        StateVector at = OrbitAdapter.StateAt(v.orbit, apUt);
                        double ra = at.Position.Length, rp = v.mainBody.Radius + _raiseTo;
                        double want = Math.Sqrt(mu * (2 / ra - 2 / (ra + rp)));
                        double have = at.Velocity.Length;
                        if (want <= have) { _phase = Phases.PlanePlan; Status = "Checking orbital planes."; break; }
                        Vec3 dv = at.Velocity.Normalized * (want - have);
                        NodePlanner.ClearNodes(v);
                        if (NodePlanner.AddFrameDeltaV(v, apUt, dv) == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        _burn = new NodeExecutor();
                        _burn.Begin(ctx);
                        if (_burn.Failed) { Fail(_burn.FailReason, ctx); break; }
                        _phase = Phases.RaiseBurn;
                        Status = "Raising periapsis: " + Math.Round(dv.Length) + " m/s at apoapsis.";
                        line = "Periapsis " + Math.Round(v.orbit.PeA / 1000) + " km is in the air: raising it to " + Math.Round(_raiseTo / 1000) + " km, " + Math.Round(dv.Length) + " m/s at apoapsis.";
                    }
                    break;
                case Phases.RaiseBurn:
                    line = RunBurn(ctx, Phases.PlanePlan, "Checking orbital planes.");
                    break;
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
                        if (StationOps.Distance(v, _target) < CloseRange) { _phase = Phases.ClosePlan; break; }
                        double period = v.orbit.period;
                        if (double.IsNaN(period) || period <= 0) { Fail("not in a closed orbit", ctx); break; }
                        double mu = v.mainBody.gravParameter;
                        // Never an arc through the air (0.8.163): the cheapest intercept between two low orbits ran through Kerbin.
                        double floor = v.mainBody.Radius + v.mainBody.atmosphereDepth + 10000;
                        TransferPlan plan = TransferPlanner.Plan(OrbitAdapter.Sampler(v.orbit), OrbitAdapter.Sampler(_target.orbit), mu,
                            now + 120, now + 2.5 * period, 0.3 * period, 1.5 * period, 60, 30, 1.0, floor);
                        if (plan == null || double.IsNaN(plan.DeltaVMagnitude) || plan.DepartureUt <= now)
                        {
                            if (!_phased) { _phase = Phases.PhasePlan; Status = "No direct intercept; planning a phasing orbit."; break; }
                            Fail("no intercept found", ctx); break;
                        }
                        if (!_phased && plan.DeltaVMagnitude > MaxDirectIntercept)
                        {
                            _phase = Phases.PhasePlan;
                            Status = "Direct intercept costs " + Math.Round(plan.DeltaVMagnitude) + " m/s; planning a phasing orbit.";
                            break;
                        }
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
                case Phases.PhasePlan:
                    {
                        _phased = true;
                        double mu = v.mainBody.gravParameter;
                        double floor = v.mainBody.Radius + v.mainBody.atmosphereDepth + 10000;
                        double nodeUt = now + 90;
                        StateVector us = OrbitAdapter.StateAt(v.orbit, nodeUt), them = OrbitAdapter.StateAt(_target.orbit, nodeUt);
                        PhasingPlan plan = Phasing.Plan(mu, us.Position, us.Velocity, them.Position, _target.orbit.period, floor, v.mainBody.sphereOfInfluence * 0.5, MaxPhasingOrbits);
                        if (plan == null) { _phase = Phases.InterceptPlan; Status = "No phasing orbit fits; trying a direct intercept."; break; }
                        NodePlanner.ClearNodes(v);
                        if (NodePlanner.AddFrameDeltaV(v, nodeUt, plan.DeltaV) == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        _phaseEnd = nodeUt + plan.Orbits * plan.Period - 0.5 * plan.Period;
                        _burn = new NodeExecutor();
                        _burn.Begin(ctx);
                        if (_burn.Failed) { Fail(_burn.FailReason, ctx); break; }
                        _phase = Phases.PhaseBurn;
                        Status = "Phasing burn: " + Math.Round(plan.DeltaVMagnitude) + " m/s.";
                        line = "Phasing: " + Math.Round(plan.DeltaVMagnitude) + " m/s to a " + Math.Round((plan.Apoapsis - v.mainBody.Radius) / 1000) + " x " + Math.Round((plan.Periapsis - v.mainBody.Radius) / 1000)
                            + " km orbit, " + plan.Orbits + " orbit" + (plan.Orbits == 1 ? string.Empty : "s") + " until the station comes round.";
                    }
                    break;
                case Phases.PhaseBurn:
                    line = RunBurn(ctx, Phases.PhaseCoast, "Phasing orbit; waiting for the station.");
                    break;
                case Phases.PhaseCoast:
                    if (now >= _phaseEnd)
                    {
                        c.StopWarp();
                        _phase = Phases.InterceptPlan;
                        Status = "Station nearly in phase; planning the intercept.";
                    }
                    else
                    {
                        Status = "Phasing orbit, " + Math.Round((_phaseEnd - now) / 60) + " min until the intercept.";
                        if (_phaseEnd - now > 30) c.WarpTo(_phaseEnd); else if (c.IsWarping) c.StopWarp();
                    }
                    break;
                case Phases.ClosePlan:
                    {
                        // The last few km (0.8.165): push straight at the station at a speed that gets there in about
                        // five minutes, coast, then kill the relative velocity on arrival. 5 km at 1 m/s had been
                        // "missed" and replanned as a fresh 8-orbit phasing.
                        if (_closes >= MaxCloses) { Fail("rendezvous missed", ctx); break; }
                        double nodeUt = now + 60;
                        StateVector us = OrbitAdapter.StateAt(v.orbit, nodeUt), them = OrbitAdapter.StateAt(_target.orbit, nodeUt);
                        Vec3 toThem = them.Position - us.Position;
                        if (toThem.Length < 1) { _phase = Phases.Check; break; }
                        // Aim short of the station, never at it: the engine must not fly the ship into the port.
                        Vec3 toAim = toThem - toThem.Normalized * CloseStandoff;
                        double dist = toAim.Length;
                        if (dist < 50 || Vec3.Dot(toAim, toThem) <= 0) { _phase = Phases.Check; break; }
                        _shortClose = dist < ShortRange;
                        double speed = _shortClose ? Math.Max(2.0, Math.Min(10.0, dist / ShortTransit)) : Math.Max(0.5, Math.Min(20.0, dist / 300.0));
                        Vec3 dv = toAim.Normalized * speed - (us.Velocity - them.Velocity);
                        NodePlanner.ClearNodes(v);
                        if (NodePlanner.AddFrameDeltaV(v, nodeUt, dv) == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        _arrivalUt = nodeUt + dist / speed;
                        _closes++;
                        _burn = new NodeExecutor();
                        _burn.Begin(ctx);
                        if (_burn.Failed) { Fail(_burn.FailReason, ctx); break; }
                        _phase = Phases.CloseBurn;
                        Status = "Closing burn: " + Math.Round(dv.Length) + " m/s.";
                        line = "Closing the last " + Math.Round(dist / 100) / 10 + " km at " + Math.Round(speed) + " m/s (" + Math.Round(dv.Length) + " m/s burn, " + Math.Round((_arrivalUt - now) / 60) + " min).";
                    }
                    break;
                case Phases.CloseBurn:
                    line = RunBurn(ctx, Phases.Coast, "Closing on the station.");
                    if (_phase == Phases.Coast)
                    {
                        double burnTime = c.BurnTime(MatchDeltaV(v).Length);
                        if (double.IsInfinity(burnTime) || double.IsNaN(burnTime)) burnTime = 60;
                        // A short push leaves a minute to turn round for the match (0.8.198).
                        _coastEnd = _arrivalUt - Math.Max(_shortClose ? 60 : 30, burnTime + 15);
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
                        // Match where the paths really come closest (0.8.174): a short intercept burn left the ship 48 km from the
                        // station at the planned time, and a "match" there was 145 m/s that put the periapsis 596 km underground.
                        double caUt, caDist;
                        ClosestApproach(v, now, _arrivalUt + 600, out caUt, out caDist);
                        if (caDist > CloseRange || caUt <= now + 5)
                        {
                            _phase = Phases.Check;
                            line = "Closest approach is " + Math.Round(caDist / 1000) + " km, not a meeting; replanning.";
                            break;
                        }
                        _arrivalUt = caUt;
                        Vec3 dv = MatchDeltaV(v);
                        if (dv.Length < 0.5) { _phase = Phases.Check; break; }
                        NodePlanner.ClearNodes(v);
                        ManeuverNode match = NodePlanner.AddFrameDeltaV(v, _arrivalUt, dv);
                        if (match == null) { Fail("maneuver nodes unavailable", ctx); break; }
                        double airTop = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
                        if (match.nextPatch != null && match.nextPatch.PeA < airTop + 5000)
                        {
                            NodePlanner.ClearNodes(v);
                            _phase = Phases.Check;
                            line = "A velocity match here would drop the periapsis into the air; replanning.";
                            break;
                        }
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
                        double airAbove = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
                        if (dist < DoneDistance && speed < DoneSpeed)
                        {
                            Status = "Alongside " + StationOps.NameOf(_target) + ".";
                            line = "Rendezvous: " + Math.Round(dist) + " m from " + StationOps.NameOf(_target) + ", closing at " + speed.ToString("0.0") + " m/s.";
                            ctx.Say(line, "Rendezvous:" + StationOps.BodyOf(v));
                            State = LegState.Done;
                        }
                        else if (v.orbit.PeA < airAbove + 5000 && v.orbit.ApA > airAbove + 5000 && _raises < 2)
                        {
                            // A burn left the periapsis in the air (0.8.174): raise it before any other plan, or the next intercept is from a falling ship.
                            _raises++;
                            _raiseTo = airAbove + 15000;
                            _phase = Phases.RaisePlan;
                            Status = "Periapsis in the air; raising it first.";
                            line = "Periapsis " + Math.Round(v.orbit.PeA / 1000) + " km is in the air; raising it before another try.";
                        }
                        else if (dist < CloseRange)
                        {
                            _phase = Phases.ClosePlan;
                            Status = "Short by " + Math.Round(dist / 100) / 10 + " km; closing.";
                            line = "Short by " + Math.Round(dist / 100) / 10 + " km at " + Math.Round(speed) + " m/s; closing in.";
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
        /// <summary>When and how close the two paths come, sampled every 10 s between the two times, then refined to the second.</summary>
        private void ClosestApproach(Vessel v, double from, double to, out double ut, out double dist)
        {
            ut = from; dist = double.PositiveInfinity;
            for (double t = from; t <= to; t += 10)
            {
                double d = (OrbitAdapter.StateAt(_target.orbit, t).Position - OrbitAdapter.StateAt(v.orbit, t).Position).Length;
                if (d < dist) { dist = d; ut = t; }
            }
            double lo = Math.Max(from, ut - 10), hi = Math.Min(to, ut + 10);
            for (double t = lo; t <= hi; t += 1)
            {
                double d = (OrbitAdapter.StateAt(_target.orbit, t).Position - OrbitAdapter.StateAt(v.orbit, t).Position).Length;
                if (d < dist) { dist = d; ut = t; }
            }
        }

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
        /// <summary>Sideways clearance when we are behind the port plane: go round the station, never through it (0.8.168).</summary>
        private const double KeepOut = 60;
        private const double Gain = 0.5;
        private const double Deadband = 0.05;

        private Phases _phase;
        private Vessel _target;
        private ModuleDockingNode _ourPort, _targetPort;
        private double _started;
        private bool _targetModeOk;
        private bool _sasOnPort;
        private double _lastSasRefresh;
        private VesselControl _stationControl;
        private double _stationNoted = -1e9;
        private readonly List<ModuleRCS> _rotationOff = new List<ModuleRCS>();
        private double _monoAtStart;

        public string Name { get { return "Docking"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        private void Fail(string why, FlightContext ctx)
        {
            State = LegState.Failed;
            Status = "Docking failed: " + why + ".";
            RestoreRcs();
            if (ctx != null) ctx.Say("Docking failed: " + why + ".");
        }

        /// <summary>Gives the thrusters their rotation back once the dock is over, however it ended.</summary>
        private void RestoreRcs()
        {
            foreach (ModuleRCS r in _rotationOff) if (r != null) r.enablePitch = r.enableYaw = r.enableRoll = true;
            _rotationOff.Clear();
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
                if (Mono(v) < 0.5) { Fail("no monopropellant aboard; docking needs RCS", ctx); return; }
                _monoAtStart = Mono(v);
                // Translation only (0.8.171): with RCS on, stock SAS fires the thrusters to turn the ship too, and swinging a
                // 40 t stack onto the port spent 80 monopropellant in three minutes. The reaction wheel does the turning.
                _rotationOff.Clear();
                foreach (ModuleRCS r in v.FindPartModulesImplementing<ModuleRCS>())
                {
                    if (!(r.enablePitch || r.enableYaw || r.enableRoll)) continue;
                    r.enablePitch = r.enableYaw = r.enableRoll = false;
                    _rotationOff.Add(r);
                }
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
            if (bestOurs == null) { Fail("no port on this craft matches a port on " + StationOps.NameOf(_target) + " (" + ours[0].part.partInfo.title + " to " + theirs[0].part.partInfo.title + ")", ctx); return false; }
            _ourPort = bestOurs; _targetPort = bestTheirs;
            ctx.Control.ControlFrom(_ourPort);
            ctx.Say("Docking " + _ourPort.part.partInfo.title + " to " + _targetPort.part.partInfo.title + " on " + StationOps.NameOf(_target) + ".");
            // The station turns its port toward us (0.8.168, T): its reaction wheel does the pointing, so we come straight in
            // instead of going round the back on thrusters. Works through SAS's rotation lock on the loaded station.
            try
            {
                if (_target.GetCrewCount() == 0 && _target.Autopilot != null && _target.Autopilot.SAS != null)
                {
                    _stationControl = new VesselControl(_target);
                    _stationControl.ControlFrom(_targetPort);
                    ModuleDockingNode ourNode = _ourPort, theirNode = _targetPort;
                    _stationControl.PointAlong(() => (Vector3d)ourNode.nodeTransform.position - (Vector3d)theirNode.nodeTransform.position);
                    ctx.Say(StationOps.NameOf(_target) + " is turning its port toward us.");
                }
            }
            catch (Exception e) { KsaCore.Log.Warn("Station aim: " + e.Message); _stationControl = null; }
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
            RestoreRcs();
            KsaCore.Log.Info("Docking used " + Math.Round(_monoAtStart - Mono(ctx.Vessel), 1) + " monopropellant of " + Math.Round(_monoAtStart, 1));
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
            if (Mono(v) < 0.2) { c.ClearInputs(); Fail("out of monopropellant", ctx); return null; }
            if (now - _started > TimeoutSeconds) { c.ClearInputs(); Fail("docking timed out", ctx); return null; }
            double vesselDist = StationOps.Distance(v, _target);
            if (vesselDist > AbortDistance) { c.ClearInputs(); Fail("drifted " + Math.Round(vesselDist / 1000.0, 1) + " km from the station", ctx); return null; }

            // Ports become available once the station loads (physics range); until then approach its centre.
            bool havePorts = SelectPorts(ctx);
            if (State != LegState.Running) return null;

            string line = null;
            Vector3d relVel = StationOps.RelativeVelocity(v, _target);
            Vector3d desired;
            if (havePorts && _stationControl != null)
            {
                _stationControl.Drive();
                if (now - _stationNoted > 15)
                {
                    _stationNoted = now;
                    double off = Vector3d.Angle(_targetPort.nodeTransform.forward, (Vector3d)_ourPort.nodeTransform.position - (Vector3d)_targetPort.nodeTransform.position);
                    KsaCore.Log.Info("Station aim: port " + Math.Round(off) + " deg off us, station turning " + VesselControl.TurnRate(_target).ToString("0.0") + " deg/s; " + Status + " RCS " + Math.Round(Mono(v), 1) + " of " + Math.Round(_monoAtStart, 1) + " left");
                }
            }

            if (!havePorts || _phase == Phases.Approach)
            {
                Vector3d from = havePorts ? (Vector3d)_ourPort.nodeTransform.position : v.GetWorldPos3D();
                Vector3d aim = havePorts ? AimPoint(from) : _target.GetWorldPos3D();
                Vector3d toAim = aim - from;
                double dist = toAim.magnitude;
                HoldSas(ctx, false);
                double speed = Clamp(vesselDist / 60.0, 0.3, 2.0) * (ctx.Crew != null ? ctx.Crew.ApproachFactor : 1.0);
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
                    // Behind the port plane: round the side first, never a straight line through the station (0.8.168).
                    Vector3d point = axial < 10 ? AimPoint(ourPos) : tgtPos + tgtFwd * (axial > Standoff ? Standoff : holdAxial);
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

        /// <summary>The standoff point in front of the port, or, while we are behind the port plane, a point beside it 60 m off the axis.</summary>
        private Vector3d AimPoint(Vector3d from)
        {
            Vector3d tgtPos = _targetPort.nodeTransform.position;
            Vector3d fwd = ((Vector3d)_targetPort.nodeTransform.forward).normalized;
            Vector3d rel = from - tgtPos;
            double axial = Vector3d.Dot(rel, fwd);
            if (axial >= 10) return StandoffPoint();
            Vector3d lateral = rel - fwd * axial;
            Vector3d side = lateral.magnitude > 1 ? lateral.normalized : Vector3d.Cross(fwd, Vector3d.up).normalized;
            if (side.magnitude < 0.5) side = Vector3d.Cross(fwd, Vector3d.right).normalized;
            return tgtPos + fwd * Standoff + side * KeepOut;
        }

        private static double Mono(Vessel v)
        {
            double total = 0;
            if (v == null) return 0;
            foreach (Part p in v.parts) { PartResource r = p.Resources.Get("MonoPropellant"); if (r != null) total += r.amount; }
            return total;
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
            RestoreRcs();
            ctx.Control.Throttle = 0;
            ctx.Control.ClearInputs();
            ctx.Control.StopWarp();
        }
    }
}
