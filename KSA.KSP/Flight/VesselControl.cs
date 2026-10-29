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
        private double _throttleUp = -1e9;

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
            // Hand over as it is (2026-10-28): the Director takes control mid-burn, so the throttle stays where it was
            // (this used to zero the stick throttle too and the rocket fell back). Our own setting is cleared, nothing else.
            _throttle = 0;
            Yaw = Pitch = Roll = 0;
            TransX = TransY = TransZ = 0;
            EndManualHold();
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
            AutoRcs();
        }

        // ------------------------------------------------------------------ automatic RCS
        // The crew uses RCS the way a pilot would (2026-10-28): in space, while the craft is turning or tumbling, so the
        // thrusters help the reaction wheels; off again once it is steady, to save monopropellant. Only RCS this code
        // switched on is switched off; a leg that sets RCS itself (docking) owns it until it lets go.

        private bool _autoRcs, _rcsOwnedByLeg;
        private double _rcsCheckAt = -1, _rcsSteadySince = -1;
        private bool _hasRcsCached;

        private void AutoRcs()
        {
            if (_v == null || _rcsOwnedByLeg) return;
            double now = Now;
            if (_rcsCheckAt < 0 || now - _rcsCheckAt > 2) { _hasRcsCached = HasRcs(); _rcsCheckAt = now; }
            if (!_hasRcsCached) return;
            bool inSpace = !_v.mainBody.atmosphere || _v.altitude > _v.mainBody.atmosphereDepth * 0.9;
            bool sasWorking = _v.ActionGroups[KSPActionGroup.SAS] || _manualHold;
            double rate = _v.angularVelocity.magnitude;
            double err = 0;
            if (sasWorking && _mode != VesselAutopilot.AutopilotMode.StabilityAssist)
            {
                Vector3d? t = HoldTarget();
                if (t.HasValue && t.Value.magnitude > 1e-6) err = AttitudeError(t.Value);
            }
            bool want = inSpace && sasWorking && (rate > 0.05 || err > 5);
            bool on = _v.ActionGroups[KSPActionGroup.RCS];
            if (want)
            {
                _rcsSteadySince = -1;
                if (!on) { _v.ActionGroups.SetGroup(KSPActionGroup.RCS, true); _autoRcs = true; }
            }
            else if (_autoRcs && on)
            {
                // Steady for two seconds (or back in the air): thrusters off.
                if (_rcsSteadySince < 0) _rcsSteadySince = now;
                if (now - _rcsSteadySince > 2 || !inSpace) { _v.ActionGroups.SetGroup(KSPActionGroup.RCS, false); _autoRcs = false; _rcsSteadySince = -1; }
            }
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
            if (_customDir != null) { try { return _customDir(); } catch { return null; } }
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
            // Point with stock SAS (2026-10-28): every pilot has Stability Assist, and it holds whatever attitude it is locked
            // to with KSP's own controller, which knows the craft's axes and torque. Our own stick inputs guessed the axes
            // and could drift the wrong way (a rocket ended up pointing northwest and down). Lock it to the wanted direction,
            // keeping the current roll, and give no stick input so SAS does not let go.
            if (_v.Autopilot != null && _v.Autopilot.SAS != null)
            {
                if (!_v.ActionGroups[KSPActionGroup.SAS]) _v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                if (_v.Autopilot.Mode != VesselAutopilot.AutopilotMode.StabilityAssist) _v.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
                Vector3 want = ((Vector3)target.Value).normalized;
                Quaternion wanted = Quaternion.FromToRotation(rt.up, want) * rt.rotation;
                _v.Autopilot.SAS.LockRotation(wanted);
                s.yaw = 0; s.pitch = 0;
                return;
            }
            // Spin first (2026-10-28: capsules tumbled out of control). Stock Stability Assist, which every pilot has, is on
            // and damps rotation with the pod's reaction wheels; while the craft still turns faster than about 6 deg/s we
            // give no input at all, so SAS can stop it. Only a steady craft is steered toward the target.
            double rate = _v.angularVelocity.magnitude;
            if (rate > 0.1)
            {
                s.yaw = 0; s.pitch = 0;
                _prevTime = -1; _signCheckAt = -1; // start the steering fresh once steady
                if (!_v.ActionGroups[KSPActionGroup.SAS]) _v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                return;
            }
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
            // Within about 3 degrees: no input, and Stability Assist holds the attitude.
            double err0 = Math.Sqrt(yawErr * yawErr + pitchErr * pitchErr);
            if (err0 < 0.05) { s.yaw = 0; s.pitch = 0; _signCheckAt = -1; return; }
            // Gentle steering: Stability Assist releases while there is input and holds wherever we stop.
            const double kp = 1.0, kd = 0.9, limit = 0.5;
            float yaw = (float)Math.Max(-limit, Math.Min(limit, (kp * yawErr + kd * dYaw))) * _yawSign;
            float pitch = (float)Math.Max(-limit, Math.Min(limit, (kp * pitchErr + kd * dPitch))) * _pitchSign;
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
            // Stability Assist stays on (2026-10-28): it damps spin and holds between our nudges instead of fighting them.
            if (_v != null)
            {
                _v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                if (_v.Autopilot != null) _v.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
        }

        private void EndManualHold()
        {
            _manualHold = false;
            _customDir = null;
        }

        public float Throttle
        {
            get { return _throttle; }
            set
            {
                float next = Mathf.Clamp01(value);
                if (_throttle < 0.05f && next >= 0.05f) _throttleUp = Now; // engines take a moment to build thrust
                _throttle = next;
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

        private Func<Vector3d> _customDir;

        /// <summary>Holds an arbitrary world direction with the manual attitude hold (KSA-65: kill relative velocity, close on a target).</summary>
        public void PointAlong(Func<Vector3d> direction)
        {
            if (_v == null || direction == null) return;
            BeginManualHold(VesselAutopilot.AutopilotMode.StabilityAssist);
            _customDir = direction;
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

        /// <summary>A leg takes RCS over (true) or hands it back to the automatic use (false).</summary>
        public void Rcs(bool on) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.RCS, on); _rcsOwnedByLeg = on; _autoRcs = false; }
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
            // While some engine still burns, stage only when the next stage throws away nothing that is burning (2026-10-28:
            // side boosters burnt out and the crew dropped the whole first stage with its main engine still running).
            if (anyActive && anyFlamedOut && anyThrust && !NextStageDropsOnlySpent()) return;
            if ((anyActive && (anyFlamedOut || !anyThrust)) || (!anyActive && HasStagesLeft)) if (NextStageIsWorthFiring())
            {
                Stage();
                line = "Staging (" + (anyFlamedOut ? "burnout" : "no thrust") + "), stage " + _v.currentStage + ".";
            }
        }

        /// <summary>
        /// Drops dead weight (2026-10-28): when every lit engine has burnt out and the next stage only separates (decouplers,
        /// no engines, no parachutes), the crew fires it, so a spent booster does not ride along into reentry. Nothing that
        /// would light an engine or open a chute is fired here. Returns true when it staged.
        /// </summary>
        public bool DropSpentStage(ref string line)
        {
            if (_v == null || _v.currentStage <= 0 || Now - _lastStage < 1.5) return false;
            bool anyLit = false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (!e.EngineIgnited) continue;
                anyLit = true;
                if (!e.flameout && e.finalThrust > 0.1f) return false; // still burning
                if (!e.flameout && HasPropellant(e)) return false;    // throttled down, not spent
            }
            if (!anyLit) return false;
            int next = _v.currentStage - 1;
            bool decoupler = false;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                if (p.FindModuleImplementing<ModuleEngines>() != null || p.FindModuleImplementing<ModuleParachute>() != null) return false;
                if (p.FindModuleImplementing<ModuleDecouple>() != null || p.FindModuleImplementing<ModuleAnchoredDecoupler>() != null) decoupler = true;
            }
            if (!decoupler) return false;
            Stage();
            line = "Spent stage jettisoned.";
            return true;
        }

        /// <summary>
        /// Falling back for a landing under parachutes: stages away the next stage when it only decouples (no engine to
        /// light, no parachute to fire), so the capsule comes down alone, blunt end first. One stage per call, 1.5 s apart.
        /// Sets line when it staged.
        /// </summary>
        public bool ShedForReentry(ref string line)
        {
            if (_v == null || _v.currentStage <= 0 || Now - _lastStage < 1.5) return false;
            int next = _v.currentStage - 1;
            bool decoupler = false;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                if (p.FindModuleImplementing<ModuleEngines>() != null || p.FindModuleImplementing<ModuleParachute>() != null) return false;
                if (p.FindModuleImplementing<ModuleAblator>() != null) return false; // a heat shield (it can decouple itself): keep it
                if (p.FindModuleImplementing<ModuleDecouple>() != null || p.FindModuleImplementing<ModuleAnchoredDecoupler>() != null) decoupler = true;
            }
            if (!decoupler) return false;
            Stage();
            line = "Staged away everything below the capsule for the descent.";
            return true;
        }

        private static bool HasPropellant(ModuleEngines e)
        {
            if (e.part == null || e.propellants == null) return false;
            foreach (Propellant pr in e.propellants)
            {
                if (pr.name == "ElectricCharge" || pr.name == "IntakeAir") continue;
                if (pr.totalResourceAvailable > 0.01) return true;
            }
            return false;
        }

        /// <summary>
        /// True when every engine that the next stage's decouplers would cut loose has burnt out, so staging only sheds dead
        /// weight. A decoupler takes everything hanging below or beside it in the part tree.
        /// </summary>
        public bool NextStageDropsOnlySpent()
        {
            if (_v == null || _v.currentStage <= 0) return false;
            int next = _v.currentStage - 1;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                if (p.FindModuleImplementing<ModuleDecouple>() == null && p.FindModuleImplementing<ModuleAnchoredDecoupler>() == null) continue;
                if (BurningBelow(p)) return false;
            }
            return true;
        }

        private static bool BurningBelow(Part p)
        {
            foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f) return true;
            foreach (Part c in p.children) if (BurningBelow(c)) return true;
            return false;
        }

        /// <summary>
        /// Drops the side boosters early when they drag the rocket off course: fires the next stage only when it is radial
        /// decouplers and nothing else (no engines, no stack decouplers, no chutes). True when it staged.
        /// </summary>
        public bool DropSideBoosters(ref string line)
        {
            if (_v == null || _v.currentStage <= 0 || Now - _lastStage < 1.5) return false;
            int next = _v.currentStage - 1;
            bool any = false;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                if (p.FindModuleImplementing<ModuleAnchoredDecoupler>() == null) return false; // only radial decouplers
                any = true;
            }
            if (!any) return false;
            Stage();
            line = "Side boosters were pulling the rocket off course: dropped them early.";
            return true;
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
            // Just opened the throttle: no thrust yet is the engine spooling up, not empty tanks (2026-10-29: the
            // circularisation burn was given up in the frame it started, with 1,600 m/s still aboard).
            if (Now - _throttleUp < 3) return false;
            foreach (ModuleEngines e in _v.FindPartModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f) return false;
            return _v.currentStage <= 0 || Now - _lastStage > 4;
        }

        /// <summary>
        /// True when the craft has (nearly) no delta-v left in any stage, by the stock calculator. Unlike
        /// <see cref="NoThrustAvailable"/> this is right with the throttle closed, so coasting legs use it (2026-10-28: a
        /// coast to apoapsis read "no engine burning" as "out of fuel" and gave up on orbit). Unknown counts as fuel left.
        /// </summary>
        public bool OutOfDeltaV(double minimum = 15)
        {
            if (_v == null) return true;
            try
            {
                if (_v.VesselDeltaV == null || !_v.VesselDeltaV.IsReady) return false;
                return _v.VesselDeltaV.TotalDeltaVVac < minimum;
            }
            catch { return false; }
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

        /// <summary>
        /// Arms stowed chutes in the stock "open when safe" mode: the game opens each one the moment the airflow is cool
        /// enough, at any height (it judges by shock heating, not speed). Called early on every descent, so no safe window
        /// is missed. With <paramref name="force"/> armed chutes open at once whatever the risk: better a torn chute than a crater.
        /// </summary>
        public void DeployChutes(ref string line, bool force)
        {
            if (_v == null) return;
            int armed = 0, forced = 0;
            foreach (ModuleParachute chute in _v.FindPartModulesImplementing<ModuleParachute>())
            {
                if (chute.deploymentState == ModuleParachute.deploymentStates.STOWED)
                {
                    chute.automateSafeDeploy = force ? 2 : 0; // 0 = when safe, 2 = immediately
                    chute.Deploy();
                    armed++;
                }
                else if (force && chute.deploymentState == ModuleParachute.deploymentStates.ACTIVE && chute.automateSafeDeploy < 2)
                {
                    chute.automateSafeDeploy = 2;
                    forced++;
                }
            }
            if (forced > 0 || (force && armed > 0)) line = "Parachutes forced open at " + Math.Round(_v.altitude) + " m, " + Math.Round(_v.srfSpeed) + " m/s (too fast to be safe; no choice).";
            else if (armed > 0) line = "Parachutes armed at " + Math.Round(_v.altitude) + " m: they open as soon as it is safe.";
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
