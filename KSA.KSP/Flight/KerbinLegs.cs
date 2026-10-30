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
                    c.AutoStage(ref line);
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
                    if (!c.ManualAttitude) c.PointAlong(() => Horizontal(v));
                    LogAttitude(ctx, "circularise");
                    {
                        // Burn only with the nose on the horizon: thrust while still pointing up only raises the apoapsis
                        // (2026-10-29: a gimbal-less upper stage burned straight up to a 2,000 km apoapsis).
                        double offFlat = c.AttitudeError(Horizontal(v));
                        bool aligned = offFlat < 15;
                        // Off the horizon, a swivelling engine turns the rocket on a little throttle; the pod's reaction
                        // wheel alone could not swing a full upper stage (2026-10-30: stuck at 45 deg, burn never lit).
                        c.Throttle = v.orbit.PeA > atmo + 2000 ? 0f : aligned ? 1f : (c.CanSteerWithEngine() ? SteerThrottle : 0f);
                        if (!aligned) Status = "Circularising: turning to the horizon (" + Math.Round(offFlat) + " deg to go" + (c.Throttle > 0 ? ", steering on the engine" : string.Empty) + ").";
                        else { Status = "Circularising."; c.AutoStage(ref line); }
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

        public void End(FlightContext ctx) { ctx.Control.Yaw = 0; }

        /// <summary>The throttle a swivelling engine is given to steer while the burn waits (2026-10-30).</summary>
        private const float SteerThrottle = 0.1f;

        private bool _pointedFlat;
        private double _lastAttitudeLog = -1;

        /// <summary>Every 10 s in the log: how far the nose is from the horizon and how fast it turns (2026-10-29).</summary>
        private void LogAttitude(FlightContext ctx, string what)
        {
            if (ctx.Now - _lastAttitudeLog < 10) return;
            _lastAttitudeLog = ctx.Now;
            Vessel v = ctx.Vessel;
            double off = ctx.Control.AttitudeError(Horizontal(v));
            double spin = v.angularVelocity.magnitude * 180 / Math.PI;
            KsaCore.Log.Info("Ascent " + what + ": nose " + Math.Round(off) + " deg from the horizon, turning " + Math.Round(spin, 1) + " deg/s, SAS "
                + (v.ActionGroups[KSPActionGroup.SAS] ? "on" : "off") + (ctx.Control.ManualAttitude ? ", crew steering by hand" : ", stock hold")
                + ", pitch input " + ctx.Control.Pitch.ToString("0.00") + ", yaw input " + ctx.Control.Yaw.ToString("0.00") + ", alt " + Math.Round(v.altitude / 1000, 1) + " km, apoapsis in " + Math.Round(v.orbit.timeToAp) + " s" + ChargeLog.Note(v));
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
        internal const double TurnEnd = 55000;
        /// <summary>Straight up until this surface speed, then the small kick east.</summary>
        internal const double KickSpeed = 100;
        /// <summary>Below this speed the kick leans at most 5 degrees off vertical.</summary>
        internal const double KickDone = 150;

        /// <summary>
        /// The ascent pitch, degrees above the horizon: a classic gravity turn (2026-10-29, T: "fine as long as it can
        /// maintain control"). Straight up (88, a touch east) to 100 m/s, a kick of at most 5 degrees up to 150 m/s, then a
        /// curve from the ground, 88 x (1 - sqrt(alt / 55 km)): about 77 at 1 km, 50 at 10 km, 33 at 20 km, 23 at 30 km, flat
        /// at 55 km. To keep control the nose never leans far off the flight path: at most 10 degrees below 20 km, 8 while the
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
            double pathPitch = 90 - Vector3d.Angle(v.srf_velocity, up);
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
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
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
            bool needsBurn = v.orbit.PeA > _targetPe && v.altitude > atmo;
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
                    c.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                    if (ctx.Now - _phaseStart > 5)
                    {
                        c.EnsureEngine(ref line);
                        c.Throttle = 1f;
                        _phase = Phases.Burn; _phaseStart = ctx.Now;
                        line = line ?? "Deorbit burn started.";
                    }
                    break;
                case Phases.Burn:
                    c.AutoStage(ref line);
                    if (v.orbit.PeA < _targetPe || c.NoThrustAvailable())
                    {
                        c.Throttle = 0;
                        _phase = Phases.Reentry; _phaseStart = ctx.Now; Status = "Reentry.";
                        line = "Deorbit complete; periapsis " + Math.Round(v.orbit.PeA / 1000) + " km.";
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
                    if (alt > atmo + 20000 && v.verticalSpeed > -50 && v.orbit.timeToPe > 120) c.WarpTo(ctx.Now + Math.Min(v.orbit.timeToPe, 600) - 90);
                    // Shed everything but the capsule once we are coming down, one stage every two seconds.
                    if (v.verticalSpeed < 0 && v.currentStage > 1 && ctx.Now - _lastShed > 2 && alt < atmo) { c.Stage(); _lastShed = ctx.Now; line = "Staged away the service section for reentry."; }
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
