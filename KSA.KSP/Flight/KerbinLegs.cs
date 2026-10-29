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
                    if (v.orbit.ApA >= _targetAp)
                    {
                        c.Throttle = 0;
                        _phase = Phases.Coast; _phaseStart = ctx.Now;
                        Status = "Coasting to apoapsis.";
                        line = "Main engine cutoff; apoapsis " + Math.Round(v.orbit.ApA / 1000) + " km.";
                        if (!_orbit) State = LegState.Done;
                    }
                    else if (c.NoThrustAvailable())
                    {
                        _phase = Phases.Coast; _phaseStart = ctx.Now;
                        Status = "Out of propellant; coasting.";
                        line = "Propellant exhausted during ascent at " + Math.Round(alt / 1000) + " km.";
                        if (!_orbit || v.orbit.ApA < atmo) State = LegState.Done;
                    }
                    break;
                case Phases.Coast:
                    c.DropSpentStage(ref line); // a burnt-out booster does not ride along
                    // Fuel left in the tanks, not an engine burning: the throttle is closed while coasting.
                    if (v.orbit.ApA < atmo + 5000 || c.OutOfDeltaV()) { State = LegState.Done; break; }
                    // Burn at apoapsis, or at once if already past it (the Director handed back on the way down).
                    if (alt > atmo && (v.orbit.timeToAp < 25 || v.verticalSpeed < 0))
                    {
                        c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                        _phase = Phases.Circularize; _phaseStart = ctx.Now;
                        Status = "Circularising."; c.Throttle = 1f;
                        c.EnsureEngine(ref line); // after the coast the next engine may not be lit yet
                        line = line ?? "Circularisation burn.";
                    }
                    else if (alt > atmo) c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                    break;
                case Phases.Circularize:
                    c.AutoStage(ref line);
                    if (v.orbit.PeA > atmo + 2000 || v.orbit.ApA > _targetAp * 1.6)
                    {
                        c.Throttle = 0;
                        Status = "In orbit.";
                        if (_cutoff < 0) { _cutoff = ctx.Now; line = "Orbit: " + Math.Round(v.orbit.ApA / 1000) + " x " + Math.Round(v.orbit.PeA / 1000) + " km."; }
                        // Let the game flip the situation to ORBITING before the next leg looks at it.
                        if (v.situation == Vessel.Situations.ORBITING || ctx.Now - _cutoff > 3) State = LegState.Done;
                    }
                    else if (c.NoThrustAvailable())
                    {
                        Status = "Out of propellant before circular orbit.";
                        line = "Propellant exhausted during circularisation.";
                        State = LegState.Done;
                    }
                    break;
            }
            return line;
        }

        public void End(FlightContext ctx) { ctx.Control.Yaw = 0; }

        /// <summary>No turning below this: the thick lower air tips a rocket that leans over (2026-10-29).</summary>
        internal const double TurnStart = 15000;
        /// <summary>The turn is flat by here; above it the rocket burns horizontally for apoapsis.</summary>
        internal const double TurnEnd = 55000;

        /// <summary>
        /// The ascent pitch, degrees above the horizon. Straight up (88, a touch east) to 15 km, then an even turn that is
        /// flat at 55 km. Above 15 km and over 150 m/s it stays close to the surface flight path while the air is thick
        /// (within 8 degrees at 25 kPa of dynamic pressure or more), and is free to follow the program once the air is thin
        /// (5 kPa or less). A suborbital hop climbs at 88 all the way.
        /// </summary>
        internal static double TargetPitch(Vessel v, bool orbit = true)
        {
            double alt = Math.Max(0, v.altitude);
            // 2026-10-29: the old program leaned 5 degrees at lift-off and then followed the flight path down; a finless
            // Suborbital hop was flying flat at 3 km. Below the turn the program holds, whatever the path does, so a rocket
            // that drifts is pushed back upright.
            double program = orbit && alt > TurnStart ? Math.Max(0, 88 * (1 - Math.Min(1, (alt - TurnStart) / (TurnEnd - TurnStart)))) : 88;
            if (v.srfSpeed < 150 || alt <= TurnStart || !orbit) return program;
            Vector3d up = v.upAxis;
            double pathPitch = 90 - Vector3d.Angle(v.srf_velocity, up);
            // 2026-10-29: a fixed 8 degrees kept a fast rocket going straight up (the path hardly bends when the nose may
            // lean only 8 degrees off it): Kerbin orbit coasted to 84 km at 386 m/s sideways. The band opens as the air thins.
            double band = MaxLean(v.dynamicPressurekPa);
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
                                + (c.ManualAttitude ? ", crew steering by hand" : ", stock retrograde hold") + ", alt " + Math.Round(alt / 1000, 1) + " km");
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
