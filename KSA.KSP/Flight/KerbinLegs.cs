using System;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Kerbin launch (M5): vertical climb, an eastward kick, prograde hold with auto-staging, coast and circularise.
    /// Suborbital missions end the leg at engine cutoff and let <see cref="ReentryLeg"/> bring the capsule down.
    /// Not MechJeb; it promises to fly sane stock-style rockets with TWR 1.2 to 2.5.
    /// </summary>
    public sealed class AscentLeg : IFlightLeg
    {
        private enum Phases { Prelaunch, Climb, Kick, Ascent, Coast, Circularize }

        private readonly double _targetAp;
        private readonly bool _orbit;
        private Phases _phase;
        private double _phaseStart;
        private double _cutoff = -1;

        private bool _climbSteer;

        /// <summary>Coasting to apoapsis or burning for orbit: the stage aboard is about to light (the survival net leaves it).</summary>
        public bool GoingForOrbit { get { return _orbit && (_phase == Phases.Coast || _phase == Phases.Circularize) && State == LegState.Running; } }

        public AscentLeg(double targetApoapsis, bool wantOrbit)
        {
            _targetAp = Math.Max(75000, targetApoapsis);
            _orbit = wantOrbit;
        }

        public string Name { get { return _orbit ? "Ascent to orbit" : "Suborbital ascent"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            Status = "Pre-launch checks.";
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            // A craft just placed on the pad can read as FLYING or LANDED for a moment: on the ground and still means launch.
            bool onGround = v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED
                            || (v.srfSpeed < 1.0 && (v.heightFromTerrain >= 0 ? v.heightFromTerrain : v.altitude - v.terrainAltitude) < 60);
            if (onGround) _phase = Phases.Prelaunch;
            else switch (v.situation)
            {
                case Vessel.Situations.ORBITING: State = LegState.Done; return;
                case Vessel.Situations.SUB_ORBITAL: _phase = v.orbit.ApA > _targetAp * 0.9 ? Phases.Coast : Phases.Ascent; break;
                default: _phase = Phases.Ascent; break;
            }
            _phaseStart = ctx.Now;
            if (_phase == Phases.Ascent) ctx.Control.Sas(VesselAutopilot.AutopilotMode.Prograde, v.altitude < 40000);
        }

        private double _offSince = -1;

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            string line = null;
            double alt = v.altitude;
            double atmo = v.mainBody.atmosphereDepth;
            switch (_phase)
            {
                case Phases.Prelaunch:
                    c.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, true);
                    c.Throttle = 1f;
                    // Stage until an engine is lit, one stage per second, never past the engines; then climb.
                    if (ctx.Now - _phaseStart < 1.0) { Status = "Ignition sequence."; break; }
                    if (c.EnsureEngine(ref line)) { _phase = Phases.Climb; _phaseStart = ctx.Now; Status = "Climbing."; line = "Ignition. Full throttle."; }
                    else if (!c.HasStagesLeft) { Status = "No engine to light."; line = "No engine found in any stage; launch scrubbed."; State = LegState.Failed; }
                    break;
                case Phases.Climb:
                    LogAttitude(ctx, "climb");
                    c.AutoStage(ref line);
                    // Steer to the program (88 degrees, a touch east) from just off the pad instead of the stock hold, which
                    // keeps whatever attitude the rocket drifts to (2026-10-30: Orbiter mk III leaned west 3 degrees by 500 m;
                    // the turn then followed its westward path and gravity bent it on over the top, west to 106 degrees).
                    if (!_climbSteer && !c.ManualAttitude && (v.heightFromTerrain > 30 || alt > 200)) { c.PointAlong(() => AscentDirection(v, _orbit)); _climbSteer = true; }
                    if (alt > 1500 || v.srfSpeed > 90)
                    {
                        // Pitch program (2026-10-28): hold a set angle for the altitude, east, instead of a yaw kick and
                        // "follow prograde", which let heavy, slow rockets fall over and could kick the wrong way.
                        _phase = Phases.Ascent; _phaseStart = ctx.Now;
                        c.PointAlong(() => AscentDirection(v, _orbit));
                        Status = "Gravity turn: pitching east on the program.";
                        line = "Pitch program started.";
                    }
                    break;
                case Phases.Kick:
                    c.AutoStage(ref line);
                    if (ctx.Now - _phaseStart > 3.0 || alt > 4000)
                    {
                        c.Yaw = 0f;
                        c.Sas(VesselAutopilot.AutopilotMode.Prograde, true);
                        _phase = Phases.Ascent; _phaseStart = ctx.Now;
                        Status = "Gravity turn, holding prograde.";
                        line = "Pitch program started; following prograde.";
                    }
                    break;
                case Phases.Ascent:
                    c.AutoStage(ref line);
                    if (!c.ManualAttitude) c.PointAlong(() => AscentDirection(v, _orbit));
                    LogAttitude(ctx, "turn (program " + Math.Round(TargetPitch(v, _orbit)) + " deg)");
                    Status = "Gravity turn: pitch " + Math.Round(TargetPitch(v, _orbit)) + " deg.";
                    // Side boosters pulling the rocket off course that SAS cannot hold: drop them, as a pilot would
                    // (2026-10-28: heavy side boosters tipped a rocket over until the Director dropped them by hand).
                    {
                        double off = c.AttitudeError(AscentDirection(v, _orbit));
                        if (off > 25) { if (_offSince < 0) _offSince = ctx.Now; } else _offSince = -1;
                        if (_offSince > 0 && ctx.Now - _offSince > 3 && c.DropSideBoosters(ref line)) _offSince = -1;
                    }
                    // Below the end of the turn, an apoapsis at target is no reason to stop (2026-10-29: a strong rocket
                    // reached 75 km of apoapsis at about 28 km up, cut off still steep, and the upper stage could not make
                    // up the sideways speed). The crew throttles back to hold the apoapsis within 10% over target and
                    // keeps following the turn, so the speed it adds goes sideways. Cutoff once level at the end of the turn.
                    bool holding = _orbit && alt < TurnEnd && v.orbit.ApA >= _targetAp;
                    if (holding)
                    {
                        c.Throttle = (float)Math.Max(0, Math.Min(1, (_targetAp * 1.1 - v.orbit.ApA) / (_targetAp * 0.1)));
                        // A swivelling engine steers only while it burns: keep a little throttle so the turn goes on
                        // (2026-10-30: throttled to nothing, the rocket stayed steep and cut off at 66 deg).
                        if (c.Throttle < SteerThrottle && c.CanSteerWithEngine()) c.Throttle = SteerThrottle;
                        Status = "Gravity turn: pitch " + Math.Round(TargetPitch(v, _orbit)) + " deg, holding apoapsis at " + Math.Round(v.orbit.ApA / 1000) + " km (throttle " + Math.Round(c.Throttle * 100) + "%).";
                    }
                    else if (_orbit && c.Throttle < 1f && v.orbit.ApA < _targetAp) c.Throttle = 1f;
                    if (v.orbit.ApA >= _targetAp && !holding)
                    {
                        c.Throttle = 0;
                        _phase = Phases.Coast; _phaseStart = ctx.Now;
                        Status = "Coasting to apoapsis.";
                        line = "Main engine cutoff; apoapsis " + Math.Round(v.orbit.ApA / 1000) + " km.";
                        if (!_orbit) State = LegState.Done;
                        else { c.PointAlong(() => Horizontal(v)); _pointedFlat = true; } // start swinging round to the horizon now
                    }
                    else if (c.Throttle > 0.05f && c.NoThrustAvailable())
                    {
                        _phase = Phases.Coast; _phaseStart = ctx.Now;
                        Status = "Out of propellant; coasting.";
                        line = "Propellant exhausted during ascent at " + Math.Round(alt / 1000) + " km.";
                        ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted during ascent at " + Math.Round(alt / 1000) + " km.");
                        if (!_orbit || v.orbit.ApA < atmo) State = LegState.Done;
                    }
                    break;
                case Phases.Coast:
                    c.DropSpentStage(ref line); // a burnt-out booster does not ride along
                    // Fuel left in the tanks, not an engine burning: the throttle is closed while coasting.
                    if (v.orbit.ApA < atmo + 5000 || c.OutOfDeltaV()) { State = LegState.Done; break; }
                    if (alt > atmo)
                    {
                        // Out of the air: face along the horizon, the way the circularisation burn will push (2026-10-29:
                        // the burn followed prograde, which still pointed mostly up, and raised the apoapsis instead of
                        // the periapsis). Light the next engine ahead of the burn so its length can be worked out.
                        if (!_pointedFlat) { c.PointAlong(() => Horizontal(v)); _pointedFlat = true; }
                        LogAttitude(ctx, "coast");
                        if (v.orbit.timeToAp < 120) c.EnsureEngine(ref line);
                        double halfBurn = CircularizeBurnTime(v, c) / 2;
                        // Out of warp well before the burn: engines do not fire on rails (2026-10-30).
                        if (c.IsWarping && v.orbit.timeToAp - halfBurn < 30) c.StopWarp();
                        Status = "Coasting to apoapsis; circularisation burn in " + Math.Max(0, Math.Round(v.orbit.timeToAp - halfBurn)) + " s.";
                        // Burn centred on apoapsis, or at once if already past it (the Director handed back on the way down).
                        if (v.orbit.timeToAp <= Math.Max(5, halfBurn) || v.verticalSpeed < 0)
                        {
                            _phase = Phases.Circularize; _phaseStart = ctx.Now;
                            Status = "Circularising.";
                            c.EnsureEngine(ref line);
                            line = line ?? "Circularisation burn, " + Math.Round(CircularizeDeltaV(v)) + " m/s along the horizon.";
                        }
                    }
                    break;
                case Phases.Circularize:
                    if (c.OutOfDeltaV()) { c.Throttle = 0; line = "No fuel left to circularise."; State = LegState.Done; break; }
                    if (c.IsWarping) { c.StopWarp(); c.Throttle = 0; Status = "Circularising: coming out of time warp."; break; }
                    if (!c.ManualAttitude) c.PointAlong(() => Horizontal(v));
                    LogAttitude(ctx, "circularise");
                    {
                        // Burn only with the nose on the horizon: thrust while still pointing up only raises the apoapsis
                        // (2026-10-29: a gimbal-less upper stage burned straight up to a 2,000 km apoapsis).
                        double offFlat = c.AttitudeError(Horizontal(v));
                        // Steady first (2026-10-30): a craft swinging through the horizon at 100 deg/s was "aligned" for an
                        // instant at a time and burned every which way, throwing the apoapsis out to 348 km.
                        double turn = VesselControl.TurnRate(v);
                        bool aligned = offFlat < 15 && turn < 5;
                        // Off the horizon, a swivelling engine turns the rocket on a little throttle; the pod's reaction
                        // wheel alone could not swing a full upper stage (2026-10-30: stuck at 45 deg, burn never lit).
                        c.Throttle = v.orbit.PeA > atmo + 2000 ? 0f : aligned ? 1f : (c.CanSteerWithEngine() && turn < 10 ? SteerThrottle : 0f);
                        if (turn >= 10) Status = "Circularising: engine off until the craft steadies (turning " + Math.Round(turn) + " deg/s).";
                        else if (!aligned) Status = "Circularising: turning to the horizon (" + Math.Round(offFlat) + " deg to go" + (c.Throttle > 0 ? ", steering on the engine" : string.Empty) + ").";
                        else { Status = "Circularising."; c.AutoStage(ref line); }
                        if (turn >= 10 && !_spinNoted) { _spinNoted = true; line = "Engine off: the craft is turning " + Math.Round(turn) + " deg/s; the crew steadies it before burning."; }
                        // Never fling the apoapsis away chasing the periapsis.
                        if (v.orbit.PeA <= atmo + 2000 && v.orbit.ApA > Math.Max(_targetAp * 2, _targetAp + 150000))
                        {
                            c.Throttle = 0;
                            line = "Burn stopped: the apoapsis ran away to " + Math.Round(v.orbit.ApA / 1000) + " km with the periapsis still at " + Math.Round(v.orbit.PeA / 1000) + " km.";
                            State = LegState.Done;
                            break;
                        }
                    }
                    if (v.orbit.PeA > atmo + 2000)
                    {
                        c.Throttle = 0;
                        Status = "In orbit.";
                        if (_cutoff < 0) { _cutoff = ctx.Now; line = "Orbit: " + Math.Round(v.orbit.ApA / 1000) + " x " + Math.Round(v.orbit.PeA / 1000) + " km."; }
                        // Let the game flip the situation to ORBITING before the next leg looks at it.
                        if (v.situation == Vessel.Situations.ORBITING || ctx.Now - _cutoff > 3) State = LegState.Done;
                    }
                    else if (c.Throttle > 0.05f && c.NoThrustAvailable())
                    {
                        Status = "Out of propellant before circular orbit.";
                        line = "Propellant exhausted during circularisation.";
                        ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted during circularisation.");
                        State = LegState.Done;
                    }
                    break;
            }
            return line;
        }

        public void End(FlightContext ctx)
        {
            ctx.Control.Yaw = 0;
            Vessel v = ctx.Vessel;
            FellShort = State == LegState.Done && _orbit && v != null && v.orbit != null && v.orbit.PeA < v.mainBody.atmosphereDepth;
        }

        /// <summary>An ascent for orbit that ended without one: the program goes to reentry, not on to the next burn.</summary>
        public bool FellShort { get; private set; }

        /// <summary>The throttle a swivelling engine is given to steer while the burn waits (2026-10-30).</summary>
        private const float SteerThrottle = 0.1f;

        private bool _pointedFlat;
        private bool _spinNoted;
        private double _lastAttitudeLog = -1;

        /// <summary>Every 10 s in the log: how far the nose is from the horizon and how fast it turns (2026-10-29).</summary>
        private void LogAttitude(FlightContext ctx, string what)
        {
            if (ctx.Now - _lastAttitudeLog < 10) return;
            _lastAttitudeLog = ctx.Now;
            Vessel v = ctx.Vessel;
            double off = ctx.Control.AttitudeError(Horizontal(v));
            double spin = VesselControl.TurnRate(v);
            KsaCore.Log.Info("Ascent " + what + ": nose " + Math.Round(off) + " deg from the horizon, turning " + Math.Round(spin, 1) + " deg/s, rolling " + Math.Round(VesselControl.RollRate(v), 1) + " deg/s, throttle " + ctx.Control.Throttle.ToString("0.00") + ", SAS "
                + (v.ActionGroups[KSPActionGroup.SAS] ? "on" : "off") + (ctx.Control.ManualAttitude ? ", crew steering by hand" : ", stock hold")
                + ", nose pitch " + Math.Round(NosePitch(v)) + " deg heading " + Math.Round(NoseHeading(v))
                + ", pitch input " + ctx.Control.Pitch.ToString("0.00") + ", yaw input " + ctx.Control.Yaw.ToString("0.00") + ", alt " + Math.Round(v.altitude / 1000, 1) + " km, " + Math.Round(v.srfSpeed) + " m/s, apoapsis " + Math.Round(v.orbit.ApA / 1000, 1) + " km in " + Math.Round(v.orbit.timeToAp) + " s" + ChargeLog.Note(v));
        }

        /// <summary>The nose above the horizon, degrees.</summary>
        private static double NosePitch(Vessel v)
        {
            if (v.ReferenceTransform == null) return 0;
            return Math.Asin(Math.Max(-1, Math.Min(1, Vector3d.Dot((Vector3d)v.ReferenceTransform.up, v.upAxis)))) * 180 / Math.PI;
        }

        /// <summary>The nose's compass heading, degrees (90 east, 270 west).</summary>
        private static double NoseHeading(Vessel v)
        {
            if (v.ReferenceTransform == null) return 0;
            Vector3d n = v.ReferenceTransform.up;
            double h = Math.Atan2(Vector3d.Dot(n, v.east), Vector3d.Dot(n, v.north)) * 180 / Math.PI;
            return h < 0 ? h + 360 : h;
        }

        /// <summary>Along the horizon in the direction of travel: the orbital velocity with its up-down part taken out.</summary>
        internal static Vector3d Horizontal(Vessel v)
        {
            Vector3d up = v.upAxis;
            Vector3d vel = v.obt_velocity;
            Vector3d flat = vel - up * Vector3d.Dot(vel, up);
            return flat.magnitude > 1 ? flat.normalized : (Vector3d)v.east;
        }

        /// <summary>Speed to add at apoapsis for a circular orbit there, m/s.</summary>
        internal static double CircularizeDeltaV(Vessel v)
        {
            double mu = v.mainBody.gravParameter;
            double rAp = v.orbit.ApR;
            double a = v.orbit.semiMajorAxis;
            if (rAp <= 0 || a <= 0) return 0;
            double vAp = Math.Sqrt(Math.Max(0, mu * (2 / rAp - 1 / a)));
            return Math.Max(0, Math.Sqrt(mu / rAp) - vAp);
        }

        /// <summary>Seconds the circularisation burn takes at full throttle; a minute when no engine is lit yet.</summary>
        private static double CircularizeBurnTime(Vessel v, VesselControl c)
        {
            double t = c.BurnTime(CircularizeDeltaV(v));
            return double.IsInfinity(t) || double.IsNaN(t) ? 60 : Math.Min(t, 300);
        }

        /// <summary>The turn is flat by here; above it the rocket burns horizontally for apoapsis.</summary>
        internal const double TurnEnd = 50000; // level by 50 km (2026-10-30, T: "turn the big rocket level at 50 to 55 km")
        /// <summary>Straight up until this surface speed, then the small kick east.</summary>
        internal const double KickSpeed = 100;
        /// <summary>Below this speed the kick leans at most 5 degrees off vertical.</summary>
        internal const double KickDone = 150;

        /// <summary>
        /// The ascent pitch, degrees above the horizon: a classic gravity turn (2026-10-29, T: "fine as long as it can
        /// maintain control"). Straight up (88, a touch east) to 100 m/s, a kick of at most 5 degrees up to 150 m/s, then a
        /// curve from the ground, 88 x (1 - sqrt(alt / 50 km)): about 76 at 1 km, 49 at 10 km, 32 at 20 km, 20 at 30 km, flat
        /// at 50 km. To keep control the nose never leans far off the flight path: at most 10 degrees below 20 km, 8 while the
        /// air is thick (25 kPa or more), opening as it thins (free at 5 kPa). A suborbital hop climbs at 88 all the way.
        /// </summary>
        internal static double TargetPitch(Vessel v, bool orbit = true)
        {
            if (!orbit) return 88;
            double alt = Math.Max(0, v.altitude);
            if (v.srfSpeed < KickSpeed) return 88;
            // 2026-10-29: straight up to 15 km left a fast, finned rocket pointing up (fins keep the nose on the path and the
            // pod cannot pull it over at 600 m/s), so it never built sideways speed. The turn starts low and slow instead.
            double program = Math.Max(0, 88 * (1 - Math.Sqrt(Math.Min(1, alt / TurnEnd))));
            if (v.srfSpeed < KickDone) return Math.Max(85, program);
            Vector3d up = v.upAxis;
            // The flight path's pitch from the eastern horizon, in the up-east plane: over 90 means it leans west
            // (2026-10-30: a rocket on fixed engines drifted west; the unsigned angle read that as leaning east, so the
            // "stay near the path" band kept it going west and it climbed past vertical).
            Vector3d vel = v.srf_velocity;
            double pathPitch = Math.Atan2(Vector3d.Dot(vel, up), Vector3d.Dot(vel, (Vector3d)v.east)) * 180 / Math.PI;
            double band = MaxLean(v.dynamicPressurekPa);
            if (alt < 20000) band = Math.Min(band, 10);
            return Math.Max(pathPitch - band, Math.Min(pathPitch + band, program));
        }

        /// <summary>How far the nose may lean off the flight path, degrees: 8 in thick air (25 kPa or more), none in thin air (5 kPa or less).</summary>
        internal static double MaxLean(double dynamicPressureKPa)
        {
            if (dynamicPressureKPa >= 25) return 8;
            if (dynamicPressureKPa <= 5) return 90;
            return 8 + (25 - dynamicPressureKPa) / 20.0 * 82;
        }

        /// <summary>World direction for the pitch program: east, tipped up by <see cref="TargetPitch"/>.</summary>
        internal static Vector3d AscentDirection(Vessel v, bool orbit = true)
        {
            double p = TargetPitch(v, orbit) * Math.PI / 180;
            Vector3d up = v.upAxis;
            Vector3d east = v.east;
            return (up * Math.Sin(p) + east * Math.Cos(p)).normalized;
        }
    }

    /// <summary>Stay in orbit for a while (the "one full orbit" of an orbital mission), then hand over.</summary>
    public sealed class HoldOrbitLeg : IFlightLeg
    {
        private readonly double _seconds;
        private double _until;
        private readonly int _orbits;

        public HoldOrbitLeg(double seconds) { _seconds = seconds; }
        /// <summary>Hold for a whole number of orbits (the period is read when the leg begins).</summary>
        public HoldOrbitLeg(int orbits) { _orbits = Math.Max(1, orbits); }
        public string Name { get { return "On orbit"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            // Not actually in orbit (the ascent ran short): skip straight on to coming home (2026-10-28).
            Vessel cv = ctx.Vessel;
            if (cv != null && cv.mainBody != null && cv.orbit != null && cv.orbit.PeA < (cv.mainBody.atmosphere ? cv.mainBody.atmosphereDepth : 10000))
            {
                ctx.Say("Not in orbit (periapsis " + Math.Round(cv.orbit.PeA / 1000) + " km): skipping the orbit hold and coming home.");
                State = LegState.Done;
                return;
            }
            double seconds = _seconds;
            if (_orbits > 0 && ctx.Vessel != null && ctx.Vessel.orbit != null && ctx.Vessel.orbit.period > 0 && !double.IsInfinity(ctx.Vessel.orbit.period)) { seconds = ctx.Vessel.orbit.period * _orbits; ctx.Say("Holding for " + _orbits + " orbit" + (_orbits == 1 ? "" : "s") + " (" + Math.Round(seconds / 60) + " min)."); }
            _until = ctx.Now + seconds;
            ctx.Control.Throttle = 0;
            // Face retrograde for the whole hold, so the deorbit burn needs no turn later (2026-10-30: Valentina held prograde,
            // the deorbit burn lit after a fixed 5 s while she still pointed forward, and it raised her apoapsis to 1,064 km).
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
        }

        public string Step(FlightContext ctx)
        {
            if (ctx.Now >= _until) { State = LegState.Done; return null; }

            Status = "In orbit, " + Math.Round(_until - ctx.Now) + " s to go.";
            if (_until - ctx.Now > 60) ctx.Control.WarpTo(_until - 20);
            return null;
        }

        public void End(FlightContext ctx) { ctx.Control.StopWarp(); }
    }

    /// <summary>
    /// Home: a deorbit burn when in orbit, retrograde reentry shedding spent stages, parachutes, touchdown.
    /// Also the tail of a suborbital hop and the last leg of every return from another body.
    /// </summary>
    internal static class ChargeLog
    {
        /// <summary>", charge 37%" for the attitude log lines: a flat battery leaves the reaction wheel dead (2026-10-30).</summary>
        public static string Note(Vessel v)
        {
            double f = VesselControl.ChargeFraction(v);
            return f < 0 ? ", no electric charge aboard" : ", charge " + Math.Round(f * 100) + "%";
        }
    }

    public sealed class ReentryLeg : IFlightLeg
    {
        private enum Phases { Deorbit, Burn, Reentry, Descent, Landed }
        private readonly double _targetPe;
        private Phases _phase;
        private double _phaseStart;
        private double _lastShed;
        private double _lastAttitudeLog = -1;

        private bool _waitSaid;
        private bool _handTurn;
        private double _alignSince = -1;

        /// <summary>About 1 g: a deorbit is a few dozen m/s, so a strong upper stage at full throttle is over in a blink.</summary>
        private static float GentleThrottle(VesselControl c)
        {
            double accel = c.MaxAcceleration();
            return accel > 0 ? (float)Math.Max(0.05, Math.Min(1.0, 10.0 / accel)) : 1f;
        }

        /// <summary>When the orbit next comes down to <paramref name="altitude"/> (metres), or -1 if it does not before periapsis.</summary>
        private static double TimeDownTo(Vessel v, double altitude, double now)
        {
            if (v.orbit == null || v.orbit.PeA > altitude) return -1;
            double end = now + Math.Min(v.orbit.timeToPe, 7200), step = Math.Max(1, (end - now) / 400);
            for (double t = now; t <= end; t += step)
                if (v.orbit.getRelativePositionAtUT(t).magnitude - v.mainBody.Radius <= altitude) return t;
            return -1;
        }
        private const double AlignDeg = 15, AbortDeg = 30;
        public ReentryLeg(double targetPeriapsis) { _targetPe = targetPeriapsis; }
        public string Name { get { return "Reentry"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            double atmo = v.mainBody.atmosphereDepth;
            if (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED) { _phase = Phases.Landed; State = LegState.Done; return; }
            // Only a path that stays out of the air needs a burn (2026-10-30: a return from the Mun with its periapsis at
            // 35.7 km, in the air but a whisker over the 35 km target, waited for an apoapsis six hours off, warping mid-reentry).
            bool needsBurn = v.orbit.PeA > Math.Max(_targetPe, atmo) && v.altitude > atmo;
            _phase = needsBurn ? Phases.Deorbit : Phases.Reentry;
            _phaseStart = ctx.Now;
            Status = needsBurn ? "Deorbit burn." : "Reentry.";
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.Retrograde, !needsBurn && v.altitude < atmo * 0.5);
        }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            string line = null;
            double alt = v.altitude;
            double atmo = v.mainBody.atmosphereDepth;
            switch (_phase)
            {
                case Phases.Deorbit:
                    if (v.orbit.PeA < atmo || alt < atmo)
                    {
                        c.StopWarp(); c.Throttle = 0;
                        _phase = Phases.Reentry; _phaseStart = ctx.Now; Status = "Reentry.";
                        break;
                    }
                    {
                        c.Throttle = 0;
                        if (!_handTurn) c.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                        // Never light the engine until the nose is on retrograde: a burn the wrong way throws the craft into a
                        // higher orbit (2026-10-30). A craft that cannot turn (flat battery) waits in its safe orbit instead.
                        double off = c.AttitudeError(-v.obt_velocity);
                        double charge = VesselControl.ChargeFraction(v);
                        bool dead = charge >= 0 && charge < 0.02 || charge < 0;
                        Status = dead ? "Deorbit: no power to turn; waiting for the orbit to swing the nose round to retrograde, " + Math.Round(off) + " deg to go."
                                      : "Deorbit: turning retrograde, " + Math.Round(off) + " deg to go.";
                        // With power, the burn waits for apoapsis, where it costs least and the fall is steepest (2026-10-30, T:
                        // "deorbit burn was a split second"); time warp gets there. Without power, the first moment the nose lines up.
                        double toAp = v.orbit.timeToAp, period = v.orbit.period;
                        // Warp first, turn after: on rails the attitude freezes while the orbit moves on (2026-10-30: lined up, then
                        // warped, the nose came out 175 degrees off, too late to turn, and the window closed).
                        bool atAp = dead || v.orbit.eccentricity < 0.002 || toAp < 30 || toAp > period - 240;
                        if (!atAp)
                        {
                            Status = "Deorbit: apoapsis in " + Math.Round(toAp) + " s; turning retrograde shortly before it.";
                            if (toAp > 200) c.WarpTo(ctx.Now + toAp - 150);
                            else if (c.IsWarping) c.StopWarp();
                        }
                        // Out of time warp before lining up (so the nose does not sweep past retrograde between frames), and the
                        // engine never lit on rails: a burn "started" in warp gives no thrust (2026-10-30: 76 s passed in warp, then
                        // one real second of burn).
                        if (c.IsWarping && atAp && off < AlignDeg + 30) c.StopWarp();
                        // The stock hold has had its chance: the crew steers it by hand, as in the ascent (T: "I had to manually do it").
                        if (atAp && !c.IsWarping && off >= AlignDeg)
                        {
                            if (_alignSince < 0) _alignSince = ctx.Now;
                            if (!_handTurn && ctx.Now - _alignSince > 45)
                            {
                                _handTurn = true;
                                c.PointAlong(() => -v.obt_velocity);
                                line = "The retrograde hold is not lining up (" + Math.Round(off) + " deg off" + ChargeLog.Note(v) + "); steering it by hand.";
                            }
                        }
                        else if (off < AlignDeg) _alignSince = -1;
                        if (atAp && ctx.Now - _phaseStart > 5 && off < AlignDeg && !c.IsWarping && !v.packed)
                        {
                            c.EnsureEngine(ref line);
                            c.Throttle = GentleThrottle(c);
                            _phase = Phases.Burn; _phaseStart = ctx.Now;
                            line = line ?? "Deorbit burn started.";
                        }
                        else if (ctx.Now - _lastAttitudeLog > 30)
                        {
                            _lastAttitudeLog = ctx.Now;
                            KsaCore.Log.Info("Deorbit waiting: " + Math.Round(off) + " deg off retrograde" + ChargeLog.Note(v));
                            if (atAp && off >= AlignDeg && ctx.Now - _phaseStart > 120 && !_waitSaid)
                            {
                                _waitSaid = true;
                                line = "Cannot turn retrograde for the deorbit burn (" + Math.Round(off) + " deg off" + ChargeLog.Note(v) + "); holding the engine until the craft points the right way.";
                            }
                        }
                    }
                    break;
                case Phases.Burn:
                    if (c.IsWarping) c.StopWarp(); // no thrust on rails
                    c.Throttle = GentleThrottle(c);
                    Status = "Deorbit burn: periapsis " + Math.Round(v.orbit.PeA / 1000) + " km, burning down to " + Math.Round(_targetPe / 1000) + " km.";
                    c.AutoStage(ref line);
                    if (!_handTurn) c.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                    if (v.orbit.PeA < _targetPe)
                    {
                        c.Throttle = 0;
                        _phase = Phases.Reentry; _phaseStart = ctx.Now; Status = "Reentry.";
                        line = "Deorbit burn done: periapsis " + Math.Round(v.orbit.PeA / 1000) + " km, inside the air; coming home.";
                    }
                    else if (c.NoThrustAvailable())
                    {
                        c.Throttle = 0;
                        _phase = Phases.Reentry; _phaseStart = ctx.Now; Status = "Reentry.";
                        line = "Propellant exhausted during the deorbit burn; periapsis still " + Math.Round(v.orbit.PeA / 1000) + " km.";
                        if (v.orbit.PeA > atmo) ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, line);
                    }
                    else if (c.AttitudeError(-v.obt_velocity) > AbortDeg)
                    {
                        // Drifted off retrograde: stop burning and turn back before going on.
                        c.Throttle = 0;
                        _phase = Phases.Deorbit; _phaseStart = ctx.Now - 5;
                        line = "Deorbit burn paused: the nose drifted off retrograde.";
                    }
                    break;
                case Phases.Reentry:
                    c.DropSpentStage(ref line); // spent stages go as soon as they are empty, not when we fall back into the air
                    c.Sas(VesselAutopilot.AutopilotMode.Retrograde, alt < atmo * 0.5);
                    {
                        // Heat shield first: report how far the capsule is from retrograde, every 10 s in the log (2026-10-28).
                        Vector3d retro = -(alt < atmo * 0.5 ? v.srf_velocity : v.obt_velocity);
                        double off = c.AttitudeError(retro);
                        double spin = v.angularVelocity.magnitude * 180 / Math.PI;
                        Status = "Reentry: " + Math.Round(off) + " deg off retrograde" + (spin > 3 ? ", turning " + Math.Round(spin) + " deg/s" : string.Empty) + ".";
                        if (ctx.Now - _lastAttitudeLog > 10)
                        {
                            _lastAttitudeLog = ctx.Now;
                            KsaCore.Log.Info("Reentry attitude: " + Math.Round(off) + " deg off retrograde, spin " + Math.Round(spin, 1) + " deg/s, SAS " + (v.ActionGroups[KSPActionGroup.SAS] ? "on" : "off")
                                + (c.ManualAttitude ? ", crew steering by hand" : ", stock retrograde hold") + ", alt " + Math.Round(alt / 1000, 1) + " km" + ChargeLog.Note(v));
                        }
                    }
                    // Warp only well above the air, and leave it 15 km up: KSP drops out of warp at the edge of the atmosphere
                    // and staging in that frame tore the capsule apart (2026-10-30).
                    // Warp down to the top of the air (2026-10-30, T: "would be nice to compress time waiting for pe"). KSP drops
                    // out of warp there itself; nothing separates until a second after (VesselControl.PartsSafe).
                    if (alt > atmo + 3000 && !c.IsWarping)
                    {
                        double edge = TimeDownTo(v, atmo + 2000, ctx.Now);
                        if (edge > ctx.Now + 20) c.WarpTo(edge - 5);
                    }
                    // Shed everything but the capsule once we are coming down, one stage every two seconds.
                    if (v.verticalSpeed < 0 && v.currentStage > 1 && ctx.Now - _lastShed > 2 && alt < atmo) { if (c.Stage()) { _lastShed = ctx.Now; line = "Staged away the service section for reentry."; } }
                    // Chutes: armed as soon as we are falling inside the atmosphere, so the game opens them the moment it
                    // is safe at any height; forced open below 2.5 km if still too fast, because the ground is coming either way.
                    double aboveGround = Math.Max(0, alt - Math.Max(0, v.terrainAltitude));
                    if (v.verticalSpeed < 0 && alt < atmo) c.DeployChutes(ref line, aboveGround < 2500);
                    if (alt < 10000 && v.srfSpeed < 350) { _phase = Phases.Descent; Status = "Deploying parachutes."; }
                    if (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED) { _phase = Phases.Landed; Status = "Down."; State = LegState.Done; }
                    break;
                case Phases.Descent:
                    c.DeployChutes(ref line);
                    if (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED) { _phase = Phases.Landed; Status = "Down safely."; State = LegState.Done; }
                    break;
                case Phases.Landed:
                    c.Throttle = 0;
                    State = LegState.Done;
                    break;
            }
            return line;
        }

        public void End(FlightContext ctx) { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); }
    }
}
