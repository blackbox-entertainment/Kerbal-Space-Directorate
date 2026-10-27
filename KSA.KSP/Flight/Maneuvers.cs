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
            foreach (Orbit o in PatchesAfter(n)) if (o.referenceBody == body) return o;
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

        public bool Done { get { return _phase == Phases.Done; } }
        public bool Failed { get { return _phase == Phases.Failed; } }
        public string Status { get; private set; }
        public string FailReason { get; private set; }

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
            _burnTime = ctx.Control.BurnTime(_startDv);
            if (double.IsInfinity(_burnTime)) _burnTime = 60;
            _phase = Phases.Align;
            _tolerance = ctx.Crew != null ? Math.Max(0.1, ctx.Crew.BurnTolerance) : 0.3;
            _lateBy = 0;
            if (ctx.Crew != null && ctx.Crew.RollMistake(ctx.Rng)) { _lateBy = ctx.Crew.LateIgnition(ctx.Rng); ctx.Say(ctx.Crew.PilotName + " is slow on the ignition call: burn will start " + Math.Round(_lateBy) + " s late."); }
            Status = "Aligning for a " + Math.Round(_startDv) + " m/s burn.";
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.Maneuver, false);
        }

        private void Fail(string why) { _phase = Phases.Failed; FailReason = why; Status = "Burn failed: " + why; }

        /// <summary>One physics step. Returns a log line or null.</summary>
        public string Step(FlightContext ctx)
        {
            if (_phase == Phases.Done || _phase == Phases.Failed) return null;
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            ManeuverNode n = Node(v);
            if (n == null) { Fail("node vanished"); return "Maneuver node lost; burn skipped."; }
            double now = ctx.Now;
            double ignition = n.UT - _burnTime / 2 + _lateBy;
            Vector3d burn = n.GetBurnVector(v.orbit);
            string line = null;
            switch (_phase)
            {
                case Phases.Align:
                    RefreshBurnTime(c);
                    c.Sas(VesselAutopilot.AutopilotMode.Maneuver, false);
                    if (c.AttitudeError(burn) < 5 || now > ignition - 5) { _phase = Phases.Warp; }
                    break;
                case Phases.Warp:
                    RefreshBurnTime(c);
                    c.Sas(VesselAutopilot.AutopilotMode.Maneuver, false);
                    if (now >= ignition)
                    {
                        c.StopWarp();
                        c.EnsureEngine(ref line);
                        c.Throttle = 1f;
                        _phase = Phases.Burn; _started = now;
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
                    c.AutoStage(ref line);
                    if (!_burnTimed && c.MaxAcceleration() > 0) { double bt = c.BurnTime(burn.magnitude); if (!double.IsInfinity(bt)) { _burnTime = bt; _burnTimed = true; } }
                    double remaining = burn.magnitude;
                    // Taper: last few m/s at low throttle so we do not overshoot; cutoff when the vector flips.
                    if (remaining < _tolerance || Vector3d.Dot(burn, _lastBurnVector) < 0 || (remaining < 2 && now - _started > _burnTime + 10))
                    {
                        c.Throttle = 0;
                        _phase = Phases.Done;
                        Status = "Burn complete.";
                        n.RemoveSelf();
                        line = "Burn complete (" + Math.Round(_startDv - remaining) + " of " + Math.Round(_startDv) + " m/s).";
                        break;
                    }
                    double accel = c.MaxAcceleration();
                    float t = 1f;
                    if (accel > 0) { double full = remaining / accel; if (full < 2) t = (float)Math.Max(0.05, full / 2); }
                    c.Throttle = t;
                    if (c.AttitudeError(burn) > 30) c.Throttle = 0.1f;
                    _lastBurnVector = burn;
                    if (c.NoThrustAvailable() && now - _started > 5)
                    {
                        c.Throttle = 0;
                        n.RemoveSelf();
                        Fail("propellant exhausted with " + Math.Round(remaining) + " m/s to go");
                        line = "Propellant exhausted mid-burn; " + Math.Round(remaining) + " m/s short.";
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

        public void End(FlightContext ctx) { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); }
    }
}
