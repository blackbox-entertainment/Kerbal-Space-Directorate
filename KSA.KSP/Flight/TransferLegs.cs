using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;
using KSA.Core.Flight.Sim;

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
                if (o.referenceBody == body && ReachesPeriapsis(o)) return o;
                if (o.patchEndTransition == Orbit.PatchTransitionType.FINAL || o.patchEndTransition == Orbit.PatchTransitionType.INITIAL) break;
                o = o.nextPatch;
                if (o != null && !o.activePatch) break;
            }
            return null;
        }

        /// <summary>
        /// Periapsis altitude at the body along the vessel's current prediction, or NaN when it never gets there. When the vessel's own
        /// patch chain shows nothing, a zero-length node at the same spot is asked instead (0.8.212, 2026-11-04: after a deep-space burn
        /// the vessel chain said "no Kerbin encounter" while a node of 0 m/s showed one at -600 km, and the crew halted 173 days out with
        /// 293 m/s aboard). The two readers of the same solver should agree; when they do not, the one that sees the encounter wins, and
        /// the first disagreement of a flight is logged with both chains.
        /// </summary>
        public static double VesselPeriapsisAt(Vessel v, CelestialBody body)
        {
            Orbit o = VesselPatchAround(v, body);
            if (o != null) return o.PeA;
            if (v == null || body == null || v.mainBody == body || !NodePlanner.Available(v)) return double.NaN;
            double probe = double.NaN;
            try { probe = Trial(v, Planetarium.GetUniversalTime() + 60, Vector3d.zero, n => NodePlanner.PeriapsisAt(n, body)); }
            catch (Exception e) { KsaCore.Log.Warn("VesselPeriapsisAt probe: " + e.Message); }
            if (!double.IsNaN(probe) && _probeNoted.Add(v.persistentId))
                KsaCore.Log.Info("Path reader: the vessel's own chain shows no " + body.bodyName + " encounter but a zero node shows periapsis " + Km(probe) + ". Vessel chain: " + DescribeChain(v.orbit));
            return probe;
        }

        private static readonly HashSet<uint> _probeNoted = new HashSet<uint>();

        /// <summary>The vessel's prediction, by either reader, reaches the body.</summary>
        public static bool VesselReaches(Vessel v, CelestialBody body) { return !double.IsNaN(VesselPeriapsisAt(v, body)); }

        /// <summary>One line per patch: body, how it ends, periapsis, length in days.</summary>
        public static string DescribeChain(Orbit o)
        {
            var parts = new List<string>();
            int guard = 0;
            while (o != null && guard++ < 8)
            {
                parts.Add(BodyName(o.referenceBody) + " " + o.patchEndTransition + " Pe " + Km(o.PeA) + " " + Math.Round((o.EndUT - o.StartUT) / Day, 1) + " d" + (o.activePatch ? "" : " (inactive)"));
                if (o.patchEndTransition == Orbit.PatchTransitionType.FINAL || o.patchEndTransition == Orbit.PatchTransitionType.INITIAL) break;
                o = o.nextPatch;
            }
            return string.Join(" > ", parts.ToArray());
        }

        /// <summary>
        /// The patch's low point is really flown (0.8.199): a patch that ends in a moon's sphere while still descending never reaches
        /// its periapsis (2026-11-04: the first Duna patch said 46 km two days out; after the Ike pass the real one was underground,
        /// and the correction had trimmed to the wrong one). Everything but a moon encounter on the way down counts.
        /// </summary>
        public static bool ReachesPeriapsis(Orbit o)
        {
            if (o == null) return false;
            if (o.patchEndTransition != Orbit.PatchTransitionType.ENCOUNTER) return true;
            try
            {
                if (o.eccentricity < 1 && o.EndUT - o.StartUT > o.period) return true;
                Vector3d r = o.getRelativePositionAtUT(o.EndUT), vel = o.getOrbitalVelocityAtUT(o.EndUT);
                return Vector3d.Dot(r, vel) >= 0;
            }
            catch { return true; }
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

        /// <summary>
        /// The path now reaches the target's orbit: stop, whatever the node says, and let the correction trim it (2026-10-31: a
        /// low-thrust lander burned 9 minutes on a 5-minute node, drifted off it and left Kerbin's reach with oxygen for 5 days).
        /// </summary>
        private bool FarEnough(Vessel v)
        {
            if (v.orbit == null || _target.orbit == null) return false;
            // Never short of the plan: from a high orbit the planned path itself runs past the moon (2026-10-31: 79 of a 91 m/s burn, a miss).
            return TransferRules.FarEnough(v.orbit.ApR, v.orbit.eccentricity >= 1, _plannedApR, _target.orbit.ApR, _target.sphereOfInfluence);
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
                _exec = new NodeExecutor { CutOff = FarEnough };
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

            // A later orbit when another world sits on the path (2026-10-31: bound for Minmus, the Mun bent the flight off course; a
            // 5 m/s correction was waiting beyond it). The moons move slowly, so this may mean many orbits in the parking orbit.
            _plan = null;
            ManeuverNode n = null;
            string blocked = null;
            bool reaches = false;
            int refined = 0;
            for (int wait = 0; wait < MaxWaitOrbits; wait++)
            {
                double from = now + 120 + wait * period;
                TransferPlan plan = null;
                try
                {
                    plan = TransferPlanner.Plan(OrbitAdapter.Sampler(v.orbit), OrbitAdapter.Sampler(_target.orbit), mu,
                        from, from + 1.2 * period, 0.4 * hohmannTime, 1.5 * hohmannTime, 48, 24, 0.3);
                }
                catch (Exception e) { KsaCore.Log.Error("TransferLeg planner threw: " + e); }
                if (plan == null) continue;
                NodePlanner.ClearNodes(v);
                n = NodePlanner.AddFrameDeltaV(v, plan.DepartureUt, plan.DeltaV);
                if (n == null) { Fail("could not place the transfer node"); return null; }
                _plan = plan;
                blocked = WorldInTheWay(n, parent);
                if (blocked != null) continue;
                if (!double.IsNaN(NodePlanner.PeriapsisAt(n, _target))) { reaches = true; break; }
                if (refined++ >= MaxRefines) continue;
                // The sums aim at the moon, but the game's own path decides: trim the burn until it shows the encounter.
                Vector3d comps = n.DeltaV;
                NodePlanner.ClearNodes(v);
                comps = Refine(v, plan.DepartureUt, comps, plan.TimeOfFlight);
                n = NodePlanner.Add(v, plan.DepartureUt, comps);
                if (n == null) { Fail("could not place the transfer node"); return null; }
                if (!double.IsNaN(NodePlanner.PeriapsisAt(n, _target)) && WorldInTheWay(n, parent) == null) { reaches = true; break; }
            }
            // No encounter, no burn (2026-10-31: three Minmus burns flew plans the game never showed reaching it, and the fuel went on nothing).
            if (!reaches)
            {
                NodePlanner.ClearNodes(v);
                Fail("no path to " + _target.bodyName + " from this orbit");
                return "No burn: no path found that reaches " + _target.bodyName + (blocked != null ? " clear of " + blocked : string.Empty) + " within " + MaxWaitOrbits + " orbits. Holding in orbit; Director, your call.";
            }
            _plannedApR = n.nextPatch != null && n.nextPatch.eccentricity < 1 ? n.nextPatch.ApR : double.PositiveInfinity;
            double predicted = NodePlanner.PeriapsisAt(n, _target);
            _exec = new NodeExecutor { CutOff = FarEnough };
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return null; }
            _phase = Phases.Burn;
            Status = "Transfer burn in " + TransferSupport.Countdown(_plan.DepartureUt - now) + ".";
            string pe = double.IsNaN(predicted) ? "no encounter in the node prediction yet" : "predicted periapsis " + TransferSupport.Km(predicted);
            return "Transfer planned: " + Math.Round(n.DeltaV.magnitude) + " m/s at " + TransferSupport.Date(_plan.DepartureUt)
                + ", flight time " + TransferSupport.Countdown(_plan.TimeOfFlight) + " (" + pe + ", target " + TransferSupport.Km(_arrivalPe) + ").";
        }

        /// <summary>The first world other than the target that the path after a node runs into, or null.</summary>
        private string WorldInTheWay(ManeuverNode n, CelestialBody parent)
        {
            foreach (Orbit o in NodePlanner.PatchesAfter(n))
            {
                if (o.referenceBody == _target) return null;
                if (o.referenceBody != parent) return o.referenceBody.bodyName;
            }
            return null;
        }

        /// <summary>Closest the path after a node comes to the target, metres; 0 when the game shows the encounter.</summary>
        private double Miss(ManeuverNode n, double ut, double seconds)
        {
            if (!double.IsNaN(NodePlanner.PeriapsisAt(n, _target))) return 0;
            if (n.nextPatch == null) return double.PositiveInfinity;
            double best = double.PositiveInfinity;
            for (int i = 1; i <= 240; i++)
            {
                double t = ut + seconds * i / 240;
                best = Math.Min(best, (OrbitAdapter.StateAt(n.nextPatch, t).Position - OrbitAdapter.StateAt(_target.orbit, t).Position).Length);
            }
            return best;
        }

        /// <summary>Trims a burn along each axis, large steps to small, until the path meets the target or stops getting closer.</summary>
        private Vector3d Refine(Vessel v, double ut, Vector3d comps, double tof)
        {
            double seconds = tof * 1.5;
            double best = TransferSupport.Trial(v, ut, comps, n => Miss(n, ut, seconds));
            Vector3d[] axes = { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1) };
            foreach (double step in new[] { 40.0, 15.0, 5.0, 1.5, 0.5 })
                for (int pass = 0; pass < 4 && best > 0; pass++)
                {
                    bool better = false;
                    foreach (Vector3d axis in axes)
                    {
                        Vector3d next = comps + axis * step;
                        double miss = TransferSupport.Trial(v, ut, next, n => Miss(n, ut, seconds));
                        if (!double.IsNaN(miss) && miss < best) { best = miss; comps = next; better = true; if (best <= 0) break; }
                    }
                    if (!better) break;
                }
            return comps;
        }

        private const int MaxRefines = 8;
        private const int MaxWaitOrbits = 40;
        private double _plannedApR;

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
        private enum Phases { Plan, Lower, Burn, Finished }
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
                // Only a burn under way is resumed: one still to come is planned afresh (2026-11-01: a save held a burn that missed Duna).
                if (existing.UT - ctx.Now > StartedWithin) { NodePlanner.ClearNodes(v); return; }
                _burnUt = existing.UT; _windowDv = existing.DeltaV.magnitude;
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                _phase = Phases.Burn;
                Status = "Resuming the ejection burn at " + TransferSupport.Date(_burnUt) + ".";
            }
        }

        /// <summary>
        /// The cheapest window from origin to target in the next cycle, with the near-window rule: the cheapest date in a whole cycle can be
        /// the next window when this one is only hours off or just gone, and a little more burn now beats two more years in orbit
        /// (2026-11-01, T: launched on a good window, reached orbit and was told 913 days). Null when nothing is found.
        /// </summary>
        public static TransferPlan Window(CelestialBody origin, CelestialBody target, double now, double synodic, double hohmannTime)
        {
            CelestialBody sun = origin.referenceBody;
            double sunMu = sun.gravParameter, day = TransferSupport.Day;
            TransferPlan plan = null;
            try
            {
                plan = TransferPlanner.Plan(OrbitAdapter.Sampler(origin.orbit), OrbitAdapter.Sampler(target.orbit), sunMu,
                    now + day, now + 1.1 * synodic, 0.5 * hohmannTime, 1.6 * hohmannTime, 60, 30, 0.5);
            }
            catch (Exception e) { KsaCore.Log.Error("InterplanetaryTransferLeg planner threw: " + e); plan = null; }
            if (plan == null) return null;
            if (plan.DepartureUt - now > NearWindowDays * day)
            {
                TransferPlan near = null;
                try { near = TransferPlanner.Plan(OrbitAdapter.Sampler(origin.orbit), OrbitAdapter.Sampler(target.orbit), sunMu, now + 600, now + NearWindowDays * day, 0.5 * hohmannTime, 1.6 * hohmannTime, 40, 30, 0.5); }
                catch (Exception e) { KsaCore.Log.Error("InterplanetaryTransferLeg near search threw: " + e); }
                if (near != null && near.DeltaVMagnitude <= plan.DeltaVMagnitude * NearWindowPrice) plan = near;
            }
            return plan;
        }

        /// <summary>The window home from a planet, for the ascent to climb into its plane (0.8.209). Null for a moon of home, or when nothing is found.</summary>
        public static TransferPlan HomeWindow(CelestialBody origin, CelestialBody home, double now)
        {
            if (origin == null || home == null || origin.orbit == null || home.orbit == null || origin.referenceBody == null || origin.referenceBody != home.referenceBody) return null;
            double t1 = origin.orbit.period, t2 = home.orbit.period;
            double synodic = (t1 > 0 && t2 > 0 && Math.Abs(1 / t1 - 1 / t2) > 1e-12) ? 1 / Math.Abs(1 / t1 - 1 / t2) : Math.Max(t1, t2);
            if (synodic <= 0 || double.IsNaN(synodic)) synodic = 400 * TransferSupport.Day;
            double hohmannTime;
            Ejection.Hohmann(origin.referenceBody.gravParameter, origin.orbit.semiMajorAxis, home.orbit.semiMajorAxis, out hohmannTime);
            if (double.IsNaN(hohmannTime) || hohmannTime <= 0) hohmannTime = synodic / 2;
            return Window(origin, home, now, synodic, hohmannTime);
        }

        private const double StartedWithin = 120; // seconds before a node: its burn may already be running
        private const double NearWindowDays = 30;
        private const double NearWindowPrice = 1.3; // of the best excess speed in the cycle
        private const double MaxParkingApoapsis = 130000;
        private const double LowParkingApoapsis = 110000;
        // Supplies set the latest arrival (0.8.202): land this many days before they run out, keep this much fuel for the trims and the
        // reentry, and never arrive faster than this (the shield's limit, well past a Duna return's 900 m/s).
        private const double SupplyMarginDays = 10, ReturnReserve = 150, MaxArrivalExcess = 2000;
        /// <summary>Kept aboard after the ejection for the deep-space and arrival corrections (0.8.207).</summary>
        private const double EjectionReserve = 60; // 100 in 0.8.207; a planned ejection's corrections ran 54 + 4 m/s on the way out

        /// <summary>
        /// Closest the path after a burn comes to the target, metres; 0 when it passes inside the target's sphere. All by the two-body sums:
        /// the game shows no path at all this far ahead of a node (2026-11-01: "closest pass Infinity", twice).
        /// </summary>
        private double Miss(Orbit parking, double ut, Vec3 burn, double arrival)
        {
            StateVector now = OrbitAdapter.StateAt(parking, ut);
            double seconds;
            StateVector rel = Ejection.ExitState(new StateVector(now.Position, now.Velocity + burn), _origin.gravParameter, _origin.sphereOfInfluence, out seconds);
            if (double.IsNaN(seconds)) return double.PositiveInfinity;
            double exit = ut + seconds, to = arrival + 40 * TransferSupport.Day, mu = _origin.referenceBody.gravParameter, best = double.PositiveInfinity;
            StateVector planet = OrbitAdapter.StateAt(_origin.orbit, exit);
            var start = new StateVector(rel.Position + planet.Position, rel.Velocity + planet.Velocity);
            for (int i = 1; i <= 240; i++)
            {
                double t = exit + (to - exit) * i / 240;
                best = Math.Min(best, (Kepler.Propagate(start, mu, t - exit).Position - OrbitAdapter.StateAt(_target.orbit, t).Position).Length);
            }
            return best < NearEnough * _target.sphereOfInfluence ? 0 : best;
        }

        private const double NearEnough = 0.5;

        /// <summary>Trims a burn along each axis, large steps to small, until the path meets the target or stops getting closer.</summary>
        private Vec3 Refine(Orbit parking, double ut, Vec3 burn, double arrival)
        {
            StateVector s = OrbitAdapter.StateAt(parking, ut);
            Vec3 along = s.Velocity.Normalized, up = Vec3.Cross(s.Position, s.Velocity).Normalized, side = Vec3.Cross(up, along);
            Vec3[] axes = { along, -along, up, -up, side, -side };
            double best = Miss(parking, ut, burn, arrival);
            foreach (double step in new[] { 40.0, 15.0, 5.0, 1.5, 0.5 })
                for (int pass = 0; pass < 8 && best > 0; pass++)
                {
                    bool better = false;
                    foreach (Vec3 axis in axes)
                    {
                        Vec3 next = burn + axis * step;
                        double miss = Miss(parking, ut, next, arrival);
                        if (miss < best) { best = miss; burn = next; better = true; if (best <= 0) break; }
                    }
                    if (!better) break;
                }
            return burn;
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
                            NodePlanner.ClearNodes(v); // nothing of the ejection stays in the plan for the next leg to find (0.8.177)
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
                case Phases.Lower:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed) { Fail(_exec.FailReason ?? "lowering burn failed"); break; }
                        _phase = Phases.Plan;
                    }
                    else Status = "Lowering the orbit: " + _exec.Status;
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

            TransferPlan plan = Window(_origin, _target, now, synodic, hohmannTime);
            if (plan == null) { Fail("no transfer window to " + _target.bodyName + " found"); return "Transfer planning failed: no window found."; }

            // The supplies aboard set the latest arrival (0.8.202, 2026-11-04: the mk IIIe's oxygen runs out 1,080 days after launch and the
            // cheapest return lands 1,150 days after it; the crew before died the same way at T+1,081 d, "of thirst" by the report). A dearer,
            // faster path home that lands while the air lasts beats the cheapest window; the fuel aboard less a reserve is the budget.
            string deadlineLine = null;
            if (ctx.SuppliesRunOutUt > 0 && !double.IsInfinity(ctx.SuppliesRunOutUt) && plan.ArrivalUt > ctx.SuppliesRunOutUt - SupplyMarginDays * day)
            {
                double latest = ctx.SuppliesRunOutUt - SupplyMarginDays * day;
                double rPark = v.orbit.semiMajorAxis > 0 ? v.orbit.semiMajorAxis : v.orbit.PeR;
                double aboard = ctx.Control.DeltaVAvailable(), budget = aboard - ReturnReserve;
                string what = ctx.ShortestSupply ?? "supplies";
                string shortBy = Math.Max(1, Math.Round((plan.ArrivalUt - ctx.SuppliesRunOutUt) / day)) + " days";
                TransferPlan fast = null;
                foreach (double weight in new[] { 0.5, 0.0 })
                {
                    TransferPlan f = null;
                    try { f = TransferPlanner.Plan(OrbitAdapter.Sampler(_origin.orbit), OrbitAdapter.Sampler(_target.orbit), sunMu, now + 600, Math.Max(now + 1200, latest - 0.2 * hohmannTime), 0.2 * hohmannTime, 1.6 * hohmannTime, 80, 40, weight, 0, latest, HomeArrival.MaxExcess(_target, MaxArrivalExcess)); }
                    catch (Exception e) { KsaCore.Log.Error("InterplanetaryTransferLeg deadline search threw: " + e); }
                    if (f != null && Ejection.DeltaV(_origin.gravParameter, rPark, f.DeltaV.Length) <= budget) { fast = f; break; }
                }
                if (fast != null)
                {
                    deadlineLine = "The " + what + " aboard runs out " + TransferSupport.Date(ctx.SuppliesRunOutUt) + ", " + shortBy + " before the cheapest window lands: taking a faster path home, "
                        + Math.Round(Ejection.DeltaV(_origin.gravParameter, rPark, fast.DeltaV.Length)) + " of the " + Math.Round(aboard) + " m/s aboard, landing " + TransferSupport.Date(fast.ArrivalUt) + " at " + Math.Round(fast.ArrivalSpeed) + " m/s excess.";
                    plan = fast;
                }
                else deadlineLine = "The " + what + " aboard runs out " + TransferSupport.Date(ctx.SuppliesRunOutUt) + ", " + shortBy + " before the cheapest window lands, and no faster path fits the " + Math.Round(aboard) + " m/s aboard. Flying the window. Director: without a resupply the crew will not make it.";
            }

            // Hyperbolic excess relative to the planet, in the same frame as the parking orbit.
            Vec3 vInf = plan.DeltaV;
            double vInfMag = vInf.Length;
            if (vInfMag < 1) { Fail("degenerate transfer solution"); return null; }
            double planetMu = _origin.gravParameter;
            double period = v.orbit.period;
            double r = v.orbit.semiMajorAxis > 0 ? v.orbit.semiMajorAxis : v.orbit.PeR;

            // The burn point and vector on the orbit as it is, tilted or not (0.8.207): the old in-plane sums from a parking orbit tilted by
            // the landing site's latitude missed Kerbin by 447,000 km, and the 334 m/s correction ran the tanks and the thrusters dry.
            double start = Math.Max(now + 60, plan.DepartureUt - 0.5 * period);
            double bestUt; Vec3 burn3;
            if (!Ejection.PlanFromOrbit(OrbitAdapter.Sampler(v.orbit), planetMu, vInf, start, period, out bestUt, out burn3))
            {
                Fail("the leaving direction is too far out of the parking orbit's plane");
                return "Transfer planning failed: the way to " + _target.bodyName + " leaves too far out of this orbit's plane for one burn. Director, your call.";
            }
            double planeCost = burn3.Length - Ejection.DeltaV(planetMu, r, vInfMag);
            double outOfPlane = Math.Abs(90 - Vec3.AngleBetween(OrbitAdapter.Normal(v.orbit, now), vInf) * 180 / Math.PI);
            if (outOfPlane > 2) KsaCore.Log.Info("Ejection: the way to " + _target.bodyName + " lies " + Math.Round(outOfPlane, 1) + " deg out of the parking orbit's plane (inclination " + Math.Round(v.orbit.inclination, 1) + " deg); plane change in the burn " + Math.Round(planeCost) + " m/s.");

            // A long wait high up is a dose of radiation with Kerbalism: come down first (2026-11-01: 390 x 75 km, all three dead in 17 days;
            // 197 x 75 km, all three dead by day 198). A low wait is safe: 111 x 75 km for 128 days.
            if (KsaCore.Agency != null && KsaCore.Agency.LifeSupportOn && v.orbit.ApA > MaxParkingApoapsis && bestUt - now > 2 * day)
            {
                double rPe = v.orbit.PeR, rWanted = _origin.Radius + LowParkingApoapsis, utPe = now + v.orbit.timeToPe;
                double dvLower = Math.Sqrt(planetMu * (2 / rPe - 1 / v.orbit.semiMajorAxis)) - Math.Sqrt(planetMu * (2 / rPe - 2 / (rPe + rWanted)));
                NodePlanner.ClearNodes(v);
                if (NodePlanner.Add(v, utPe, new Vector3d(0, 0, -dvLower)) == null) { Fail("could not place the lowering burn"); return null; }
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                if (_exec.Failed) { Fail(_exec.FailReason); return null; }
                _phase = Phases.Lower;
                Status = "Lowering the orbit out of the radiation: " + Math.Round(dvLower) + " m/s.";
                return "Apoapsis " + TransferSupport.Km(v.orbit.ApA) + " is in the radiation and the window is " + TransferSupport.Countdown(bestUt - now) + " away: lowering it to " + TransferSupport.Km(LowParkingApoapsis) + " with " + Math.Round(dvLower) + " m/s.";
            }
            double dv = burn3.Length;
            // Never leave on a burn the craft cannot pay for with a correction to spare (0.8.207): the crew wait for the Director instead.
            // The thrusters count (0.8.208: 803 m/s in the tanks against an 820 m/s ejection, with 168 monopropellant unspent), and a window
            // picked for the cheapest departure is tried before giving up: the search weighs the arrival speed, which a reentry at home
            // does not pay for.
            double aboardNow = ctx.Control.DeltaVAvailable(), onThrusters = ctx.Control.RcsDeltaV();
            string cheaper = null;
            if (aboardNow > 0 && dv + EjectionReserve > aboardNow + onThrusters)
            {
                TransferPlan lean = null;
                try { lean = TransferPlanner.Plan(OrbitAdapter.Sampler(_origin.orbit), OrbitAdapter.Sampler(_target.orbit), sunMu, now + day, now + 1.1 * synodic, 0.5 * hohmannTime, 1.6 * hohmannTime, 60, 30, 0.0, 0, double.PositiveInfinity, HomeArrival.MaxExcess(_target, MaxArrivalExcess)); }
                catch (Exception e) { KsaCore.Log.Error("InterplanetaryTransferLeg lean search threw: " + e); }
                double leanUt; Vec3 leanBurn;
                if (lean != null && lean.DeltaV.Length >= 1 && Ejection.PlanFromOrbit(OrbitAdapter.Sampler(v.orbit), planetMu, lean.DeltaV, Math.Max(now + 60, lean.DepartureUt - 0.5 * period), period, out leanUt, out leanBurn) && leanBurn.Length < dv - 1)
                {
                    cheaper = " The gentlest window needed " + Math.Round(dv) + " m/s; this one arrives faster (" + Math.Round(lean.ArrivalSpeed) + " m/s excess) for " + Math.Round(leanBurn.Length) + ".";
                    plan = lean; vInf = lean.DeltaV; vInfMag = vInf.Length; bestUt = leanUt; burn3 = leanBurn; dv = burn3.Length;
                    planeCost = dv - Ejection.DeltaV(planetMu, r, vInfMag);
                }
            }
            if (aboardNow > 0 && dv + EjectionReserve > aboardNow + onThrusters)
            {
                Fail("the ejection needs " + Math.Round(dv) + " m/s plus " + EjectionReserve + " for corrections, and " + Math.Round(aboardNow + onThrusters) + " is aboard");
                return "Holding in orbit: the ejection to " + _target.bodyName + " needs " + Math.Round(dv) + " m/s" + (planeCost > 5 ? " (" + Math.Round(planeCost) + " of it the plane change)" : string.Empty) + " plus " + EjectionReserve + " for the corrections, and the tanks hold " + Math.Round(aboardNow) + (onThrusters > 0 ? " with " + Math.Round(onThrusters) + " more on the thrusters" : string.Empty) + ". Director: a tanker, or a resupply.";
            }
            string thrusterNote = onThrusters > 0 && dv + EjectionReserve > aboardNow ? " The tanks hold " + Math.Round(aboardNow) + "; the rest and the corrections ride on the thrusters (" + Math.Round(onThrusters) + " m/s there)." : string.Empty;
            NodePlanner.ClearNodes(v);
            // The sums ignore the days to the edge of the sphere: trim against the game's own path
            // (2026-11-01: a perfect 1,047 m/s burn missed Duna, and no correction out in the sun's space could find it).
            Vec3 burn = Refine(v.orbit, bestUt, burn3, plan.ArrivalUt);
            ManeuverNode n = NodePlanner.AddFrameDeltaV(v, bestUt, burn);
            if (n == null) { Fail("could not place the ejection node"); return null; }
            double pass = Miss(v.orbit, bestUt, burn, plan.ArrivalUt);
            ctx.Say("Ejection trimmed to " + Math.Round(n.DeltaV.magnitude) + " m/s: " + (pass > 0 ? "closest pass " + TransferSupport.Km(pass) : "the sums reach " + _target.bodyName + "'s sphere") + ".");
            dv = n.DeltaV.magnitude;
            _burnUt = bestUt; _windowDv = dv;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return null; }
            _phase = Phases.Burn;
            Status = "Ejection " + TransferSupport.Date(bestUt) + " (" + Math.Round(dv) + " m/s): waiting for the window.";
            bool escape = n.nextPatch != null && n.nextPatch.patchEndTransition == Orbit.PatchTransitionType.ESCAPE;
            return (deadlineLine != null ? deadlineLine + " " : string.Empty) + "Window to " + _target.bodyName + ": depart " + TransferSupport.Date(plan.DepartureUt) + ", arrive " + TransferSupport.Date(plan.ArrivalUt)
                + " (" + TransferSupport.Countdown(plan.TimeOfFlight) + " flight, " + Math.Round(vInfMag) + " m/s excess). Ejection burn "
                + Math.Round(dv) + " m/s" + (planeCost > 5 ? " (" + Math.Round(planeCost) + " of it the plane change)" : string.Empty) + " at " + TransferSupport.Date(bestUt) + (escape ? "." : "; node prediction shows no escape yet.") + (cheaper ?? string.Empty) + thrusterNote;
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
        private double _peLo, _peHi;
        private readonly double _maxDv;
        private bool _hotNoted;
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
        private double _budget;
        private int _rounds;
        private double _settleUntil;
        private bool _trim;
        /// <summary>Trims after a burn that left the periapsis out of band (0.8.173: one 75 m/s burn 336 days out, 12,579 km off, "complete").</summary>
        private const int MaxRounds = 3;
        /// <summary>What a crew turning for home keeps back for the trims and the reentry.</summary>
        private const double HomeReserve = 150;
        /// <summary>A correction on the way home: it may spend the fuel aboard, not a fixed allowance, and wait for a cheaper window.</summary>
        public bool Homebound { get; set; }
        /// <summary>Out around the sun with no path to the target within the budget: the program turns the crew for home (0.8.173).</summary>
        public bool GaveUp { get; private set; }

        /// <summary>The fuel this correction may spend: the fixed allowance, or on the way home everything aboard but a reserve.</summary>
        private double Budget(FlightContext ctx)
        {
            if (!Homebound || ctx == null || ctx.Control == null) return _maxDv;
            double aboard = ctx.Control.DeltaVAvailable();
            return aboard > 0 ? Math.Max(_maxDv, aboard - HomeReserve) : _maxDv;
        }

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
            _rounds = 0; _trim = false; GaveUp = false; _budget = _maxDv;
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
                // Only a node this leg could have made, about now (0.8.177): the ejection's spent node, left in the plan, was
                // "resumed" here and 883 m/s went into nothing.
                ManeuverNode found = v.patchedConicSolver.maneuverNodes[0];
                double size = found.DeltaV.magnitude;
                if (size <= Budget(ctx) && found.UT > ctx.Now - 600 && found.UT < ctx.Now + 3600)
                {
                    _exec = new NodeExecutor();
                    _exec.Begin(ctx);
                    _phase = Phases.Burn;
                    Status = "Resuming the correction burn.";
                }
                else
                {
                    NodePlanner.ClearNodes(v);
                    ctx.Say("Cleared a stale maneuver node of " + Math.Round(size) + " m/s before the correction.");
                }
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
                        // Arriving hot at home (0.8.215): the band moves up to the periapsis at which the simulator says one skimming pass
                        // captures the capsule, instead of the dive that burned a KV-3 up at 4,187 m/s.
                        if (Homebound && ctx.Home != null && _target == ctx.Home && _target.atmosphere)
                        {
                            double entry;
                            if (HomeArrival.Hot(v, _target, out entry))
                            {
                                double passPe = HomeArrival.PassPeriapsis(_target, HomeArrival.ExcessAt(v, _target));
                                if (!double.IsNaN(passPe))
                                {
                                    if (Math.Abs((_peLo + _peHi) / 2 - passPe) > 100) { _peLo = passPe - HomeArrival.PassBand; _peHi = passPe + HomeArrival.PassBand; }
                                    if (!_hotNoted) { _hotNoted = true; ctx.Say("Arriving hot: " + Math.Round(entry) + " m/s at the top of the air, over the " + AirbrakeRules.SafeDirectEntry + " a direct entry can take. Aiming the periapsis at " + TransferSupport.Km(passPe) + " to skim the air, the capsule alone, as many passes as it takes."); }
                                }
                                else if (!_hotNoted) { _hotNoted = true; ctx.Say("Arriving hot: " + Math.Round(entry) + " m/s at the top of the air, and no skimming pass between " + TransferSupport.Km(HomeArrival.PassFloor) + " and " + TransferSupport.Km(HomeArrival.PassCeiling) + " captures at that speed. A direct entry is all there is."); }
                            }
                        }
                        double current = TransferSupport.VesselPeriapsisAt(v, _target);
                        _budget = Budget(ctx);
                        double back = _returnTo != null ? ReturnPeriapsis(v.orbit, _target, _returnTo) : double.NaN;
                        bool comesHome = _returnTo == null || double.IsNaN(back) || back < GoodReturnPe;
                        if (TransferSupport.InBand(current, _peLo, _peHi) && comesHome)
                        {
                            State = LegState.Done; Status = "On course.";
                            line = "Trajectory check: " + _target.bodyName + " periapsis " + TransferSupport.Km(current) + (double.IsNaN(back) ? string.Empty : ", then back to Kerbin at " + TransferSupport.Km(back)) + ", no correction needed.";
                            break;
                        }
                        double now = ctx.Now;
                        // No encounter, or one that passes far out: small nudges do not reach (2026-11-01: 25,334 km over Duna, then 1,455 m/s to stop there).
                        // After a burn the preview shows where the path goes: nudge it in, never plan a fresh transfer again.
                        if (!_trim && (double.IsNaN(current) || current > _peHi + FarOff) && AimFromDeepSpace(ctx, ref line)) break;
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
                            Status = "No correction within " + Math.Round(_budget) + " m/s; flying on.";
                            double cur = TransferSupport.VesselPeriapsisAt(v, _target);
                            // No encounter at all and no fresh transfer within the budget: the program may turn the crew for home.
                            // A path that still meets the target is flown on; the mid-course check gets another go (0.8.175: 4,971 km over Duna
                            // 340 days out was called "no path" and 875 m/s went on a burn for home eight days into the flight).
                            GaveUp = _target.referenceBody == v.mainBody && double.IsNaN(cur);
                            line = "No correction found within " + Math.Round(_budget) + " m/s"
                                + (double.IsNaN(cur) ? "; no " + _target.bodyName + " encounter on the current path." : "; continuing with periapsis " + TransferSupport.Km(cur) + ".");
                            break;
                        }
                        if (_bestDv < MinCorrection && !(ctx.Control.HasRcs() && ctx.Control.RcsPropellant() > 1))
                        {
                            // Below what the engine can burn cleanly (0.8.189): fly on; the correction nearer the target has a shorter lever and a real burn.
                            State = LegState.Done;
                            double cur = TransferSupport.VesselPeriapsisAt(v, _target);
                            Status = "On course within the finest burn.";
                            line = "A correction of " + _bestDv.ToString("0.00") + " m/s is below what the engine can burn; flying on with " + _target.bodyName + " periapsis " + TransferSupport.Km(cur) + ".";
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
                        // The solver's prediction lags a burn by a frame or two (0.8.212): read it a second later, never in the frame the burn ended.
                        if (_settleUntil <= 0) { _settleUntil = ctx.Now + 1.0; Status = "Reading the path after the burn."; break; }
                        if (ctx.Now < _settleUntil) break;
                        _settleUntil = 0;
                        double pe = TransferSupport.VesselPeriapsisAt(v, _target);
                        string peText = double.IsNaN(pe) ? "no " + _target.bodyName + " encounter predicted" : "predicted " + _target.bodyName + " periapsis " + TransferSupport.Km(pe);
                        // Out of band with the encounter showing: trim again while the lever arm is long (0.8.173).
                        if (!_exec.Failed && !double.IsNaN(pe) && !TransferSupport.InBand(pe, _peLo, _peHi) && _rounds < MaxRounds)
                        {
                            _rounds++; _trim = true;
                            _phase = Phases.Prepare;
                            ctx.Say("Correction complete; " + peText + "; trimming (" + _rounds + " of " + MaxRounds + ").");
                            Status = "Trimming the path.";
                            break;
                        }
                        _phase = Phases.Finished;
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

        /// <summary>
        /// Out around the sun with no encounter, small nudges find nothing: work out a fresh transfer from this orbit to the target's,
        /// and burn it when it shows the encounter and fits the budget.
        /// </summary>
        private bool AimFromDeepSpace(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            CelestialBody sun = _target.referenceBody;
            if (sun == null || v.mainBody != sun) return false;
            double mu = sun.gravParameter, now = ctx.Now, flight;
            Ejection.Hohmann(mu, v.orbit.semiMajorAxis, _target.orbit.semiMajorAxis, out flight);
            if (double.IsNaN(flight) || flight <= 0) return false;
            TransferPlan plan = null;
            // Outbound: leave within days. Homebound (0.8.173): wait up to 200 days for a cheaper window, and weigh the arrival speed in
            // full (a 1,741 m/s path home from beside Duna came in at 5.2 km/s excess; 200 days later it was 3.0 km/s).
            double latest = now + (Homebound ? 200 : 5) * TransferSupport.Day;
            try
            {
                plan = TransferPlanner.Plan(OrbitAdapter.Sampler(v.orbit), OrbitAdapter.Sampler(_target.orbit), mu,
                    now + 600, latest, 0.3 * flight, 1.6 * flight, Homebound ? 40 : 24, 30, Homebound ? 1.0 : 0.5);
            }
            catch (Exception e) { KsaCore.Log.Error("Deep-space correction planner threw: " + e); }
            if (plan == null || plan.DeltaVMagnitude > _budget)
            {
                ctx.Say(plan == null ? "No fresh transfer to " + _target.bodyName + " found from here." : "A fresh transfer to " + _target.bodyName + " needs " + Math.Round(plan.DeltaVMagnitude) + " m/s, over the " + Math.Round(_budget) + " allowed.");
                return false;
            }
            // The air aboard sets the latest arrival here too (0.8.212: 161 days of oxygen, 173 days to Kerbin, 293 m/s in the tanks and the
            // thrusters' share unspent): the cheapest path that lands before it, within everything aboard less a reserve, or the plain plan
            // and a plain warning when nothing fits.
            string supplyLine = null;
            if (Homebound && ctx.SuppliesRunOutUt > 0 && !double.IsInfinity(ctx.SuppliesRunOutUt) && plan.ArrivalUt > ctx.SuppliesRunOutUt - DeepSpaceSupplyMarginDays * TransferSupport.Day)
            {
                double deadline = ctx.SuppliesRunOutUt - DeepSpaceSupplyMarginDays * TransferSupport.Day;
                double everything = ctx.Control.DeltaVAvailable() + ctx.Control.RcsDeltaV() - DeepSpaceReserve;
                string what = ctx.ShortestSupply ?? "supplies";
                TransferPlan fast = null;
                foreach (double weight in new[] { 0.5, 0.0 })
                {
                    TransferPlan f = null;
                    try { f = TransferPlanner.Plan(OrbitAdapter.Sampler(v.orbit), OrbitAdapter.Sampler(_target.orbit), mu, now + 600, Math.Max(now + 1200, Math.Min(latest, deadline - 0.2 * flight)), 0.15 * flight, 1.6 * flight, 60, 40, weight, 0, deadline, HomeArrival.MaxExcess(_target, DeepSpaceMaxArrivalExcess)); }
                    catch (Exception e) { KsaCore.Log.Error("Deep-space deadline search threw: " + e); }
                    if (f != null && f.DeltaVMagnitude <= everything) { fast = f; break; }
                }
                double shortBy = Math.Max(1, Math.Round((plan.ArrivalUt - ctx.SuppliesRunOutUt) / TransferSupport.Day));
                if (fast != null)
                {
                    supplyLine = " The " + what + " aboard runs out " + TransferSupport.Date(ctx.SuppliesRunOutUt) + ", " + shortBy + " days before the plain path lands: taking the faster one, " + Math.Round(fast.DeltaVMagnitude) + " of the " + Math.Round(everything + DeepSpaceReserve) + " m/s aboard, landing " + TransferSupport.Date(fast.ArrivalUt) + " at " + Math.Round(fast.ArrivalSpeed) + " m/s excess." + HomeArrival.EntryNote(_target, fast.ArrivalSpeed);
                    plan = fast;
                }
                else supplyLine = " The " + what + " aboard runs out " + TransferSupport.Date(ctx.SuppliesRunOutUt) + ", " + shortBy + " days before this path lands, and no faster path fits the " + Math.Round(everything + DeepSpaceReserve) + " m/s aboard. Director: the crew will not make it on this.";
            }
            NodePlanner.ClearNodes(v);
            ManeuverNode n = NodePlanner.AddFrameDeltaV(v, plan.DepartureUt, plan.DeltaV);
            if (n == null) return false;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { NodePlanner.ClearNodes(v); return false; }
            _phase = Phases.Burn;
            Status = "Correction burn of " + Math.Round(plan.DeltaVMagnitude) + " m/s.";
            double pe = NodePlanner.PeriapsisAt(n, _target);
            line = "Deep-space correction planned: " + Math.Round(plan.DeltaVMagnitude) + " m/s in " + TransferSupport.Countdown(plan.DepartureUt - now) + ", " + TransferSupport.Countdown(plan.TimeOfFlight) + " to " + _target.bodyName + (double.IsNaN(pe) ? "; the game's preview does not show the encounter yet." : ", predicted periapsis " + TransferSupport.Km(pe) + ".") + (supplyLine ?? string.Empty);
            return true;
        }

        private const double DeepSpaceSupplyMarginDays = 5, DeepSpaceReserve = 40, DeepSpaceMaxArrivalExcess = 2500;

        private const double FarOff = 200000; // metres past the band: a fresh transfer, not a nudge
        private const double MinCorrection = 0.3; // m/s: the finest burn worth making

        private double Score(ManeuverNode n) { return NodePlanner.PeriapsisAt(n, _target); }

        /// <summary>
        /// Search caps, smallest first (0.8.175): months out a nudge of a fraction of a metre per second moves the periapsis thousands of
        /// kilometres, and a bisection against the whole allowance loses the encounter at its far end and never brackets the band.
        /// </summary>
        private static readonly double[] Caps = { 1, 5, 25, 100 };

        private void TrySingle(Vessel v, Vector3d dir)
        {
            double dv = double.NaN;
            foreach (double cap in CapsUpTo(_budget))
            {
                try { dv = NodePlanner.Bisect(v, _ut, dir, cap, Score, _peLo, _peHi); } catch { dv = double.NaN; }
                if (double.IsNaN(dv))
                {
                    try { dv = TransferSupport.LineSearch(v, _ut, Vector3d.zero, dir, cap, 20, Score, _peLo, _peHi); } catch { dv = double.NaN; }
                }
                if (!double.IsNaN(dv)) break;
            }
            Consider(dv, dir * dv);
        }

        private static List<double> CapsUpTo(double budget)
        {
            var caps = new List<double>();
            foreach (double c in Caps) if (c < budget) caps.Add(c);
            caps.Add(budget);
            return caps;
        }

        /// <summary>Normal (+/-) first until an encounter exists, then radial (+/-) within what budget remains.</summary>
        private void TryCombo(Vessel v, int index)
        {
            Vector3d normal = index < 2 ? new Vector3d(0, 1, 0) : new Vector3d(0, -1, 0);
            Vector3d radial = (index % 2 == 0) ? new Vector3d(1, 0, 0) : new Vector3d(-1, 0, 0);
            double dn;
            try { dn = TransferSupport.FirstFinite(v, _ut, normal, _budget, 20, Score); } catch { dn = double.NaN; }
            if (double.IsNaN(dn) || dn >= _budget) return;
            double budget = _budget - dn;
            double dr = double.NaN;
            foreach (double cap in CapsUpTo(budget))
            {
                try { dr = TransferSupport.LineSearch(v, _ut, normal * dn, radial, cap, 20, Score, _peLo, _peHi); } catch { dr = double.NaN; }
                if (!double.IsNaN(dr)) break;
            }
            if (double.IsNaN(dr)) return;
            Vector3d comps = normal * dn + radial * dr;
            Consider(comps.magnitude, comps);
        }

        private void Consider(double dv, Vector3d comps)
        {
            if (double.IsNaN(dv) || dv < 0 || dv > _budget) return;
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
        private readonly double _stopBefore; // seconds short of the body's space to stop for a mid-course check (0.8.173); 0 = all the way in
        private double _deadline;
        private double _lastWarpRequest;
        private double _turnStarted;
        private bool _panelsChecked;
        private const double SettleLimit = 120; // seconds allowed for the turn before the coast goes on regardless
        private const double ReaimDegrees = 10; // the sun moves this far round the sky: stop, turn the panels back to it, warp on (0.8.186)

        /// <summary>
        /// No warp until the turn to the sun is done (0.8.178): the coast had warped in the same second it ordered the turn, the
        /// attitude froze half way round, and Kerbalism took that for the whole cruise (0.01 EC/s from 48 panels, three dead).
        /// Logs what the panels make once settled. True while still turning.
        /// </summary>
        private bool Turning(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            if (v == null || v.packed) return false;
            double now = ctx.Now;
            bool settled = ctx.Control.HoldSettled() && now - _turnStarted > 5;
            if (!settled && now - _turnStarted < SettleLimit) { Status = "Turning the panels to the sun."; if (ctx.Control.IsWarping) ctx.Control.StopWarp(); return true; }
            if (!_panelsChecked)
            {
                _panelsChecked = true;
                double made = ctx.Control.PanelOutput();
                line = "Panels making " + made.ToString("0.00") + " EC/s" + (settled ? "" : " (turn not settled after " + Math.Round(now - _turnStarted) + " s)") + ".";
            }
            return false;
        }

        public WaitForSoiLeg(CelestialBody body, double maxDays)
        {
            _body = body;
            _maxDays = Math.Max(0.01, maxDays);
        }

        /// <summary>Stops the given days short of the body's space, so a correction can fix the path while a nudge still reaches.</summary>
        public WaitForSoiLeg(CelestialBody body, double maxDays, double stopDaysBefore) : this(body, maxDays) { _stopBefore = Math.Max(0, stopDaysBefore) * TransferSupport.Day; }

        public string Name { get { return (_stopBefore > 0 ? "Cruise toward " : "Coast to ") + TransferSupport.BodyName(_body); } }
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
            ctx.Control.FacePanelsAtSun(); // the panels' side to the sun for the long coast (0.8.176, 0.8.178)
            _turnStarted = ctx.Now; _panelsChecked = false;
            string plan = ctx.Control.PanelPlan;
            if (plan != null) ctx.Say("Turning the panels to the sun: " + plan + ".");
            string power = Parts.KspLifeSupport.PowerReport(v);
            if (power != null) ctx.Say(power);
            // A moon that the path misses: never warp on while the supplies run out (2026-10-31: five days of warp to the edge of
            // Kerbin's space, 3,381 m/s still aboard, and the crew out of oxygen). Stop here so the Director can fly it home.
            if (_stopBefore > 0 && !TransferSupport.VesselReaches(v, _body))
            {
                // No encounter yet: the correction after this leg gets its look; the coast after that halts if there is still none.
                State = LegState.Done; Status = "No " + _body.bodyName + " encounter yet; checking the path.";
                return;
            }
            if (_body.referenceBody == v.mainBody && !TransferSupport.VesselReaches(v, _body))
            {
                State = LegState.Failed;
                Status = "This path misses " + _body.bodyName + ".";
                ctx.Say("No " + _body.bodyName + " encounter on this path; holding here, no warp. Director, take control.");
            }
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
            // Only once the last turn has settled (0.8.188): the pointing error is large while the turn is still under way, and
            // checking it then re-ordered the turn every frame, reset the settle clock and stopped the warp for good.
            if (_panelsChecked && ctx.Control.SunDrift() > ReaimDegrees)
            {
                ctx.Control.StopWarp();
                if (v.packed) { Status = "Stopping to turn the panels to the sun."; return null; }
                double moved = ctx.Control.SunDrift();
                ctx.Control.FacePanelsAtSun();
                _turnStarted = now; _panelsChecked = false;
                return "The sun has moved " + Math.Round(moved) + " deg round the sky: turning the panels back to it.";
            }
            string turning = null;
            if (Turning(ctx, ref turning)) return null;
            if (turning != null) return turning;
            if (_stopBefore > 0)
            {
                Orbit around = TransferSupport.VesselPatchAround(v, _body);
                double entry = around != null ? around.StartUT : double.NaN;
                if (double.IsNaN(entry) || entry - now <= _stopBefore + 120)
                {
                    ctx.Control.StopWarp();
                    State = LegState.Done;
                    Status = double.IsNaN(entry) ? "No " + _body.bodyName + " encounter on this path." : TransferSupport.Countdown(entry - now) + " from " + _body.bodyName + ".";
                    return double.IsNaN(entry) ? null : "Mid-course check: " + TransferSupport.Countdown(entry - now) + " from " + _body.bodyName + ", periapsis " + TransferSupport.Km(around.PeA) + ".";
                }
                Status = "Mid-course check in " + TransferSupport.Countdown(entry - now - _stopBefore) + ".";
                if (!ctx.Control.IsWarping && now - _lastWarpRequest > 2) { _lastWarpRequest = now; ctx.Control.WarpTo(Math.Min(entry - _stopBefore, _deadline + 5)); }
                return null;
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
    /// Since 0.8.199 a moon across the approach is dodged before the burn is planned, a moon's sphere met anyway is
    /// ridden through, and a path that moved under the planned burn is trimmed back with a radial burn.
    /// </summary>
    public sealed class CaptureLeg : IFlightLeg
    {
        private enum Phases { Check, Dodge, Capture, Retrim, Circularize, Finished }
        private readonly CelestialBody _target;
        private readonly double _altitude;
        private Phases _phase;
        private NodeExecutor _exec;
        private bool _secondPlanned;
        // The path under the planned burn (0.8.199): a moon pass on the way in moves it, and the burn is planned again.
        private double _plannedPe, _lastWarpAsk = -1e9;
        private bool _moonPassed;
        private int _dodges, _retrims;
        private const int MaxDodges = 2, MaxRetrims = 3;
        private const double DodgeMaxCost = 100, TurnSeconds = 60, MoonSafeHeight = 20000, RetrimMaxDv = 900;

        public CaptureLeg(CelestialBody target, double orbitAltitude)
        {
            _target = target;
            _altitude = Math.Max(1000, orbitAltitude);
        }

        /// <summary>A capture for aerobraking: only enough to stay bound, into a long orbit that the air then shortens (AirbrakeLeg).</summary>
        public CaptureLeg(CelestialBody target, double orbitAltitude, double looseApoapsis) : this(target, orbitAltitude) { _loose = looseApoapsis; }

        private readonly double _loose;
        private const double MaxCapturePe = 1000000;
        private const double BurnAboveAir = 80000; // metres: the burn is over, and the craft turned round, before the air

        /// <summary>The burn runs through the air on a loose capture, where the game calls the craft flying, not orbiting.</summary>
        private bool Bound(Vessel v) { return _loose > 0 ? v.orbit != null && v.orbit.eccentricity < 1 : Captured(v); }

        public string Name { get { return "Orbit insertion at " + TransferSupport.BodyName(_target); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _phase = Phases.Check;
            _secondPlanned = false;
            _moonPassed = false;
            _plannedPe = 0;
            _dodges = 0;
            _retrims = 0;
            Status = "Preparing orbit insertion.";
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _target == null) { Fail("no vessel orbit or target"); return; }
            if (v.mainBody != _target) { Fail("not in " + _target.bodyName + "'s sphere of influence"); return; }
            if (Captured(v) && v.orbit.ApA <= _altitude * 1.3 + 5000)
            {
                State = LegState.Done; Status = "Already in orbit.";
                return;
            }
            if (_loose > 0) ctx.Control.TendDeployables(false); // before the long warp to the air
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
                    if (_loose > 0 && v.orbit.eccentricity >= 1 && v.orbit.PeA > MaxCapturePe)
                    {
                        // Far too high to aerobrake: a capture there costs more than the whole way down. Stop for the Director with the fuel intact.
                        Fail("periapsis " + TransferSupport.Km(v.orbit.PeA) + " is too high to capture");
                        line = "Holding: the path passes " + TransferSupport.Km(v.orbit.PeA) + " over " + _target.bodyName + ", no correction reaches the air, and a capture there would cost most of the fuel. Director, your call.";
                        break;
                    }
                    if (v.orbit.PeA < 5000)
                    {
                        // Into the ground (0.8.199): a radial burn now lifts the low point back into its band before anything else.
                        if (PlanRetrim(ctx, ref line)) break;
                        Fail("trajectory impacts " + _target.bodyName);
                        line = line ?? ("Incoming trajectory impacts " + _target.bodyName + " (periapsis " + TransferSupport.Km(v.orbit.PeA) + ") and no radial burn within " + RetrimMaxDv + " m/s lifts it; insertion aborted. Director, your call.");
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
                    if (PlanDodge(ctx, ref line)) break;
                    line = line ?? PlanCapture(ctx);
                    break;
                case Phases.Dodge:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed && !_exec.GroundAhead) { Fail("moon dodge burn failed: " + _exec.FailReason); break; }
                        _phase = Phases.Check;
                    }
                    else Status = "Clearing the moon: " + _exec.Status;
                    break;
                case Phases.Capture:
                    if (!_exec.Burning && WatchThePath(ctx, ref line)) break;
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed && _exec.GroundAhead) { if (WatchThePath(ctx, ref line)) break; }
                        if (!Bound(v))
                        {
                            Fail(_exec.Failed ? "capture burn incomplete: " + _exec.FailReason : "still not bound to " + _target.bodyName + " after the burn");
                            line = line ?? ("Insertion burn did not capture; still escaping " + _target.bodyName + ".");
                            break;
                        }
                        if (_exec.Failed || !PlanCircularise(ctx, ref line)) Finish(ctx, ref line);
                    }
                    else Status = "Insertion: " + _exec.Status;
                    break;
                case Phases.Retrim:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed && !_exec.GroundAhead) { Fail("retrim burn failed: " + _exec.FailReason); break; }
                        _phase = Phases.Check;
                        if (v.mainBody == _target) line = line ?? ("Approach trimmed: periapsis now " + TransferSupport.Km(v.orbit.PeA) + "; planning the insertion again.");
                    }
                    else Status = "Retrimming the approach: " + _exec.Status;
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

        /// <summary>
        /// The path under the planned burn, while the crew waits for it (0.8.199, 2026-11-04: the mk IIIe passed Ike two days before
        /// its Duna capture, the pass bent the path into the ground, the node kept its time, and the crew warped into the surface at
        /// 632 m/s; three dead). A moon's sphere on the way in is ridden through under warp; back around the planet the insertion is
        /// planned again, after a radial burn when the periapsis moved or the executor raised its ground alarm. True when the leg
        /// took the step over from the executor.
        /// </summary>
        private bool WatchThePath(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            bool alarm = _exec.Failed && _exec.GroundAhead;
            if (v.mainBody != _target && !alarm)
            {
                if (!_moonPassed)
                {
                    _moonPassed = true;
                    line = "Passing through " + TransferSupport.BodyName(v.mainBody) + "'s sphere on the way in (" + TransferSupport.Km(v.orbit.PeA) + " over it); the insertion is planned again on the far side.";
                }
                Status = "Passing " + v.mainBody.bodyName + ".";
                if (!ctx.Control.IsWarping && ctx.Now - _lastWarpAsk > 2 && v.orbit.EndUT > ctx.Now + 30) { _lastWarpAsk = ctx.Now; ctx.Control.WarpTo(v.orbit.EndUT + 10); }
                return true;
            }
            double band = Math.Max(20000, 0.3 * _plannedPe);
            bool moved = _plannedPe > 0 && Math.Abs(v.orbit.PeA - _plannedPe) > band;
            if (!alarm && !_moonPassed && !moved) return false;
            if (ctx.Control.IsWarping) { ctx.Control.StopWarp(); Status = "The path changed under the burn; stopping the warp."; return true; }
            _moonPassed = false;
            if (!alarm && !moved)
            {
                NodePlanner.ClearNodes(v);
                _phase = Phases.Check;
                line = "Clear of the moon with the periapsis still at " + TransferSupport.Km(v.orbit.PeA) + "; planning the insertion again.";
                return true;
            }
            if (PlanRetrim(ctx, ref line)) return true;
            Fail("the path moved under the burn and no trim brings it back");
            line = line ?? ("Holding: the path moved under the insertion burn (periapsis now " + TransferSupport.Km(v.orbit.PeA) + " at " + TransferSupport.BodyName(v.mainBody) + ") and no radial burn within " + RetrimMaxDv + " m/s brings it back. Director, your call.");
            return true;
        }

        /// <summary>
        /// A radial burn in a minute that puts the periapsis back in its band (0.8.199): the planned one around the target, a safe
        /// height over a moon the ship is caught by. False when none within RetrimMaxDv does, or the retrims are used up.
        /// </summary>
        private bool PlanRetrim(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            CelestialBody body = v.mainBody;
            if (_retrims >= MaxRetrims || !NodePlanner.Available(v) || body == null) return false;
            double lo, hi;
            if (body == _target)
            {
                lo = _plannedPe > 0 ? _plannedPe : (_loose > 0 && _target.atmosphereDepth > 0 ? 0.7 * _target.atmosphereDepth : _altitude);
                hi = lo + Math.Max(5000, 0.1 * lo);
            }
            else { lo = MoonSafeHeight; hi = lo + 40000; }
            double now = ctx.Now;
            double ut = now + (v.orbit.timeToPe > 0 && v.orbit.timeToPe < 2 * TurnSeconds ? 10 : TurnSeconds);
            double was = v.orbit.PeA;
            Vector3d dir = was < lo ? new Vector3d(1, 0, 0) : new Vector3d(-1, 0, 0);
            double dv = NodePlanner.Bisect(v, ut, dir, RetrimMaxDv, n => NodePlanner.PeriapsisAt(n, body), lo, hi);
            if (double.IsNaN(dv)) return false;
            NodePlanner.ClearNodes(v);
            if (NodePlanner.Add(v, ut, dir * dv) == null) return false;
            _retrims++;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return false; }
            _phase = Phases.Retrim;
            Status = "Retrimming the approach: " + Math.Round(dv) + " m/s radial.";
            line = "The path moved under the insertion burn: periapsis " + TransferSupport.Km(was) + " at " + TransferSupport.BodyName(body) + ". Burning " + Math.Round(dv) + " m/s radial " + (was < lo ? "out" : "in") + " in " + TransferSupport.Countdown(ut - now) + " to bring it to " + TransferSupport.Km(lo) + ".";
            return true;
        }

        /// <summary>
        /// A moon across the approach before periapsis (0.8.199): a burn along the velocity now changes the timing so the moon has
        /// moved on at the crossing, with a horizontal part that keeps the periapsis (MoonDodge.Approach; a tilt about the vertical,
        /// the aerobraking passes' dodge, does almost nothing on a path falling straight in). False when there is none, or it is too
        /// close or too dear to dodge, in which case the ship rides through and the insertion is planned again on the far side.
        /// </summary>
        private bool PlanDodge(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            Orbit o = v.orbit;
            List<CelestialBody> moons = _target.orbitingBodies;
            if (moons == null || moons.Count == 0 || _dodges >= MaxDodges || o.timeToPe <= 0) return false;
            var at = new Func<double, Vec3>[moons.Count];
            var reach = new double[moons.Count];
            for (int i = 0; i < moons.Count; i++)
            {
                Orbit moon = moons[i].orbit;
                at[i] = t => OrbitAdapter.StateAt(moon, t).Position;
                reach[i] = moons[i].sphereOfInfluence * MoonDodge.Reach;
            }
            double now = ctx.Now, mu = _target.gravParameter, until = now + o.timeToPe + 600;
            double hit = MoonDodge.FirstMeeting(OrbitAdapter.StateAt(o, now), mu, now, until, at, reach);
            if (double.IsPositiveInfinity(hit)) return false;
            string which = "A moon";
            double nearest = double.PositiveInfinity;
            Vec3 there = OrbitAdapter.StateAt(o, hit).Position;
            for (int i = 0; i < moons.Count; i++)
            {
                double gap = (there - at[i](hit)).Length;
                if (gap < nearest) { nearest = gap; which = TransferSupport.BodyName(moons[i]); }
            }
            string when = TransferSupport.Countdown(hit - now);
            double burn = now + TurnSeconds;
            if (hit - burn < 600)
            {
                line = which + " lies across the approach in " + when + ", too close to dodge; riding through and planning the insertion on the far side.";
                return false;
            }
            Vec3 dv;
            double rp = o.PeR, band = Math.Max(5000, 0.1 * Math.Max(0, o.PeA));
            string how = "shifting the arrival past it";
            StateVector atBurn = OrbitAdapter.StateAt(o, burn);
            if (!MoonDodge.Approach(atBurn, mu, burn, until, at, reach, rp - band, rp + band, DodgeMaxCost, out dv))
            {
                // Fallback (0.8.200, T): tilt the approach plane about the vertical, same speed and height, so the same periapsis.
                how = "tilting the approach past it";
                if (!MoonDodge.Tilt(atBurn, mu, burn, until, at, reach, DodgeMaxCost, out dv))
                {
                    line = which + " lies across the approach in " + when + " and neither a timing shift nor a tilt under " + DodgeMaxCost + " m/s clears it; riding through and planning the insertion on the far side.";
                    return false;
                }
            }
            NodePlanner.ClearNodes(v);
            if (NodePlanner.AddFrameDeltaV(v, burn, dv) == null) return false;
            _dodges++;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return false; }
            _phase = Phases.Dodge;
            Status = "Clearing " + which + ": " + Math.Round(dv.Length, 1) + " m/s.";
            line = which + " lies across the approach in " + when + ": " + how + " with " + Math.Round(dv.Length, 1) + " m/s, same periapsis.";
            return true;
        }

        private string PlanCapture(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            double mu = _target.gravParameter;
            double rTarget = _target.Radius + (_loose > 0 ? _loose : _altitude);
            double utPe = ctx.Now + v.orbit.timeToPe;
            double rPe = v.orbit.PeR;
            double vPe = OrbitAdapter.StateAt(v.orbit, utPe).Velocity.Length;
            if (double.IsNaN(vPe) || vPe <= 0) vPe = Math.Sqrt(Math.Max(0, mu * (2 / rPe - 1 / v.orbit.semiMajorAxis)));
            double aNew = rPe > rTarget ? rPe : (rPe + rTarget) / 2;
            double vNew = Math.Sqrt(mu * (2 / rPe - 1 / aNew));
            double dv = vPe - vNew;
            if (dv <= 0) { Fail("already slower than the capture speed at periapsis"); return null; }
            NodePlanner.ClearNodes(v);
            ManeuverNode n = null;
            double utHigh = _loose > 0 ? AirbrakeLeg.TimeToAir(v, _target.atmosphereDepth + BurnAboveAir, ctx.Now) : -1;
            if (utHigh > ctx.Now + 60)
            {
                // Thrust in the air tumbles a tall rocket, engine first with its fins in front (2026-11-01, T): burn well above it, for the same periapsis.
                StateVector s = OrbitAdapter.StateAt(v.orbit, utHigh);
                double r = s.Position.Length, speed = Math.Sqrt(mu * (2 / r - 1 / aNew)), tangential = vNew * rPe / r;
                double radial = -Math.Sqrt(Math.Max(0, speed * speed - tangential * tangential));
                Vec3 up = s.Position.Normalized, along = Vec3.Cross(Vec3.Cross(s.Position, s.Velocity), s.Position).Normalized;
                Vec3 burn = along * tangential + up * radial - s.Velocity;
                dv = burn.Length;
                n = NodePlanner.AddFrameDeltaV(v, utHigh, burn);
            }
            else n = NodePlanner.Add(v, utPe, new Vector3d(0, 0, -dv));
            if (n == null) { Fail("could not place the insertion node"); return null; }
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return null; }
            _phase = Phases.Capture;
            _plannedPe = v.orbit.PeA;
            _moonPassed = false;
            Status = "Insertion burn of " + Math.Round(dv) + " m/s in " + TransferSupport.Countdown(v.orbit.timeToPe) + ".";
            return "Orbit insertion planned: " + Math.Round(dv) + " m/s retrograde at periapsis (" + TransferSupport.Km(v.orbit.PeA) + ") into a "
                + TransferSupport.Km(rPe - _target.Radius) + " x " + TransferSupport.Km(aNew * 2 - rPe - _target.Radius) + " orbit.";
        }

        /// <summary>Plans the circularisation at apoapsis when the orbit is still eccentric and something can burn. False when skipped.</summary>
        private bool PlanCircularise(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            if (_loose > 0 || _secondPlanned || !Captured(v)) return false;
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
            if (!Bound(v))
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
    /// Lets a thin atmosphere shorten a long orbit, pass by pass, then lifts the low point clear of the air at apoapsis
    /// (2026-11-01, T: Duna, where a burn all the way down to a low orbit left too little fuel to land and come home).
    /// </summary>
    public sealed class AirbrakeLeg : IFlightLeg
    {
        private enum Phases { Passes, Burn, Finished }
        private readonly CelestialBody _target;
        private readonly double _altitude;
        private Phases _phase;
        private NodeExecutor _exec;
        private bool _inAir, _climbing, _caughtSaid, _moonsChecked, _trimDone, _finishing, _passWarped;
        private int _passes, _dodges;
        private double _energyBefore, _lastDrop;

        private const double MaxOffForClimb = 25;     // degrees off the climb: thrust beyond this only pushes the wrong way
        private const double ClimbPitch = 35 * Math.PI / 180;


        private const double TurnSeconds = 90;
        private const double DodgeMaxCost = 60, MoonSafeHeight = 20000, MinTrim = 0.2, JustPastTop = 3600;
        private const int MaxDodges = 3;



        public AirbrakeLeg(CelestialBody target, double orbitAltitude)
        {
            _target = target;
            _altitude = orbitAltitude;
        }

        public string Name { get { return "Aerobraking at " + TransferSupport.BodyName(_target); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _phase = Phases.Passes;
            Status = "Aerobraking.";
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || _target == null) { Fail("no vessel orbit or target"); return; }
            if (v.mainBody != _target && v.mainBody.referenceBody != _target) { Fail("not in " + _target.bodyName + "'s sphere of influence"); return; }
            if (v.mainBody != _target) return;
            // Clear of the air on a long orbit, the air has not been used yet: the rules bring the low point down at apoapsis (2026-11-01: arrived at 53 km, 3 km over the
            // air, and went straight to the landing from a 14,773 km orbit with no ground in reach).
            if (v.orbit.eccentricity < 1 && v.orbit.PeA > _target.atmosphereDepth)
            {
                if (v.orbit.ApA <= _altitude + AirbrakeRules.GoalMargin) State = LegState.Done;
                else _passes = 1;
            }
        }

        private void Fail(string why) { State = LegState.Failed; Status = "Aerobraking failed: " + why; }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            string line = null;
            if (State != LegState.Running || v == null || v.orbit == null) return null;
            VesselControl c = ctx.Control;
            double air = _target.atmosphereDepth;
            switch (_phase)
            {
                case Phases.Passes:
                    if (v.mainBody != _target)
                    {
                        if (v.mainBody.referenceBody == _target) ThroughMoon(ctx);
                        else Fail("left " + _target.bodyName + "'s sphere of influence");
                        break;
                    }
                    if (v.LandedOrSplashed) { Fail("on the ground"); break; }
                    bool inAir = v.altitude < air;
                    // Trapped in the air, with the whole orbit inside it: climb out now (2026-11-01: the second pass at 26 km took the orbit
                    // down to 26 x -19 km, and the crew rode it to the ground waiting for the air to end).
                    // Any path that meets the ground, low and falling, goes to the landing: no pass ends there.
                    if (inAir && (_climbing || v.orbit.PeA < 0) && AirbrakeRules.TooLowToClimb(v.altitude, v.verticalSpeed, air))
                    {
                        _climbing = false;
                        line = "Too low to climb out at " + TransferSupport.Km(v.altitude) + ": landing from here.";
                        _phase = Phases.Finished;
                        State = LegState.Done;
                        break;
                    }
                    if (_climbing || (inAir && v.orbit.ApA < air))
                    {
                        if (!_climbing) { _climbing = true; if (!_caughtSaid) line = "Caught in the air at " + TransferSupport.Km(v.altitude) + ": full power to climb out."; _caughtSaid = true; }
                        // Up as well as along: level in thick air the thrust only fights the drag (2026-11-01, T: 460 m/s for 2 km of apoapsis).
                        Vector3d up = v.upAxis;
                        Vector3d climb = (up * Math.Sin(ClimbPitch) + Vector3d.Exclude(up, v.obt_velocity).normalized * Math.Cos(ClimbPitch)).normalized;
                        c.PointAlong(() => climb);
                        c.Throttle = c.AttitudeError(climb) < MaxOffForClimb ? 1f : 0f;
                        Status = "Climbing out of the air: apoapsis " + TransferSupport.Km(v.orbit.ApA) + ".";
                        if (v.orbit.ApA >= _altitude + AirbrakeRules.GoalMargin) { _climbing = false; c.Throttle = 0; }
                        break;
                    }
                    c.Throttle = 0;
                    // Nose first, as it flew up from Kerbin, with the fins behind it (2026-11-01, T: engine first it tumbled).
                    c.Sas(VesselAutopilot.AutopilotMode.Prograde, inAir);
                    Status = "Aerobraking: pass " + _passes + ", " + TransferSupport.Km(v.orbit.ApA) + " x " + TransferSupport.Km(v.orbit.PeA) + ".";
                    double mu = _target.gravParameter;
                    if (inAir)
                    {
                        if (!_inAir) { _inAir = true; _passes++; _passWarped = false; }
                        // 4x physics warp through the air once the nose is steady (0.8.182, T: "slow and boring"): a pass is seven minutes at 1x.
                        // Rails warp never in the air; the warp is dropped if the craft starts to swing.
                        bool physicsWarp = c.IsWarping && TimeWarp.WarpMode == TimeWarp.Modes.LOW;
                        if (physicsWarp && VesselControl.TurnRate(v) > 3) { c.StopWarp(); _passWarped = true; }
                        else if (!c.IsWarping && !_passWarped && VesselControl.TurnRate(v) < 0.5 && c.AttitudeError(v.obt_velocity) < 10)
                        {
                            _passWarped = true;
                            TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
                            TimeWarp.SetRate(3, false); // 4x
                        }
                        break;
                    }
                    if (c.IsWarping && TimeWarp.WarpMode == TimeWarp.Modes.LOW) c.StopWarp(); // out of the air: back to 1x before the rails warp to the next pass
                    double energy = -mu / (2 * v.orbit.semiMajorAxis);
                    if (_inAir)
                    {
                        _inAir = false;
                        _moonsChecked = false;
                        _trimDone = false;
                        _dodges = 0;
                        _lastDrop = _energyBefore - energy;
                        line = "Air pass " + _passes + " done: orbit now " + TransferSupport.Km(v.orbit.ApA) + " x " + TransferSupport.Km(v.orbit.PeA) + ".";
                    }
                    _energyBefore = energy;
                    if (v.orbit.eccentricity >= 1) { Fail("still escaping " + _target.bodyName); break; }
                    if (c.IsWarping) break;
                    if (!_moonsChecked)
                    {
                        _moonsChecked = true;
                        if (PlanDodge(ctx, ref line)) { _phase = Phases.Burn; break; }
                    }
                    // The rules live in Core, where FlightSimTests fly them through every arrival height (KSA-75).
                    AirbrakeStep step = AirbrakeRules.AfterPass(_passes, v.orbit.ApA, v.orbit.PeA, air, _altitude, _target.Radius, mu, _lastDrop);
                    if (step == AirbrakeStep.LiftClear) { _trimDone = false; _finishing = true; if (PlanLift(ctx, Math.Min(v.orbit.ApR, _target.Radius + _altitude), ref line)) _phase = Phases.Burn; break; }
                    if (step == AirbrakeStep.RaisePass && !_trimDone) { if (PlanLift(ctx, _target.Radius + AirbrakeRules.PassShare * air, ref line)) _phase = Phases.Burn; break; }
                    if (step == AirbrakeStep.DeepenPass && !_trimDone) { if (PlanLift(ctx, _target.Radius + AirbrakeRules.DeepenShare * air, ref line)) _phase = Phases.Burn; break; }
                    double edge = TimeToAir(v, air + 2000, ctx.Now);
                    if (edge > ctx.Now + TurnSeconds + 20) c.WarpTo(edge - TurnSeconds);
                    break;
                case Phases.Burn:
                    if (TransferSupport.PumpExecutor(_exec, ctx, ref line))
                    {
                        if (_exec.Failed) { Fail(_exec.FailReason); break; }
                        _moonsChecked = false;
                        if (_finishing && v.orbit.PeA > air)
                        {
                            // Clear of the air: round it out (0.8.184, T: "it should just round the orbit out"), one retrograde burn at periapsis.
                            if (!_rounded && v.orbit.ApA > _altitude * 1.3 + 5000 && PlanRound(ctx, ref line)) { _rounded = true; break; }
                            _phase = Phases.Finished; State = LegState.Done;
                            ctx.Say("Aerobraking done after " + _passes + " passes: " + TransferSupport.Km(v.orbit.ApA) + " x " + TransferSupport.Km(v.orbit.PeA) + " orbit of " + _target.bodyName + ".");
                        }
                        else { _finishing = false; _phase = Phases.Passes; }
                    }
                    else Status = "Lifting the orbit: " + _exec.Status;
                    break;
            }
            return line;
        }

        /// <summary>A moon on the way down to the next pass: turn the orbit past it, at the top where that is cheap (MoonDodge).</summary>
        private bool PlanDodge(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            Orbit o = v.orbit;
            List<CelestialBody> moons = _target.orbitingBodies;
            if (moons == null || moons.Count == 0 || o.eccentricity >= 1 || _dodges >= MaxDodges) return false;
            var at = new Func<double, Vec3>[moons.Count];
            var reach = new double[moons.Count];
            for (int i = 0; i < moons.Count; i++)
            {
                Orbit moon = moons[i].orbit;
                at[i] = t => OrbitAdapter.StateAt(moon, t).Position;
                reach[i] = moons[i].sphereOfInfluence * MoonDodge.Reach;
            }
            double now = ctx.Now, mu = _target.gravParameter, until = now + o.timeToPe + 600;
            double hit = MoonDodge.FirstMeeting(OrbitAdapter.StateAt(o, now), mu, now, until, at, reach);
            if (double.IsPositiveInfinity(hit)) return false;
            bool topFirst = o.timeToAp < o.timeToPe && now + o.timeToAp < hit - 600;
            double burn = topFirst ? now + o.timeToAp : now + TurnSeconds;
            Vec3 dv;
            if (!MoonDodge.Tilt(OrbitAdapter.StateAt(o, burn), mu, burn, until, at, reach, DodgeMaxCost, out dv))
            {
                ctx.Say("A moon lies across the path in " + Math.Round((hit - now) / TransferSupport.Day, 1) + " days and no cheap burn clears it.");
                return false;
            }
            NodePlanner.ClearNodes(v);
            if (NodePlanner.AddFrameDeltaV(v, burn, dv) == null) return false;
            _dodges++;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return false; }
            line = "A moon lies across the path in " + Math.Round((hit - now) / TransferSupport.Day, 1) + " days: turning the orbit past it with " + Math.Round(dv.Length, 1) + " m/s.";
            return true;
        }

        /// <summary>Caught by a moon after all: clear its surface and ride through. The rules take over back around the planet.</summary>
        private void ThroughMoon(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            Orbit o = v.orbit;
            _moonsChecked = false;
            _trimDone = false;
            if (o.PeA < MoonSafeHeight && v.verticalSpeed < 0)
            {
                Vector3d away = Vector3d.Exclude(v.obt_velocity, v.upAxis).normalized;
                c.PointAlong(() => away);
                c.Throttle = c.AttitudeError(away) < 10 ? 1f : 0f;
                Status = "Clearing " + TransferSupport.BodyName(v.mainBody) + ": low point " + TransferSupport.Km(o.PeA) + ".";
                return;
            }
            c.Throttle = 0;
            Status = "Passing " + TransferSupport.BodyName(v.mainBody) + ": low point " + TransferSupport.Km(o.PeA) + ".";
            if (!c.IsWarping && o.patchEndTransition == Orbit.PatchTransitionType.ESCAPE && o.EndUT > ctx.Now + 120) c.WarpTo(o.EndUT - 60);
        }

        private bool _rounded;

        /// <summary>A retrograde burn at periapsis that brings the high point down to the wanted orbit height (0.8.184).</summary>
        private bool PlanRound(FlightContext ctx, ref string line)
        {
            Vessel v = ctx.Vessel;
            double mu = _target.gravParameter, rPe = v.orbit.PeR, rAp = _target.Radius + _altitude;
            if (rAp < rPe) rAp = rPe;
            double sincePe = v.orbit.period - v.orbit.timeToPe;
            bool justPastPe = sincePe < Math.Min(JustPastTop, 0.25 * v.orbit.period);
            double utPe = ctx.Now + (justPastPe ? TurnSeconds : v.orbit.timeToPe);
            double vPe = Math.Sqrt(Math.Max(0, mu * (2 / rPe - 1 / v.orbit.semiMajorAxis)));
            double dv = Math.Sqrt(mu * (2 / rPe - 2 / (rPe + rAp))) - vPe;
            if (Math.Abs(dv) < MinTrim) return false;
            NodePlanner.ClearNodes(v);
            if (NodePlanner.Add(v, utPe, new Vector3d(0, 0, dv)) == null) return false;
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) return false;
            line = "Rounding the orbit: " + Math.Round(Math.Abs(dv)) + " m/s at periapsis to " + TransferSupport.Km(_altitude) + ".";
            return true;
        }

        /// <summary>A prograde burn at apoapsis that puts the low point at the given radius from the centre.</summary>
        private bool PlanLift(FlightContext ctx, double rLow, ref string line)
        {
            Vessel v = ctx.Vessel;
            double mu = _target.gravParameter, rAp = v.orbit.ApR;
            // Just past the top after another burn: now, not a whole lap on through the air. Within an hour of the top, but never
            // more than a quarter of the orbit past it (0.8.183: a 51-minute orbit is always "within an hour", so the lift clear
            // of the air was burned near the periapsis, raising the apoapsis instead, 24 m/s a lap).
            double sinceAp = v.orbit.period - v.orbit.timeToAp;
            bool justPastTop = sinceAp < Math.Min(JustPastTop, 0.25 * v.orbit.period);
            double utAp = ctx.Now + (justPastTop ? TurnSeconds : v.orbit.timeToAp);
            double vAp = Math.Sqrt(Math.Max(0, mu * (2 / rAp - 1 / v.orbit.semiMajorAxis)));
            double dv = Math.Sqrt(mu * (2 / rAp - 2 / (rAp + rLow))) - vAp;
            if (!_finishing && Math.Abs(dv) < MinTrim) { _trimDone = true; return false; }
            NodePlanner.ClearNodes(v);
            if (NodePlanner.Add(v, utAp, new Vector3d(0, 0, dv)) == null) { Fail("could not place the lift burn"); return false; }
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { Fail(_exec.FailReason); return false; }
            line = (dv < 0 ? "Lowering" : "Lifting") + " the low point to " + TransferSupport.Km(rLow - _target.Radius) + " at apoapsis with " + Math.Round(Math.Abs(dv)) + " m/s.";
            return true;
        }

        /// <summary>When the path next comes down to a height, or -1.</summary>
        public static double TimeToAir(Vessel v, double altitude, double now)
        {
            double end = now + v.orbit.timeToPe, step = Math.Max(1, (end - now) / 400);
            for (double t = now; t <= end; t += step)
                if (v.orbit.getRelativePositionAtUT(t).magnitude - v.mainBody.Radius <= altitude) return t;
            return -1;
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
                        if (PlanBySums(ctx, v, ref line)) break;
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

        /// <summary>
        /// The cheapest prograde burn over one orbit that brings Kerbin's periapsis into the band, by the sums alone: every 30 s, every
        /// 1 m/s. The game's path preview only checks it afterwards (2026-10-31: the preview search found nothing with 383 m/s aboard).
        /// </summary>
        private bool PlanBySums(FlightContext ctx, Vessel v, ref string line)
        {
            try
            {
                if (!TransferSupport.IsClosed(v.orbit)) return false;
                CelestialBody moon = v.mainBody;
                double now = ctx.Now, period = v.orbit.period, homeRadius = _home.Radius;
                double fuel = v.VesselDeltaV != null && v.VesselDeltaV.IsReady ? v.VesselDeltaV.TotalDeltaVVac : 1500;
                double maxDv = Math.Min(1500, fuel);
                double bestDv = double.NaN, bestUt = 0, bestPe = double.NaN;
                double closest = double.PositiveInfinity, closestDv = 0;
                for (double ut = now + 120; ut < now + 120 + period; ut += 30)
                {
                    StateVector s = OrbitAdapter.StateAt(v.orbit, ut);
                    Vec3 along = s.Velocity.Normalized;
                    double burnUt = ut;
                    for (double dv = 1; dv <= maxDv && (double.IsNaN(bestDv) || dv < bestDv); dv += 1)
                    {
                        var after = new StateVector(s.Position, s.Velocity + along * dv);
                        double rp = ReturnMath.HomePeriapsis(after, moon.gravParameter, moon.sphereOfInfluence, _home.gravParameter, t => OrbitAdapter.StateAt(moon.orbit, burnUt + t));
                        if (double.IsNaN(rp)) continue;
                        double pe = rp - homeRadius;
                        double off = Math.Abs(pe - _reentryPe);
                        if (off < closest) { closest = off; closestDv = dv; }
                        if (off <= 8000) { bestDv = dv; bestUt = ut; bestPe = pe; }
                    }
                }
                if (double.IsNaN(bestDv))
                {
                    KsaCore.Log.Warn("Return sums: nothing within " + Math.Round(closest / 1000) + " km of the target periapsis, at " + closestDv + " m/s, " + Math.Round(fuel) + " m/s aboard.");
                    return false;
                }
                ManeuverNode n = NodePlanner.Add(v, bestUt, new Vector3d(0, 0, bestDv));
                if (n == null) return false;
                double predicted = NodePlanner.PeriapsisAt(n, _home);
                _exec = new NodeExecutor();
                _exec.Begin(ctx);
                if (_exec.Failed) { Fail(_exec.FailReason); return true; }
                _phase = Phases.Burn;
                Status = "Return burn of " + Math.Round(bestDv) + " m/s in " + TransferSupport.Countdown(bestUt - now) + ".";
                line = "Return burn planned: " + Math.Round(bestDv) + " m/s at " + TransferSupport.Date(bestUt) + ", Kerbin periapsis " + TransferSupport.Km(bestPe)
                    + " by the sums, " + (double.IsNaN(predicted) ? "no path in the game's preview yet" : TransferSupport.Km(predicted) + " in the game's preview") + ".";
                return true;
            }
            catch (Exception e) { KsaCore.Log.Warn("Return sums failed: " + e.Message); return false; }
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
