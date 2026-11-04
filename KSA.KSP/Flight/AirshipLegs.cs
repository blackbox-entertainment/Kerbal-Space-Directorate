using System;
using KSA.KSP.Parts;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Cloud cities dock like stations (KSA-91, decided 2026-11-03, T). The first module enters, inflates, climbs to the city
    /// altitude and anchors there as the city; each later one enters, inflates, climbs to that altitude, flies to the city on a
    /// gentle rocket push, and docks with the docking leg. One floating vessel, so power, supplies and crew share with no new rules.
    /// Everything here is unflown at build time: HL Airships Core is driven by reflection (KspAirships).
    /// </summary>
    public sealed class EntryLeg : IFlightLeg
    {
        private enum Phases { Deorbit, Burn, Coast }
        private const double WarpStopAbove = 20000; // metres above the air: no rails warp below this

        private readonly CelestialBody _body;
        private readonly double _handover;
        private Phases _phase;
        private double _phaseStart, _lastLog;

        /// <param name="handoverAltitude">Altitude at which the airship leg takes over, metres above sea level.</param>
        public EntryLeg(CelestialBody body, double handoverAltitude) { _body = body; _handover = handoverAltitude; }

        public string Name { get { return "Entry at " + (_body != null ? _body.bodyName : "the target"); } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        private double PeWanted { get { return KSA.Core.Flight.Sim.DescentRules.DeorbitPeShare * _body.atmosphereDepth; } }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Preparing to deorbit.";
            Vessel v = ctx.Vessel;
            if (v == null || _body == null || !_body.atmosphere) { State = LegState.Failed; Status = "No atmosphere to enter."; return; }
            if (v.mainBody != _body) { State = LegState.Failed; Status = "Not at " + _body.bodyName + "."; return; }
            _phaseStart = ctx.Now;
            if (v.altitude < _handover) { State = LegState.Done; Status = "Already in the air."; return; }
            _phase = v.orbit != null && v.orbit.PeA < PeWanted ? Phases.Coast : Phases.Deorbit;
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
            ctx.Say("Entry program engaged over " + _body.bodyName + ": the air will slow us, then the envelopes take the weight.");
        }

        public string Step(FlightContext ctx)
        {
            string line = null;
            if (State != LegState.Running) return null;
            try
            {
                Vessel v = ctx.Vessel;
                VesselControl c = ctx.Control;
                if (LandingHelpers.IsDead(v)) { State = LegState.Failed; Status = "Vessel lost."; ctx.Say("Vessel lost on entry."); return null; }
                bool warping = LandingHelpers.Warping;
                switch (_phase)
                {
                    case Phases.Deorbit:
                        c.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                        if (warping) { c.StopWarp(); break; }
                        Status = "Aligning retrograde for the deorbit burn.";
                        if (ctx.Now - _phaseStart > 5 && c.AttitudeError(-v.obt_velocity) < 15)
                        {
                            c.EnsureEngine(ref line);
                            c.Throttle = 1f;
                            _phase = Phases.Burn; _phaseStart = ctx.Now;
                            Status = "Deorbit burn.";
                            ctx.Say("Deorbit burn started: aiming a periapsis " + Math.Round(PeWanted / 1000) + " km up, inside the air.");
                        }
                        break;
                    case Phases.Burn:
                        c.AutoStage(ref line);
                        if (v.orbit != null && v.orbit.PeA < PeWanted)
                        {
                            c.Throttle = 0;
                            _phase = Phases.Coast; _phaseStart = ctx.Now;
                            ctx.Say("Deorbit complete; periapsis " + LandingHelpers.Km(v.orbit.PeA) + " km.");
                        }
                        else if (c.NoThrustAvailable() && ctx.Now - _phaseStart > 3)
                        {
                            c.Throttle = 0;
                            if (v.orbit != null && v.orbit.PeA < _body.atmosphereDepth) { _phase = Phases.Coast; ctx.Say("Propellant exhausted; the path still enters the air. Coasting."); }
                            else { State = LegState.Failed; Status = "Out of propellant in orbit."; ctx.Say("Propellant exhausted during the deorbit burn; the airship stays in orbit."); ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted during the deorbit burn."); }
                        }
                        break;
                    case Phases.Coast:
                        {
                            bool inAir = v.altitude < _body.atmosphereDepth;
                            c.Sas(VesselAutopilot.AutopilotMode.Retrograde, inAir);
                            c.Throttle = 0;
                            if (v.altitude < _handover) { State = LegState.Done; Status = "Through the heat; handing over to the envelopes."; ctx.Say("Down to " + LandingHelpers.Km(v.altitude) + " km at " + Math.Round(v.srfSpeed) + " m/s: the envelopes take over."); break; }
                            double floor = _body.atmosphereDepth + WarpStopAbove;
                            if (v.altitude > floor)
                            {
                                if (!warping)
                                {
                                    double rate = Math.Max(50, -v.verticalSpeed);
                                    double seconds = Math.Min(600, Math.Max(30, (v.altitude - floor) / rate));
                                    c.WarpTo(ctx.Now + seconds);
                                }
                                Status = "Coasting to the air, " + LandingHelpers.Km(v.altitude) + " km.";
                            }
                            else
                            {
                                if (warping) c.StopWarp();
                                Status = "Entering: " + LandingHelpers.Km(v.altitude) + " km, " + Math.Round(v.srfSpeed) + " m/s.";
                                if (ctx.Now - _lastLog >= 10) { _lastLog = ctx.Now; KsaCore.Log.Info("Entry: " + Math.Round(v.altitude) + " m, " + Math.Round(v.srfSpeed) + " m/s, down " + Math.Round(-v.verticalSpeed) + " m/s."); }
                            }
                        }
                        break;
                }
            }
            catch (Exception e) { KsaCore.Log.Error("EntryLeg.Step: " + e); }
            return line;
        }

        public void End(FlightContext ctx) { try { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); } catch { } }
    }

    /// <summary>Inflates the envelopes, arrests the fall and climbs to the city altitude; the core anchors there as the city.</summary>
    public sealed class AirshipLeg : IFlightLeg
    {
        private const double ClimbGain = 0.02;     // m/s of climb per metre of height error
        private const double MaxClimb = 8;         // m/s up or down
        private const double HeldWithin = 300;     // metres of the target altitude
        private const double HeldSpeed = 1.5;      // m/s vertical
        private const double HeldSeconds = 20;
        private const double Timeout = 3600;

        private readonly double _altitude;
        private readonly bool _anchor;
        private double _start, _heldSince = -1, _lastLog, _lift = 0.5;
        private bool _altitudeControl = true;

        public AirshipLeg(double altitude, bool anchorAsCity) { _altitude = altitude; _anchor = anchorAsCity; }

        public string Name { get { return _anchor ? "Floating the city" : "Floating up to the city"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            if (v == null) { State = LegState.Failed; Status = "No vessel."; return; }
            if (!KspAirships.HasEnvelope(v)) { State = LegState.Failed; Status = "No airship envelope on this craft."; ctx.Say("No airship envelope on this craft: it cannot float. The design needs HL Airships Core envelopes."); return; }
            _start = ctx.Now;
            ctx.Control.Throttle = 0;
            ctx.Control.ClearInputs();
            ctx.Control.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, true);
            _altitudeControl = KspAirships.HoldVerticalSpeed(v, MaxClimb);
            if (!_altitudeControl) KspAirships.SetLift(v, 1);
            Status = "Inflating the envelopes.";
            ctx.Say("Envelopes inflating; climbing to " + Math.Round(_altitude / 1000, 1) + " km" + (_altitudeControl ? "." : " (no altitude control on these envelopes: lift set by hand)."));
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            try
            {
                Vessel v = ctx.Vessel;
                if (LandingHelpers.IsDead(v)) { State = LegState.Failed; Status = "Vessel lost."; ctx.Say("Vessel lost while floating."); return null; }
                if (LandingHelpers.Warping) ctx.Control.StopWarp();
                ctx.Control.Throttle = 0;
                double error = _altitude - v.altitude;
                double wanted = Math.Max(-MaxClimb, Math.Min(MaxClimb, error * ClimbGain));
                if (v.verticalSpeed < -30) wanted = MaxClimb; // still falling out of the entry: all the lift there is
                if (_altitudeControl) KspAirships.HoldVerticalSpeed(v, wanted);
                else
                {
                    // No altitude control on the module: nudge the lift target toward the climb rate wanted.
                    _lift = Math.Max(0, Math.Min(1, _lift + (wanted - v.verticalSpeed) * 0.002));
                    KspAirships.SetLift(v, _lift);
                }
                bool held = Math.Abs(error) < HeldWithin && Math.Abs(v.verticalSpeed) < HeldSpeed;
                if (!held) _heldSince = -1; else if (_heldSince < 0) _heldSince = ctx.Now;
                double lift = KspAirships.LiftFraction(v);
                Status = (error > 0 ? "Climbing" : "Settling") + " to " + Math.Round(_altitude / 1000, 1) + " km: " + Math.Round(v.altitude / 1000, 1) + " km, " + Math.Round(v.verticalSpeed, 1) + " m/s" + (lift >= 0 ? ", lift " + Math.Round(lift * 100) + "%" : "") + ".";
                if (ctx.Now - _lastLog >= 10) { _lastLog = ctx.Now; KsaCore.Log.Info("Airship: " + Status); }
                if (held && ctx.Now - _heldSince >= HeldSeconds)
                {
                    if (_anchor)
                    {
                        bool anchored = KspAirships.Anchor(v, true);
                        ctx.Say("Holding at " + Math.Round(v.altitude / 1000, 1) + " km over " + v.mainBody.bodyName + (anchored ? "; anchored: this is the city." : "; the anchor could not be set, the city may drift."), "Float:" + v.mainBody.bodyName);
                    }
                    else ctx.Say("Holding at " + Math.Round(v.altitude / 1000, 1) + " km over " + v.mainBody.bodyName + ".", "Float:" + v.mainBody.bodyName);
                    Status = "Holding at " + Math.Round(v.altitude / 1000, 1) + " km.";
                    State = LegState.Done;
                }
                else if (ctx.Now - _start > Timeout)
                {
                    State = LegState.Failed; Status = "Could not reach the city altitude.";
                    ctx.Say("An hour of floating and still " + Math.Round(Math.Abs(error) / 1000, 1) + " km from the city altitude: not enough lift. Director, your call.");
                }
            }
            catch (Exception e) { KsaCore.Log.Error("AirshipLeg.Step: " + e); }
            return null;
        }

        public void End(FlightContext ctx) { }
    }

    /// <summary>Flies the module to the floating city on a gentle rocket push at the city's altitude, to a 200 m hold for the docking leg.</summary>
    public sealed class AirRendezvousLeg : IFlightLeg
    {
        private const double MaxApproach = 12;   // m/s closing at most
        private const double HoldRange = 200;    // metres: hand over to the docking leg here
        private const double Timeout = 2400;
        private const double ClimbGain = 0.02, MaxClimb = 5;

        private readonly double _altitude;
        private Vessel _city;
        private double _start, _lastLog, _lift = 0.5;
        private bool _altitudeControl, _retro;

        public AirRendezvousLeg(double altitude) { _altitude = altitude; }

        public string Name { get { return "Flying to the city"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            _city = StationOps.ResolveTarget(ctx);
            if (_city == null) { State = LegState.Failed; Status = "The city was not found in the air."; ctx.Say("No cloud city found over " + (v != null && v.mainBody != null ? v.mainBody.bodyName : "the target") + ": nothing to dock with."); return; }
            _start = ctx.Now;
            string line = null;
            ctx.Control.EnsureEngine(ref line);
            _altitudeControl = KspAirships.HoldVerticalSpeed(v, 0);
            ctx.Control.PointAlong(() => Heading(v));
            Status = "Turning toward " + StationOps.NameOf(_city) + ".";
            ctx.Say("Flying to " + StationOps.NameOf(_city) + ", " + Math.Round(StationOps.Distance(v, _city) / 1000, 1) + " km away at " + Math.Round(_city.altitude / 1000, 1) + " km.");
        }

        private Vector3d Heading(Vessel v)
        {
            if (_city == null) return v.srf_velocity.sqrMagnitude > 1 ? v.srf_velocity.normalized : (Vector3d)v.north;
            Vector3d up = v.upAxis;
            Vector3d to = _city.GetWorldPos3D() - v.GetWorldPos3D();
            Vector3d flat = to - up * Vector3d.Dot(to, up);
            if (_retro)
            {
                Vector3d vel = v.srf_velocity - up * Vector3d.Dot(v.srf_velocity, up);
                if (vel.sqrMagnitude > 0.25) return -vel.normalized;
            }
            return flat.sqrMagnitude > 1 ? flat.normalized : (Vector3d)v.north;
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            try
            {
                Vessel v = ctx.Vessel;
                VesselControl c = ctx.Control;
                if (LandingHelpers.IsDead(v)) { State = LegState.Failed; Status = "Vessel lost."; return null; }
                if (_city == null || _city.state == Vessel.State.DEAD) { State = LegState.Failed; Status = "The city is gone."; ctx.Say("The cloud city is gone."); return null; }
                if (LandingHelpers.Warping) c.StopWarp();
                // Height: the city's altitude, through the envelopes.
                double target = _city.altitude > 1000 ? _city.altitude : _altitude;
                double wanted = Math.Max(-MaxClimb, Math.Min(MaxClimb, (target - v.altitude) * ClimbGain));
                if (_altitudeControl) KspAirships.HoldVerticalSpeed(v, wanted);
                else { _lift = Math.Max(0, Math.Min(1, _lift + (wanted - v.verticalSpeed) * 0.002)); KspAirships.SetLift(v, _lift); }

                Vector3d up = v.upAxis;
                Vector3d to = _city.GetWorldPos3D() - v.GetWorldPos3D();
                Vector3d flat = to - up * Vector3d.Dot(to, up);
                double dist = flat.magnitude;
                Vector3d vel = v.srf_velocity - up * Vector3d.Dot(v.srf_velocity, up);
                double closing = dist > 1 ? Vector3d.Dot(vel, flat / dist) : 0;
                double want = Math.Min(MaxApproach, Math.Max(1, dist / 40));
                if (dist < HoldRange && vel.magnitude < 2)
                {
                    c.Throttle = 0;
                    State = LegState.Done;
                    Status = "At the city.";
                    ctx.Say("Alongside " + StationOps.NameOf(_city) + " at " + Math.Round(dist) + " m; the docking leg takes it from here.");
                    return null;
                }
                // Too fast: turn round and push against it; else push toward the city when slow, coast when fast enough.
                bool tooFast = closing > want + 3 || (dist < HoldRange && vel.magnitude >= 2);
                if (tooFast != _retro) { _retro = tooFast; c.PointAlong(() => Heading(v)); }
                bool aligned = c.AttitudeError(Heading(v)) < 10;
                if (tooFast) c.Throttle = aligned ? 0.2f : 0f;
                else if (closing < want - 1) c.Throttle = aligned ? 0.15f : 0f;
                else c.Throttle = 0;
                if (c.Throttle > 0 && c.NoThrustAvailable() && ctx.Now - _start > 10) { State = LegState.Failed; Status = "No engine to close with."; ctx.Say("No thrust to fly to the city: the module needs an engine for the approach."); return null; }
                Status = (tooFast ? "Slowing: " : "Closing: ") + Math.Round(dist) + " m, " + Math.Round(closing, 1) + " m/s.";
                if (ctx.Now - _lastLog >= 10) { _lastLog = ctx.Now; KsaCore.Log.Info("Air rendezvous: " + Status + " Height " + Math.Round(v.altitude) + " m, wanted " + Math.Round(target) + "."); }
                if (ctx.Now - _start > Timeout) { State = LegState.Failed; Status = "Could not reach the city."; ctx.Say("Forty minutes of flying and still " + Math.Round(dist) + " m from the city. Director, your call."); }
            }
            catch (Exception e) { KsaCore.Log.Error("AirRendezvousLeg.Step: " + e); }
            return null;
        }

        public void End(FlightContext ctx) { try { ctx.Control.Throttle = 0; } catch { } }
    }
}
