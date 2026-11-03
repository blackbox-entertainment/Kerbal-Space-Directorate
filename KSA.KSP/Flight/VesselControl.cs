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

        // ------------------------------------------------------------------ RCS
        // Thrusters are for docking only (0.8.164, T: "rcs was only supposed to be used for docking"). Until then the crew
        // had switched them on to help the reaction wheels through every turn in space, and aligning one 23 m/s burn on the
        // way to the tanker spent 13 of the 40 monopropellant the dock needed. The docking leg owns them through Rcs().

        private bool _rcsOwnedByLeg;

        private void AutoRcs() { }

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

        /// <summary>
        /// Runs the attitude hold for a craft that is not the active vessel (0.8.168, T: the station turns its port toward the
        /// ship coming in). The hold works through SAS's rotation lock, which KSP's own controller keeps on every loaded
        /// vessel, so no fly-by-wire hook is needed; call this every physics step.
        /// </summary>
        public void Drive() { if (_manualHold && _v != null && _v.loaded) ManualHold(_scratch); }
        private readonly FlightCtrlState _scratch = new FlightCtrlState();

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
                // Hold the roll SAS was locked to, not the roll of the moment (2026-10-30): taking the current roll every
                // frame told SAS the roll was always right, so nothing damped it; rockets spun up to 60-156 deg/s at max q
                // and, spinning, could not pitch over (both Kerbin orbit attempts climbed nearly straight up).
                Quaternion baseRot = _lockAt >= 0 ? _lastLock : rt.rotation;
                Quaternion wanted = Quaternion.FromToRotation(baseRot * Vector3.up, want) * baseRot;
                // Planes (2026-10-30): a whole attitude, nose and wings, so the crew can bank to turn and keep the wings level.
                Vector3d? fwd = null;
                if (_customForward != null) { try { fwd = _customForward(); } catch { fwd = null; } }
                if (fwd.HasValue)
                {
                    Vector3 f = (Vector3)fwd.Value;
                    f = (f - want * Vector3.Dot(f, want));
                    if (f.sqrMagnitude > 1e-6f) wanted = Quaternion.LookRotation(f.normalized, want);
                }
                // Re-lock only when the target has moved (2026-10-30): locking afresh every physics frame left SAS no
                // time to work against a pushing engine, and an upper stage spun up to 100 deg/s under thrust.
                double lockNow = Now;
                if (_lockAt < 0 || Quaternion.Angle(wanted, _lastLock) > 0.5f || lockNow - _lockAt > 1.0)
                {
                    _v.Autopilot.SAS.LockRotation(wanted);
                    _lastLock = wanted; _lockAt = lockNow;
                }
                // Leave SAS's own commands alone (2026-10-30). KSP runs OnFlyByWire after SAS (Vessel.FeedInputFeed: pre-
                // autopilot, autopilot, post-autopilot, fly-by-wire), so the zeroing of pitch and yaw that stood here wiped
                // out every SAS correction: since 2026-10-28 no locked attitude was ever steered to (rockets climbed
                // straight up and drifted west, the coast never faced the horizon).
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

        private Quaternion _lastLock = Quaternion.identity;
        private double _lockAt = -1;

        private void EndManualHold()
        {
            _manualHold = false;
            _customDir = null;
            _customForward = null;
            _lockAt = -1;
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
            FlightGlobals.SpeedDisplayModes wanted = surfaceMode ? FlightGlobals.SpeedDisplayModes.Surface : FlightGlobals.SpeedDisplayModes.Orbit;
            if (FlightGlobals.speedDisplayMode != wanted) FlightGlobals.SetSpeedMode(wanted);
            if (mode == VesselAutopilot.AutopilotMode.StabilityAssist || (_v.Autopilot != null && _v.Autopilot.CanSetMode(mode)))
            {
                EndManualHold();
                _mode = mode;
                // Only on a change: legs ask every frame, and setting the mode restarts the hold, so it never turned the ship
                // (2026-10-30: "stock retrograde hold", spin 0, the nose drifting 7 to 74 degrees off; the deorbit never lined up).
                if (!_v.ActionGroups[KSPActionGroup.SAS]) _v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                if (_v.Autopilot != null && _v.Autopilot.Mode != mode) _v.Autopilot.SetMode(mode);
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
            _customForward = null;
        }

        private Func<Vector3d> _customForward;

        /// <summary>
        /// Nose square to the sun for a long coast or a long wait before a burn (0.8.176): panels on the sides of the stack see
        /// it. A Duna crew cruised 300 days nose-on to the sun after a 0.2 m/s trim, 29 of their 48 panels at 0% exposure, and
        /// died of CO2 poisoning when the battery ran flat 23 days short of Duna.
        /// </summary>
        /// <summary>
        /// The side with the panels toward the sun (0.8.178, T: "keep the side of the ship facing the sun"). The panels' normals in
        /// the craft's frame give the one direction the sun should come from, roll included; a nose square to the sun with the roll
        /// left where it was lit nothing (0.01 EC/s from 48 panels). Falls back to broadside when no panel can be read.
        /// </summary>
        public void FacePanelsAtSun()
        {
            if (_v == null) return;
            Vector3d from = PanelSunDirection();
            if (from == Vector3d.zero) { FaceSunBroadside(); return; }
            _panelSun = from;
            _panelAttitudeAt = -1;
            PointAlong(() => { PanelAttitude(); return _panelNose; }, () => { PanelAttitude(); return _panelForward; });
        }

        private Vector3d _panelSun, _panelNose, _panelForward;
        private double _panelAttitudeAt = -1;

        /// <summary>The nose and forward axis, in world space, that put the panels' direction on the sun: the least turn from where the craft is now.</summary>
        private void PanelAttitude()
        {
            if (_panelAttitudeAt == Now) return;
            _panelAttitudeAt = Now;
            Transform rt = _v.ReferenceTransform;
            if (rt == null) return;
            Vector3d sun = (Planetarium.fetch.Sun.position - _v.GetWorldPos3D()).normalized;
            Vector3d fromNow = rt.TransformDirection((Vector3)_panelSun);
            Quaternion q = Quaternion.FromToRotation((Vector3)fromNow, (Vector3)sun);
            _panelNose = q * rt.up;
            _panelForward = q * rt.forward;
        }

        /// <summary>
        /// Where the sun should come from in the craft's frame for the most panel output: the normals (Kerbalism's reading of them
        /// when it is there, else the panel's own sun transform), weighted by charge rate, searched over the sphere. Zero with no panels.
        /// </summary>
        private Vector3d PanelSunDirection()
        {
            Transform rt = _v.ReferenceTransform;
            if (rt == null) return Vector3d.zero;
            var normals = new List<Vector3d>();
            var weights = new List<double>();
            foreach (ModuleDeployableSolarPanel p in _v.FindPartModulesImplementing<ModuleDeployableSolarPanel>())
            {
                try
                {
                    if (p.deployState != ModuleDeployablePart.DeployState.EXTENDED) continue;
                    Vector3d n = Vector3d.zero;
                    PartModule fixer = p.part.Modules.Contains("SolarPanelFixer") ? p.part.Modules["SolarPanelFixer"] : null;
                    if (fixer != null)
                    {
                        Type t = fixer.GetType();
                        System.Reflection.FieldInfo has = t.GetField("hasPanelOrientation"), fx = t.GetField("panelNormalPartLocalX"), fy = t.GetField("panelNormalPartLocalY"), fz = t.GetField("panelNormalPartLocalZ");
                        if (has != null && fx != null && fy != null && fz != null && (bool)has.GetValue(fixer))
                            n = p.part.transform.TransformDirection(new Vector3((float)(double)fx.GetValue(fixer), (float)(double)fy.GetValue(fixer), (float)(double)fz.GetValue(fixer)));
                    }
                    if (n == Vector3d.zero) n = p.trackingDotTransform != null ? (Vector3d)p.trackingDotTransform.forward : (Vector3d)p.part.transform.forward;
                    if (n.magnitude < 1e-6) continue;
                    normals.Add(((Vector3d)rt.InverseTransformDirection((Vector3)n)).normalized);
                    weights.Add(Math.Max(0.01, p.chargeRate));
                }
                catch { }
            }
            if (normals.Count == 0) return Vector3d.zero;
            Func<Vector3d, double> score = d => { double s = 0; for (int i = 0; i < normals.Count; i++) s += weights[i] * Math.Max(0, Vector3d.Dot(normals[i], d)); return s; };
            var candidates = new List<Vector3d>(normals);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) if (x != 0 || y != 0 || z != 0) candidates.Add(new Vector3d(x, y, z).normalized);
            Vector3d best = candidates[0]; double bestScore = -1;
            foreach (Vector3d c in candidates) { double s = score(c); if (s > bestScore) { bestScore = s; best = c; } }
            // Climb from the best candidate in shrinking steps.
            foreach (double step in new[] { 0.3, 0.1, 0.03 })
            {
                bool moved = true;
                for (int guard = 0; moved && guard < 20; guard++)
                {
                    moved = false;
                    foreach (Vector3d axis in new[] { Vector3d.right, Vector3d.up, Vector3d.forward })
                        foreach (double sign in new[] { 1.0, -1.0 })
                        {
                            Vector3d c = (best + axis * (step * sign)).normalized;
                            double s = score(c);
                            if (s > bestScore + 1e-6) { bestScore = s; best = c; moved = true; }
                        }
                }
            }
            double total = 0; foreach (double w in weights) total += w;
            _panelNominal = bestScore; _panelTotal = total;
            return best;
        }

        private double _panelNominal, _panelTotal;

        /// <summary>What the panel attitude should give against every panel full on, nominal rates at Kerbin's distance.</summary>
        public string PanelPlan { get { return _panelTotal > 0 ? "best side gives " + _panelNominal.ToString("0.0") + " of " + _panelTotal.ToString("0.0") + " EC/s nominal" : null; } }

        /// <summary>What the panels are making now, EC/s: Kerbalism's figure when it is there, else the stock flow.</summary>
        public double PanelOutput()
        {
            double total = 0;
            if (_v == null) return 0;
            foreach (ModuleDeployableSolarPanel p in _v.FindPartModulesImplementing<ModuleDeployableSolarPanel>())
            {
                try
                {
                    PartModule fixer = p.part.Modules.Contains("SolarPanelFixer") ? p.part.Modules["SolarPanelFixer"] : null;
                    System.Reflection.FieldInfo f = fixer != null ? fixer.GetType().GetField("currentOutput") : null;
                    total += f != null ? Convert.ToDouble(f.GetValue(fixer)) : p.flowRate;
                }
                catch { }
            }
            return total;
        }

        /// <summary>The held attitude is reached and the craft has stopped turning: safe to go on rails (0.8.178: warp froze a turn half done).</summary>
        public bool HoldSettled(double maxDegrees = 5)
        {
            if (_v == null || _v.ReferenceTransform == null) return true;
            if (!_manualHold || _lockAt < 0) return true;
            return Quaternion.Angle(_v.ReferenceTransform.rotation, _lastLock) < maxDegrees && TurnRate(_v) < 0.5;
        }

        public void FaceSunBroadside()
        {
            if (_v == null) return;
            PointAlong(() =>
            {
                Vector3d sun = (Planetarium.fetch.Sun.position - _v.GetWorldPos3D()).normalized;
                Vector3d nose = Vector3d.Cross(sun, Vector3d.up);
                if (nose.magnitude < 0.1) nose = Vector3d.Cross(sun, Vector3d.right);
                return nose.normalized;
            });
        }

        /// <summary>
        /// Holds a whole attitude (2026-10-30, aircraft): the nose along <paramref name="nose"/> and the control point's
        /// forward axis (belly or back, whichever the craft has) along <paramref name="forwardAxis"/>.
        /// </summary>
        public void PointAlong(Func<Vector3d> nose, Func<Vector3d> forwardAxis)
        {
            if (_v == null || nose == null) return;
            BeginManualHold(VesselAutopilot.AutopilotMode.StabilityAssist);
            _customDir = nose;
            _customForward = forwardAxis;
        }

        /// <summary>How fast the nose swings, deg/s: the turn across the long axis, roll left out (2026-10-30).</summary>
        public static double TurnRate(Vessel v)
        {
            if (v == null) return 0;
            Vector3 w = v.angularVelocity; // vessel frame: x pitch, y roll (about the nose), z yaw
            return Math.Sqrt(w.x * w.x + w.z * w.z) * Mathf.Rad2Deg;
        }

        /// <summary>Roll rate about the nose, deg/s.</summary>
        public static double RollRate(Vessel v) { return v == null ? 0 : Math.Abs(v.angularVelocity.y) * Mathf.Rad2Deg; }

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
        public void Rcs(bool on) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.RCS, on); _rcsOwnedByLeg = on; }
        public void Gear(bool down) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.Gear, down); }
        public void Lights(bool on) { if (_v != null) _v.ActionGroups.SetGroup(KSPActionGroup.Light, on); }

        // ------------------------------------------------------------------ staging

        private double _settleUntil = -1;

        /// <summary>
        /// False while the vessel is on rails (packed) or time is warping, and for a second after it unpacks: parts must not
        /// separate then (2026-10-30: dropping out of warp at the top of the air, the capsule was decoupled in the same frame
        /// the ship unpacked, the parts overlapped and Valentina's pod exploded). Asking stops the warp, so call it only when
        /// something is about to separate (0.8.3: the reentry leg asked every frame and the player could not warp at all).
        /// </summary>
        public bool PartsSafe()
        {
            if (_v == null) return false;
            if (_v.packed || IsWarping)
            {
                StopWarp();
                _settleUntil = Now + 1.0;
                return false;
            }
            return _settleUntil < 0 || Now >= _settleUntil;
        }

        /// <summary>Fires the next stage; false when it could not now (not the active vessel, or parts not safe to separate).</summary>
        public bool Stage()
        {
            if (_v == null || _v != FlightGlobals.ActiveVessel || !PartsSafe()) return false;
            global::KSP.UI.Screens.StageManager.ActivateNextStage();
            _lastStage = Now;
            return true;
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
            // Lit, not burnt out, but no thrust yet: an engine just lit or a throttle just opened is still building up, and a
            // closed throttle gives none. Not a reason to stage (2026-10-29: the crew lit the second stage for the
            // circularisation burn and dropped it, full, 1.5 s later).
            if (anyActive && !anyFlamedOut && !anyThrust && (_throttle < 0.05f || Now - _lastStage < 3 || Now - _throttleUp < 3)) return;
            if ((anyActive && (anyFlamedOut || !anyThrust)) || (!anyActive && HasStagesLeft)) if (NextStageIsWorthFiring())
            {
                if (Stage()) line = "Staging (" + (anyFlamedOut ? "burnout" : "no thrust") + "), stage " + _v.currentStage + ".";
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
            if (!decoupler || !Stage()) return false;
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
            if (_v == null || Now - _lastStage < 1.5) return false;
            // The decoupler nearest under the capsule, fired directly: everything below goes at once, whatever stage it
            // was put in (2026-10-29: a Director's rocket had it in the parachute stage, which the crew never fires).
            ModuleDecouple under = DecouplerUnderCapsule();
            if (under != null)
            {
                if (!PartsSafe()) return false;
                try
                {
                    under.Decouple();
                    _lastStage = Now;
                    line = "Decoupled everything below the capsule for the descent.";
                    return true;
                }
                catch (Exception e) { KsaCore.Log.Warn("Decoupling under the capsule failed: " + e.Message); }
            }
            if (_v.currentStage <= 0) return false;
            int next = _v.currentStage - 1;
            bool decoupler = false;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                if (p.FindModuleImplementing<ModuleEngines>() != null || p.FindModuleImplementing<ModuleParachute>() != null) return false;
                if (p.FindModuleImplementing<ModuleAblator>() != null) return false; // a heat shield (it can decouple itself): keep it
                if (p.FindModuleImplementing<ModuleDecouple>() != null || p.FindModuleImplementing<ModuleAnchoredDecoupler>() != null) decoupler = true;
            }
            if (!decoupler || !Stage()) return false;
            line = "Staged away everything below the capsule for the descent.";
            return true;
        }

        /// <summary>
        /// The unfired stack decoupler closest below a crewed part (not a heat shield's own), or null when the capsule has
        /// nothing under it to let go of.
        /// </summary>
        private ModuleDecouple DecouplerUnderCapsule()
        {
            ModuleDecouple best = null;
            int bestHops = int.MaxValue;
            foreach (Part p in _v.parts)
            {
                if (p.FindModuleImplementing<ModuleAblator>() != null) continue;
                ModuleDecouple d = p.FindModuleImplementing<ModuleDecouple>();
                if (d == null || d.isDecoupled) continue;
                int hops = 0;
                Part up = p.parent;
                while (up != null && up.CrewCapacity == 0 && hops < 50) { up = up.parent; hops++; }
                if (up == null || up.CrewCapacity == 0) continue; // not under a crew part
                if (hops < bestHops) { bestHops = hops; best = d; }
            }
            return best;
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

        private static void EnginesBurning(Part p, List<ModuleEngines> found)
        {
            foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f) found.Add(e);
            foreach (Part c in p.children) EnginesBurning(c, found);
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
        /// A burning solid cannot be shut off and flies on up the side of the rocket, so it stays on until it burns out; a
        /// liquid booster is shut down first (2026-10-30, T: "the side boosters just flew off and exploded everything").
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
            var burning = new List<ModuleEngines>();
            foreach (Part p in _v.parts)
                if (p.inverseStage == next) foreach (Part c in p.children) EnginesBurning(c, burning);
            foreach (ModuleEngines e in burning) if (e.throttleLocked) return false;
            foreach (ModuleEngines e in burning) e.Shutdown();
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

        /// <summary>Electric charge left aboard, 0 to 1; -1 when the vessel holds none at all.</summary>
        public static double ChargeFraction(Vessel v)
        {
            if (v == null) return -1;
            double amount = 0, max = 0;
            foreach (Part p in v.parts)
            {
                PartResource r = p.Resources != null ? p.Resources.Get("ElectricCharge") : null;
                if (r != null) { amount += r.amount; max += r.maxAmount; }
            }
            return max > 0 ? amount / max : -1;
        }

        /// <summary>
        /// True when a lit engine can swivel (ModuleGimbal): it steers the rocket, but only while it burns, so a turn far
        /// from the burn direction is made on a little throttle (2026-10-30).
        /// </summary>
        /// <summary>True when something aboard turns the craft without an engine: a reaction wheel or RCS thrusters.</summary>
        public bool HasWheelOrRcs()
        {
            if (_v == null) return false;
            foreach (Part p in _v.parts)
                if (p.FindModuleImplementing<ModuleReactionWheel>() != null || p.FindModuleImplementing<ModuleRCS>() != null) return true;
            return false;
        }

        public bool CanSteerWithEngine()
        {
            if (_v == null) return false;
            foreach (Part p in _v.parts)
            {
                ModuleEngines e = p.FindModuleImplementing<ModuleEngines>();
                if (e == null || !e.EngineIgnited || e.flameout) continue;
                ModuleGimbal g = p.FindModuleImplementing<ModuleGimbal>();
                if (g != null && !g.gimbalLock && g.gimbalRange > 0) return true;
            }
            return false;
        }

        /// <summary>True when nothing is burning and staging will not help (or has just been tried).</summary>
        public bool NoThrustAvailable()
        {
            if (_v == null) return true;
            // On-rails time warp: engines cannot fire, which is not empty tanks (2026-10-30: the circularisation burn was
            // given up under 50x warp with 2,770 m/s aboard). The grace starts again when warp ends.
            if (TimeWarp.CurrentRate > 1.01f) { _throttleUp = Now; return false; }
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
        private double _dvLowSince = -1;

        /// <summary>
        /// True when the stages left have less than <paramref name="minimum"/> m/s, and have said so for 3 s out of time warp
        /// (2026-10-30: just after the ship unpacked from warp the game's delta-v read 0 for a moment; the ascent took that
        /// as out of fuel, ended before circularising, and the Mun flyby halted on a suborbital path).
        /// </summary>
        /// <summary>The vacuum delta-v the game reads for the whole craft; 0 while it has none or the readout is not ready.</summary>
        public double DeltaVAvailable()
        {
            try { return _v != null && _v.VesselDeltaV != null && _v.VesselDeltaV.IsReady ? Math.Max(0, _v.VesselDeltaV.TotalDeltaVVac) : 0; }
            catch { return 0; }
        }

        public bool OutOfDeltaV(double minimum = 15)
        {
            if (_v == null) return true;
            try
            {
                if (_v.packed || IsWarping || _v.VesselDeltaV == null || !_v.VesselDeltaV.IsReady || _v.VesselDeltaV.TotalDeltaVVac >= minimum) { _dvLowSince = -1; return false; }
                if (_dvLowSince < 0) _dvLowSince = Now;
                return Now - _dvLowSince >= 3.0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Antennas and solar panels out above the air, folded before coming back into it (2026-10-30, T: "I guess my antenna
        /// cannot be extended to transmit"; nothing ever opened them, and air snaps them off). Parts cannot move on rails.
        /// </summary>
        public void TendDeployables(bool outside)
        {
            if (_v == null || _v.packed || IsWarping) return;
            foreach (ModuleDeployablePart d in _v.FindPartModulesImplementing<ModuleDeployablePart>())
            {
                if (!(d is ModuleDeployableAntenna) && !(d is ModuleDeployableSolarPanel)) continue;
                if (string.IsNullOrEmpty(d.animationName)) continue; // fixed panels (OX-STAT) have nothing to move: told to, they threw every frame
                if (!d.retractable) { if (outside && d.deployState == ModuleDeployablePart.DeployState.RETRACTED) d.Extend(); continue; }
                if (outside && d.deployState == ModuleDeployablePart.DeployState.RETRACTED) d.Extend();
                else if (!outside && d.deployState == ModuleDeployablePart.DeployState.EXTENDED) d.Retract();
            }
        }

        /// <summary>
        /// Drop tanks: when the next stage is only radial decouplers and everything they hold is empty of fuel, it goes, whether or not the
        /// engine is burning (2026-11-01, T's short wide lander: the rule for spent stages waits for the engines to stop, and these feed one that never does).
        /// </summary>
        public bool DropEmptyTanks(ref string line)
        {
            if (_v == null || _v.packed || IsWarping || _v.currentStage <= 1 || Now - _lastStage < 1.5) return false;
            // Empty side tanks are the room for a tanker's fuel: they stay on until it has been met (2026-11-01).
            if (TankerStopLeg.TankerAhead(_v)) return false;
            int next = _v.currentStage - 1, tanks = 0;
            foreach (Part p in _v.parts)
            {
                if (p.inverseStage != next) continue;
                // The tanks carry the same stage number as their decouplers (2026-11-01: empty on Duna arrival, and they stayed on).
                if (p.FindModuleImplementing<ModuleAnchoredDecoupler>() == null)
                {
                    if (p.CrewCapacity > 0 || p.FindModuleImplementing<ModuleEngines>() != null || p.FindModuleImplementing<ModuleParachute>() != null || p.FindModuleImplementing<ModuleDecouple>() != null) return false;
                    continue;
                }
                foreach (Part held in p.children)
                {
                    if (!EmptyBranch(held, ref tanks)) return false;
                }
            }
            if (tanks == 0 || !Stage()) return false;
            line = tanks + " empty drop tanks jettisoned.";
            return true;
        }

        /// <summary>True when a branch of parts holds no fuel, crew or engine. Counts the tanks in it.</summary>
        private static bool EmptyBranch(Part p, ref int tanks)
        {
            if (p.CrewCapacity > 0 || p.FindModuleImplementing<ModuleEngines>() != null) return false;
            foreach (PartResource r in p.Resources)
            {
                if (r.resourceName != "LiquidFuel" && r.resourceName != "Oxidizer") continue;
                if (r.amount > EmptyTank) return false;
                tanks++;
            }
            foreach (Part child in p.children) if (!EmptyBranch(child, ref tanks)) return false;
            return true;
        }

        private const double EmptyTank = 0.5;

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
            if (ut <= Now + 1 || Now < _noWarpUntil) return;
            _warpAskedUntil = ut;
            // Out of the air, full warp: physics warp left over from the air stops at 4x (2026-10-30, T: "would be nice to speed up faster").
            bool inAir = _v != null && _v.mainBody != null && _v.mainBody.atmosphere && _v.altitude < _v.mainBody.atmosphereDepth;
            if (!inAir && TimeWarp.WarpMode == TimeWarp.Modes.LOW)
            {
                if (IsWarping) TimeWarp.SetRate(0, true);
                TimeWarp.fetch.Mode = TimeWarp.Modes.HIGH;
            }
            else if (IsWarping) return;
            TimeWarp.fetch.WarpTo(ut);
        }

        private double _warpAskedUntil, _noWarpUntil;

        /// <summary>
        /// The Director's warp keys, every frame: stopping the crew's warp holds for as long as that warp was to run, so it does not
        /// start again by itself (2026-11-01, T: "if I cancel warp it should not reenter warp on its own until near the next leg").
        /// </summary>
        public void WatchWarpKeys()
        {
            if (Now >= _warpAskedUntil || Now < _noWarpUntil) return;
            if (!GameSettings.TIME_WARP_STOP.GetKeyDown() && !GameSettings.TIME_WARP_DECREASE.GetKeyDown()) return;
            _noWarpUntil = _warpAskedUntil;
            ScreenMessages.PostScreenMessage("[KSA] Warp stopped: the crew will not warp again until the next step.", 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void StopWarp()
        {
            if (TimeWarp.fetch != null && IsWarping) TimeWarp.fetch.CancelAutoWarp();
            if (TimeWarp.fetch != null) TimeWarp.fetch.CancelAutoWarp(); // a warp-to-time keeps running otherwise, whatever the rate is set to
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

        /// <summary>
        /// Landing away from home: only the chutes that leave with the lander. The capsule's are for the reentry at home, and a chute
        /// opens once (2026-11-01: a Duna descent would have armed them all, and the crew would have come home with none).
        /// </summary>
        public void DeployLanderChutes(ref string line)
        {
            if (_v == null) return;
            int armed = 0;
            foreach (ModuleParachute chute in _v.FindPartModulesImplementing<ModuleParachute>())
            {
                if (chute.deploymentState != ModuleParachute.deploymentStates.STOWED || StaysWithCapsule(chute.part)) continue;
                chute.automateSafeDeploy = 0;
                chute.Deploy();
                armed++;
            }
            if (armed > 0) line = armed + " lander parachutes armed at " + Math.Round(_v.altitude) + " m; the capsule keeps its own for home.";
        }

        /// <summary>True when no decoupler lies between a part and the root (a heat shield's or a crew pod's built-in one does not count).</summary>
        private static bool StaysWithCapsule(Part p)
        {
            for (Part q = p; q != null; q = q.parent)
            {
                bool decoupler = q.FindModuleImplementing<ModuleDecouple>() != null || q.FindModuleImplementing<ModuleAnchoredDecoupler>() != null;
                if (decoupler && q.CrewCapacity == 0 && q.FindModuleImplementing<ModuleAblator>() == null) return false;
            }
            return true;
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
