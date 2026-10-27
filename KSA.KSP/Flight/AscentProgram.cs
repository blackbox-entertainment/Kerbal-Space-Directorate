using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// A small, dependable flight program for Kerbin launches (M5): vertical climb, an eastward kick, prograde
    /// hold with auto-staging, coast and circularise (orbit missions), then deorbit, retrograde reentry and
    /// parachutes. Not MechJeb; it only promises to fly sane stock-style rockets with TWR 1.2 to 2.5.
    /// </summary>
    public sealed class AscentProgram
    {
        public enum Phases { Prelaunch, Climb, Kick, Ascent, Coast, Circularize, OnOrbit, Deorbit, Reentry, Descent, Landed, Off }

        private readonly Vessel _v;
        private readonly double _targetAp;
        private readonly bool _orbit;
        private readonly Action<string, string> _log;
        private bool _engaged;
        private double _phaseStart;
        private double _lastStage;
        private double _orbitUntil;
        private int _stageCount;

        public Phases Phase { get; private set; }
        public string Status { get; private set; }

        public AscentProgram(Vessel vessel, double targetApoapsis, bool wantOrbit, Action<string, string> log)
        {
            _v = vessel;
            _targetAp = Math.Max(75000, targetApoapsis);
            _orbit = wantOrbit;
            _log = log;
            Phase = Phases.Prelaunch;
            Status = "Pre-launch checks.";
        }

        public void Engage()
        {
            if (_engaged) return;
            _engaged = true;
            _v.OnFlyByWire += Fly;
            if (Phase == Phases.Off) Phase = GuessPhase();
            _phaseStart = Now;
        }

        public void Disengage()
        {
            if (!_engaged) return;
            _engaged = false;
            _v.OnFlyByWire -= Fly;
            SetThrottle(0);
        }

        private double Now { get { return Planetarium.GetUniversalTime(); } }

        private Phases GuessPhase()
        {
            switch (_v.situation)
            {
                case Vessel.Situations.PRELAUNCH: return Phases.Prelaunch;
                case Vessel.Situations.LANDED: case Vessel.Situations.SPLASHED: return Phases.Landed;
                case Vessel.Situations.ORBITING: return Phases.OnOrbit;
                case Vessel.Situations.SUB_ORBITAL: return _v.orbit.ApA > _targetAp * 0.9 ? Phases.Coast : Phases.Ascent;
                default: return _v.altitude < 20000 && _v.verticalSpeed < 0 ? Phases.Descent : Phases.Ascent;
            }
        }

        private float _yawInput;
        private float _throttle;

        private void Fly(FlightCtrlState s)
        {
            s.mainThrottle = _throttle;
            if (_yawInput != 0) s.yaw = _yawInput;
        }

        private void SetThrottle(float t)
        {
            _throttle = Mathf.Clamp01(t);
            FlightInputHandler.state.mainThrottle = _throttle;
        }

        private void Sas(VesselAutopilot.AutopilotMode mode, bool surface)
        {
            _v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            FlightGlobals.SetSpeedMode(surface ? FlightGlobals.SpeedDisplayModes.Surface : FlightGlobals.SpeedDisplayModes.Orbit);
            if (_v.Autopilot != null && _v.Autopilot.CanSetMode(mode)) _v.Autopilot.SetMode(mode);
        }

        /// <summary>One physics step. Returns a timeline line when something notable happened.</summary>
        public string Step()
        {
            if (!_engaged || _v == null) return null;
            string line = null;
            double alt = _v.altitude;
            double atmo = _v.mainBody.atmosphereDepth;
            switch (Phase)
            {
                case Phases.Prelaunch:
                    Sas(VesselAutopilot.AutopilotMode.StabilityAssist, true);
                    SetThrottle(1f);
                    Stage("Ignition and liftoff.");
                    Phase = Phases.Climb; _phaseStart = Now;
                    Status = "Climbing.";
                    line = "Ignition. Full throttle.";
                    break;
                case Phases.Climb:
                    AutoStage(ref line);
                    if (alt > 1500 || _v.srfSpeed > 90) { Phase = Phases.Kick; _phaseStart = Now; _yawInput = 1f; Status = "Pitching east."; }
                    break;
                case Phases.Kick:
                    AutoStage(ref line);
                    if (Now - _phaseStart > 3.0 || alt > 4000)
                    {
                        _yawInput = 0f;
                        Sas(VesselAutopilot.AutopilotMode.Prograde, true);
                        Phase = Phases.Ascent; _phaseStart = Now;
                        Status = "Gravity turn, holding prograde.";
                        line = "Pitch program started; following prograde.";
                    }
                    break;
                case Phases.Ascent:
                    AutoStage(ref line);
                    if (alt > 40000) Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                    if (_v.orbit.ApA >= _targetAp)
                    {
                        SetThrottle(0);
                        Phase = Phases.Coast; _phaseStart = Now;
                        Status = "Coasting to apoapsis.";
                        line = "Main engine cutoff; apoapsis " + Math.Round(_v.orbit.ApA / 1000) + " km.";
                    }
                    else if (NoThrustAvailable())
                    {
                        Phase = Phases.Coast; _phaseStart = Now;
                        Status = "Out of propellant; coasting.";
                        line = "Propellant exhausted during ascent at " + Math.Round(alt / 1000) + " km.";
                    }
                    break;
                case Phases.Coast:
                    if (!_orbit || _v.orbit.ApA < atmo + 5000 || NoThrustAvailable())
                    {
                        if (_v.orbit.ApA > atmo) Sas(VesselAutopilot.AutopilotMode.Retrograde, true);
                        if (_v.verticalSpeed < 0 && alt < atmo * 0.9) { Phase = Phases.Reentry; _phaseStart = Now; Status = "Reentry."; line = "Reentry, retrograde hold."; }
                        break;
                    }
                    if (alt > atmo && _v.orbit.timeToAp < 25) { Sas(VesselAutopilot.AutopilotMode.Prograde, false); Phase = Phases.Circularize; _phaseStart = Now; Status = "Circularising."; line = "Circularisation burn."; SetThrottle(1f); }
                    else if (alt > atmo) Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                    break;
                case Phases.Circularize:
                    AutoStage(ref line);
                    if (_v.orbit.PeA > atmo + 2000 || _v.orbit.ApA > _targetAp * 1.6)
                    {
                        SetThrottle(0);
                        Phase = Phases.OnOrbit; _phaseStart = Now; _orbitUntil = Now + 600;
                        Status = "In orbit.";
                        line = "Orbit: " + Math.Round(_v.orbit.ApA / 1000) + " x " + Math.Round(_v.orbit.PeA / 1000) + " km.";
                    }
                    else if (NoThrustAvailable())
                    {
                        Phase = Phases.Coast; Status = "Out of propellant before circular orbit.";
                        line = "Propellant exhausted during circularisation.";
                    }
                    break;
                case Phases.OnOrbit:
                    if (Now > _orbitUntil)
                    {
                        Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                        Phase = Phases.Deorbit; _phaseStart = Now; Status = "Deorbit burn.";
                        line = "Deorbit burn started.";
                    }
                    else Status = "In orbit, " + Math.Round(_orbitUntil - Now) + " s to deorbit.";
                    break;
                case Phases.Deorbit:
                    if (Now - _phaseStart > 5) SetThrottle(1f);
                    AutoStage(ref line);
                    if (_v.orbit.PeA < 25000 || NoThrustAvailable())
                    {
                        SetThrottle(0);
                        Phase = Phases.Reentry; _phaseStart = Now; Status = "Reentry.";
                        line = "Deorbit complete; periapsis " + Math.Round(_v.orbit.PeA / 1000) + " km.";
                    }
                    break;
                case Phases.Reentry:
                    Sas(VesselAutopilot.AutopilotMode.Retrograde, alt < atmo * 0.5);
                    // Shed everything but the capsule once we are coming down, one stage every two seconds.
                    if (_v.verticalSpeed < 0 && _v.currentStage > 1 && Now - _lastStage > 2 && alt < atmo)
                        Stage("Staged away the service section for reentry.");
                    if (alt < 10000 && _v.srfSpeed < 350) { Phase = Phases.Descent; Status = "Deploying parachutes."; }
                    break;
                case Phases.Descent:
                    DeployChutes(ref line);
                    if (_v.situation == Vessel.Situations.LANDED || _v.situation == Vessel.Situations.SPLASHED) { Phase = Phases.Landed; Status = "Down safely."; }
                    break;
                case Phases.Landed:
                    SetThrottle(0);
                    break;
            }
            return line;
        }

        private void Stage(string note)
        {
            global::KSP.UI.Screens.StageManager.ActivateNextStage();
            _lastStage = Now;
            _stageCount++;
        }

        private void AutoStage(ref string line)
        {
            if (Now - _lastStage < 1.5 || _v.currentStage <= 1) return;
            bool anyActive = false, anyFlamedOut = false, anyThrust = false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!e.EngineIgnited) continue;
                anyActive = true;
                if (e.flameout) anyFlamedOut = true;
                if (e.finalThrust > 0.1f) anyThrust = true;
            }
            if (!anyActive || anyFlamedOut || !anyThrust)
            {
                Stage(null);
                line = "Staging (" + (anyFlamedOut ? "burnout" : "no thrust") + "), stage " + _v.currentStage + ".";
            }
        }

        private bool NoThrustAvailable()
        {
            if (_v.currentStage > 1 && Now - _lastStage < 3) return false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f) return false;
            // Nothing burning: if there are stages left, AutoStage will try them; otherwise we are dry.
            return _v.currentStage <= 1 || Now - _lastStage > 4;
        }

        private void DeployChutes(ref string line)
        {
            foreach (ModuleParachute chute in _v.FindPartModulesImplementing<ModuleParachute>())
            {
                if (chute.deploymentState != ModuleParachute.deploymentStates.STOWED) continue;
                if (chute.deploymentSafeState != ModuleParachute.deploymentSafeStates.SAFE) continue;
                chute.Deploy();
                line = "Parachutes deployed at " + Math.Round(_v.altitude) + " m.";
            }
        }
    }
}
