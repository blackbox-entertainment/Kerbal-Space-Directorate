using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Shared helpers for the transfer legs (KSA-51): patch walking, trajectory line searches with trial nodes,
    /// and small formatting helpers. Everything here is tolerant of null orbits and missing patched-conic solvers.
    /// </summary>
    internal static class TransferSupport
    {
        public static double Day { get { return KSPUtil.dateTimeFormatter != null && KSPUtil.dateTimeFormatter.Day > 0 ? KSPUtil.dateTimeFormatter.Day : 21600; } }

        public static string Km(double metres) { return Math.Round(metres / 1000) + " km"; }

        public static string Date(double ut)
        {
            try { return KSPUtil.PrintDate(ut, true); }
            catch { return Math.Round(ut) + " s"; }
        }

        public static string Countdown(double seconds)
        {
            if (seconds < 0) seconds = 0;
            double day = Day;
            if (seconds >= day) return Math.Floor(seconds / day) + " d " + Math.Floor((seconds % day) / 3600) + " h";
            if (seconds >= 3600) return Math.Floor(seconds / 3600) + " h " + Math.Floor((seconds % 3600) / 60) + " min";
            if (seconds >= 60) return Math.Floor(seconds / 60) + " min " + Math.Floor(seconds % 60) + " s";
            return Math.Round(seconds) + " s";
        }

        public static string BodyName(CelestialBody b) { return b != null ? b.bodyName : "unknown body"; }

        public static bool IsClosed(Orbit o) { return o != null && o.eccentricity < 1 && !double.IsNaN(o.period) && o.period > 0; }

        public static bool EndsInTransition(Orbit o)
        {
            return o != null && (o.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER || o.patchEndTransition == Orbit.PatchTransitionType.ESCAPE);
        }

        /// <summary>Walks the vessel's own predicted patches (no maneuver node) for the first one around the body.</summary>
        public static Orbit VesselPatchAround(Vessel v, CelestialBody body)
        {
            if (v == null || v.orbit == null || body == null) return null;
            Orbit o = v.orbit;
            int guard = 0;
            while (o != null && guard++ < 8)
            {
                if (o.referenceBody == body) return o;
                if (o.patchEndTransition == Orbit.PatchTransitionType.FINAL || o.patchEndTransition == Orbit.PatchTransitionType.INITIAL) break;
                o = o.nextPatch;
                if (o != null && !o.activePatch) break;
            }
            return null;
        }

        /// <summary>Periapsis altitude at the body along the vessel's current prediction, or NaN when it never gets there.</summary>
        public static double VesselPeriapsisAt(Vessel v, CelestialBody body)
        {
            Orbit o = VesselPatchAround(v, body);
            return o != null ? o.PeA : double.NaN;
        }

        /// <summary>Scores a trial node (added, scored, removed). NaN when the node cannot be placed or the score throws.</summary>
        public static double Trial(Vessel v, double ut, Vector3d components, Func<ManeuverNode, double> score)
        {
            ManeuverNode n = NodePlanner.Add(v, ut, components);
            if (n == null) return double.NaN;
            double s;
            try { s = score(n); } catch { s = double.NaN; }
            try { n.RemoveSelf(); } catch { }
            return s;
        }

        /// <summary>
        /// Smallest delta-v along <paramref name="direction"/> (added to <paramref name="baseComponents"/>) whose score lands
        /// in [lo, hi]. Samples the line first so bands narrower than the score's swing are not skipped, then bisects the
        /// first sample pair that brackets or enters the band. Returns NaN when no sample pair does. Costs ~samples + 14 solver updates.
        /// </summary>
        public static double LineSearch(Vessel v, double ut, Vector3d baseComponents, Vector3d direction, double maxDv, int samples,
            Func<ManeuverNode, double> score, double lo, double hi)
        {
            if (!NodePlanner.Available(v) || direction.magnitude < 1e-6 || maxDv <= 0) return double.NaN;
            direction = direction.normalized;
            if (samples < 2) samples = 2;
            double step = maxDv / samples;
            double prevDv = 0, prevScore = Trial(v, ut, baseComponents, score);
            if (InBand(prevScore, lo, hi)) return 0;
            for (int k = 1; k <= samples; k++)
            {
                double dv = k * step;
                double s = Trial(v, ut, baseComponents + direction * dv, score);
                if (InBand(s, lo, hi))
                {
                    // Pull the burn back toward the smallest in-band value between the previous sample and this one.
                    double a = prevDv, b = dv;
                    for (int i = 0; i < 12; i++)
                    {
                        double m = (a + b) / 2;
                        double sm = Trial(v, ut, baseComponents + direction * m, score);
                        if (InBand(sm, lo, hi)) b = m; else a = m;
                    }
                    return b;
                }
                if (Brackets(prevScore, s, lo, hi))
                {
                    double a = prevDv, b = dv, sa = prevScore;
                    for (int i = 0; i < 14; i++)
                    {
                        double m = (a + b) / 2;
                        double sm = Trial(v, ut, baseComponents + direction * m, score);
                        if (InBand(sm, lo, hi)) return m;
                        // Lost the encounter in the middle: shrink toward the known-finite lower end.
                        if (double.IsNaN(sm)) { b = m; continue; }
                        // Keep the half that still brackets the band.
                        if (Brackets(sa, sm, lo, hi)) b = m; else { a = m; sa = sm; }
                    }
                }
                prevDv = dv; prevScore = s;
            }
            return double.NaN;
        }

        public static bool InBand(double s, double lo, double hi) { return !double.IsNaN(s) && s >= lo && s <= hi; }

        private static bool Brackets(double s1, double s2, double lo, double hi)
        {
            if (double.IsNaN(s1) || double.IsNaN(s2)) return false;
            return (s1 < lo && s2 > hi) || (s1 > hi && s2 < lo);
        }

        /// <summary>Smallest delta-v along a direction that gives any finite score at all (an encounter exists). NaN when none.</summary>
        public static double FirstFinite(Vessel v, double ut, Vector3d direction, double maxDv, int samples, Func<ManeuverNode, double> score)
        {
            if (!NodePlanner.Available(v) || direction.magnitude < 1e-6 || maxDv <= 0) return double.NaN;
            direction = direction.normalized;
            if (samples < 2) samples = 2;
            double step = maxDv / samples;
            for (int k = 1; k <= samples; k++)
            {
                double dv = k * step;
                if (!double.IsNaN(Trial(v, ut, direction * dv, score))) return dv;
            }
            return double.NaN;
        }

        /// <summary>Runs the executor one step and tells the caller whether the burn is over. Line goes to the flight log.</summary>
        public static bool PumpExecutor(NodeExecutor exec, FlightContext ctx, ref string line)
        {
            if (exec == null) return true;
            string l = exec.Step(ctx);
            if (l != null) line = l;
            return exec.Done || exec.Failed;
        }
    }

    /// <summary>
    /// Transfer from a parking orbit to a moon of the same planet (Kerbin to Mun or Minmus). Plans with the portable
    /// porkchop search, burns with a stock maneuver node, and reports whether an encounter is predicted. A
    /// <see cref="CorrectionLeg"/> is expected to follow, so the leg is Done either way after the burn.
    /// </summary>
    public sealed class TransferLeg : IFlightLeg
    {
        private enum Phases { Plan, Burn, Finished }
        private readonly CelestialBody _target;
        private readonly double _arrivalPe;
        private Phases _phase;
        private NodeExecutor _exec;
        private TransferPlan _plan;

        public TransferLeg(CelestialBody target, double arrivalPeriapsis)
        {
            _target = target;
            _arrivalPe = arrivalPeriapsis;
        }

        public string Name { get { return "Transfer to " + TransferSupport.BodyName(_target); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Planning the transfer.";
            _phase = Phases.Plan;
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _target == null) { Fail("no vessel orbit or target"); return; }
            if (v.mainBody == _target)
            {
                State = LegState.Done; Status = "Already at " + _target.bodyName + ".";
                return;
            }
            if (TransferSupport.VesselPatchAround(v, _target) != null)
            {
                ctx.Say("Already on a trajectory to " + _target.bodyName + "; periapsis " + TransferSupport.Km(TransferSupport.VesselPeriapsisAt(v, _target)) + ".");
                State = LegState.Done; Status = "Encounter already predicted.";
                return;
            }
            if (_target.referenceBody != v.mainBody)
            {
                Fail("vessel orbits " + TransferSupport.BodyName(v.mainBody) + ", not " + TransferSupport.BodyName(_target.referenceBody));
                return;
            }
            // Resuming with a node already planned: just fly it.
            if (NodePlanner.Available(v) && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                _phase = Phases.Burn;
                Status = "Resuming the transfer burn.";
            }
        }

        private void Fail(string why) { State = LegState.Failed; Status = "Transfer failed: " + why; }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            string line = null;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            switch (_phase)
            {
                case Phases.Plan:
                    if (ctx.Control.IsWarping) { ctx.Control.StopWarp(); break; }
                    if (!NodePlanner.Available(v)) { Fail("no patched-conic solver on this vessel"); break; }
                    line = Plan(ctx);
                    break;
                case Phases.Burn:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed)
                        {
                            Fail(_exec.FailReason ?? "burn failed");
                            line = line ?? ("Transfer burn failed: " + _exec.FailReason);
                            break;
                        }
                        _phase = Phases.Finished;
                        double pe = TransferSupport.VesselPeriapsisAt(v, _target);
                        if (!double.IsNaN(pe))
                        {
                            Status = _target.bodyName + " encounter predicted.";
                            ctx.Say("Transfer burn complete; " + _target.bodyName + " encounter predicted, periapsis " + TransferSupport.Km(pe) + ".");
                        }
                        else
                        {
                            Status = "No encounter yet; correction to follow.";
                            ctx.Say("Transfer burn complete; no encounter predicted yet; mid-course correction to follow.");
                        }
                        State = LegState.Done;
                    }
                    else Status = _exec.Status;
                    break;
                case Phases.Finished:
                    State = LegState.Done;
                    break;
            }
            return line;
        }

        private string Plan(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            CelestialBody parent = v.mainBody;
            double mu = parent.gravParameter;
            double now = ctx.Now;
            double period = TransferSupport.IsClosed(v.orbit) ? v.orbit.period : 3600;
            double r1 = Math.Max(parent.Radius + 1000, v.orbit.semiMajorAxis > 0 ? v.orbit.semiMajorAxis : v.orbit.PeR);
            double r2 = _target.orbit != null ? _target.orbit.semiMajorAxis : r1 * 10;
            double hohmannTime;
            Ejection.Hohmann(mu, r1, r2, out hohmannTime);
            if (double.IsNaN(hohmannTime) || hohmannTime <= 0) hohmannTime = period * 2;

            _plan = null;
            try
            {
                _plan = TransferPlanner.Plan(OrbitAdapter.Sampler(v.orbit), OrbitAdapter.Sampler(_target.orbit), mu,
                    now + 120, now + 1.2 * period, 0.4 * hohmannTime, 1.5 * hohmannTime, 48, 24, 0.3);
            }
            catch (Exception e) { KsaCore.Log.Error("TransferLeg planner threw: " + e); _plan = null; }
            if (_plan == null) { Fail("no transfer to " + _target.bodyName + " found from this orbit"); return "Transfer planning failed: no solution."; }

            NodePlanner.ClearNodes(v);
            ManeuverNode n = NodePlanner.AddFrameDeltaV(v, _plan.DepartureUt, _plan.DeltaV);
            if (n == null) { Fail("could not place the transfer node"); return null; }
            double predicted = NodePlanner.PeriapsisAt(n, _target);
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return null; }
            _phase = Phases.Burn;
            Status = "Transfer burn in " + TransferSupport.Countdown(_plan.DepartureUt - now) + ".";
            string pe = double.IsNaN(predicted) ? "no encounter in the node prediction yet" : "predicted periapsis " + TransferSupport.Km(predicted);
            return "Transfer planned: " + Math.Round(_plan.DeltaVMagnitude) + " m/s at " + TransferSupport.Date(_plan.DepartureUt)
                + ", flight time " + TransferSupport.Countdown(_plan.TimeOfFlight) + " (" + pe + ", target " + TransferSupport.Km(_arrivalPe) + ").";
        }

        public void End(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Control.StopWarp();
            if (_exec != null) _exec.End(ctx);
        }
    }

    /// <summary>
    /// Ejection from a parking orbit around a planet onto a heliocentric transfer to another planet. Plans the
    /// heliocentric leg with the porkchop search, places a prograde ejection burn at the point of the parking orbit
    /// whose outgoing asymptote lines up with the required hyperbolic excess, warps to the window and burns.
    /// Done once the vessel's patch escapes the planet. Also used for the interplanetary return (origin = any planet).
    /// </summary>
    public sealed class InterplanetaryTransferLeg : IFlightLeg
    {
        private enum Phases { Plan, Burn, Finished }
        private readonly CelestialBody _target;
        private Phases _phase;
        private NodeExecutor _exec;
        private double _burnUt;
        private double _windowDv;
        private CelestialBody _origin;

        public InterplanetaryTransferLeg(CelestialBody target) { _target = target; }

        public string Name { get { return "Transfer to " + TransferSupport.BodyName(_target); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Planning the interplanetary transfer.";
            _phase = Phases.Plan;
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _target == null) { Fail("no vessel orbit or target"); return; }
            _origin = v.mainBody;
            if (_origin == null) { Fail("no main body"); return; }
            if (Escaped(v))
            {
                State = LegState.Done; Status = "Already on the escape trajectory.";
                return;
            }
            CelestialBody sun = _origin.referenceBody;
            if (sun == null || sun == _origin) { Fail("vessel already orbits the star"); return; }
            if (_target.referenceBody != sun) { Fail(_target.bodyName + " does not orbit " + sun.bodyName); return; }
            if (!TransferSupport.IsClosed(v.orbit)) { Fail("parking orbit is not closed"); return; }
            if (NodePlanner.Available(v) && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                ManeuverNode existing = v.patchedConicSolver.maneuverNodes[0];
                _burnUt = existing.UT; _windowDv = existing.DeltaV.magnitude;
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                _phase = Phases.Burn;
                Status = "Resuming the ejection burn at " + TransferSupport.Date(_burnUt) + ".";
            }
        }

        private bool Escaped(Vessel v)
        {
            if (v.mainBody == null) return false;
            CelestialBody sun = _origin != null ? _origin.referenceBody : null;
            if (sun != null && v.mainBody == sun) return true;
            if (v.mainBody == _target) return true;
            return v.orbit != null && v.mainBody == _origin && v.orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE;
        }

        private void Fail(string why) { State = LegState.Failed; Status = "Transfer failed: " + why; }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            string line = null;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            switch (_phase)
            {
                case Phases.Plan:
                    if (ctx.Control.IsWarping) { ctx.Control.StopWarp(); break; }
                    if (!NodePlanner.Available(v)) { Fail("no patched-conic solver on this vessel"); break; }
                    line = Plan(ctx);
                    break;
                case Phases.Burn:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed)
                        {
                            Fail(_exec.FailReason ?? "burn failed");
                            line = line ?? ("Ejection burn failed: " + _exec.FailReason);
                            break;
                        }
                        _phase = Phases.Finished;
                        if (Escaped(v))
                        {
                            Status = "Escaping " + _origin.bodyName + ".";
                            ctx.Say("Ejection burn complete; escaping " + _origin.bodyName + " toward " + _target.bodyName + ".", "Escape:" + _origin.bodyName);
                            State = LegState.Done;
                        }
                        else
                        {
                            Fail("ejection burn did not reach escape velocity");
                            line = line ?? "Ejection burn complete but the vessel is still bound to " + _origin.bodyName + ".";
                        }
                    }
                    else Status = "Ejection " + TransferSupport.Date(_burnUt) + " (" + Math.Round(_windowDv) + " m/s): " + _exec.Status;
                    break;
                case Phases.Finished:
                    State = Escaped(v) ? LegState.Done : State;
                    break;
            }
            return line;
        }

        private string Plan(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            CelestialBody sun = _origin.referenceBody;
            double sunMu = sun.gravParameter;
            double now = ctx.Now;
            double day = TransferSupport.Day;
            double t1 = _origin.orbit != null ? _origin.orbit.period : 0;
            double t2 = _target.orbit != null ? _target.orbit.period : 0;
            double synodic = (t1 > 0 && t2 > 0 && Math.Abs(1 / t1 - 1 / t2) > 1e-12) ? 1 / Math.Abs(1 / t1 - 1 / t2) : Math.Max(t1, t2);
            if (synodic <= 0 || double.IsNaN(synodic)) synodic = 400 * day;
            double r1 = _origin.orbit != null ? _origin.orbit.semiMajorAxis : 1;
            double r2 = _target.orbit != null ? _target.orbit.semiMajorAxis : r1;
            double hohmannTime;
            Ejection.Hohmann(sunMu, r1, r2, out hohmannTime);
            if (double.IsNaN(hohmannTime) || hohmannTime <= 0) hohmannTime = synodic / 2;

            TransferPlan plan = null;
            try
            {
                plan = TransferPlanner.Plan(OrbitAdapter.Sampler(_origin.orbit), OrbitAdapter.Sampler(_target.orbit), sunMu,
                    now + day, now + 1.1 * synodic, 0.5 * hohmannTime, 1.6 * hohmannTime, 60, 30, 0.5);
            }
            catch (Exception e) { KsaCore.Log.Error("InterplanetaryTransferLeg planner threw: " + e); plan = null; }
            if (plan == null) { Fail("no transfer window to " + _target.bodyName + " found"); return "Transfer planning failed: no window found."; }

            // Hyperbolic excess relative to the planet, in the same frame as the parking orbit.
            Vec3 vInf = plan.DeltaV;
            double vInfMag = vInf.Length;
            if (vInfMag < 1) { Fail("degenerate transfer solution"); return null; }
            double planetMu = _origin.gravParameter;
            double period = v.orbit.period;
            double r = v.orbit.semiMajorAxis > 0 ? v.orbit.semiMajorAxis : v.orbit.PeR;
            Vec3 normal = OrbitAdapter.Normal(v.orbit, now);
            Vec3 burnDir = Ejection.BurnPointDirection(vInf.Normalized, normal, planetMu, r, vInfMag);
            if (burnDir.LengthSquared == 0) { Fail("could not find the ejection point"); return null; }

            // First time after (window - half a period) where the vessel passes the ejection point.
            double start = Math.Max(now + 60, plan.DepartureUt - 0.5 * period);
            double bestUt = start, bestAngle = double.PositiveInfinity;
            for (double ut = start; ut <= start + period + 10; ut += 10)
            {
                double a = Vec3.AngleBetween(OrbitAdapter.StateAt(v.orbit, ut).Position, burnDir);
                if (a < bestAngle) { bestAngle = a; bestUt = ut; }
            }
            for (double ut = bestUt - 10; ut <= bestUt + 10; ut += 0.25)
            {
                if (ut < now + 60) continue;
                double a = Vec3.AngleBetween(OrbitAdapter.StateAt(v.orbit, ut).Position, burnDir);
                if (a < bestAngle) { bestAngle = a; bestUt = ut; }
            }
            if (bestAngle > 1.0 * Math.PI / 180) KsaCore.Log.Warn("Ejection point only matched to " + Math.Round(bestAngle * 180 / Math.PI, 2) + " degrees.");

            double dv = Ejection.DeltaV(planetMu, r, vInfMag);
            NodePlanner.ClearNodes(v);
            ManeuverNode n = NodePlanner.Add(v, bestUt, new Vector3d(0, 0, dv));
            if (n == null) { Fail("could not place the ejection node"); return null; }
            _burnUt = bestUt; _windowDv = dv;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return null; }
            _phase = Phases.Burn;
            Status = "Ejection " + TransferSupport.Date(bestUt) + " (" + Math.Round(dv) + " m/s): waiting for the window.";
            bool escape = n.nextPatch != null && n.nextPatch.patchEndTransition == Orbit.PatchTransitionType.ESCAPE;
            return "Window to " + _target.bodyName + ": depart " + TransferSupport.Date(plan.DepartureUt) + ", arrive " + TransferSupport.Date(plan.ArrivalUt)
                + " (" + TransferSupport.Countdown(plan.TimeOfFlight) + " flight, " + Math.Round(vInfMag) + " m/s excess). Ejection burn "
                + Math.Round(dv) + " m/s at " + TransferSupport.Date(bestUt) + (escape ? "." : "; node prediction shows no escape yet.");
        }

        public void End(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Control.StopWarp();
            if (_exec != null) _exec.End(ctx);
        }
    }

    /// <summary>
    /// Mid-course correction: finds the cheapest small burn (one of six node directions, or normal then radial) that
    /// puts the predicted periapsis at the target inside [peLo, peHi]. Never fails: when no correction exists within
    /// the budget the mission continues on its current path and the later legs decide what that means.
    /// </summary>
    public sealed class CorrectionLeg : IFlightLeg
    {
        private enum Phases { Prepare, Search, Burn, Finished }
        private readonly CelestialBody _target;
        private readonly double _peLo, _peHi, _maxDv;
        private Phases _phase;
        private NodeExecutor _exec;
        private double _ut;
        private int _candidate;
        private double _bestDv = double.NaN;
        private Vector3d _bestComponents;
        private static readonly Vector3d[] Directions =
        {
            new Vector3d(0, 0, 1), new Vector3d(0, 0, -1),
            new Vector3d(0, 1, 0), new Vector3d(0, -1, 0),
            new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0)
        };

        /// <summary>A flyby of a moon: the world the path must fall back to afterwards (null for every other correction).</summary>
        private readonly CelestialBody _returnTo;
        private double _bestReturn = double.PositiveInfinity;
        private Vessel _vessel;

        /// <summary>
        /// Where the path comes after the target: the periapsis around <paramref name="home"/>; +infinity when it leaves home's
        /// space, NaN when the game predicts no patch that far (2026-10-30, T: "that green line has it going out to nowhere":
        /// passing behind the Mun flings a craft away, passing ahead of it drops it back to Kerbin).
        /// </summary>
        private static double ReturnPeriapsis(Orbit first, CelestialBody target, CelestialBody home)
        {
            bool passed = false;
            Orbit o = first;
            for (int guard = 0; o != null && guard < 8; guard++, o = o.nextPatch)
            {
                if (o.referenceBody == target) { passed = true; continue; }
                if (!passed) continue;
                if (o.referenceBody != home || o.eccentricity >= 1 || o.patchEndTransition == Orbit.PatchTransitionType.ESCAPE) return double.PositiveInfinity;
                return o.PeA;
            }
            return double.NaN;
        }

        /// <summary>A path that already comes home: back within this of Kerbin's surface needs little more than a small trim.</summary>
        private const double GoodReturnPe = 1000000;

        public CorrectionLeg(CelestialBody target, double peLo, double peHi, double maxDv, CelestialBody returnTo) : this(target, peLo, peHi, maxDv) { _returnTo = returnTo; }

        public CorrectionLeg(CelestialBody target, double peLo, double peHi, double maxDv)
        {
            _target = target;
            _peLo = Math.Min(peLo, peHi);
            _peHi = Math.Max(peLo, peHi);
            _maxDv = Math.Max(1, maxDv);
        }

        public string Name { get { return "Course correction (" + TransferSupport.BodyName(_target) + ")"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _phase = Phases.Prepare;
            _candidate = 0; _bestDv = double.NaN; _bestReturn = double.PositiveInfinity;
            Status = "Checking the trajectory.";
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _target == null) { State = LegState.Done; Status = "Nothing to correct."; return; }
            double pe = TransferSupport.VesselPeriapsisAt(v, _target);
            if (v.mainBody == _target && TransferSupport.InBand(pe, _peLo, _peHi))
            {
                State = LegState.Done; Status = "Periapsis " + TransferSupport.Km(pe) + " is in band.";
                return;
            }
            if (v.mainBody == _target && v.situation == Vessel.Situations.ORBITING && TransferSupport.InBand(v.orbit.PeA, _peLo, _peHi))
            {
                State = LegState.Done; Status = "Already orbiting " + _target.bodyName + ".";
                return;
            }
            if (NodePlanner.Available(v) && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                _phase = Phases.Burn;
                Status = "Resuming the correction burn.";
            }
        }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            string line = null;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            switch (_phase)
            {
                case Phases.Prepare:
                    if (ctx.Control.IsWarping) { ctx.Control.StopWarp(); break; }
                    if (!NodePlanner.Available(v)) { State = LegState.Done; Status = "No solver; flying on."; line = "No patched-conic solver; correction skipped."; break; }
                    {
                        double current = TransferSupport.VesselPeriapsisAt(v, _target);
                        double back = _returnTo != null ? ReturnPeriapsis(v.orbit, _target, _returnTo) : double.NaN;
                        bool comesHome = _returnTo == null || double.IsNaN(back) || back < GoodReturnPe;
                        if (TransferSupport.InBand(current, _peLo, _peHi) && comesHome)
                        {
                            State = LegState.Done; Status = "On course.";
                            line = "Trajectory check: " + _target.bodyName + " periapsis " + TransferSupport.Km(current) + (double.IsNaN(back) ? string.Empty : ", then back to Kerbin at " + TransferSupport.Km(back)) + ", no correction needed.";
                            break;
                        }
                        double now = ctx.Now;
                        _ut = now + 60;
                        if (TransferSupport.EndsInTransition(v.orbit) && v.orbit.EndUT > now) _ut = Math.Max(now + 60, now + 0.1 * (v.orbit.EndUT - now));
                        NodePlanner.ClearNodes(v);
                        _phase = Phases.Search; _candidate = 0;
                        Status = "Searching for a correction (" + (double.IsNaN(current) ? "no encounter" : "periapsis " + TransferSupport.Km(current)) + ").";
                    }
                    break;
                case Phases.Search:
                    // One candidate per frame keeps the solver hitches short.
                    _vessel = v;
                    if (_candidate < Directions.Length) { TrySingle(v, Directions[_candidate]); }
                    else if (_candidate < Directions.Length + 4 && double.IsNaN(_bestDv)) { TryCombo(v, _candidate - Directions.Length); }
                    _candidate++;
                    Status = "Searching for a correction (" + _candidate + "/" + (Directions.Length + 4) + ").";
                    if (_candidate >= Directions.Length + 4 || (_candidate >= Directions.Length && !double.IsNaN(_bestDv)))
                    {
                        if (double.IsNaN(_bestDv))
                        {
                            State = LegState.Done;
                            Status = "No correction within " + Math.Round(_maxDv) + " m/s; flying on.";
                            double cur = TransferSupport.VesselPeriapsisAt(v, _target);
                            line = "No correction found within " + Math.Round(_maxDv) + " m/s"
                                + (double.IsNaN(cur) ? "; no " + _target.bodyName + " encounter on the current path." : "; continuing with periapsis " + TransferSupport.Km(cur) + ".");
                            break;
                        }
                        ManeuverNode n = NodePlanner.Add(v, _ut, _bestComponents);
                        if (n == null) { State = LegState.Done; Status = "Could not place the node."; line = "Correction node could not be placed; flying on."; break; }
                        double predicted = NodePlanner.PeriapsisAt(n, _target);
                        _exec = new NodeExecutor();
                        _exec.Begin(ctx);
                        _phase = Phases.Burn;
                        Status = "Correction burn of " + Math.Round(_bestDv, 1) + " m/s.";
                        line = "Course correction planned: " + Math.Round(_bestDv, 1) + " m/s, predicted " + _target.bodyName + " periapsis " + TransferSupport.Km(predicted) + ".";
                        if (_returnTo != null)
                            line += double.IsPositiveInfinity(_bestReturn) ? " No path found that stays near Kerbin after the flyby." : double.IsNaN(_bestReturn) ? string.Empty : " Then back to Kerbin at " + TransferSupport.Km(_bestReturn) + ".";
                    }
                    break;
                case Phases.Burn:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        _phase = Phases.Finished;
                        double pe = TransferSupport.VesselPeriapsisAt(v, _target);
                        string peText = double.IsNaN(pe) ? "no " + _target.bodyName + " encounter predicted" : "predicted " + _target.bodyName + " periapsis " + TransferSupport.Km(pe);
                        if (_exec.Failed) ctx.Say("Correction burn incomplete (" + _exec.FailReason + "); " + peText + ".");
                        else ctx.Say("Correction complete; " + peText + ".");
                        Status = peText + ".";
                        State = LegState.Done;
                    }
                    else Status = _exec.Status;
                    break;
                case Phases.Finished:
                    State = LegState.Done;
                    break;
            }
            return line;
        }

        private double Score(ManeuverNode n) { return NodePlanner.PeriapsisAt(n, _target); }

        private void TrySingle(Vessel v, Vector3d dir)
        {
            double dv = double.NaN;
            try { dv = NodePlanner.Bisect(v, _ut, dir, _maxDv, Score, _peLo, _peHi); } catch { dv = double.NaN; }
            if (double.IsNaN(dv))
            {
                try { dv = TransferSupport.LineSearch(v, _ut, Vector3d.zero, dir, _maxDv, 20, Score, _peLo, _peHi); } catch { dv = double.NaN; }
            }
            Consider(dv, dir * dv);
        }

        /// <summary>Normal (+/-) first until an encounter exists, then radial (+/-) within what budget remains.</summary>
        private void TryCombo(Vessel v, int index)
        {
            Vector3d normal = index < 2 ? new Vector3d(0, 1, 0) : new Vector3d(0, -1, 0);
            Vector3d radial = (index % 2 == 0) ? new Vector3d(1, 0, 0) : new Vector3d(-1, 0, 0);
            double dn;
            try { dn = TransferSupport.FirstFinite(v, _ut, normal, _maxDv, 20, Score); } catch { dn = double.NaN; }
            if (double.IsNaN(dn) || dn >= _maxDv) return;
            double budget = _maxDv - dn;
            double dr;
            try { dr = TransferSupport.LineSearch(v, _ut, normal * dn, radial, budget, 20, Score, _peLo, _peHi); } catch { dr = double.NaN; }
            if (double.IsNaN(dr)) return;
            Vector3d comps = normal * dn + radial * dr;
            Consider(comps.magnitude, comps);
        }

        private void Consider(double dv, Vector3d comps)
        {
            if (double.IsNaN(dv) || dv < 0 || dv > _maxDv) return;
            if (_returnTo != null)
            {
                // A flyby: the candidate that falls closest to home after the moon wins, not the cheapest.
                double back = TransferSupport.Trial(_vessel, _ut, comps, n => ReturnPeriapsis(n.nextPatch, _target, _returnTo));
                bool none = double.IsNaN(_bestDv);
                if (none || (!double.IsNaN(back) && (double.IsNaN(_bestReturn) || back < _bestReturn))) { _bestDv = dv; _bestComponents = comps; _bestReturn = back; }
                return;
            }
            if (double.IsNaN(_bestDv) || dv < _bestDv) { _bestDv = dv; _bestComponents = comps; }
        }

        public void End(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Control.StopWarp();
            if (_exec != null) _exec.End(ctx);
        }
    }

    /// <summary>Coasts (warping along the patches) until the vessel is inside the body's sphere of influence.</summary>
    public sealed class WaitForSoiLeg : IFlightLeg
    {
        private readonly CelestialBody _body;
        private readonly double _maxDays;
        private double _deadline;
        private double _lastWarpRequest;

        public WaitForSoiLeg(CelestialBody body, double maxDays)
        {
            _body = body;
            _maxDays = Math.Max(0.01, maxDays);
        }

        public string Name { get { return "Coast to " + TransferSupport.BodyName(_body); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _deadline = ctx.Now + _maxDays * TransferSupport.Day;
            _lastWarpRequest = -1e9;
            Status = "Coasting.";
            ctx.Control.Throttle = 0;
            Vessel v = ctx.Vessel;
            if (v == null || _body == null) { State = LegState.Failed; Status = "No vessel or body."; return; }
            if (v.mainBody == _body) { State = LegState.Done; Status = "In " + _body.bodyName + "'s sphere of influence."; return; }
            if (ctx.Control.CanSas(VesselAutopilot.AutopilotMode.StabilityAssist)) ctx.Control.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, false);
        }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            if (State != LegState.Running || v == null) return null;
            double now = ctx.Now;
            if (v.mainBody == _body)
            {
                ctx.Control.StopWarp();
                State = LegState.Done;
                Status = "Entered " + _body.bodyName + "'s sphere of influence.";
                bool flybyWorthy = _body.referenceBody != null && _body.referenceBody != _body && _body != FlightGlobals.GetHomeBody();
                ctx.Say("Entered " + _body.bodyName + "'s sphere of influence; periapsis " + TransferSupport.Km(v.orbit != null ? v.orbit.PeA : 0) + ".", flybyWorthy ? "Flyby:" + _body.bodyName : null);
                return null;
            }
            if (now > _deadline)
            {
                ctx.Control.StopWarp();
                State = LegState.Failed;
                Status = "Never reached " + _body.bodyName + ".";
                return "No " + _body.bodyName + " encounter after " + Math.Round(_maxDays, 1) + " days; coast abandoned.";
            }
            double eta = double.NaN;
            if (v.orbit != null && TransferSupport.EndsInTransition(v.orbit) && v.orbit.EndUT > now) eta = v.orbit.EndUT - now;
            Status = (double.IsNaN(eta) ? "Coasting toward " + _body.bodyName : "Next SOI change in " + TransferSupport.Countdown(eta))
                + "; " + TransferSupport.Countdown(_deadline - now) + " left.";
            // Re-issue the warp request every few seconds (WarpTo is a no-op while already warping).
            if (!ctx.Control.IsWarping && now - _lastWarpRequest > 2)
            {
                _lastWarpRequest = now;
                double target = double.IsNaN(eta) ? now + 600 : v.orbit.EndUT + 5;
                if (target > _deadline + 5) target = _deadline + 5;
                ctx.Control.WarpTo(target);
            }
            return null;
        }

        public void End(FlightContext ctx) { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); }
    }

    /// <summary>Flyby: ride through periapsis of the current body, log the closest approach, hand over once past it.</summary>
    public sealed class PassPeriapsisLeg : IFlightLeg
    {
        private CelestialBody _body;
        private double _minAltitude = double.PositiveInfinity;
        private double _lastWarpRequest;

        public string Name { get { return _body != null ? "Flyby of " + _body.bodyName : "Flyby"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _minAltitude = double.PositiveInfinity;
            _lastWarpRequest = -1e9;
            ctx.Control.Throttle = 0;
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null) { State = LegState.Done; Status = "No orbit."; return; }
            _body = v.mainBody;
            Status = "Approaching periapsis of " + TransferSupport.BodyName(_body) + ".";
            if (ctx.Control.CanSas(VesselAutopilot.AutopilotMode.Prograde)) ctx.Control.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
        }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            double now = ctx.Now;
            if (v.altitude < _minAltitude) _minAltitude = v.altitude;
            if (v.mainBody != _body) return Finish(ctx, "Left " + TransferSupport.BodyName(_body) + "'s sphere of influence.");
            if (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED || v.situation == Vessel.Situations.FLYING)
                return Finish(ctx, null);
            double tPe = v.orbit.timeToPe;
            bool closed = TransferSupport.IsClosed(v.orbit);
            bool passed = closed ? tPe > v.orbit.period / 2 : tPe < 0;
            if (passed) return Finish(ctx, null);
            Status = "Periapsis of " + _body.bodyName + " in " + TransferSupport.Countdown(tPe) + " (" + TransferSupport.Km(v.orbit.PeA) + ").";
            if (tPe > 30 && !ctx.Control.IsWarping && now - _lastWarpRequest > 2)
            {
                _lastWarpRequest = now;
                ctx.Control.WarpTo(now + tPe + 10);
            }
            return null;
        }

        private string Finish(FlightContext ctx, string extra)
        {
            ctx.Control.StopWarp();
            State = LegState.Done;
            double closest = _minAltitude;
            Vessel v = ctx.Vessel;
            if (v != null && v.orbit != null && v.mainBody == _body && v.orbit.PeA < closest) closest = v.orbit.PeA;
            if (double.IsInfinity(closest)) closest = 0;
            string name = TransferSupport.BodyName(_body);
            Status = "Closest approach to " + name + ": " + TransferSupport.Km(closest) + ".";
            ctx.Say("Closest approach to " + name + ": " + TransferSupport.Km(closest) + ".", "Flyby:" + name);
            return extra;
        }

        public void End(FlightContext ctx) { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); }
    }

    /// <summary>
    /// Orbit insertion at the target: a retrograde burn at periapsis into an ellipse reaching the wanted altitude
    /// (or a circular orbit at periapsis when that is already higher), then a circularisation burn at apoapsis when
    /// the orbit is still eccentric and propellant remains. Fails when the incoming path hits the body.
    /// </summary>
    public sealed class CaptureLeg : IFlightLeg
    {
        private enum Phases { Check, Capture, Circularize, Finished }
        private readonly CelestialBody _target;
        private readonly double _altitude;
        private Phases _phase;
        private NodeExecutor _exec;
        private bool _secondPlanned;

        public CaptureLeg(CelestialBody target, double orbitAltitude)
        {
            _target = target;
            _altitude = Math.Max(1000, orbitAltitude);
        }

        public string Name { get { return "Orbit insertion at " + TransferSupport.BodyName(_target); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _phase = Phases.Check;
            _secondPlanned = false;
            Status = "Preparing orbit insertion.";
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _target == null) { Fail("no vessel orbit or target"); return; }
            if (v.mainBody != _target) { Fail("not in " + _target.bodyName + "'s sphere of influence"); return; }
            if (Captured(v) && v.orbit.ApA <= _altitude * 1.3 + 5000)
            {
                State = LegState.Done; Status = "Already in orbit.";
                return;
            }
            if (NodePlanner.Available(v) && v.patchedConicSolver.maneuverNodes.Count > 0)
            {
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                _phase = Captured(v) ? Phases.Circularize : Phases.Capture;
                _secondPlanned = _phase == Phases.Circularize;
                Status = "Resuming the insertion burn.";
            }
        }

        private static bool Captured(Vessel v)
        {
            return v.situation == Vessel.Situations.ORBITING && v.orbit != null && v.orbit.PeA > 0 && v.orbit.eccentricity < 1;
        }

        private void Fail(string why) { State = LegState.Failed; Status = "Insertion failed: " + why; }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            string line = null;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            switch (_phase)
            {
                case Phases.Check:
                    if (ctx.Control.IsWarping) { ctx.Control.StopWarp(); break; }
                    if (v.mainBody != _target) { Fail("left " + _target.bodyName + "'s sphere of influence"); break; }
                    if (!NodePlanner.Available(v)) { Fail("no patched-conic solver on this vessel"); break; }
                    if (v.orbit.PeA < 5000)
                    {
                        Fail("trajectory impacts " + _target.bodyName);
                        line = "Incoming trajectory impacts " + _target.bodyName + " (periapsis " + TransferSupport.Km(v.orbit.PeA) + "); insertion aborted.";
                        break;
                    }
                    if (Captured(v))
                    {
                        // Already bound (an earlier partial burn): go straight to the clean-up burn or finish.
                        if (!PlanCircularise(ctx, ref line)) Finish(ctx, ref line);
                        break;
                    }
                    if (!TransferSupport.IsClosed(v.orbit) && v.orbit.timeToPe < 0)
                    {
                        Fail("already past periapsis of " + _target.bodyName);
                        break;
                    }
                    line = PlanCapture(ctx);
                    break;
                case Phases.Capture:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (!Captured(v))
                        {
                            Fail(_exec.Failed ? "capture burn incomplete: " + _exec.FailReason : "still not bound to " + _target.bodyName + " after the burn");
                            line = line ?? ("Insertion burn did not capture; still escaping " + _target.bodyName + ".");
                            break;
                        }
                        if (_exec.Failed || !PlanCircularise(ctx, ref line)) Finish(ctx, ref line);
                    }
                    else Status = "Insertion: " + _exec.Status;
                    break;
                case Phases.Circularize:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line)) Finish(ctx, ref line);
                    else Status = "Circularising: " + _exec.Status;
                    break;
                case Phases.Finished:
                    State = LegState.Done;
                    break;
            }
            return line;
        }

        private string PlanCapture(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            double mu = _target.gravParameter;
            double rTarget = _target.Radius + _altitude;
            double utPe = ctx.Now + v.orbit.timeToPe;
            double rPe = v.orbit.PeR;
            double vPe = OrbitAdapter.StateAt(v.orbit, utPe).Velocity.Length;
            if (double.IsNaN(vPe) || vPe <= 0) vPe = Math.Sqrt(Math.Max(0, mu * (2 / rPe - 1 / v.orbit.semiMajorAxis)));
            double aNew = rPe > rTarget ? rPe : (rPe + rTarget) / 2;
            double vNew = Math.Sqrt(mu * (2 / rPe - 1 / aNew));
            double dv = vPe - vNew;
            if (dv <= 0) { Fail("already slower than the capture speed at periapsis"); return null; }
            NodePlanner.ClearNodes(v);
            ManeuverNode n = NodePlanner.Add(v, utPe, new Vector3d(0, 0, -dv));
            if (n == null) { Fail("could not place the insertion node"); return null; }
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return null; }
            _phase = Phases.Capture;
            Status = "Insertion burn of " + Math.Round(dv) + " m/s in " + TransferSupport.Countdown(v.orbit.timeToPe) + ".";
            return "Orbit insertion planned: " + Math.Round(dv) + " m/s retrograde at periapsis (" + TransferSupport.Km(v.orbit.PeA) + ") into a "
                + TransferSupport.Km(rPe - _target.Radius) + " x " + TransferSupport.Km(aNew * 2 - rPe - _target.Radius) + " orbit.";
        }

        /// <summary>Plans the circularisation at apoapsis when the orbit is still eccentric and something can burn. False when skipped.</summary>
        private bool PlanCircularise(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            if (_secondPlanned || !Captured(v)) return false;
            if (v.orbit.ApA - v.orbit.PeA < 0.3 * _altitude + 5000) return false;
            if (ctx.Control.NoThrustAvailable() && !ctx.Control.HasStagesLeft) return false;
            double mu = _target.gravParameter;
            double utAp = ctx.Now + v.orbit.timeToAp;
            double rAp = v.orbit.ApR;
            double vAp = OrbitAdapter.StateAt(v.orbit, utAp).Velocity.Length;
            if (double.IsNaN(vAp) || vAp <= 0) vAp = Math.Sqrt(Math.Max(0, mu * (2 / rAp - 1 / v.orbit.semiMajorAxis)));
            double dv = Kepler.CircularSpeed(mu, rAp) - vAp;
            if (Math.Abs(dv) < 0.5) return false;
            NodePlanner.ClearNodes(v);
            ManeuverNode n = NodePlanner.Add(v, utAp, new Vector3d(0, 0, dv));
            if (n == null) return false;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { try { n.RemoveSelf(); } catch { } return false; }
            _secondPlanned = true;
            _phase = Phases.Circularize;
            Status = "Circularisation burn of " + Math.Round(Math.Abs(dv)) + " m/s at apoapsis.";
            line = "Captured; circularising at apoapsis (" + TransferSupport.Km(v.orbit.ApA) + ") with " + Math.Round(Math.Abs(dv)) + " m/s.";
            return true;
        }

        private void Finish(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            _phase = Phases.Finished;
            if (!Captured(v))
            {
                Fail("orbit not stable after the insertion burns");
                line = line ?? ("Insertion ended without a stable orbit around " + _target.bodyName + ".");
                return;
            }
            string orbit = TransferSupport.Km(v.orbit.ApA) + " x " + TransferSupport.Km(v.orbit.PeA);
            Status = "In a " + orbit + " orbit of " + _target.bodyName + ".";
            ctx.Say("Captured into " + orbit + " orbit of " + _target.bodyName + "; it counts as orbit after one full orbit.");
            State = LegState.Done;
        }

        public void End(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Control.StopWarp();
            if (_exec != null) _exec.End(ctx);
        }
    }

    /// <summary>
    /// Heading home. From a moon of the home planet: one prograde burn, timed over the next orbit, that drops the home
    /// periapsis into the reentry band. From another planet: an interplanetary ejection toward home. The mission
    /// planner follows with WaitForSoi(home), a CorrectionLeg and the ReentryLeg.
    /// </summary>
    public sealed class ReturnLeg : IFlightLeg
    {
        private enum Phases { Prepare, Search, Burn, Finished }
        private readonly CelestialBody _home;
        private readonly double _reentryPe;
        private Phases _phase;
        private NodeExecutor _exec;
        private InterplanetaryTransferLeg _inner;
        private double[] _candidateUts;
        private int _candidate;
        private bool _widened;
        private double _bestDv = double.NaN, _bestUt;
        private double _lo, _hi;

        public ReturnLeg(CelestialBody home, double reentryPeriapsis)
        {
            _home = home;
            _reentryPe = reentryPeriapsis;
        }

        public string Name { get { return "Return to " + TransferSupport.BodyName(_home); } }
        public string Status { get { return _inner != null ? _inner.Status : _status; } private set { _status = value; } }
        private string _status;
        public LegState State { get { return _inner != null ? _inner.State : _state; } private set { _state = value; } }
        private LegState _state;

        public void Begin(FlightContext ctx)
        {
            _inner = null;
            State = LegState.Running;
            _phase = Phases.Prepare;
            _widened = false; _bestDv = double.NaN; _candidate = 0;
            Status = "Planning the return.";
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _home == null) { Fail("no vessel orbit or home body"); return; }
            CelestialBody here = v.mainBody;
            if (here == _home) { State = LegState.Done; Status = "Already at " + _home.bodyName + "."; return; }
            if (here == null) { Fail("no main body"); return; }
            if (here.referenceBody == _home)
            {
                // Moon of home: handled here.
                if (NodePlanner.Available(v) && v.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    _exec = new NodeExecutor();
                    _exec.Begin(ctx);
                    _phase = Phases.Burn;
                    Status = "Resuming the return burn.";
                }
                return;
            }
            if (here.referenceBody != null && here.referenceBody == _home.referenceBody)
            {
                _inner = new InterplanetaryTransferLeg(_home);
                _inner.Begin(ctx);
                return;
            }
            if (here == _home.referenceBody) { Fail("vessel orbits the star; no ejection needed, a correction leg should aim at " + _home.bodyName); return; }
            Fail("return from " + here.bodyName + " (a moon of " + TransferSupport.BodyName(here.referenceBody) + ") is not supported");
        }

        private void Fail(string why) { State = LegState.Failed; Status = "Return failed: " + why; }

        public string Step(FlightContext ctx)
        {
            if (_inner != null) return _inner.Step(ctx);
            Vessel v = ctx.Vessel;
            string line = null;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            switch (_phase)
            {
                case Phases.Prepare:
                    if (ctx.Control.IsWarping) { ctx.Control.StopWarp(); break; }
                    if (!NodePlanner.Available(v)) { Fail("no patched-conic solver on this vessel"); break; }
                    {
                        double current = TransferSupport.VesselPeriapsisAt(v, _home);
                        if (TransferSupport.InBand(current, _reentryPe - 8000, _reentryPe + 8000))
                        {
                            State = LegState.Done; Status = "Already heading home.";
                            line = "Already on a return trajectory; " + _home.bodyName + " periapsis " + TransferSupport.Km(current) + ".";
                            break;
                        }
                        double now = ctx.Now;
                        int count = 24;
                        double period = TransferSupport.IsClosed(v.orbit) ? v.orbit.period : 0;
                        if (period <= 0) count = 1;
                        _candidateUts = new double[count];
                        for (int i = 0; i < count; i++) _candidateUts[i] = now + 120 + (count > 1 ? period * i / count : 0);
                        _candidate = 0; _bestDv = double.NaN;
                        SetBand(false);
                        NodePlanner.ClearNodes(v);
                        _phase = Phases.Search;
                        Status = "Searching for a return burn.";
                    }
                    break;
                case Phases.Search:
                    {
                        // One candidate time per frame.
                        double ut = _candidateUts[_candidate];
                        double dv = double.NaN;
                        try { dv = TransferSupport.LineSearch(v, ut, Vector3d.zero, new Vector3d(0, 0, 1), 1500, 32, Score, _lo, _hi); } catch { dv = double.NaN; }
                        if (!double.IsNaN(dv) && (double.IsNaN(_bestDv) || dv < _bestDv)) { _bestDv = dv; _bestUt = ut; }
                        _candidate++;
                        Status = "Searching for a return burn (" + _candidate + "/" + _candidateUts.Length + (_widened ? ", wide band" : "") + ").";
                        if (_candidate < _candidateUts.Length) break;
                        if (double.IsNaN(_bestDv))
                        {
                            if (!_widened)
                            {
                                _widened = true; _candidate = 0; SetBand(true);
                                line = "No return burn hits the reentry band; widening the search.";
                                break;
                            }
                            Fail("no return trajectory found");
                            line = "No return trajectory to " + _home.bodyName + " found within 1500 m/s.";
                            break;
                        }
                        ManeuverNode n = NodePlanner.Add(v, _bestUt, new Vector3d(0, 0, _bestDv));
                        if (n == null) { Fail("could not place the return node"); break; }
                        double predicted = NodePlanner.PeriapsisAt(n, _home);
                        _exec = new NodeExecutor();
                        _exec.Begin(ctx);
                        if (_exec.Failed) { Fail(_exec.FailReason); break; }
                        _phase = Phases.Burn;
                        Status = "Return burn of " + Math.Round(_bestDv) + " m/s in " + TransferSupport.Countdown(_bestUt - ctx.Now) + ".";
                        line = "Return burn planned: " + Math.Round(_bestDv) + " m/s at " + TransferSupport.Date(_bestUt) + ", predicted " + _home.bodyName
                            + " periapsis " + TransferSupport.Km(predicted) + ".";
                    }
                    break;
                case Phases.Burn:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        _phase = Phases.Finished;
                        if (_exec.Failed)
                        {
                            Fail(_exec.FailReason ?? "burn failed");
                            line = line ?? ("Return burn failed: " + _exec.FailReason);
                            break;
                        }
                        double pe = TransferSupport.VesselPeriapsisAt(v, _home);
                        Status = double.IsNaN(pe) ? "Burn complete; correction to follow." : "Heading home, periapsis " + TransferSupport.Km(pe) + ".";
                        ctx.Say(double.IsNaN(pe)
                            ? "Return burn complete; no " + _home.bodyName + " encounter predicted yet, correction to follow."
                            : "Return burn complete; " + _home.bodyName + " periapsis " + TransferSupport.Km(pe) + ".");
                        State = LegState.Done;
                    }
                    else Status = _exec.Status;
                    break;
                case Phases.Finished:
                    State = LegState.Done;
                    break;
            }
            return line;
        }

        private void SetBand(bool wide)
        {
            if (wide) { _lo = 10000; _hi = 60000; }
            else { _lo = _reentryPe - 8000; _hi = _reentryPe + 8000; }
        }

        private double Score(ManeuverNode n) { return NodePlanner.PeriapsisAt(n, _home); }

        public void End(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Control.StopWarp();
            if (_inner != null) { _inner.End(ctx); return; }
            if (_exec != null) _exec.End(ctx);
        }
    }
}
