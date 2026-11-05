using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;

namespace KSA.KSP.Flight
{
    /// <summary>Bridges KSP orbits to the portable math in KSA.Core.Flight.Orbital. Everything stays in KSP's internal orbit frame.</summary>
    public static class OrbitAdapter
    {
        public static Vec3 ToVec(Vector3d v) { return new Vec3(v.x, v.y, v.z); }
        public static Vector3d ToVector(Vec3 v) { return new Vector3d(v.X, v.Y, v.Z); }

        /// <summary>State relative to the orbit's reference body at a universal time.</summary>
        public static StateVector StateAt(Orbit o, double ut)
        {
            return new StateVector(ToVec(o.getRelativePositionAtUT(ut)), ToVec(o.getOrbitalVelocityAtUT(ut)));
        }

        /// <summary>A sampler for the planner: the orbit's state over time, relative to its reference body.</summary>
        public static Func<double, StateVector> Sampler(Orbit o) { return ut => StateAt(o, ut); }

        /// <summary>Orbit normal (Cross(pos, vel)) in the internal frame, unit length.</summary>
        public static Vec3 Normal(Orbit o, double ut)
        {
            StateVector s = StateAt(o, ut);
            return Vec3.Cross(s.Position, s.Velocity).Normalized;
        }

        /// <summary>Converts a frame delta-v at a time on an orbit into maneuver-node components (radial, normal, prograde).</summary>
        public static Vector3d ToNodeComponents(Orbit o, double ut, Vec3 deltaV)
        {
            StateVector s = StateAt(o, ut);
            Vec3 prograde = s.Velocity.Normalized;
            Vec3 normal = Vec3.Cross(s.Position, s.Velocity).Normalized;
            Vec3 radial = Vec3.Exclude(prograde, s.Position.Normalized).Normalized;
            return new Vector3d(Vec3.Dot(radial, deltaV), Vec3.Dot(normal, deltaV), Vec3.Dot(prograde, deltaV));
        }
    }

    /// <summary>
    /// Uses stock maneuver nodes as a trajectory oracle: add a node, read what the patched-conic solver predicts,
    /// keep it or throw it away. Also bisects burn sizes against a predicate ("periapsis at Mun between 20 and 60 km").
    /// </summary>
    public static class NodePlanner
    {
        public static bool Available(Vessel v) { return v != null && v.patchedConicSolver != null; }

        public static void ClearNodes(Vessel v)
        {
            if (!Available(v)) return;
            while (v.patchedConicSolver.maneuverNodes.Count > 0) v.patchedConicSolver.maneuverNodes[0].RemoveSelf();
        }

        /// <summary>Adds a node with the given (radial, normal, prograde) delta-v and updates the flight plan.</summary>
        public static ManeuverNode Add(Vessel v, double ut, Vector3d components)
        {
            if (!Available(v)) return null;
            ManeuverNode n = v.patchedConicSolver.AddManeuverNode(ut);
            n.DeltaV = components;
            v.patchedConicSolver.UpdateFlightPlan();
            return n;
        }

        /// <summary>Adds a node for a delta-v expressed in the orbit frame. Checks the predicted post-burn velocity and
        /// flips the normal component if KSP's sign convention disagrees with ours.</summary>
        public static ManeuverNode AddFrameDeltaV(Vessel v, double ut, Vec3 deltaV)
        {
            if (!Available(v)) return null;
            Vector3d comps = OrbitAdapter.ToNodeComponents(v.orbit, ut, deltaV);
            ManeuverNode n = Add(v, ut, comps);
            if (n == null) return null;
            Vec3 want = OrbitAdapter.StateAt(v.orbit, ut).Velocity + deltaV;
            double err = Error(n, ut, want);
            if (err > 1.0 && Math.Abs(comps.y) > 0.5)
            {
                Vector3d flipped = new Vector3d(comps.x, -comps.y, comps.z);
                n.RemoveSelf();
                n = Add(v, ut, flipped);
                double err2 = Error(n, ut, want);
                if (err2 > err) { n.RemoveSelf(); n = Add(v, ut, comps); }
            }
            return n;
        }

        private static double Error(ManeuverNode n, double ut, Vec3 wantVelocity)
        {
            if (n == null || n.nextPatch == null) return double.PositiveInfinity;
            Vec3 got = OrbitAdapter.StateAt(n.nextPatch, ut).Velocity;
            return (got - wantVelocity).Length;
        }

        /// <summary>The patches after a node: the burn orbit, then each SOI change in order.</summary>
        public static List<Orbit> PatchesAfter(ManeuverNode n)
        {
            var list = new List<Orbit>();
            Orbit o = n != null ? n.nextPatch : null;
            int guard = 0;
            while (o != null && guard++ < 8)
            {
                list.Add(o);
                if (o.patchEndTransition == Orbit.PatchTransitionType.FINAL || o.patchEndTransition == Orbit.PatchTransitionType.INITIAL) break;
                o = o.nextPatch;
                if (o != null && !o.activePatch) break;
            }
            return list;
        }

        /// <summary>First predicted patch around the body, or null when the trajectory never gets there.</summary>
        public static Orbit PatchAround(ManeuverNode n, CelestialBody body)
        {
            foreach (Orbit o in PatchesAfter(n)) if (o.referenceBody == body && TransferSupport.ReachesPeriapsis(o)) return o;
            return null;
        }

        /// <summary>Periapsis altitude at the body after the node, or NaN when there is no encounter.</summary>
        public static double PeriapsisAt(ManeuverNode n, CelestialBody body)
        {
            Orbit o = PatchAround(n, body);
            return o != null ? o.PeA : double.NaN;
        }

        /// <summary>
        /// Finds the smallest burn along a fixed direction (unit node components) that makes <paramref name="score"/>
        /// fall within [lo, hi]. score receives the trial node. Bisection between 0 and maxDv; the node is removed afterwards.
        /// Returns NaN when nothing in range works.
        /// </summary>
        public static double Bisect(Vessel v, double ut, Vector3d direction, double maxDv, Func<ManeuverNode, double> score, double lo, double hi)
        {
            if (!Available(v) || direction.magnitude < 1e-6) return double.NaN;
            direction = direction.normalized;
            double a = 0, b = maxDv;
            double sa = Trial(v, ut, direction * a, score);
            double sb = Trial(v, ut, direction * b, score);
            if (!double.IsNaN(sa) && sa >= lo && sa <= hi) return a;
            bool rising = sb > sa;
            if (double.IsNaN(sa) || double.IsNaN(sb)) return double.NaN;
            double target = (lo + hi) / 2;
            // Does the range even cross the band?
            if ((rising && (sa > hi || sb < lo)) || (!rising && (sa < lo || sb > hi))) return double.NaN;
            for (int i = 0; i < 18; i++)
            {
                double m = (a + b) / 2;
                double sm = Trial(v, ut, direction * m, score);
                if (double.IsNaN(sm)) { b = m; continue; }
                if (sm >= lo && sm <= hi) return m;
                if ((sm < target) == rising) a = m; else b = m;
            }
            double final = (a + b) / 2;
            double sf = Trial(v, ut, direction * final, score);
            return !double.IsNaN(sf) && sf >= lo * 0.5 && sf <= hi * 1.5 ? final : double.NaN;
        }

        private static double Trial(Vessel v, double ut, Vector3d components, Func<ManeuverNode, double> score)
        {
            ManeuverNode n = Add(v, ut, components);
            if (n == null) return double.NaN;
            double s;
            try { s = score(n); } catch { s = double.NaN; }
            n.RemoveSelf();
            return s;
        }
    }

    /// <summary>
    /// Flies the vessel's next maneuver node: warps to the burn, points along it with SAS Maneuver mode, burns with
    /// a taper, auto-stages, and removes the node. Used by every leg that plans with nodes.
    /// </summary>
    public sealed class NodeExecutor
    {
        private enum Phases { Align, Warp, Burn, Done, Failed }
        private Phases _phase;
        private double _burnTime;
        private double _startDv;
        private double _started;
        private Vector3d _lastBurnVector;
        private bool _burnTimed;
        private double _tolerance = 0.3;
        private double _lateBy;
        private double _burned, _lastStep;
        private double _stagedAt = -1e9;
        // The node's burn vector is read off the orbit it was placed on; once the patch changes under it (an ejection crossing to
        // escape) that vector drifts and flips, and the burn ended short (0.8.177: 661 of 884 m/s, 223 short, no Duna encounter;
        // the earlier flight 1,044 of 1,063). From then on the direction is frozen and the burn runs on the delta-v counted.
        private CelestialBody _bodyAtStart;
        private bool _hyperbolicAtStart, _frozen;
        private Vector3d _frozenDir;
        // The engine ran dry mid-burn and the thrusters finish it (0.8.205, 2026-11-04: 51 m/s short of a Kerbin encounter in deep space
        // with 168 monopropellant aboard; a crew would push on the RCS, not sit there). Forward thrust is TransZ = -1 (the docking leg's frame).
        private bool _rcs;
        private double _rcsFrom;
        private const double RcsMinPropellant = 5;
        /// <summary>A burn under MinBurn done on the thrusters from the attitude held, no turn (0.8.213).</summary>
        private bool _nudge;

        private string Nudge(Vessel v, VesselControl c, ManeuverNode n, Vector3d burn, double now)
        {
            if (!_rcs) { _rcs = true; _rcsFrom = now; c.Rcs(true); }
            c.Throttle = 0;
            double dt = Math.Max(0, now - _lastStep);
            _burned += v.geeForce * 9.80665 * dt;
            _lastStep = now;
            double remaining = burn.magnitude;
            bool done = remaining < _tolerance || _burned >= _startDv * 1.05 || Vector3d.Dot(burn, _lastBurnVector) < 0;
            _lastBurnVector = burn;
            if (done || now - _rcsFrom > 60 || c.RcsPropellant() < 0.5)
            {
                c.ClearInputs();
                c.Rcs(false);
                n.RemoveSelf();
                if (done) { _phase = Phases.Done; Status = "Nudge complete."; return "Nudge complete (" + (_startDv - remaining).ToString("0.00") + " of " + _startDv.ToString("0.00") + " m/s on the thrusters)."; }
                Fail("the nudge did not finish (" + remaining.ToString("0.00") + " m/s to go)");
                return "The nudge on the thrusters did not finish; " + remaining.ToString("0.00") + " m/s to go.";
            }
            Transform rt = v.ReferenceTransform;
            if (rt == null) { c.ClearInputs(); return null; }
            Vector3d dir = burn.normalized;
            double push = remaining < 0.08 ? 0.3 : 1.0;
            // Stock ModuleRCS accelerates the craft along -(X*right + Z*up + Y*forward), so the inputs are negated (the docking leg's frame).
            c.TransX = (float)(-push * Vector3d.Dot(dir, rt.right));
            c.TransY = (float)(-push * Vector3d.Dot(dir, rt.forward));
            c.TransZ = (float)(-push * Vector3d.Dot(dir, rt.up));
            Status = "Nudging on the thrusters: " + remaining.ToString("0.00") + " m/s to go.";
            return null;
        }

        public bool Done { get { return _phase == Phases.Done; } }
        public bool Failed { get { return _phase == Phases.Failed; } }
        public string Status { get; private set; }
        public string FailReason { get; private set; }
        /// <summary>The burn never started: the path runs into the ground before it (0.8.199). The leg replans.</summary>
        public bool GroundAhead { get; private set; }
        /// <summary>The engine is lit on the node.</summary>
        public bool Burning { get { return _phase == Phases.Burn; } }

        /// <summary>Ends the burn early whatever the node says (a long burn drifts off its node; the transfer stops on its apoapsis).</summary>
        public Func<Vessel, bool> CutOff { get; set; }

        public ManeuverNode Node(Vessel v)
        {
            return NodePlanner.Available(v) && v.patchedConicSolver.maneuverNodes.Count > 0 ? v.patchedConicSolver.maneuverNodes[0] : null;
        }

        public void Begin(FlightContext ctx)
        {
            ManeuverNode n = Node(ctx.Vessel);
            if (n == null) { Fail("no maneuver node to execute"); return; }
            string line = null;
            ctx.Control.EnsureEngine(ref line);
            _startDv = n.DeltaV.magnitude;
            // A node too small to burn (0.8.189): pointing SAS at a node of no length set the craft spinning on its direction noise,
            // three times running at the mid-course check. The next correction, nearer the target, takes what is left.
            if (_startDv < MinBurn)
            {
                // A nudge on the thrusters instead (0.8.213): no turn at all, the thrusters push along the node from whatever attitude the
                // craft holds, so the SAS noise of a node of no length never comes into it. A 0.10 m/s trim 185 days out had been left to
                // "the next correction" with the path through Kerbin's centre.
                if (ctx.Control.HasRcs() && ctx.Control.RcsPropellant() > 1)
                {
                    _nudge = true;
                    _tolerance = Math.Max(0.02, _startDv / 4);
                    _burnTime = 5;
                    _bodyAtStart = ctx.Vessel != null ? ctx.Vessel.mainBody : null;
                    _hyperbolicAtStart = ctx.Vessel != null && ctx.Vessel.orbit != null && ctx.Vessel.orbit.eccentricity >= 1;
                    _lateBy = 0; _sunHold = false; _frozen = false;
                    _phase = Phases.Warp;
                    ctx.Control.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, false);
                    Status = "Nudging " + _startDv.ToString("0.00") + " m/s on the thrusters.";
                    return;
                }
                n.RemoveSelf();
                _phase = Phases.Done;
                Status = "Nothing to burn.";
                return;
            }
            _burnTime = ctx.Control.BurnTime(_startDv);
            if (double.IsInfinity(_burnTime)) _burnTime = 60;
            _phase = Phases.Align;
            _sunHold = false;
            _frozen = false;
            _bodyAtStart = ctx.Vessel != null ? ctx.Vessel.mainBody : null;
            _hyperbolicAtStart = ctx.Vessel != null && ctx.Vessel.orbit != null && ctx.Vessel.orbit.eccentricity >= 1;
            _tolerance = ctx.Crew != null ? Math.Max(0.1, ctx.Crew.BurnTolerance) : 0.3;
            _lateBy = 0;
            if (ctx.Crew != null && ctx.Crew.RollMistake(ctx.Rng)) { _lateBy = ctx.Crew.LateIgnition(ctx.Rng); ctx.Say(ctx.Crew.PilotName + " is slow on the ignition call: burn will start " + Math.Round(_lateBy) + " s late."); }
            Status = "Aligning for a " + Math.Round(_startDv) + " m/s burn.";
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.Maneuver, false);
        }

        private void Fail(string why) { _phase = Phases.Failed; FailReason = why; Status = "Burn failed: " + why; }

        /// <summary>A wait longer than this before the burn is spent broadside to the sun, panels lit; the turn comes after (0.8.176).</summary>
        private const double MinBurn = 0.3;
        private const double LongWait = 1200; // 600 until 0.8.179: a 40 t stack on a small wheel needs more than ten minutes to come round
        private const double AlignLimit = 5;
        private bool _sunHold, _sunSettled;
        private double _sunHoldAt;
        private double _lastWarpAsk = -1e9;

        /// <summary>One physics step. Returns a log line or null.</summary>
        public string Step(FlightContext ctx)
        {
            if (_phase == Phases.Done || _phase == Phases.Failed) return null;
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            ManeuverNode n = Node(v);
            if (n == null) { Fail("node vanished"); return "Maneuver node lost; burn skipped."; }
            double now = ctx.Now;
            double ignition = KSA.Core.Flight.Sim.BurnRules.Ignition(n.UT, _burnTime, _lateBy);
            Vector3d burn = n.GetBurnVector(v.orbit);
            string line = null;
            // The ground before the burn (0.8.199, 2026-11-04: an Ike pass two days before the Duna capture bent the path into the
            // ground; the node kept its time, the crew warped to it and hit the surface at 632 m/s, three dead). While waiting, a
            // periapsis under the surface whose ground comes before the burn would end stops the warp and hands the leg the problem.
            if ((_phase == Phases.Align || _phase == Phases.Warp) && v.orbit.PeA < 0 && v.orbit.timeToPe > 0)
            {
                double groundUt = AirbrakeLeg.TimeToAir(v, 0, now);
                if (groundUt > 0 && groundUt < ignition + Math.Max(60, _burnTime))
                {
                    c.StopWarp();
                    GroundAhead = true;
                    Fail("the path hits " + TransferSupport.BodyName(v.mainBody) + " before the burn");
                    return "The path runs into " + TransferSupport.BodyName(v.mainBody) + " in " + TransferSupport.Countdown(groundUt - now) + ", before the burn (periapsis " + TransferSupport.Km(v.orbit.PeA) + "); stopping the warp.";
                }
            }
            switch (_phase)
            {
                case Phases.Align:
                    RefreshBurnTime(c);
                    if (ignition - now > LongWait)
                    {
                        if (!_sunHold) { c.FacePanelsAtSun(); _sunHold = true; _sunHoldAt = now; _sunSettled = false; }
                        else if (_sunSettled && c.SunDrift() > 10) { c.StopWarp(); if (!v.packed) { c.FacePanelsAtSun(); _sunHoldAt = now; _sunSettled = false; line = "The sun has moved round the sky: turning the panels back to it."; } break; }
                        bool settled = (c.HoldSettled() && now - _sunHoldAt > 5) || now - _sunHoldAt > 120;
                        if (settled) _sunSettled = true;
                        Status = "Burn in " + TransferSupport.Countdown(ignition - now) + (settled ? "; panels to the sun." : "; turning the panels to the sun.");
                        if (settled && !c.IsWarping && now - _lastWarpAsk > 2) { _lastWarpAsk = now; c.WarpTo(ignition - LongWait + 60); }
                        break;
                    }
                    if (_sunHold) { c.StopWarp(); _sunHold = false; }
                    c.Sas(VesselAutopilot.AutopilotMode.Maneuver, false);
                    if (c.AttitudeError(burn) < 5 || now > ignition - 5) { _phase = Phases.Warp; }
                    break;
                case Phases.Warp:
                    if (!_nudge) RefreshBurnTime(c);
                    if (!_nudge) c.Sas(VesselAutopilot.AutopilotMode.Maneuver, false);
                    if (now >= ignition)
                    {
                        c.StopWarp();
                        // A burn lit on rails gives no thrust (0.8.174: 43 m/s "started" under warp, 32 counted, the station missed by 48 km).
                        if (v.packed || c.IsWarping) { Status = "Waiting for warp to end."; break; }
                        if (_nudge)
                        {
                            _phase = Phases.Burn; _started = now; _burned = 0; _lastStep = now; _lastBurnVector = burn;
                            line = "Nudge started: " + _startDv.ToString("0.00") + " m/s on the thrusters, no turn.";
                            break;
                        }
                        // Never light pointing the wrong way (0.8.179): seven 1 m/s trims at the edge of Duna's space fired before the
                        // turn was done and walked the periapsis from 36 km to 139 km, out of the air. A late burn costs little; a
                        // wrong one costs the pass. Two minutes, or the burn's own length, of grace, then it goes as it is.
                        double off = c.AttitudeError(burn);
                        if (off > AlignLimit && now < ignition + Math.Max(120, _burnTime)) { Status = "Holding the burn: " + Math.Round(off) + " deg off the node."; break; }
                        if (off > AlignLimit) line = "Burning " + Math.Round(off) + " deg off the node; the turn never settled.";
                        c.EnsureEngine(ref line);
                        c.Throttle = 1f;
                        _phase = Phases.Burn; _started = now;
                        _burned = 0; _lastStep = now;
                        _lastBurnVector = burn;
                        Status = "Burning.";
                        line = line ?? ("Burn started: " + Math.Round(_startDv) + " m/s, " + Math.Round(_burnTime) + " s.");
                    }
                    else
                    {
                        Status = "Burn in " + Math.Round(ignition - now) + " s.";
                        if (ignition - now > 30) c.WarpTo(ignition - 15); else if (c.IsWarping) c.StopWarp();
                    }
                    break;
                case Phases.Burn:
                    if (v.packed) { c.StopWarp(); _lastStep = now; Status = "Waiting for warp to end."; break; }
                    if (_nudge) { line = Nudge(v, c, n, burn, now) ?? line; break; }
                    c.AutoStage(ref line);
                    if (line != null) { _stagedAt = now; _lastBurnVector = burn; } // a stage dropped mid-burn (0.8.165): the next engine carries on
                    bool justStaged = now - _stagedAt < 3;
                    if (!justStaged && (c.Throttle > 0 || _rcs)) _burned += v.geeForce * 9.80665 * Math.Max(0, now - _lastStep);
                    _lastStep = now;
                    if (!_burnTimed && c.MaxAcceleration() > 0) { double bt = c.BurnTime(burn.magnitude); if (!double.IsInfinity(bt)) { _burnTime = bt; _burnTimed = true; } }
                    bool patchChanged = v.mainBody != _bodyAtStart || (v.orbit != null && (v.orbit.eccentricity >= 1) != _hyperbolicAtStart);
                    if (patchChanged && !_frozen && _lastBurnVector.magnitude > 1e-3)
                    {
                        _frozen = true;
                        _frozenDir = _lastBurnVector.normalized;
                        c.PointAlong(() => _frozenDir);
                        line = line ?? ("The path crossed to " + (v.orbit != null && v.orbit.eccentricity >= 1 ? "escape" : "a new orbit") + " mid-burn; finishing the burn by the count, " + Math.Round(Math.Max(0, _startDv - _burned)) + " m/s to go.");
                    }
                    if (_frozen) burn = _frozenDir * Math.Max(0, _startDv - _burned);
                    double remaining = burn.magnitude;
                    // Taper: last few m/s at low throttle so we do not overshoot; cutoff when the vector flips.
                    // A long burn leaves the node behind and the vector never empties: stop on what was really burned
                    // (2026-11-01: a 1,047 m/s ejection under warp burned 1,567 m/s and went past Duna).
                    bool spent = KSA.Core.Flight.Sim.BurnRules.Spent(_burned, _startDv);
                    // The first second after ignition is also the first second off rails, and the orbit the node's vector is read from
                    // can be mid-reinitialisation (0.8.180: a 9 m/s Ike dodge "completed" at 0 of 9 a second after it lit, and was redone).
                    bool justLit = now - _started < 1.0;
                    bool flipped = !justStaged && !justLit && !_frozen && Vector3d.Dot(burn, _lastBurnVector) < 0;
                    bool overran = !justStaged && remaining < 2 && now - _started > _burnTime + 10;
                    if (spent || remaining < _tolerance || flipped || overran || (CutOff != null && CutOff(v)))
                    {
                        c.Throttle = 0;
                        if (_rcs) { c.TransZ = 0; c.Rcs(false); }
                        _phase = Phases.Done;
                        Status = "Burn complete.";
                        n.RemoveSelf();
                        line = "Burn complete (" + Math.Round(_startDv - remaining) + " of " + Math.Round(_startDv) + " m/s" + (_rcs ? ", the last on the thrusters" : string.Empty) + ").";
                        break;
                    }
                    if (_rcs)
                    {
                        // On the thrusters: push forward while the nose is on the node, and stop when the monopropellant is gone.
                        c.Throttle = 0;
                        c.TransZ = c.AttitudeError(burn) > 30 ? 0f : -1f;
                        Status = "Burning on the thrusters: " + Math.Round(remaining) + " m/s to go, " + Math.Round(c.RcsPropellant()) + " monopropellant.";
                        _lastBurnVector = burn;
                        if (c.RcsPropellant() < 0.5 || now - _rcsFrom > 1800)
                        {
                            c.TransZ = 0; c.Rcs(false);
                            n.RemoveSelf();
                            Fail("propellant and monopropellant exhausted with " + Math.Round(remaining) + " m/s to go");
                            line = "The thrusters ran dry too; " + Math.Round(remaining) + " m/s short.";
                            ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant and monopropellant exhausted mid-burn; " + Math.Round(remaining) + " m/s short.");
                        }
                        break;
                    }
                    double accel = c.MaxAcceleration();
                    float t = 1f;
                    if (accel > 0) { double full = remaining / accel; if (full < 2) t = (float)Math.Max(0.05, full / 2); }
                    c.Throttle = t;
                    if (c.AttitudeError(burn) > 30) c.Throttle = 0.1f;
                    _lastBurnVector = burn;
                    if (c.NoThrustAvailable() && now - _started > 5 && !justStaged)
                    {
                        c.Throttle = 0;
                        if (c.HasRcs() && c.RcsPropellant() > RcsMinPropellant)
                        {
                            _rcs = true; _rcsFrom = now;
                            c.Rcs(true);
                            line = "Propellant exhausted with " + Math.Round(remaining) + " m/s to go: finishing the burn on the RCS thrusters (" + Math.Round(c.RcsPropellant()) + " monopropellant aboard).";
                            break;
                        }
                        n.RemoveSelf();
                        Fail("propellant exhausted with " + Math.Round(remaining) + " m/s to go");
                        line = "Propellant exhausted mid-burn; " + Math.Round(remaining) + " m/s short.";
                        ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted mid-burn; " + Math.Round(remaining) + " m/s short.");
                    }
                    else if (now - _started > _burnTime * 3 + 60)
                    {
                        c.Throttle = 0;
                        n.RemoveSelf();
                        Fail("burn ran far over time");
                        line = "Burn aborted: engines underperforming.";
                    }
                    break;
            }
            return line;
        }

        /// <summary>Begin may run before the staged engine reports thrust; take the real burn time as soon as it is known.</summary>
        private void RefreshBurnTime(VesselControl c)
        {
            if (_burnTimed) return;
            double bt = c.BurnTime(_startDv);
            if (!double.IsInfinity(bt) && bt > 0) { _burnTime = bt; _burnTimed = true; }
        }

        public void End(FlightContext ctx) { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); if (_rcs) { ctx.Control.ClearInputs(); ctx.Control.Rcs(false); } }
    }
}
