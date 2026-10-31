using System;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Spaceplane ascent (2026-10-28): roll down the runway, rotate, climb on the jets, switch to rockets when the jets fade
    /// (multi-mode engines are toggled, separate rockets are staged), raise the apoapsis and, for an orbital mission,
    /// circularise. Steering is a direction held by stock SAS, east with a pitch for each phase.
    /// </summary>
    public sealed class PlaneAscentLeg : IFlightLeg
    {
        private enum Phases { Roll, Climb, Accelerate, Rocket, Coast, Circularize }
        private readonly double _targetAp;
        private readonly bool _orbit;
        private Phases _phase;
        private double _phaseStart, _cutoff = -1;
        private double _pitch;
        private bool _rocketsOn;

        public PlaneAscentLeg(double targetAp, bool orbit) { _targetAp = Math.Max(72000, targetAp); _orbit = orbit; }
        public string Name { get { return _orbit ? "Spaceplane ascent to orbit" : "Spaceplane climb"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            _phase = v.LandedOrSplashed || v.situation == Vessel.Situations.PRELAUNCH ? Phases.Roll : Phases.Climb;
            _phaseStart = ctx.Now;
            _pitch = 0;
            v.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            ctx.Control.Throttle = 1f;
            ctx.Control.PointAlong(() => Heading(v, _pitch));
            Status = _phase == Phases.Roll ? "Rolling down the runway." : "Climbing.";
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
                case Phases.Roll:
                    c.Throttle = 1f;
                    if (!AnyEngineLit(v) && ctx.Now - _phaseStart > 1) { c.EnsureEngine(ref line); line = line ?? "Engines started."; }
                    _pitch = v.srfSpeed > 70 ? 10 : 0; // rotate at about 70 m/s
                    if (!v.LandedOrSplashed && alt - v.terrainAltitude > 30)
                    {
                        v.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
                        _phase = Phases.Climb; _phaseStart = ctx.Now; line = "Airborne; gear up."; Status = "Climbing on the jets.";
                    }
                    else if (ctx.Now - _phaseStart > 90) { State = LegState.Failed; Status = "Could not get airborne."; line = "Ran out of runway: the plane would not lift off."; }
                    break;
                case Phases.Climb:
                    // Steady climb to 10 km.
                    _pitch = v.srfSpeed < 120 ? 10 : 18;
                    if (alt > 10000) { _phase = Phases.Accelerate; _phaseStart = ctx.Now; Status = "Building speed on the jets."; }
                    if (JetsFading(v, alt)) GoRocket(ctx, ref line);
                    break;
                case Phases.Accelerate:
                    // Shallow at altitude, where the jets are fastest.
                    _pitch = alt < 16000 ? 10 : 7;
                    if (JetsFading(v, alt) || v.srfSpeed > 1350 || alt > 24000) GoRocket(ctx, ref line);
                    break;
                case Phases.Rocket:
                    c.AutoStage(ref line);
                    _pitch = alt < 30000 ? 22 : 15;
                    if (v.orbit.ApA >= _targetAp)
                    {
                        c.Throttle = 0;
                        _phase = Phases.Coast; _phaseStart = ctx.Now;
                        line = "Rockets off; apoapsis " + Math.Round(v.orbit.ApA / 1000) + " km.";
                        Status = "Coasting to apoapsis.";
                        if (!_orbit) State = LegState.Done;
                    }
                    else if (c.NoThrustAvailable()) { line = "Out of rocket propellant at " + Math.Round(alt / 1000) + " km."; State = LegState.Done; }
                    break;
                case Phases.Coast:
                    c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                    if (alt > atmo && v.orbit.timeToAp < 25) { _phase = Phases.Circularize; c.Throttle = 1f; line = "Circularisation burn."; Status = "Circularising."; }
                    break;
                case Phases.Circularize:
                    c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                    c.AutoStage(ref line);
                    if (v.orbit.PeA > atmo + 2000)
                    {
                        c.Throttle = 0;
                        if (_cutoff < 0) { _cutoff = ctx.Now; line = "Orbit: " + Math.Round(v.orbit.ApA / 1000) + " x " + Math.Round(v.orbit.PeA / 1000) + " km."; Status = "In orbit."; }
                        if (ctx.Now - _cutoff > 3) State = LegState.Done;
                    }
                    else if (c.NoThrustAvailable()) { line = "Out of propellant before a circular orbit."; State = LegState.Done; }
                    break;
            }
            if (_phase != Phases.Coast && _phase != Phases.Circularize) Status = (_phase == Phases.Roll ? "Rolling" : _phase == Phases.Rocket ? "Rocket climb" : "Jet climb") + ": pitch " + Math.Round(_pitch) + " deg, " + Math.Round(v.srfSpeed) + " m/s.";
            return line;
        }

        private void GoRocket(FlightContext ctx, ref string line)
        {
            if (_rocketsOn) return;
            _rocketsOn = true;
            Vessel v = ctx.Vessel;
            int toggled = 0;
            foreach (MultiModeEngine m in v.FindPartModulesImplementing<MultiModeEngine>())
            {
                if (m.runningPrimary == false) continue;
                BaseEvent e = m.Events["ModeEvent"];
                if (e != null) { e.Invoke(); toggled++; }
            }
            bool rocketLit = false;
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && !Breathes(e)) rocketLit = true;
            if (!rocketLit && toggled == 0 && v.currentStage > 0) ctx.Control.Stage(); // separate rocket engines on the next stage
            ctx.Control.Throttle = 1f;
            _phase = Phases.Rocket; _phaseStart = ctx.Now;
            line = "Jets fading at " + Math.Round(v.altitude / 1000) + " km, " + Math.Round(v.srfSpeed) + " m/s: rockets on" + (toggled > 0 ? " (engines switched to closed cycle)." : ".");
        }

        private static bool Breathes(ModuleEngines e)
        {
            foreach (Propellant p in e.propellants) if (p.name == "IntakeAir") return true;
            return false;
        }

        private static bool AnyEngineLit(Vessel v)
        {
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>()) if (e.EngineIgnited && !e.flameout) return true;
            return false;
        }

        /// <summary>Jets give less than a third of their thrust (thin air) above 12 km, or have flamed out.</summary>
        private static bool JetsFading(Vessel v, double alt)
        {
            double thrust = 0, max = 0;
            bool any = false;
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!e.EngineIgnited || !Breathes(e)) continue;
                any = true;
                if (e.flameout) return alt > 8000;
                thrust += e.finalThrust; max += e.maxThrust;
            }
            if (!any) return alt > 1000; // a rocket plane: rockets from the start of the climb
            return alt > 12000 && max > 0 && thrust < max / 3;
        }

        /// <summary>East along the surface, tipped up by <paramref name="pitchDeg"/>.</summary>
        internal static Vector3d Heading(Vessel v, double pitchDeg)
        {
            double p = pitchDeg * Math.PI / 180;
            return ((Vector3d)v.upAxis * Math.Sin(p) + v.east * Math.Cos(p)).normalized;
        }

        public void End(FlightContext ctx) { }
    }

    /// <summary>
    /// Spaceplane return (2026-10-28): deorbit, reenter nose-high, glide down holding a gentle descent. With parachutes the
    /// crew lands under them (the survival net arms them); without, the crew flies the glide and hands the Director the
    /// controls for the runway landing at 6 km.
    /// </summary>
    public sealed class PlaneReturnLeg : IFlightLeg
    {
        private enum Phases { Deorbit, Reentry, Glide, Landed }
        private readonly double _targetPe;
        private Phases _phase;
        private bool _handed;

        public PlaneReturnLeg(double targetPe) { _targetPe = targetPe; }
        public string Name { get { return "Spaceplane return"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            double atmo = v.mainBody.atmosphereDepth;
            _phase = v.orbit.PeA > atmo && v.altitude > atmo ? Phases.Deorbit : Phases.Reentry;
            Status = _phase == Phases.Deorbit ? "Deorbit burn." : "Reentry.";
        }

        public string Step(FlightContext ctx)
        {
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            string line = null;
            double atmo = v.mainBody.atmosphereDepth;
            double agl = v.altitude - Math.Max(0, v.terrainAltitude);
            if (v.LandedOrSplashed && v.srfSpeed < 1) { State = LegState.Done; Status = "Down."; return "Down at " + Math.Round(v.srfSpeed) + " m/s."; }
            switch (_phase)
            {
                case Phases.Deorbit:
                    c.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                    if (c.AttitudeError(-v.obt_velocity) < 10) { c.EnsureEngine(ref line); c.Throttle = 1f; }
                    else if (c.AttitudeError(-v.obt_velocity) > 30) c.Throttle = 0; // never burn far off retrograde (2026-10-30)
                    if (v.orbit.PeA < _targetPe || c.NoThrustAvailable()) { c.Throttle = 0; _phase = Phases.Reentry; line = "Deorbit complete; periapsis " + Math.Round(v.orbit.PeA / 1000) + " km."; }
                    break;
                case Phases.Reentry:
                    // Nose high (about 35 degrees above the flight path) to bleed speed in the thin air.
                    c.Throttle = 0;
                    c.PointAlong(() => Tilted(v, 35));
                    Status = "Reentry, nose high: " + Math.Round(v.srfSpeed) + " m/s at " + Math.Round(v.altitude / 1000) + " km.";
                    if (v.altitude < 25000 && v.srfSpeed < 700) { _phase = Phases.Glide; line = "Reentry done; gliding home."; }
                    break;
                case Phases.Glide:
                    c.PointAlong(() => Tilted(v, 6));
                    Status = "Gliding: " + Math.Round(v.srfSpeed) + " m/s at " + Math.Round(agl / 1000, 1) + " km.";
                    bool chutes = v.FindPartModulesImplementing<ModuleParachute>().Count > 0;
                    if (!chutes && agl < 6000 && !_handed && ctx.HandToDirector != null)
                    {
                        _handed = true;
                        line = "Final approach: the Director has the controls for the landing.";
                        ctx.HandToDirector();
                        State = LegState.Done;
                    }
                    break;
            }
            return line;
        }

        /// <summary>The surface flight path, tipped up by <paramref name="aboveDeg"/> degrees.</summary>
        private static Vector3d Tilted(Vessel v, double aboveDeg)
        {
            Vector3d vel = v.srf_velocity.magnitude > 1 ? v.srf_velocity.normalized : (Vector3d)v.east;
            Vector3d up = v.upAxis;
            Vector3d flat = vel - up * Vector3d.Dot(vel, up);
            if (flat.magnitude < 1e-6) flat = v.east;
            flat = flat.normalized;
            double path = Math.Asin(Math.Max(-1, Math.Min(1, Vector3d.Dot(vel, up))));
            double p = path + aboveDeg * Math.PI / 180;
            return (up * Math.Sin(p) + flat * Math.Cos(p)).normalized;
        }

        public void End(FlightContext ctx) { ctx.Control.Throttle = 0; }
    }
}
