using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>Shared arithmetic for the landing, surface and ascent legs (KSA-52).</summary>
    internal static class LandingHelpers
    {
        /// <summary>Altitude above the ground under the vessel, clamped to [0, sea-level altitude]. Radar first, then terrain altitude.</summary>
        public static double AboveTerrain(Vessel v)
        {
            if (v == null) return 0;
            double alt = v.altitude;
            double h = v.heightFromTerrain >= 0 ? v.heightFromTerrain : alt - Math.Max(0, v.terrainAltitude);
            if (double.IsNaN(h)) h = alt;
            if (h < 0) h = 0;
            if (h > alt && alt > 0) h = alt;
            return h;
        }

        public static Vec3 ToVec3(Vector3d d) { return new Vec3(d.x, d.y, d.z); }
        public static Vec3 ToVec3(Vector3 d) { return new Vec3(d.x, d.y, d.z); }

        /// <summary>Local slope under the vessel as a tangent (0 = flat), from the terrain normal and the up axis.</summary>
        public static double LocalSlope(Vessel v)
        {
            if (v == null) return 0;
            Vec3 n = ToVec3(v.terrainNormal);
            Vec3 up = ToVec3(v.upAxis);
            if (n.Length < 1e-6 || up.Length < 1e-6) return 0;
            return DescentMath.SlopeFromNormal(n, up);
        }

        /// <summary>Eight-point compass name for a north/east offset in metres.</summary>
        public static string Compass(double north, double east)
        {
            if (Math.Abs(north) < 1 && Math.Abs(east) < 1) return "here";
            double deg = Math.Atan2(east, north) * 180.0 / Math.PI;
            if (deg < 0) deg += 360;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            int idx = (int)Math.Round(deg / 45.0) % 8;
            return names[idx];
        }

        public static string Pct(double slope) { return Math.Round(slope * 100) + "%"; }

        public static bool IsDown(Vessel v) { return v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED || v.situation == Vessel.Situations.PRELAUNCH; }

        public static bool IsDead(Vessel v) { return v == null || v.state == Vessel.State.DEAD; }

        public static bool Warping { get { return TimeWarp.CurrentRate > 1f; } }

        public static string Km(double metres) { return Math.Round(metres / 1000.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    /// <summary>
    /// Powered landing from a low orbit on an airless body (Mun, Minmus) or a thin-atmosphere one (Duna):
    /// deorbit burn, coast with warp, a terrain survey that picks the flattest nearby cell, a retrograde braking burn
    /// with a descent-speed schedule, gear and lights, touchdown. Parachutes are used when the body has air.
    /// </summary>
    public sealed class LandingLeg : IFlightLeg
    {
        private enum Phases { Deorbit, DeorbitBurn, Coast, Braking, Landed, WaveOff }

        private const double SurveySpacing = 200;   // metres between grid cells
        private const int SurveySize = 5;           // 5x5 grid
        private const double SurveyBelow = 20000;   // start the survey below this height above terrain

        private readonly double _maxSlope;
        private Phases _phase;
        private double _phaseStart;
        private bool _surveyed;
        private bool _gearDown;
        private bool _holdingUp;        // switched from retrograde to the up-hold for the last metres
        private bool _fuelWarned;
        private double _burnMargin = 0.15;
        private double _lastSpeed;      // surface speed on the last step before touchdown
        private double _surveySlope = -1;
        private bool _hasAir;
        private bool _waveOffUsed;
        private bool _askedCrew;
        private bool _waveOffRequested;

        public LandingLeg(double maxSlope) { _maxSlope = maxSlope > 0 ? maxSlope : 0.3; }

        public string Name { get { return "Landing"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Preparing to deorbit.";
            try
            {
                Vessel v = ctx.Vessel;
                if (v == null || v.mainBody == null) { State = LegState.Failed; Status = "No vessel."; return; }
                _hasAir = v.mainBody.atmosphere;
                _phaseStart = ctx.Now;
                if (LandingHelpers.IsDown(v)) { _phase = Phases.Landed; State = LegState.Done; Status = "Already on the surface."; return; }

                double above = LandingHelpers.AboveTerrain(v);
                bool impactPredicted = v.orbit != null && v.orbit.PeA < DeorbitTarget(v);
                if (v.situation == Vessel.Situations.ORBITING && !impactPredicted)
                {
                    _phase = Phases.Deorbit;
                    ctx.Control.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                }
                else if (above < 5000 && v.verticalSpeed < -5)
                {
                    _phase = Phases.Braking;
                    ctx.Control.Sas(VesselAutopilot.AutopilotMode.Retrograde, true);
                    Status = "Braking burn.";
                }
                else
                {
                    _phase = Phases.Coast;
                    ctx.Control.Sas(VesselAutopilot.AutopilotMode.Retrograde, !_hasAir || v.altitude < v.mainBody.atmosphereDepth);
                    Status = "Coasting to the braking point.";
                }
                ctx.Say("Landing program engaged over " + v.mainBody.bodyName + " (" + (_hasAir ? "thin atmosphere" : "airless") + ").");
            }
            catch (Exception e) { KsaCore.Log.Error("LandingLeg.Begin: " + e); }
        }

        private double DeorbitTarget(Vessel v) { return _hasAir ? 8000 : -(0.1 * v.mainBody.Radius); }

        public string Step(FlightContext ctx)
        {
            string line = null;
            try
            {
                Vessel v = ctx.Vessel;
                VesselControl c = ctx.Control;
                if (LandingHelpers.IsDead(v)) { State = LegState.Failed; Status = "Vessel lost."; ctx.Say("Vessel lost during landing."); return null; }
                bool warping = LandingHelpers.Warping;
                double above = LandingHelpers.AboveTerrain(v);
                double g = c.Gravity();
                double accel = c.MaxAcceleration();

                if (_phase != Phases.Landed && LandingHelpers.IsDown(v)) { Touchdown(ctx, v); return null; }

                switch (_phase)
                {
                    case Phases.Deorbit:
                        c.Sas(VesselAutopilot.AutopilotMode.Retrograde, false);
                        Status = "Aligning retrograde for deorbit.";
                        if (warping) { c.StopWarp(); break; }
                        if (ctx.Now - _phaseStart > 5)
                        {
                            c.EnsureEngine(ref line);
                            c.Throttle = 1f;
                            _phase = Phases.DeorbitBurn; _phaseStart = ctx.Now;
                            Status = "Deorbit burn.";
                            ctx.Say("Deorbit burn started.");
                        }
                        break;

                    case Phases.DeorbitBurn:
                        if (warping) { c.StopWarp(); break; }
                        c.AutoStage(ref line);
                        if (v.orbit != null && v.orbit.PeA < DeorbitTarget(v))
                        {
                            c.Throttle = 0;
                            _phase = Phases.Coast; _phaseStart = ctx.Now;
                            Status = "Coasting to the braking point.";
                            ctx.Say("Deorbit complete; periapsis " + LandingHelpers.Km(v.orbit.PeA) + " km, impact predicted.");
                        }
                        else if (c.NoThrustAvailable())
                        {
                            c.Throttle = 0;
                            if (v.orbit != null && v.orbit.PeA < (_hasAir ? v.mainBody.atmosphereDepth : 0))
                            {
                                _phase = Phases.Coast; _phaseStart = ctx.Now;
                                ctx.Say("Propellant exhausted during deorbit; trajectory still comes down. Coasting.");
                            }
                            else
                            {
                                State = LegState.Failed; Status = "Out of propellant in orbit.";
                                ctx.Say("Propellant exhausted during the deorbit burn; the lander stays in orbit.");
                            }
                        }
                        break;

                    case Phases.Coast:
                        if (_waveOffRequested && !_waveOffUsed && above > 2500)
                        {
                            _waveOffUsed = true; _waveOffRequested = false;
                            if (warping) c.StopWarp();
                            c.EnsureEngine(ref line);
                            c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                            _phase = Phases.WaveOff; _phaseStart = ctx.Now;
                            Status = "Waving off: climbing back to orbit.";
                            ctx.Say("Wave-off: climbing back to orbit for another pass.");
                            break;
                        }
                        Coast(ctx, v, c, above, g, accel, warping, ref line);
                        break;

                    case Phases.WaveOff:
                        {
                            if (warping) { c.StopWarp(); break; }
                            double safePe = (_hasAir ? v.mainBody.atmosphereDepth : 0) + 6000;
                            if (ctx.Now - _phaseStart < 3) { c.Sas(VesselAutopilot.AutopilotMode.Prograde, false); break; }
                            c.AutoStage(ref line);
                            if (v.orbit != null && v.orbit.PeA > safePe)
                            {
                                c.Throttle = 0;
                                _surveyed = false; _surveySlope = -1; _askedCrew = false; _burnMargin = 0.15;
                                _phase = Phases.Deorbit; _phaseStart = ctx.Now + (v.orbit.period > 0 ? v.orbit.period * 0.45 : 600);
                                ctx.Say("Back in orbit (" + LandingHelpers.Km(v.orbit.ApA) + " x " + LandingHelpers.Km(v.orbit.PeA) + " km); next pass in half an orbit.");
                                c.WarpTo(_phaseStart);
                            }
                            else if (c.NoThrustAvailable() && ctx.Now - _phaseStart > 6)
                            {
                                c.Throttle = 0;
                                ctx.Say("Not enough propellant to wave off; continuing the descent.");
                                _phase = Phases.Coast; _phaseStart = ctx.Now;
                                c.Sas(VesselAutopilot.AutopilotMode.Retrograde, !_hasAir);
                            }
                            else c.Throttle = 1f;
                        }
                        break;

                    case Phases.Braking:
                        Braking(ctx, v, c, above, g, accel, warping, ref line);
                        break;

                    case Phases.Landed:
                        c.Throttle = 0;
                        State = LegState.Done;
                        break;
                }
                if (_phase != Phases.Landed) _lastSpeed = v.srfSpeed;
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("LandingLeg.Step: " + e);
            }
            return line;
        }

        private void Coast(FlightContext ctx, Vessel v, VesselControl c, double above, double g, double accel, bool warping, ref string line)
        {
            bool inAir = _hasAir && v.altitude < v.mainBody.atmosphereDepth;
            c.Sas(VesselAutopilot.AutopilotMode.Retrograde, !_hasAir || inAir);
            if (inAir && !warping) c.DeployChutes(ref line);

            // Light the engine while still high so the braking estimate uses real thrust (throttle stays zero).
            if (!warping && above < SurveyBelow && accel <= 0) c.EnsureEngine(ref line);
            double estAccel = accel > 0 ? accel : 2.0 * Math.Max(g, 0.1);   // assume TWR 2 until the engine is lit
            double safe = DescentMath.SafeBurnAltitude(v.srfSpeed, estAccel, g, _burnMargin * (ctx.Crew != null ? ctx.Crew.MarginFactor : 1.0));
            if (double.IsInfinity(safe)) safe = above; // thrust cannot beat gravity: start braking now and hope

            if (!_surveyed && above < SurveyBelow && v.verticalSpeed < 0) Survey(ctx, v);

            double descentRate = -v.verticalSpeed;
            bool brakeNow = above <= safe;
            if (_hasAir && inAir && above < 3000 && descentRate > DescentMath.TargetDescentSpeed(above) * 1.1) brakeNow = true;
            if (brakeNow)
            {
                if (warping) { c.StopWarp(); return; }
                _phase = Phases.Braking; _phaseStart = ctx.Now;
                c.Sas(VesselAutopilot.AutopilotMode.Retrograde, true);
                c.EnsureEngine(ref line);
                c.Throttle = 1f;
                Status = "Braking burn.";
                ctx.Say("Braking burn at " + Math.Round(above) + " m above terrain, " + Math.Round(v.srfSpeed) + " m/s.");
                return;
            }

            // Warp while there is plenty of room: stop well before the burn point.
            double stopAt = Math.Max(4 * safe, 2000);
            if (above > stopAt && (!_hasAir || v.altitude > v.mainBody.atmosphereDepth || descentRate < 100))
            {
                double rate = Math.Max(descentRate, Math.Sqrt(2 * g * Math.Max(1, above - stopAt)) * 0.5);
                double seconds = (above - stopAt) / Math.Max(rate, 1);
                seconds = Math.Min(seconds * 0.5, 600);
                if (seconds > 10 && !warping) c.WarpTo(ctx.Now + seconds);
                Status = "Coasting, " + Math.Round(above / 1000.0, 1) + " km above terrain.";
            }
            else
            {
                if (warping) c.StopWarp();
                Status = "Coasting, braking in " + Math.Round(Math.Max(0, above - safe)) + " m.";
            }
        }

        /// <summary>Samples a 5x5 grid around the predicted ground-track point and picks the flattest cell.</summary>
        private void Survey(FlightContext ctx, Vessel v)
        {
            _surveyed = true;
            try
            {
                CelestialBody body = v.mainBody;
                double above = LandingHelpers.AboveTerrain(v);
                double descentRate = Math.Max(5, -v.verticalSpeed);
                double tImpact = above / descentRate;
                double north = Vector3d.Dot(v.srf_velocity, v.north);
                double east = Vector3d.Dot(v.srf_velocity, v.east);
                double hSpeed = Math.Sqrt(north * north + east * east);
                // Braking kills the horizontal speed on the way down, so roughly half the no-burn ground track.
                double downrange = hSpeed * tImpact * 0.5;
                double dirN = hSpeed > 0.5 ? north / hSpeed : 0;
                double dirE = hSpeed > 0.5 ? east / hSpeed : 0;
                double cNorth = dirN * downrange, cEast = dirE * downrange;

                double latRad = v.latitude * Math.PI / 180.0;
                double mPerDegLat = body.Radius * Math.PI / 180.0;
                double mPerDegLon = Math.Max(1, mPerDegLat * Math.Cos(latRad));

                int n = SurveySize, half = n / 2;
                double[,] h = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        double offN = cNorth + (i - half) * SurveySpacing;
                        double offE = cEast + (j - half) * SurveySpacing;
                        double lat = v.latitude + offN / mPerDegLat;
                        double lon = v.longitude + offE / mPerDegLon;
                        h[i, j] = body.TerrainAltitude(lat, lon, true);
                    }
                double[] slopes = new double[n * n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        double worst = 0;
                        if (i > 0) worst = Math.Max(worst, Math.Abs(h[i, j] - h[i - 1, j]) / SurveySpacing);
                        if (i < n - 1) worst = Math.Max(worst, Math.Abs(h[i, j] - h[i + 1, j]) / SurveySpacing);
                        if (j > 0) worst = Math.Max(worst, Math.Abs(h[i, j] - h[i, j - 1]) / SurveySpacing);
                        if (j < n - 1) worst = Math.Max(worst, Math.Abs(h[i, j] - h[i, j + 1]) / SurveySpacing);
                        slopes[i * n + j] = worst;
                    }
                int best = DescentMath.Flattest(slopes);
                if (best < 0) return;
                int bi = best / n, bj = best % n;
                double offNorth = (bi - half) * SurveySpacing;
                double offEast = (bj - half) * SurveySpacing;
                double dist = Math.Sqrt(offNorth * offNorth + offEast * offEast);
                _surveySlope = slopes[best];
                double centreSlope = slopes[half * n + half];
                string dir = LandingHelpers.Compass(offNorth, offEast);
                string msg = "Site survey: flattest cell " + Math.Round(dist) + " m " + dir + " of the predicted touchdown, slope " + LandingHelpers.Pct(_surveySlope)
                    + " (predicted point " + LandingHelpers.Pct(centreSlope) + ").";
                ctx.Say(msg);
                if (_surveySlope > _maxSlope)
                {
                    ctx.Say("Warning: no cell within " + Math.Round(half * SurveySpacing) + " m is under the " + LandingHelpers.Pct(_maxSlope) + " slope limit.");
                    AskCrewAboutSite(ctx, _surveySlope);
                }

                // Stretch goal: bias the burn timing toward the chosen cell. Downrange cell: burn a little later; uprange: a little earlier.
                double along = offNorth * dirN + offEast * dirE;
                if (hSpeed > 0.5 && dist > 1)
                {
                    if (along > 100) _burnMargin = 0.10;
                    else if (along < -100) _burnMargin = 0.20;
                }
                ctx.LandingNote = "Survey: flattest cell " + Math.Round(dist) + " m " + dir + ", slope " + LandingHelpers.Pct(_surveySlope)
                    + (_surveySlope > _maxSlope ? " (over the " + LandingHelpers.Pct(_maxSlope) + " limit)" : "") + ".";
            }
            catch (Exception e)
            {
                KsaCore.Log.Warn("LandingLeg survey failed: " + e.Message);
                ctx.Say("Site survey unavailable; landing on the predicted point.");
            }
        }

        /// <summary>The crew's call on a rough site (decision 21): commit, or wave off once and come around again.</summary>
        private void AskCrewAboutSite(FlightContext ctx, double slope)
        {
            if (_askedCrew || _waveOffUsed || ctx.AskCrew == null) return;
            _askedCrew = true;
            double risk = Math.Min(1.0, slope / Math.Max(0.05, _maxSlope) * 0.6);
            var req = new KSA.Core.Flight.DecisionRequest
            {
                Kind = KSA.Core.Flight.DecisionKind.LandingSite,
                Situation = "Survey: " + LandingHelpers.Pct(slope) + " slope at the best nearby cell, limit " + LandingHelpers.Pct(_maxSlope) + ".",
                Risk = risk
            };
            try
            {
                ctx.AskCrew(req, d =>
                {
                    if (State != LegState.Running) return;
                    if (d.Choice != KSA.Core.Flight.DecisionChoice.Proceed && _phase == Phases.Coast) _waveOffRequested = true;
                });
            }
            catch (Exception e) { KsaCore.Log.Warn("Crew decision failed: " + e.Message); }
        }

        private void Braking(FlightContext ctx, Vessel v, VesselControl c, double above, double g, double accel, bool warping, ref string line)
        {
            if (warping) { c.StopWarp(); return; }
            bool inAir = _hasAir && v.altitude < v.mainBody.atmosphereDepth;
            if (inAir) c.DeployChutes(ref line);
            c.AutoStage(ref line);
            if (accel <= 0) c.EnsureEngine(ref line);

            double hSpeed = v.horizontalSrfSpeed;
            double descentRate = -v.verticalSpeed;
            double target = DescentMath.TargetDescentSpeed(above);

            if (!_gearDown && above < 500) { _gearDown = true; c.Gear(true); c.Lights(true); ctx.Say("Gear down, lights on at " + Math.Round(above) + " m."); }

            // Attitude: retrograde while moving, then hold "up" (radial out) so the lander does not chase a flipping marker.
            if (!_holdingUp && (v.srfSpeed < 1.0 || (above < 40 && hSpeed < 1.5)))
            {
                _holdingUp = true;
                c.Sas(VesselAutopilot.AutopilotMode.RadialOut, true);
            }
            else if (_holdingUp && hSpeed > 5 && above > 100)
            {
                _holdingUp = false;
                c.Sas(VesselAutopilot.AutopilotMode.Retrograde, true);
            }
            else if (!_holdingUp) c.Sas(VesselAutopilot.AutopilotMode.Retrograde, true);

            if (c.NoThrustAvailable())
            {
                if (!_fuelWarned)
                {
                    _fuelWarned = true;
                    if (above > 500)
                    {
                        ctx.Say("Propellant exhausted at " + Math.Round(above) + " m above terrain; holding retrograde and hoping.");
                        Status = "Out of propellant.";
                        State = LegState.Failed;
                        return;
                    }
                    ctx.Say("Propellant exhausted at " + Math.Round(above) + " m; riding it down.");
                }
                c.Throttle = 0;
                Status = "Out of propellant, " + Math.Round(above) + " m.";
                return;
            }

            float throttle;
            if (accel <= 0) throttle = 1f;
            else
            {
                double hover = Math.Min(1, g / accel);
                bool behind = DescentMath.SafeBurnAltitude(v.srfSpeed, accel, g, 0.0) >= above;
                if (behind && above > 20) throttle = 1f;
                else
                {
                    // Proportional: desired acceleration = g + 0.5 * (descentRate - target), expressed as a throttle fraction.
                    double error = descentRate - target;
                    double t = hover + 0.5 * error / accel;
                    // Still carrying sideways speed: lean on the retrograde burn to kill it.
                    if (hSpeed > 3 && !_holdingUp) t = Math.Max(t, Math.Min(1, hover + hSpeed / 20.0));
                    throttle = (float)Math.Max(0, Math.Min(1, t));
                }
            }
            c.Throttle = throttle;
            Status = "Descending " + Math.Round(descentRate, 1) + " m/s (target " + Math.Round(target, 1) + "), " + Math.Round(above) + " m.";
        }

        private void Touchdown(FlightContext ctx, Vessel v)
        {
            VesselControl c = ctx.Control;
            c.Throttle = 0;
            c.ClearInputs();
            _phase = Phases.Landed;
            string body = v.mainBody != null ? v.mainBody.bodyName : "the surface";
            double slope = LandingHelpers.LocalSlope(v);
            string speed = Math.Round(_lastSpeed, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ctx.Say("Touchdown on " + body + " at " + speed + " m/s.", "Land:" + body);
            ctx.Say("Local slope at the site " + LandingHelpers.Pct(slope) + (slope > _maxSlope ? " (over the " + LandingHelpers.Pct(_maxSlope) + " limit)" : "") + ".");
            if (_lastSpeed > 12) ctx.Say("Hard landing: " + speed + " m/s. Inspect the lander.");
            string note = "Touchdown on " + body + " at " + speed + " m/s, local slope " + LandingHelpers.Pct(slope) + ".";
            ctx.LandingNote = string.IsNullOrEmpty(ctx.LandingNote) ? note : ctx.LandingNote + " " + note;
            Status = "Landed on " + body + ".";
            State = LegState.Done;
        }

        public void End(FlightContext ctx)
        {
            try { ctx.Control.Throttle = 0; ctx.Control.ClearInputs(); ctx.Control.StopWarp(); }
            catch (Exception e) { KsaCore.Log.Error("LandingLeg.End: " + e); }
        }
    }

    /// <summary>
    /// A short surface excursion: the first crew member steps out, stands on the surface for a while, and climbs back in.
    /// The flight director only steps the program while the lander is the active vessel, so this leg keeps a small
    /// watcher component alive to run the clock and the boarding while the kerbal is the active vessel.
    /// </summary>
    public sealed class SurfaceOpsLeg : IFlightLeg
    {
        private enum Phases { Spawn, Outside, Boarding, Finished }

        private readonly double _seconds;
        private readonly bool _skip;
        private readonly bool _allowOrbit;
        private Phases _phase;
        private double _evaStart;
        private double _nextBoardTry;
        private int _boardTries;
        private ProtoCrewMember _crew;
        private Part _part;
        private KerbalEVA _eva;
        private FlightContext _ctx;
        private EvaWatcher _watcher;
        private bool _spawnTried;

        public SurfaceOpsLeg(double seconds) : this(seconds, false) { }

        public SurfaceOpsLeg(double seconds, bool skip) : this(seconds, skip, false) { }

        /// <param name="allowOrbit">Orbital spacewalk: the kerbal steps out of an orbiting craft instead of onto a surface.</param>
        public SurfaceOpsLeg(double seconds, bool skip, bool allowOrbit)
        {
            _seconds = seconds > 0 ? seconds : 30;
            _skip = skip;
            _allowOrbit = allowOrbit;
        }

        public string Name { get { return "Surface operations"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _ctx = ctx;
            Status = "Preparing for EVA.";
            try
            {
                Vessel v = ctx.Vessel;
                if (_skip) { Status = "EVA skipped."; State = LegState.Done; return; }
                if (v == null) { State = LegState.Done; return; }
                ctx.Control.Throttle = 0;
                bool down = v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.PRELAUNCH;
                bool orbit = v.situation == Vessel.Situations.ORBITING;
                if (!down && !(_allowOrbit && orbit))
                {
                    ctx.Say("Not on the ground; surface operations skipped.");
                    State = LegState.Done; return;
                }
                _phase = Phases.Spawn;
                _evaStart = ctx.Now;
            }
            catch (Exception e) { KsaCore.Log.Error("SurfaceOpsLeg.Begin: " + e); State = LegState.Done; }
        }

        public string Step(FlightContext ctx)
        {
            try { Tick(); }
            catch (Exception e) { KsaCore.Log.Error("SurfaceOpsLeg.Step: " + e); Finish(); }
            return null;
        }

        /// <summary>Advances the EVA state machine. Called from Step while the lander is active and from the watcher otherwise.</summary>
        internal void Tick()
        {
            if (State != LegState.Running || _ctx == null) return;
            Vessel v = _ctx.Vessel;
            double now = _ctx.Now;
            switch (_phase)
            {
                case Phases.Spawn:
                    if (_spawnTried) { Finish(); return; }
                    _spawnTried = true;
                    if (LandingHelpers.Warping) { TimeWarp.SetRate(0, true); }
                    if (!TrySpawn(v)) { Finish(); return; }
                    _phase = Phases.Outside;
                    _evaStart = now;
                    Status = _crew.name + " on EVA.";
                    EnsureWatcher();
                    break;

                case Phases.Outside:
                    {
                        Vessel evaVessel = EvaVessel();
                        if (evaVessel == null)
                        {
                            // Already back aboard (player boarded them) or lost.
                            if (CrewAboard()) { _ctx.Say(_crew.name + " is back aboard."); Finish(); }
                            else { _ctx.Say(_crew.name + " is no longer on EVA and not aboard; crew status unknown."); Finish(); }
                            return;
                        }
                        double left = _evaStart + _seconds - now;
                        Status = _crew.name + " on the surface, " + Math.Round(Math.Max(0, left)) + " s to go.";
                        if (left <= 0)
                        {
                            _phase = Phases.Boarding;
                            _nextBoardTry = now;
                            _boardTries = 0;
                            Status = _crew.name + " returning to the lander.";
                        }
                    }
                    break;

                case Phases.Boarding:
                    if (CrewAboard()) { _ctx.Say(_crew.name + " back aboard after " + Math.Round(now - _evaStart) + " s on the surface."); Finish(); return; }
                    if (now < _nextBoardTry) break;
                    _nextBoardTry = now + 10;
                    _boardTries++;
                    if (!TryBoard() && _boardTries >= 3)
                    {
                        _ctx.Say("Warning: " + _crew.name + " could not board the lander after 3 tries and is still outside. Bring them in by hand before launch.");
                        Finish();
                        return;
                    }
                    break;

                case Phases.Finished:
                    Finish();
                    break;
            }
        }

        private bool TrySpawn(Vessel v)
        {
            try
            {
                List<ProtoCrewMember> crew = v.GetVesselCrew();
                if (crew == null || crew.Count == 0) { _ctx.Say("No crew aboard; nobody to step outside."); return false; }
                _crew = crew[0];
                _part = null;
                foreach (Part p in v.parts)
                    if (p != null && p.protoModuleCrew != null && p.protoModuleCrew.Contains(_crew)) { _part = p; break; }
                if (_part == null) { _ctx.Say("EVA not possible from this craft; crew stayed aboard."); return false; }
                if (_part.airlock == null) { _ctx.Say("EVA not possible from this craft (no airlock on " + _part.partInfo.title + "); crew stayed aboard."); return false; }
                if (FlightEVA.fetch == null) { _ctx.Say("EVA not possible from this craft; crew stayed aboard."); return false; }
                _eva = FlightEVA.fetch.spawnEVA(_crew, _part, _part.airlock, true);
                if (_eva == null) { _ctx.Say("EVA not possible from this craft; crew stayed aboard."); return false; }
                string body = v.mainBody != null ? v.mainBody.bodyName : "the surface";
                _ctx.Say(_crew.name + " stepped onto the surface of " + body + ".", "EVA:" + body);
                return true;
            }
            catch (Exception e)
            {
                KsaCore.Log.Warn("SurfaceOpsLeg spawnEVA failed: " + e.Message);
                _ctx.Say("EVA not possible from this craft; crew stayed aboard.");
                return false;
            }
        }

        private Vessel EvaVessel()
        {
            try
            {
                if (_eva != null && _eva.vessel != null && _eva.vessel.state != Vessel.State.DEAD) return _eva.vessel;
                Vessel active = FlightGlobals.ActiveVessel;
                if (active != null && active.isEVA && active.evaController != null)
                {
                    List<ProtoCrewMember> crew = active.GetVesselCrew();
                    if (crew != null && crew.Count > 0 && crew[0] == _crew) { _eva = active.evaController; return active; }
                }
                foreach (Vessel other in FlightGlobals.Vessels)
                {
                    if (other == null || !other.isEVA || other.evaController == null) continue;
                    List<ProtoCrewMember> crew = other.GetVesselCrew();
                    if (crew != null && crew.Count > 0 && crew[0] == _crew) { _eva = other.evaController; return other; }
                }
            }
            catch (Exception e) { KsaCore.Log.Warn("SurfaceOpsLeg EvaVessel: " + e.Message); }
            return null;
        }

        private bool CrewAboard()
        {
            try
            {
                if (_part != null && _part.protoModuleCrew != null && _part.protoModuleCrew.Contains(_crew)) return true;
                Vessel v = _ctx.Vessel;
                if (v == null) return false;
                List<ProtoCrewMember> crew = v.GetVesselCrew();
                return crew != null && crew.Contains(_crew);
            }
            catch { return false; }
        }

        private bool TryBoard()
        {
            try
            {
                Vessel evaVessel = EvaVessel();
                if (evaVessel == null || _eva == null)
                {
                    if (CrewAboard()) return true;
                    _ctx.Say(_crew.name + " cannot be found outside; boarding abandoned.");
                    _boardTries = 3;
                    return false;
                }
                if (_part == null || _part.vessel == null) { _ctx.Say("The lander part " + _crew.name + " left from no longer exists."); _boardTries = 3; return false; }
                if (LandingHelpers.Warping) TimeWarp.SetRate(0, true);
                _eva.BoardPart(_part);
                bool aboard = CrewAboard();
                if (!aboard) _ctx.Say(_crew.name + " could not board (try " + _boardTries + " of 3).");
                return aboard;
            }
            catch (Exception e)
            {
                KsaCore.Log.Warn("SurfaceOpsLeg BoardPart failed: " + e.Message);
                _ctx.Say(_crew.name + " could not board (try " + _boardTries + " of 3).");
                return false;
            }
        }

        private void Finish()
        {
            _phase = Phases.Finished;
            if (State == LegState.Running) State = LegState.Done;
            if (string.IsNullOrEmpty(Status) || Status.EndsWith("to go.")) Status = "Surface operations complete.";
            DropWatcher();
        }

        private void EnsureWatcher()
        {
            try
            {
                if (_watcher != null) return;
                GameObject go = new GameObject("KSA.SurfaceOpsWatcher");
                _watcher = go.AddComponent<EvaWatcher>();
                _watcher.Leg = this;
            }
            catch (Exception e) { KsaCore.Log.Warn("SurfaceOpsLeg watcher: " + e.Message); }
        }

        private void DropWatcher()
        {
            try
            {
                if (_watcher == null) return;
                _watcher.Leg = null;
                UnityEngine.Object.Destroy(_watcher.gameObject);
                _watcher = null;
            }
            catch (Exception e) { KsaCore.Log.Warn("SurfaceOpsLeg watcher: " + e.Message); }
        }

        public void End(FlightContext ctx)
        {
            try
            {
                DropWatcher();
                ctx.Control.Throttle = 0;
                ctx.Control.StopWarp();
            }
            catch (Exception e) { KsaCore.Log.Error("SurfaceOpsLeg.End: " + e); }
        }

        /// <summary>Keeps the EVA clock running while the kerbal, not the lander, is the active vessel.</summary>
        private sealed class EvaWatcher : MonoBehaviour
        {
            public SurfaceOpsLeg Leg;
            private double _next;

            private void FixedUpdate()
            {
                SurfaceOpsLeg leg = Leg;
                if (leg == null || leg.State != LegState.Running) return;
                // The director steps the leg itself while the lander is active; only fill in when it cannot.
                if (leg._ctx != null && leg._ctx.Vessel != null && leg._ctx.Vessel == FlightGlobals.ActiveVessel) return;
                double now = Planetarium.GetUniversalTime();
                if (now < _next) return;
                _next = now + 0.5;
                try { leg.Tick(); }
                catch (Exception e) { KsaCore.Log.Error("SurfaceOpsLeg watcher tick: " + e); leg.Finish(); }
            }
        }
    }

    /// <summary>
    /// Launch from the surface of an airless (or thin-air) body into a target orbit: vertical rise, a kick toward east,
    /// prograde hold with auto-staging until the apoapsis is up, coast with warp, circularise.
    /// </summary>
    public sealed class AirlessAscentLeg : IFlightLeg
    {
        private enum Phases { Liftoff, Vertical, Kick, Ascent, Coast, Circularize }

        private readonly double _target;
        private Phases _phase;
        private double _phaseStart;
        private bool _gearUp;
        private float _kickSign = 1f;

        public AirlessAscentLeg(double targetAltitude) { _target = Math.Max(5000, targetAltitude); }

        public string Name { get { return "Ascent to orbit"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Pre-launch.";
            try
            {
                Vessel v = ctx.Vessel;
                if (v == null || v.mainBody == null) { State = LegState.Failed; Status = "No vessel."; return; }
                _phaseStart = ctx.Now;
                double atmo = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
                switch (v.situation)
                {
                    case Vessel.Situations.LANDED:
                    case Vessel.Situations.PRELAUNCH:
                    case Vessel.Situations.SPLASHED:
                        _phase = Phases.Liftoff;
                        break;
                    case Vessel.Situations.ORBITING:
                        if (v.orbit.PeA >= _target * 0.85) { State = LegState.Done; Status = "Already in orbit."; return; }
                        _phase = Phases.Coast;
                        ctx.Control.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                        break;
                    default:
                        _gearUp = true;
                        if (v.orbit.ApA >= _target * 0.9 && v.altitude > atmo) { _phase = Phases.Coast; ctx.Control.Sas(VesselAutopilot.AutopilotMode.Prograde, false); }
                        else { _phase = Phases.Ascent; ctx.Control.Sas(VesselAutopilot.AutopilotMode.Prograde, v.altitude < 20000); }
                        break;
                }
            }
            catch (Exception e) { KsaCore.Log.Error("AirlessAscentLeg.Begin: " + e); }
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            string line = null;
            try
            {
                Vessel v = ctx.Vessel;
                VesselControl c = ctx.Control;
                if (LandingHelpers.IsDead(v)) { State = LegState.Failed; Status = "Vessel lost."; ctx.Say("Vessel lost during ascent."); return null; }
                bool warping = LandingHelpers.Warping;
                double above = LandingHelpers.AboveTerrain(v);
                double atmo = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
                double verticalUntil = v.mainBody.atmosphere ? 10000 : 1500;

                if (!_gearUp && _phase != Phases.Liftoff && above > 50 && !LandingHelpers.IsDown(v)) { _gearUp = true; c.Gear(false); }

                switch (_phase)
                {
                    case Phases.Liftoff:
                        if (warping) { c.StopWarp(); break; }
                        c.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, true);
                        c.Lights(false);
                        if (!c.EnsureEngine(ref line)) { Status = "Engine start."; break; }
                        c.Throttle = 1f;
                        _phase = Phases.Vertical; _phaseStart = ctx.Now;
                        Status = "Climbing vertically.";
                        ctx.Say("Liftoff from " + v.mainBody.bodyName + ".");
                        break;

                    case Phases.Vertical:
                        if (warping) { c.StopWarp(); break; }
                        c.Throttle = 1f;
                        c.AutoStage(ref line);
                        if (ctx.Now - _phaseStart > 3 && LandingHelpers.IsDown(v) && c.NoThrustAvailable())
                        {
                            State = LegState.Failed; Status = "No thrust.";
                            ctx.Say("Liftoff failed: no thrust available on " + v.mainBody.bodyName + ".");
                            break;
                        }
                        if (above > verticalUntil || v.srfSpeed > 80)
                        {
                            _kickSign = KickSign(v);
                            c.SasOffForKick();
                            c.Yaw = _kickSign;
                            _phase = Phases.Kick; _phaseStart = ctx.Now;
                            Status = "Pitching east.";
                        }
                        break;

                    case Phases.Kick:
                        if (warping) { c.StopWarp(); break; }
                        c.AutoStage(ref line);
                        if (ctx.Now - _phaseStart > 2.5)
                        {
                            c.Yaw = 0f;
                            c.Sas(VesselAutopilot.AutopilotMode.Prograde, true);
                            _phase = Phases.Ascent; _phaseStart = ctx.Now;
                            Status = "Holding prograde.";
                            ctx.Say("Pitch program started; following prograde.");
                        }
                        break;

                    case Phases.Ascent:
                        if (warping) { c.StopWarp(); break; }
                        c.Throttle = 1f;
                        c.AutoStage(ref line);
                        c.Sas(VesselAutopilot.AutopilotMode.Prograde, v.altitude < 20000);
                        if (v.orbit.ApA >= _target)
                        {
                            c.Throttle = 0;
                            _phase = Phases.Coast; _phaseStart = ctx.Now;
                            Status = "Coasting to apoapsis.";
                            ctx.Say("Engine cutoff; apoapsis " + LandingHelpers.Km(v.orbit.ApA) + " km.");
                        }
                        else if (c.NoThrustAvailable())
                        {
                            c.Throttle = 0;
                            if (v.orbit.ApA > Math.Max(atmo, 0) + 1000 && v.orbit.ApA >= _target * 0.5)
                            {
                                _phase = Phases.Coast; _phaseStart = ctx.Now;
                                ctx.Say("Propellant exhausted during ascent at " + LandingHelpers.Km(v.altitude) + " km; coasting on what we have.");
                            }
                            else
                            {
                                State = LegState.Failed; Status = "Out of propellant.";
                                ctx.Say("Propellant exhausted during ascent at " + LandingHelpers.Km(v.altitude) + " km; no orbit possible.");
                            }
                        }
                        break;

                    case Phases.Coast:
                        if (v.orbit.PeA >= _target * 0.85) { c.Throttle = 0; Done(ctx, v); break; }
                        if (v.altitude > atmo) c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                        if (v.orbit.timeToAp < 20 || (v.verticalSpeed < 0 && v.orbit.timeToAp > v.orbit.period * 0.5))
                        {
                            if (warping) { c.StopWarp(); break; }
                            c.EnsureEngine(ref line);
                            c.Throttle = 1f;
                            _phase = Phases.Circularize; _phaseStart = ctx.Now;
                            Status = "Circularising.";
                            ctx.Say("Circularisation burn.");
                        }
                        else
                        {
                            Status = "Coasting to apoapsis, " + Math.Round(v.orbit.timeToAp) + " s.";
                            if (v.orbit.timeToAp > 60 && v.altitude > atmo && !warping) c.WarpTo(ctx.Now + v.orbit.timeToAp - 30);
                            else if (v.orbit.timeToAp <= 60 && warping) c.StopWarp();
                        }
                        break;

                    case Phases.Circularize:
                        if (warping) { c.StopWarp(); break; }
                        c.Sas(VesselAutopilot.AutopilotMode.Prograde, false);
                        c.AutoStage(ref line);
                        if (v.orbit.PeA > _target * 0.85)
                        {
                            c.Throttle = 0;
                            Done(ctx, v);
                        }
                        else if (c.NoThrustAvailable())
                        {
                            c.Throttle = 0;
                            if (v.orbit.PeA > atmo)
                            {
                                ctx.Say("Propellant exhausted during circularisation; low orbit " + LandingHelpers.Km(v.orbit.ApA) + " x " + LandingHelpers.Km(v.orbit.PeA) + " km.");
                                Done(ctx, v);
                            }
                            else
                            {
                                State = LegState.Failed; Status = "Out of propellant before orbit.";
                                ctx.Say("Propellant exhausted before a stable orbit; periapsis " + LandingHelpers.Km(v.orbit.PeA) + " km. The lander will come back down.");
                            }
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("AirlessAscentLeg.Step: " + e);
            }
            return line;
        }

        /// <summary>Positive yaw tilts the nose toward the control frame's right; pick the sign that points it east.</summary>
        internal static float KickSign(Vessel v)
        {
            try
            {
                if (v.ReferenceTransform == null) return 1f;
                double d = Vector3d.Dot(v.ReferenceTransform.right, v.east);
                return d < 0 ? -1f : 1f;
            }
            catch { return 1f; }
        }

        private void Done(FlightContext ctx, Vessel v)
        {
            Status = "In orbit.";
            State = LegState.Done;
            ctx.Say("In orbit " + LandingHelpers.Km(v.orbit.ApA) + " x " + LandingHelpers.Km(v.orbit.PeA) + " km around " + v.mainBody.bodyName + ".");
        }

        public void End(FlightContext ctx)
        {
            try { ctx.Control.Yaw = 0; ctx.Control.Throttle = 0; ctx.Control.StopWarp(); }
            catch (Exception e) { KsaCore.Log.Error("AirlessAscentLeg.End: " + e); }
        }
    }
}
