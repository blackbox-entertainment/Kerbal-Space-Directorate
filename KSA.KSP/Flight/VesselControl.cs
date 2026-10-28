using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// The flight director's hands: throttle, attitude (through stock SAS modes), RCS translation, staging, warp,
    /// and the engine arithmetic every leg needs. One instance per live flight, shared by all legs.
    /// </summary>
    public sealed class VesselControl
    {
        private Vessel _v;
        private bool _engaged;
        private float _throttle;
        private double _lastStage = -1e9;

        public float Yaw, Pitch, Roll;
        /// <summary>RCS translation inputs (-1..1): X right, Y up, Z forward in the control frame.</summary>
        public float TransX, TransY, TransZ;

        public VesselControl(Vessel vessel) { _v = vessel; }

        public Vessel Vessel { get { return _v; } set { if (_v == value) return; bool was = _engaged; Disengage(); _v = value; if (was) Engage(); } }
        public bool Engaged { get { return _engaged; } }
        public double Now { get { return Planetarium.GetUniversalTime(); } }
        public double LastStageTime { get { return _lastStage; } }

        public void Engage()
        {
            if (_engaged || _v == null) return;
            _engaged = true;
            _v.OnFlyByWire += Fly;
        }

        public void Disengage()
        {
            if (!_engaged) return;
            _engaged = false;
            if (_v != null) _v.OnFlyByWire -= Fly;
            Throttle = 0;
            Yaw = Pitch = Roll = 0;
            TransX = TransY = TransZ = 0;
        }

        private void Fly(FlightCtrlState s)
        {
            s.mainThrottle = _throttle;
            if (Yaw != 0) s.yaw = Yaw;
            if (Pitch != 0) s.pitch = Pitch;
            if (Roll != 0) s.roll = Roll;
            if (TransX != 0) s.X = TransX;
            if (TransY != 0) s.Y = TransY;
            if (TransZ != 0) s.Z = TransZ;
            if (_manualHold && Yaw == 0 && Pitch == 0) ManualHold(s);
        }

        // ------------------------------------------------------------------ manual attitude hold
        // Stock SAS only offers prograde/retrograde/maneuver holds to pilots with at least one star (or a capable probe core).
        // A rookie's flight director has to fly the attitude itself: a proportional-derivative hold on the target direction,
        // convention-free (the error is measured from the vessel's own axes, the sign is learned in flight).

        private VesselAutopilot.AutopilotMode _mode = VesselAutopilot.AutopilotMode.StabilityAssist;
        private bool _surfaceMode;
        private bool _manualHold;
        private double _prevPitchErr, _prevYawErr, _prevTime = -1;
        private float _pitchSign = 1f, _yawSign = 1f;
        private double _signCheckAt = -1, _signCheckErr;

        /// <summary>True when the flight director is holding attitude itself because SAS cannot.</summary>
        public bool ManualAttitude { get { return _manualHold; } }

        private Vector3d? HoldTarget()
        {
            if (_v == null) return null;
            switch (_mode)
            {
                case VesselAutopilot.AutopilotMode.Prograde: return _surfaceMode ? _v.srf_velocity : _v.obt_velocity;
                case VesselAutopilot.AutopilotMode.Retrograde: return -(_surfaceMode ? _v.srf_velocity : _v.obt_velocity);
                case VesselAutopilot.AutopilotMode.RadialOut: return (Vector3d)_v.upAxis;
                case VesselAutopilot.AutopilotMode.RadialIn: return -(Vector3d)_v.upAxis;
                case VesselAutopilot.AutopilotMode.Normal: return Vector3d.Cross(_v.obt_velocity, (Vector3d)_v.upAxis).normalized;
                case VesselAutopilot.AutopilotMode.Antinormal: return -Vector3d.Cross(_v.obt_velocity, (Vector3d)_v.upAxis).normalized;
                case VesselAutopilot.AutopilotMode.Maneuver:
                    if (_v.patchedConicSolver != null && _v.patchedConicSolver.maneuverNodes.Count > 0) return _v.patchedConicSolver.maneuverNodes[0].GetBurnVector(_v.orbit);
                    return null;
                case VesselAutopilot.AutopilotMode.Target:
                    ITargetable t = FlightGlobals.fetch != null ? FlightGlobals.fetch.VesselTarget : null;
                    if (t == null || t.GetTransform() == null) return null;
                    return (Vector3d)(t.GetTransform().position - _v.ReferenceTransform.position);
                default: return null;
            }
        }

        private void ManualHold(FlightCtrlState s)
        {
            Vector3d? target = HoldTarget();
            Transform rt = _v.ReferenceTransform;
            if (target == null || rt == null || target.Value.magnitude < 1e-6) return;
            Vector3 local = rt.InverseTransformDirection((Vector3)target.Value.normalized);
            // The control point's thrust axis is transform.up; x is right (yaw), z is dorsal (pitch).
            double yawErr = Math.Atan2(local.x, Math.Max(0.0001f, local.y));
            double pitchErr = Math.Atan2(local.z, Math.Max(0.0001f, local.y));
            if (local.y < 0) { yawErr = yawErr >= 0 ? Math.PI / 2 : -Math.PI / 2; pitchErr = pitchErr >= 0 ? Math.PI / 2 : -Math.PI / 2; }
            double now = Now;
            double dt = _prevTime > 0 ? Math.Max(0.01, now - _prevTime) : 0.02;
            double dYaw = _prevTime > 0 ? (yawErr - _prevYawErr) / dt : 0;
            double dPitch = _prevTime > 0 ? (pitchErr - _prevPitchErr) / dt : 0;
            _prevYawErr = yawErr; _prevPitchErr = pitchErr; _prevTime = now;
            const double kp = 1.6, kd = 0.9;
            float yaw = (float)Math.Max(-1, Math.Min(1, (kp * yawErr + kd * dYaw))) * _yawSign;
            float pitch = (float)Math.Max(-1, Math.Min(1, (kp * pitchErr + kd * dPitch))) * _pitchSign;
            s.yaw = yaw;
            s.pitch = pitch;
            // Learn the sign: if a steady command made the total error grow over a second, the axis is wired the other way.
            double err = Math.Sqrt(yawErr * yawErr + pitchErr * pitchErr);
            if (_signCheckAt < 0) { _signCheckAt = now; _signCheckErr = err; }
            else if (now - _signCheckAt > 1.0)
            {
                if (err > _signCheckErr + 0.1 && err > 0.3)
                {
                    if (Math.Abs(yawErr) > Math.Abs(pitchErr)) _yawSign = -_yawSign; else _pitchSign = -_pitchSign;
                    KsaCore.Log.Info("Manual attitude hold: flipped " + (Math.Abs(yawErr) > Math.Abs(pitchErr) ? "yaw" : "pitch") + " sign");
                }
                _signCheckAt = now; _signCheckErr = err;
            }
        }

        private void BeginManualHold(VesselAutopilot.AutopilotMode mode)
        {
            if (!_manualHold || _mode != mode) { _prevTime = -1; _signCheckAt = -1; }
            _manualHold = true;
            _mode = mode;
            if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.SAS, false); // stock SAS would fight the hold
        }

        private void EndManualHold()
        {
            _manualHold = false;
        }

        public float Throttle
        {
            get { return _throttle; }
            set
            {
                _throttle = Mathf.Clamp01(value);
                if (_v != null && _v == FlightGlobals.ActiveVessel) FlightInputHandler.state.mainThrottle = _throttle;
            }
        }

        public void ClearInputs() { Yaw = Pitch = Roll = 0; TransX = TransY = TransZ = 0; }

        // ------------------------------------------------------------------ attitude

        /// <summary>Turns SAS on in the given mode. Surface or orbit speed mode decides what prograde/retrograde mean.</summary>
        public void Sas(VesselAutopilot.AutopilotMode mode, bool surfaceMode)
        {
            if (_v == null) return;
            _surfaceMode = surfaceMode;
            FlightGlobals.SetSpeedMode(surfaceMode ? FlightGlobals.SpeedDisplayModes.Surface : FlightGlobals.SpeedDisplayModes.Orbit);
            if (mode == VesselAutopilot.AutopilotMode.StabilityAssist || (_v.Autopilot != null && _v.Autopilot.CanSetMode(mode)))
            {
                EndManualHold();
                _mode = mode;
                _v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                if (_v.Autopilot != null) _v.Autopilot.SetMode(mode);
            }
            else BeginManualHold(mode); // rookie pilot or no probe core: fly it ourselves
        }

        /// <summary>SAS off entirely (used for the ascent kick).</summary>
        public void SasOffForKick() { EndManualHold(); SasOff(); }

        /// <summary>SAS off, so an injected yaw/pitch input is not fought (used for the ascent kick).</summary>
        public void SasOff() { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.SAS, false); }

        public bool CanSas(VesselAutopilot.AutopilotMode mode) { return _v != null && _v.Autopilot != null && _v.Autopilot.CanSetMode(mode); }

        /// <summary>Points at the current target (SAS Target mode) after setting it.</summary>
        public void SasTarget(ITargetable target)
        {
            if (_v == null) return;
            if (target != null) FlightGlobals.fetch.SetVesselTarget(target);
            Sas(VesselAutopilot.AutopilotMode.Target, false);
        }

        /// <summary>Angle in degrees between the vessel's control-point forward axis and a world direction.</summary>
        public double AttitudeError(Vector3d worldDirection)
        {
            if (_v == null || _v.ReferenceTransform == null) return 180;
            return Vector3d.Angle(_v.ReferenceTransform.up, worldDirection);
        }

        public void Rcs(bool on) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.RCS, on); }
        public void Gear(bool down) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.Gear, down); }
        public void Lights(bool on) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.Light, on); }

        // ------------------------------------------------------------------ staging

        public void Stage()
        {
            if (_v == null || _v != FlightGlobals.ActiveVessel) return;
            global::KSP.UI.Screens.StageManager.ActivateNextStage();
            _lastStage = Now;
        }

        public bool HasStagesLeft { get { return _v != null && _v.currentStage > 0; } }

        /// <summary>Stages when the active engines have burnt out or nothing is burning. Sets line when it staged.</summary>
        public void AutoStage(ref string line)
        {
            if (_v == null || Now - _lastStage < 1.5 || _v.currentStage <= 0) return;
            bool anyActive = false, anyFlamedOut = false, anyThrust = false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!e.EngineIgnited) continue;
                anyActive = true;
                if (e.flameout) anyFlamedOut = true;
                if (e.finalThrust > 0.1f) anyThrust = true;
            }
            // Stage on burnout, or when lit engines give nothing; never to fire a stage that holds only parachutes.
            // Burnout, dead thrust, or nothing lit at all with stages left: keep going down the stack (never into a chute-only stage).
            if ((anyActive && (anyFlamedOut || !anyThrust)) || (!anyActive && HasStagesLeft)) if (NextStageIsWorthFiring())
            {
                Stage();
                line = "Staging (" + (anyFlamedOut ? "burnout" : "no thrust") + "), stage " + _v.currentStage + ".";
            }
        }

        /// <summary>True when the next stage carries an engine or a decoupler (so staging gets us somewhere), false for chute-only stages.</summary>
        public bool NextStageIsWorthFiring()
        {
            if (_v == null || _v.currentStage <= 0) return false;
            int next = _v.currentStage - 1;
            bool anyPart = false, allChutes = true;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                anyPart = true;
                if (p.FindModuleImplementing<ModuleParachute>() == null) allChutes = false;
            }
            // Only a stage made of nothing but parachutes is off limits; empty stages and anything with engines or decouplers are fine.
            return !(anyPart && allChutes);
        }

        /// <summary>True when nothing is burning and staging will not help (or has just been tried).</summary>
        public bool NoThrustAvailable()
        {
            if (_v == null) return true;
            if (_v.currentStage > 0 && Now - _lastStage < 3) return false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f) return false;
            return _v.currentStage <= 0 || Now - _lastStage > 4;
        }

        /// <summary>Lights the next stage if no engine is ignited (used before a burn after a long coast).</summary>
        public bool EnsureEngine(ref string line)
        {
            if (_v == null) return false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout) return true;
            if (_v.currentStage <= 0 || Now - _lastStage < 2) return false;
            if (!NextStageIsWorthFiring()) return false;
            Stage();
            line = "Engine start, stage " + _v.currentStage + ".";
            return false;
        }

        // ------------------------------------------------------------------ engine arithmetic

        /// <summary>Total available thrust of ignited, un-flamed-out engines at full throttle, newtons. Zero when none are lit.</summary>
        public double AvailableThrust()
        {
            double kn = 0;
            if (_v == null) return 0;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!e.EngineIgnited || e.flameout || !e.isOperational) continue;
                kn += e.maxThrust * (e.thrustPercentage / 100.0);
            }
            return kn * 1000.0;
        }

        /// <summary>Thrust-weighted Isp of the lit engines (seconds), vacuum value when nothing is lit yet.</summary>
        public double Isp()
        {
            double kn = 0, weighted = 0;
            if (_v == null) return 0;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!e.EngineIgnited || e.flameout || !e.isOperational) continue;
                double t = e.maxThrust * (e.thrustPercentage / 100.0);
                double isp = e.realIsp > 1 ? e.realIsp : e.atmosphereCurve.Evaluate(0f);
                kn += t; weighted += t * isp;
            }
            return kn > 0 ? weighted / kn : 0;
        }

        public double MassKg { get { return _v != null ? _v.totalMass * 1000.0 : 0; } }

        /// <summary>Full-throttle acceleration, m/s².</summary>
        public double MaxAcceleration() { double m = MassKg; return m > 0 ? AvailableThrust() / m : 0; }

        /// <summary>Seconds of full-throttle burn for a delta-v (Tsiolkovsky); infinite when no engine is lit.</summary>
        public double BurnTime(double deltaV)
        {
            double thrust = AvailableThrust();
            double isp = Isp();
            double m = MassKg;
            if (thrust <= 0 || isp <= 0 || m <= 0) return double.PositiveInfinity;
            double ve = isp * 9.80665;
            double mf = m * Math.Exp(-deltaV / ve);
            return (m - mf) * ve / thrust;
        }

        /// <summary>Local gravity at the vessel, m/s².</summary>
        public double Gravity()
        {
            if (_v == null || _v.mainBody == null) return 0;
            double r = _v.mainBody.Radius + _v.altitude;
            return _v.mainBody.gravParameter / (r * r);
        }

        public bool HasRcs()
        {
            if (_v == null) return false;
            foreach (ModuleRCS r in _v.FindPartModulesImplementing<ModuleRCS>()) if (r.rcsEnabled) return true;
            return false;
        }

        // ------------------------------------------------------------------ warp

        public bool IsWarping { get { return TimeWarp.CurrentRate > 1.01f; } }

        /// <summary>Warps to a universal time (stock WarpTo picks the rates). No-op when already there or warping.</summary>
        public void WarpTo(double ut)
        {
            if (TimeWarp.fetch == null) return;
            if (ut <= Now + 1) return;
            if (IsWarping) return;
            TimeWarp.fetch.WarpTo(ut);
        }

        public void StopWarp()
        {
            if (TimeWarp.fetch != null && IsWarping) TimeWarp.fetch.CancelAutoWarp();
            if (TimeWarp.CurrentRate > 1.01f) TimeWarp.SetRate(0, true);
        }

        // ------------------------------------------------------------------ misc hardware

        public void DeployChutes(ref string line) { DeployChutes(ref line, false); }

        /// <summary>Pops stowed chutes that report safe; with <paramref name="force"/> also the risky and unsafe ones (better a torn chute than a crater).</summary>
        public void DeployChutes(ref string line, bool force)
        {
            if (_v == null) return;
            foreach (ModuleParachute chute in _v.FindPartModulesImplementing<ModuleParachute>())
            {
                if (chute.deploymentState != ModuleParachute.deploymentStates.STOWED) continue;
                bool safe = chute.deploymentSafeState == ModuleParachute.deploymentSafeStates.SAFE;
                if (!safe && !force) continue;
                chute.Deploy();
                line = "Parachutes deployed at " + Math.Round(_v.altitude) + " m" + (safe ? "." : " (risky: too fast, no choice).");
            }
        }

        /// <summary>Makes a docking port the control point so SAS Target mode points the port, not the pod.</summary>
        public void ControlFrom(ModuleDockingNode port)
        {
            if (port == null || port.part == null) return;
            port.MakeReferenceTransform();
        }

        public List<ModuleDockingNode> DockingPorts()
        {
            var list = new List<ModuleDockingNode>();
            if (_v == null) return list;
            foreach (ModuleDockingNode n in _v.FindPartModulesImplementing<ModuleDockingNode>()) list.Add(n);
            return list;
        }
    }
}
