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
                    if (alt > 1500 || v.srfSpeed > 90) { _phase = Phases.Kick; _phaseStart = ctx.Now; c.SasOffForKick(); c.Yaw = AirlessAscentLeg.KickSign(v); Status = "Pitching east."; }
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
                    if (alt > 40000) c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
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
                    if (v.orbit.ApA < atmo + 5000 || c.NoThrustAvailable()) { State = LegState.Done; break; }
                    if (alt > atmo && v.orbit.timeToAp < 25)
                    {
                        c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                        _phase = Phases.Circularize; _phaseStart = ctx.Now;
                        Status = "Circularising."; line = "Circularisation burn."; c.Throttle = 1f;
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
                    c.Sas(VesselAutopilot.AutopilotMode.Retrograde, alt < atmo * 0.5);
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
