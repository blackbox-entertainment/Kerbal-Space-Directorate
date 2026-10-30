using System;
using KSA.Core.Missions;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Aircraft missions (2026-10-30, T: "maybe we should make some planes and goals for planes"). The crew takes off from
    /// the runway, climbs to the cruise height, flies out along the mission's heading keeping clear of the ground ahead,
    /// turns for home at half the range or half the fuel, and lands: under parachutes near the Space Center, or with the
    /// Director at the controls for the runway when the plane has none. Steering is a whole attitude (nose and wings) held
    /// by stock SAS: the crew banks to turn and trims the nose for the climb rate it wants.
    /// </summary>
    public sealed class AircraftLeg : IFlightLeg
    {
        private enum Phases { Roll, Climb, Outbound, Homebound, Approach, Chutes, Zoom, Apex }

        // Kerbin's Space Center, for a flight resumed in the air (on the runway the start point is used).
        private const double KscLat = -0.0972, KscLon = -74.5577;
        private const double RotateSpeed = 60;

        private readonly double _halfRange, _outHeading;
        private readonly bool _survey;
        private double _cruise;
        private Phases _phase;
        private double _phaseStart, _lastStep = -1;
        private double _homeLat = KscLat, _homeLon = KscLon;
        private double _trim = 3;          // nose above the horizon that holds level flight, learned in flight
        private double _bestAlt, _bestAltAt;
        private double _startFuel = -1;
        private double _runwayHeading = 90;
        private float _topSign = 1f;       // which way the control point's forward axis faces: up (+1) or down (-1) when level
        private Vector3d _nose, _forward;
        private bool _chutes;
        private double _throttleCutAt = -1;
        private string _body = "Kerbin";
        // Zoom climb (2026-10-30, T: "what about a 36 km plane? or 40 km?"): jets to their ceiling, then rockets.
        private readonly double _zoomTo;
        private double _peak;

        /// <summary>Set by the planner on a resume when the outbound half was already flown.</summary>
        public bool TurnedForHome { get; set; }

        public AircraftLeg(MissionType type)
        {
            _cruise = type != null && type.CruiseAltitude > 0 ? type.CruiseAltitude : 2000;
            _halfRange = Math.Max(10000, (type != null && type.RangeKm > 0 ? type.RangeKm : 100) * 500);
            _outHeading = type != null ? type.Heading : 90;
            if (type != null) foreach (string a in type.Achievements) if (a.StartsWith("Survey:", StringComparison.OrdinalIgnoreCase)) _survey = true;
            if (type != null && type.IsZoomClimb) { _zoomTo = _cruise; _cruise = 18000; } // the jets climb as high as they go first
        }

        public string Name { get { return "Aircraft flight"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            _body = v.mainBody != null ? v.mainBody.bodyName : "Kerbin";
            bool onGround = v.LandedOrSplashed || v.situation == Vessel.Situations.PRELAUNCH;
            Transform rt = v.ReferenceTransform;
            if (onGround)
            {
                _homeLat = v.latitude; _homeLon = v.longitude;
                _phase = Phases.Roll;
                if (rt != null) _runwayHeading = HeadingOf(v, rt.up);
            }
            else _phase = TurnedForHome ? Phases.Homebound : Phases.Climb;
            // Upright at the start: whichever way the control point's forward axis points, up or down, is "the top".
            if (rt != null) _topSign = Vector3d.Dot(rt.forward, v.upAxis) >= 0 ? 1f : -1f;
            _phaseStart = ctx.Now;
            _bestAlt = v.altitude; _bestAltAt = ctx.Now;
            _startFuel = Fuel(v);
            _chutes = v.FindPartModulesImplementing<ModuleParachute>().Count > 0;
            v.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            c.Throttle = 1f;
            double hdg = onGround ? _runwayHeading : HeadingOf(v, v.srf_velocity);
            SetAttitude(v, hdg, 0, 0, 0);
            c.PointAlong(() => _nose, () => _forward);
            Status = onGround ? "Rolling down the runway." : "Flying.";
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            Vessel v = ctx.Vessel;
            VesselControl c = ctx.Control;
            string line = null;
            double now = ctx.Now;
            double dt = _lastStep > 0 ? Math.Max(0.01, Math.Min(1.0, now - _lastStep)) : 0.02;
            _lastStep = now;
            double alt = v.altitude;
            double agl = AboveGround(v);
            double dist = DistanceHome(v);
            double hdgNow = v.srfSpeed > 20 ? HeadingOf(v, v.srf_velocity) : _runwayHeading;

            // Out of fuel or every engine flamed out in the air: chutes if there are any, else the Director lands it.
            // High up the jets starve of air: that is not an engine failure (they relight lower down).
            bool zooming = _phase == Phases.Zoom || _phase == Phases.Apex;
            if (_phase != Phases.Roll && _phase != Phases.Chutes && !v.LandedOrSplashed && ((_startFuel > 1 && Fuel(v) < 0.5) || (!zooming && alt < 15000 && c.Throttle > 0.05f && c.NoThrustAvailable() && now - _phaseStart > 5)))
            {
                c.Throttle = 0;
                string where = Math.Round(dist / 1000) + " km from the Space Center at " + Math.Round(alt) + " m";
                ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "the plane's engines quit " + where);
                if (_chutes) { _phase = Phases.Chutes; _phaseStart = now; c.DeployChutes(ref line); return (line ?? string.Empty) + " Engines out " + where + ": coming down under parachutes."; }
                return HandOver(ctx, "Engines out " + where + ": the Director has the controls for a dead-stick landing.");
            }

            switch (_phase)
            {
                case Phases.Roll:
                    c.Throttle = 1f;
                    if (!AnyEngineLit(v) && now - _phaseStart > 1) { c.EnsureEngine(ref line); line = line ?? "Engines started."; }
                    SetAttitude(v, _runwayHeading, v.srfSpeed > RotateSpeed ? 10 : 0, 0, 0);
                    if (!v.LandedOrSplashed && agl > 20)
                    {
                        c.Gear(false);
                        if (_zoomTo > 0 && ShutRockets(v) > 0) line = "Rockets off until the zoom climb.";
                        _phase = Phases.Climb; _phaseStart = now;
                        line = "Airborne at " + Math.Round(v.srfSpeed) + " m/s; gear up.";
                    }
                    else if (now - _phaseStart > 90) { State = LegState.Failed; Status = "Could not get airborne."; return "Ran out of runway: the plane would not lift off."; }
                    Status = "Takeoff roll: " + Math.Round(v.srfSpeed) + " m/s.";
                    return line;

                case Phases.Climb:
                case Phases.Outbound:
                {
                    Fly(v, hdgNow, _outHeading, TargetAltitude(v, _outHeading), agl, dt);
                    if (_phase == Phases.Climb)
                    {
                        if (alt > _bestAlt + 50) { _bestAlt = alt; _bestAltAt = now; }
                        bool stalled = now - _bestAltAt > 90;
                        if (_zoomTo > 0 && (alt >= _cruise - 150 || stalled))
                        {
                            int lit = LightRockets(v);
                            _phase = Phases.Zoom; _phaseStart = now; _peak = alt;
                            return lit > 0 ? "Jets at their ceiling (" + Math.Round(alt / 1000, 1) + " km, " + Math.Round(v.srfSpeed) + " m/s): rockets lit, zoom climb to " + Math.Round(_zoomTo / 1000) + " km."
                                           : "No rocket engine would light at " + Math.Round(alt / 1000, 1) + " km; the zoom climb is off.";
                        }
                        if (alt >= _cruise - 150) { _phase = Phases.Outbound; line = "Cruising at " + Math.Round(alt / 1000, 1) + " km, heading " + Math.Round(_outHeading) + "."; }
                        else if (stalled)
                        {
                            _cruise = Math.Max(1000, alt - 200);
                            _phase = Phases.Outbound;
                            line = "The plane will climb no higher than " + Math.Round(alt / 1000, 1) + " km; cruising there.";
                        }
                    }
                    bool farEnough = dist >= _halfRange;
                    bool fuelHalf = _startFuel > 0 && Fuel(v) < _startFuel * 0.55;
                    if (farEnough || fuelHalf)
                    {
                        _phase = Phases.Homebound; TurnedForHome = true;
                        ctx.Say("Turning for home " + Math.Round(dist / 1000) + " km out" + (fuelHalf && !farEnough ? " (half the fuel used)" : string.Empty) + ".", "Aircraft:" + _body);
                        if (_survey && dist >= _halfRange * 0.6) ctx.Say("Aerial survey flown: " + Math.Round(dist / 1000) + " km out over " + _body + ".", "Survey:" + _body);
                    }
                    Status = (_phase == Phases.Climb ? "Climbing" : "Outbound") + ": " + Math.Round(alt / 1000, 1) + " km, " + Math.Round(v.srfSpeed) + " m/s, " + Math.Round(dist / 1000) + " of " + Math.Round(_halfRange / 1000) + " km out.";
                    return line;
                }

                case Phases.Zoom:
                {
                    c.Throttle = 1f;
                    double zerr = Wrap(_outHeading - hdgNow);
                    SetAttitude(v, hdgNow + Clamp(zerr, -3, 3), 30, 0, 0);
                    if (alt > _peak) _peak = alt;
                    bool high = v.orbit != null && v.orbit.ApA >= _zoomTo + 500;
                    bool dry = Oxidizer(v) < 0.5;
                    if (high || dry || now - _phaseStart > 150)
                    {
                        ShutRockets(v);
                        _phase = Phases.Apex; _phaseStart = now;
                        double apA = v.orbit != null ? v.orbit.ApA : alt;
                        line = high ? "Rockets off: the climb carries on to " + Math.Round(apA / 1000, 1) + " km."
                                    : "Rockets " + (dry ? "dry" : "off") + " at " + Math.Round(alt / 1000, 1) + " km; coasting up to " + Math.Round(apA / 1000, 1) + " km.";
                    }
                    Status = "Zoom climb: " + Math.Round(alt / 1000, 1) + " km, " + Math.Round(v.srfSpeed) + " m/s.";
                    return line;
                }

                case Phases.Apex:
                {
                    // Nose along the flight path, wings level, jets left open to relight lower down.
                    c.Throttle = 1f;
                    double path = v.srfSpeed > 1 ? Math.Asin(Clamp(v.verticalSpeed / v.srfSpeed, -1, 1)) * 180 / Math.PI : 0;
                    SetAttitude(v, hdgNow, path, 0, 0);
                    if (alt > _peak) _peak = alt;
                    if (v.verticalSpeed < 0)
                    {
                        bool made = _peak >= _zoomTo * 0.95;
                        ctx.Say("Peak altitude " + Math.Round(_peak / 1000, 1) + " km" + (made ? "." : ", short of the " + Math.Round(_zoomTo / 1000) + " km aimed for."), made ? "RocketPlane:" + _body : null);
                        _cruise = 12000;
                        _phase = Phases.Homebound; TurnedForHome = true;
                        ctx.Say("Turning for home " + Math.Round(dist / 1000) + " km out.", "Aircraft:" + _body);
                    }
                    Status = "Coasting up: " + Math.Round(alt / 1000, 1) + " km.";
                    return line;
                }

                case Phases.Homebound:
                {
                    double bearing = BearingHome(v);
                    Fly(v, hdgNow, bearing, TargetAltitude(v, bearing), agl, dt);
                    if (dist < 20000) { _phase = Phases.Approach; _phaseStart = now; line = "Approaching the Space Center."; }
                    Status = "Homebound: " + Math.Round(dist / 1000) + " km to go, " + Math.Round(alt / 1000, 1) + " km up.";
                    return line;
                }

                case Phases.Approach:
                {
                    double bearing = BearingHome(v);
                    if (!_chutes)
                    {
                        c.Gear(true);
                        return HandOver(ctx, "Final approach: the Director has the controls for the runway landing (" + Math.Round(dist / 1000) + " km out).");
                    }
                    double ground = TerrainAhead(v, bearing);
                    Fly(v, hdgNow, bearing, ground + 1500, agl, dt);
                    if (dist < 4000 && _throttleCutAt < 0) { _throttleCutAt = now; line = "Over the Space Center: throttle back, slowing for the parachutes."; }
                    if (_throttleCutAt >= 0) c.Throttle = 0f;
                    if (_throttleCutAt >= 0 && (v.srfSpeed < 120 || now - _throttleCutAt > 60))
                    {
                        c.Gear(true);
                        c.DeployChutes(ref line);
                        _phase = Phases.Chutes; _phaseStart = now;
                        c.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, true);
                    }
                    Status = "Approach: " + Math.Round(dist / 1000, 1) + " km, " + Math.Round(v.srfSpeed) + " m/s.";
                    return line;
                }

                case Phases.Chutes:
                    c.Throttle = 0f;
                    if (agl < 1500 && v.srfSpeed > 150) c.DeployChutes(ref line, true);
                    if (v.LandedOrSplashed && v.srfSpeed < 1)
                    {
                        State = LegState.Done;
                        Status = "Down.";
                        return "Down under parachutes " + Math.Round(dist / 1000, 1) + " km from the Space Center.";
                    }
                    Status = "Under parachutes: " + Math.Round(agl) + " m, " + Math.Round(v.srfSpeed) + " m/s.";
                    return line;
            }
            return line;
        }

        private string HandOver(FlightContext ctx, string text)
        {
            State = LegState.Done;
            Status = "Director flying.";
            if (ctx.HandToDirector != null) ctx.HandToDirector();
            return text;
        }

        /// <summary>The crew's stick and rudder: bank toward the wanted heading, nose trimmed for the climb rate that reaches the wanted height.</summary>
        private void Fly(Vessel v, double hdgNow, double hdgWanted, double altWanted, double agl, double dt)
        {
            double vs = v.verticalSpeed;
            double vsMax = v.srfSpeed < 90 ? 5 : v.srfSpeed < 140 ? 12 : 30;
            double vsWanted = Clamp((altWanted - v.altitude) * 0.05, -25, vsMax);
            if (agl < 150 && vsWanted < 5) vsWanted = 5; // never settle into the ground
            double e = vsWanted - vs;
            _trim = Clamp(_trim + 0.02 * e * dt, -5, 15);
            double pitch = Clamp(_trim + 0.35 * e, -12, 22);
            double err = Wrap(hdgWanted - hdgNow);
            double bankMax = agl < 300 ? 10 : 30;
            double bank = Clamp(err * 1.2, -bankMax, bankMax);
            SetAttitude(v, hdgNow + Clamp(err, -3, 3), pitch, Math.Abs(bank), err >= 0 ? 1 : -1);
        }

        /// <summary>Nose along a compass heading, pitched up; wings banked toward <paramref name="side"/> (+1 right, -1 left).</summary>
        private void SetAttitude(Vessel v, double heading, double pitchDeg, double bankDeg, int side)
        {
            Vector3d up = v.upAxis;
            double p = pitchDeg * Math.PI / 180;
            _nose = (HeadingVector(v, heading) * Math.Cos(p) + up * Math.Sin(p)).normalized;
            Vector3d top = (up - _nose * Vector3d.Dot(up, _nose)).normalized;
            if (bankDeg > 0.1 && side != 0)
            {
                Vector3d lateral = HeadingVector(v, heading + 90 * side);
                lateral = (lateral - _nose * Vector3d.Dot(lateral, _nose)).normalized;
                double b = bankDeg * Math.PI / 180;
                top = (top * Math.Cos(b) + lateral * Math.Sin(b)).normalized;
            }
            _forward = top * _topSign;
        }

        private double TargetAltitude(Vessel v, double heading)
        {
            return Math.Max(_cruise, TerrainAhead(v, heading) + 700);
        }

        /// <summary>The highest ground under the plane and up to 10 km ahead along a heading, metres (sea level at least).</summary>
        private static double TerrainAhead(Vessel v, double heading)
        {
            double max = Math.Max(0, v.terrainAltitude);
            CelestialBody b = v.mainBody;
            if (b == null) return max;
            Vector3d pos = v.GetWorldPos3D();
            Vector3d dir = HeadingVector(v, heading);
            foreach (double d in new[] { 1500.0, 3000.0, 6000.0, 10000.0 })
            {
                Vector3d p = pos + dir * d;
                double h = b.TerrainAltitude(b.GetLatitude(p), b.GetLongitude(p));
                if (h > max) max = h;
            }
            return max;
        }

        private static double AboveGround(Vessel v)
        {
            double ground = Math.Max(0, v.terrainAltitude);
            return v.altitude - ground;
        }

        private double DistanceHome(Vessel v)
        {
            double r = v.mainBody != null ? v.mainBody.Radius : 600000;
            double la1 = _homeLat * Math.PI / 180, la2 = v.latitude * Math.PI / 180;
            double dla = la2 - la1, dlo = (v.longitude - _homeLon) * Math.PI / 180;
            double a = Math.Sin(dla / 2) * Math.Sin(dla / 2) + Math.Cos(la1) * Math.Cos(la2) * Math.Sin(dlo / 2) * Math.Sin(dlo / 2);
            return 2 * r * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
        }

        private double BearingHome(Vessel v)
        {
            double la1 = v.latitude * Math.PI / 180, la2 = _homeLat * Math.PI / 180;
            double dlo = (_homeLon - v.longitude) * Math.PI / 180;
            double y = Math.Sin(dlo) * Math.Cos(la2);
            double x = Math.Cos(la1) * Math.Sin(la2) - Math.Sin(la1) * Math.Cos(la2) * Math.Cos(dlo);
            return Norm(Math.Atan2(y, x) * 180 / Math.PI);
        }

        /// <summary>Horizontal unit vector along a compass heading (0 north, 90 east).</summary>
        private static Vector3d HeadingVector(Vessel v, double heading)
        {
            double h = heading * Math.PI / 180;
            return ((Vector3d)v.north * Math.Cos(h) + (Vector3d)v.east * Math.Sin(h)).normalized;
        }

        private static double HeadingOf(Vessel v, Vector3d dir)
        {
            return Norm(Math.Atan2(Vector3d.Dot(dir, v.east), Vector3d.Dot(dir, v.north)) * 180 / Math.PI);
        }

        private static double Fuel(Vessel v)
        {
            double total = 0;
            foreach (Part p in v.parts)
                foreach (PartResource r in p.Resources)
                    if (r.resourceName == "LiquidFuel") total += r.amount;
            return total;
        }

        private static double Oxidizer(Vessel v)
        {
            double total = 0;
            foreach (Part p in v.parts)
                foreach (PartResource r in p.Resources)
                    if (r.resourceName == "Oxidizer") total += r.amount;
            return total;
        }

        private static bool Breathes(ModuleEngines e)
        {
            foreach (Propellant p in e.propellants) if (p.name == "IntakeAir") return true;
            return false;
        }

        /// <summary>Dual-mode engines to closed cycle, plain rocket engines started. Returns how many.</summary>
        private static int LightRockets(Vessel v)
        {
            int n = 0;
            foreach (MultiModeEngine m in v.FindPartModulesImplementing<MultiModeEngine>())
            {
                if (!m.runningPrimary) continue;
                BaseEvent e = m.Events["ModeEvent"];
                if (e != null) { e.Invoke(); n++; }
            }
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (Breathes(e) || e.part.FindModuleImplementing<MultiModeEngine>() != null || e.EngineIgnited) continue;
                e.Activate(); n++;
            }
            return n;
        }

        /// <summary>Dual-mode engines back to air-breathing, plain rocket engines shut down. Returns how many.</summary>
        private static int ShutRockets(Vessel v)
        {
            int n = 0;
            foreach (MultiModeEngine m in v.FindPartModulesImplementing<MultiModeEngine>())
            {
                if (m.runningPrimary) continue;
                BaseEvent e = m.Events["ModeEvent"];
                if (e != null) { e.Invoke(); n++; }
            }
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>())
            {
                if (Breathes(e) || e.part.FindModuleImplementing<MultiModeEngine>() != null || !e.EngineIgnited) continue;
                e.Shutdown(); n++;
            }
            return n;
        }

        private static bool AnyEngineLit(Vessel v)
        {
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>()) if (e.EngineIgnited && !e.flameout) return true;
            return false;
        }

        private static double Clamp(double x, double lo, double hi) { return x < lo ? lo : x > hi ? hi : x; }
        private static double Norm(double h) { h %= 360; return h < 0 ? h + 360 : h; }
        private static double Wrap(double d) { d = Norm(d); return d > 180 ? d - 360 : d; }

        public void End(FlightContext ctx) { }
    }
}
