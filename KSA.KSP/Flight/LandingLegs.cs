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

        private const double SiteLead = 0.25;       // the descent comes down about this share of an orbit past the burn
        private const double LongOrbit = 0.2;       // eccentricity above which the deorbit waits for the high end
        private const double NearApoapsis = 0.08;   // of a period, either side of apoapsis
        private const int LongOrbitSteps = 240;     // moments looked at over one long orbit, most of them skipped
        private const int SiteSteps = 36;           // moments looked at over one orbit
        private const int SiteOrbits = 8;           // orbits searched: the world turns under the orbit, so each one crosses new ground (one orbit offered only 9%, 2026-10-31)
        private const int MaxSearches = 3;          // searches before the crew gives the landing up
        private const double SteepStepAllowance = 2.5; // one steep step counts this much less than a tilt of the whole patch: the look at 400 m deals with those
        private const double AirBrakeHeight = 15000; // metres above terrain: no engine higher on a world with air
        private const double AirFallFloor = 2000;    // metres above terrain: below it the normal descent speeds
        private const double AirFallShare = 0.7;     // of the speed the engine can just stop from
        private const double DriftHoldBelow = 60;   // metres above terrain: no lower with sideways speed
        private const double DriftAllowed = 0.7;    // m/s sideways
        private const double DriftHoldLimit = 40;   // seconds, then down anyway
        private const double WaveOffFuel = 1500;    // m/s aboard to climb back and try again
        private double _driftHoldStart = -1;
        private bool _steepWaveOff;
        private const double SideStepAt = 2000;     // metres above terrain: look at the ground here, with room for a weak engine to stop from 60 m/s
        private const double SideStepFloor = 200;   // metres: hold at least this high while sliding
        private const double SideStepStopped = 8;   // m/s down at most before sliding
        private const double SideStepCell = 125;    // metres between height samples
        private const double SideStepSpeed = 15;    // m/s across the ground at most
        private const double SideStepDone = 25;     // metres from the spot
        private const double SideStepLimit = 90;    // seconds, stopping included, then down anyway
        private const double AimedStepLimit = 300;  // the same, sliding to the Director's site
        private const double SiteReach = 4000;      // metres: the longest slide to the Director's site
        private const double SiteGoodEnough = 2000; // metres: an earlier pass this much worse is still taken
        private bool _aimed;
        private const double SideStepReserve = 1250; // m/s: enough to finish coming down, reach orbit and go home
        private double _lastStepCheck;
        private const double SiteSpacing = 250;     // metres between height samples: a 1 km patch (T), small enough for a crater floor
        private const double GoodSlope = 0.03;      // height change across the site, wanted (T; a 5% site held but leaned, the 15% one tipped the lander)
        private const double FairSlope = 0.07;      // accepted when an orbit has nothing flatter
        private const double MinSunUp = 30;         // degrees above the horizon at the site: "in the light" (at 20 a crater wall shaded the floor)
        private const double ShadowReach = 20000;   // metres along the sunbeam tested for high ground
        private const double ShadowStep = 500;      // metres between tests
        private const double SurveySpacing = 200;   // metres between grid cells
        private const int SurveySize = 5;           // 5x5 grid
        private const double SurveyBelow = 20000;   // start the survey below this height above terrain

        private readonly double _maxSlope;
        private Phases _phase;
        private double _phaseStart;
        private const double DescentWarpFloor = 3000; // back to real time below this height above terrain
        private const double UpHoldBelow = 300;       // straight up below this height above terrain
        private const double UpHoldSpeed = 10;        // or below this surface speed, where the retrograde marker wanders
        private double _lastAttitudeLog;
        private bool _descentWarped;
        private bool _coastWarped;
        private double _burnAt = -1;
        private double _searchAgainAt = -1;
        private int _siteStamp;
        private double _warpAskedFor = -1;
        private int _searches;
        private bool _sideChecked;
        private bool _stepping;
        private double _stepStart, _stepLat, _stepLon;
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
                        // The Director can pick from orbit, any time before the burn (2026-10-31, T): the crew holds while the map is open for
                        // it, and plans again for a new site.
                        var site = KsaFlightDirector.Live;
                        if (site != null && site.PickingSite)
                        {
                            Status = "Holding in orbit: the Director is picking a site.";
                            if (warping) c.StopWarp();
                            break;
                        }
                        if (site != null && site.SiteStamp != _siteStamp) { _siteStamp = site.SiteStamp; _burnAt = -1; _searchAgainAt = -1; _searches = 0; if (warping) { c.StopWarp(); break; } }
                        if (_burnAt < 0 && ctx.Now >= _searchAgainAt) PickSite(ctx, v);
                        if (_burnAt < 0)
                        {
                            if (_searches >= MaxSearches)
                            {
                                State = LegState.Failed; Status = "No flat ground under this orbit.";
                                ctx.Say("No ground of " + LandingHelpers.Pct(FairSlope) + " or flatter in the light after " + (MaxSearches * SiteOrbits) + " orbits; no landing. Director, your call.");
                                break;
                            }
                            Status = "Waiting for flat ground: looking again in " + TransferSupport.Countdown(_searchAgainAt - ctx.Now) + ".";
                            // Asked once: stopped by hand, it stays stopped (2026-10-31, T: "I slash to stop the warp and it restarts").
                            if (!warping && _warpAskedFor != _searchAgainAt) { _warpAskedFor = _searchAgainAt; c.WarpTo(_searchAgainAt); }
                            break;
                        }
                        if (ctx.Now < _burnAt - 30)
                        {
                            Status = "Waiting for the landing site: deorbit in " + TransferSupport.Countdown(_burnAt - ctx.Now) + ".";
                            if (!warping && _warpAskedFor != _burnAt) { _warpAskedFor = _burnAt; c.WarpTo(_burnAt - 60); }
                            break;
                        }
                        Status = "Aligning retrograde for deorbit.";
                        if (warping) { c.StopWarp(); break; }
                        // Only once the nose is on retrograde: a burn the wrong way raises the orbit (2026-10-30, Kerbin orbit).
                        if (ctx.Now - _phaseStart > 5 && c.AttitudeError(-v.obt_velocity) < 15)
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
                                ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted during the deorbit burn; the lander stays in orbit.");
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
                            double safePe = (_hasAir ? v.mainBody.atmosphereDepth : 0) + (_steepWaveOff ? 9000 : 6000);
                            if (_steepWaveOff)
                            {
                                // From a hover there is no speed to follow: climb at 45 degrees along the orbit, flattening once it stays up.
                                double p = (v.orbit.PeA < 0 ? 45 : 10) * Math.PI / 180;
                                Vector3d up = v.upAxis;
                                Vector3d along = v.obt_velocity - up * Vector3d.Dot(v.obt_velocity, up);
                                c.PointAlong(() => (up * Math.Sin(p) + along.normalized * Math.Cos(p)).normalized);
                            }
                            else if (ctx.Now - _phaseStart < 3) { c.Sas(VesselAutopilot.AutopilotMode.Prograde, false); break; }
                            c.AutoStage(ref line);
                            if (v.orbit != null && v.orbit.PeA > safePe)
                            {
                                c.Throttle = 0;
                                _surveyed = false; _surveySlope = -1; _askedCrew = false; _burnMargin = 0.15;
                                // Plan again from the new orbit (2026-10-31: the old wait sat at 1x, the deorbit step cancelling every warp).
                                _phase = Phases.Deorbit; _phaseStart = ctx.Now;
                                _burnAt = -1; _searchAgainAt = -1; _searches = 0; _warpAskedFor = -1; _sideChecked = false; _steepWaveOff = false; _holdingUp = false;
                                ctx.Say("Back in orbit (" + LandingHelpers.Km(v.orbit.ApA) + " x " + LandingHelpers.Km(v.orbit.PeA) + " km); choosing the next pass.");
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
            if (inAir && !warping) c.DeployLanderChutes(ref line);

            // Light the engine while still high so the braking estimate uses real thrust (throttle stays zero).
            if (!warping && above < SurveyBelow && accel <= 0) c.EnsureEngine(ref line);
            double estAccel = accel > 0 ? accel : 2.0 * Math.Max(g, 0.1);   // assume TWR 2 until the engine is lit
            double safe = DescentMath.SafeBurnAltitude(v.srfSpeed, estAccel, g, _burnMargin * (ctx.Crew != null ? ctx.Crew.MarginFactor : 1.0));
            if (double.IsInfinity(safe)) safe = above; // thrust cannot beat gravity: start braking now and hope

            if (!_surveyed && above < SurveyBelow && v.verticalSpeed < 0) Survey(ctx, v);

            double descentRate = -v.verticalSpeed;
            bool brakeNow = above <= safe;
            // Air does the first of the braking for nothing: the engine waits for it (2026-11-01: from 895 m/s the sums lit it at 65 km, above the
            // air, and the whole 2,400 m/s went on a slow drive down, out of fuel at 2,262 m).
            if (_hasAir && above > AirBrakeHeight) brakeNow = false;
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
                // 4x on the last of a long coast, ending well before the burn (2026-11-01, T: "would be nice to speed up time compression").
                double warpFloor = 1.5 * safe + 1000;
                bool physicsWarp = warping && TimeWarp.WarpMode == TimeWarp.Modes.LOW;
                if (warping && !(physicsWarp && above > warpFloor)) c.StopWarp();
                else if (!warping && !_coastWarped && !_hasAir && above > warpFloor + 3000)
                {
                    _coastWarped = true;
                    TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
                    TimeWarp.SetRate(3, false);
                }
                Status = "Coasting, braking in " + Math.Round(Math.Max(0, above - safe)) + " m.";
            }
        }

        /// <summary>Samples a 5x5 grid around the predicted ground-track point and picks the flattest cell.</summary>
        /// <summary>
        /// When to burn: over one orbit, the ground a quarter of an orbit past each moment is where the descent comes down. Keep
        /// what the sun is on, take the flattest (2026-10-31, T: "we need to land on the sunny side of a planet even the mun",
        /// "and some place flat"; the crew had burned wherever it happened to be, with solar panels and no idea of the sun).
        /// </summary>
        private void PickSite(FlightContext ctx, Vessel v)
        {
            try
            {
                CelestialBody b = v.mainBody;
                Orbit o = v.orbit;
                if (o == null || o.eccentricity >= 1 || b.pqsController == null) { _burnAt = ctx.Now; return; }
                var live = KsaFlightDirector.Live;
                if (live != null && live.HasSite && live.SiteBody == b.bodyName) { AimForSite(ctx, v, live.SiteLat, live.SiteLon); return; }
                Vector3d sun = (Planetarium.fetch.Sun.position - b.position).normalized;
                // A weak lander brakes right after the burn, so it comes down almost under it (2026-10-31, T: "it could have waited
                // to more in the light": a Terrier's whole descent took 130 s of braking from 46 km, not a quarter of an orbit).
                double best = double.PositiveInfinity, bestSun = 0;
                double accel = ctx.Control.MaxAcceleration(), g = b.gravParameter / (b.Radius * b.Radius);
                // Flat anywhere in the light (2026-10-31, T: "3% anywhere in the light, 7% fall back to anywhere in the light", "it should
                // abort anything more than 7%"): a lander on its side goes nowhere; a morning rule took a 15% site and it tipped over.
                double[] slopeLimit = { GoodSlope, FairSlope };
                // Near apoapsis alone, the usual steps leave three moments an orbit (2026-11-01: "no flat ground", over and over).
                int steps = o.eccentricity > LongOrbit ? LongOrbitSteps : SiteSteps;
                for (int pass = 0; pass < slopeLimit.Length && double.IsPositiveInfinity(best); pass++)
                    for (int i = 0; i < steps * SiteOrbits; i++)
                    {
                        double ut = ctx.Now + 60 + o.period * i / steps;
                        // A long orbit is left from its high end: from the low end the same drop costs ten times the fuel (2026-11-01: 25 m/s against 300 from 742 x 70 km).
                        if (o.eccentricity > LongOrbit)
                        {
                            double fromAp = Math.Abs(ut - (ctx.Now + o.timeToAp)) % o.period;
                            if (Math.Min(fromAp, o.period - fromAp) > o.period * NearApoapsis) continue;
                        }
                        double lead; Vector3d down = PredictDown(o, ut, b, accel, g, out lead);
                        double sunUp = 90 - Vector3d.Angle(down, sun);
                        if (sunUp < MinSunUp) continue;
                        Vector3d ground = b.position + down * b.Radius;
                        double lat = b.GetLatitude(ground);
                        double lon = b.GetLongitude(ground) - 360 * (ut + lead - ctx.Now) / b.rotationPeriod; // the world turns meanwhile

                        double rough = Roughness(b, lat, lon);
                        if (rough < best && rough <= slopeLimit[pass]) { best = rough; bestSun = sunUp; _burnAt = ut; }
                    }
                _searches++;
                if (double.IsPositiveInfinity(best))
                {
                    _burnAt = -1;
                    _searchAgainAt = ctx.Now + o.period * SiteOrbits;
                    if (_searches < MaxSearches) ctx.Say("No ground of " + LandingHelpers.Pct(FairSlope) + " or flatter in the light yet; staying in orbit and looking again.");
                }
                else ctx.Say("Landing site chosen: sun " + Math.Round(bestSun) + " deg up, ground slope about " + LandingHelpers.Pct(best) + ", deorbit in " + TransferSupport.Countdown(_burnAt - ctx.Now) + ".");
            }
            catch (Exception e) { KsaCore.Log.Warn("Landing site search failed: " + e.Message); _burnAt = ctx.Now; _searches = MaxSearches; }
        }

        /// <summary>
        /// The Director's own site (2026-10-31, T: right click to pick a spot): the burn whose descent comes down closest to it, searched
        /// every 5 s over 20 orbits. An orbit only crosses its own ground track, so a site far off it is reported, not reached.
        /// </summary>
        private void AimForSite(FlightContext ctx, Vessel v, double siteLat, double siteLon)
        {
            CelestialBody b = v.mainBody;
            Orbit o = v.orbit;
            double accel = ctx.Control.MaxAcceleration(), g = b.gravParameter / (b.Radius * b.Radius);
            double bestMiss = double.PositiveInfinity, bestUt = ctx.Now + 60;
            for (double ut = ctx.Now + 60; ut < ctx.Now + 60 + o.period * 20; ut += 5)
            {
                double lead; Vector3d down = PredictDown(o, ut, b, accel, g, out lead);
                Vector3d ground = b.position + down * b.Radius;
                double lon = b.GetLongitude(ground) - 360 * (ut + lead - ctx.Now) / b.rotationPeriod; // the world turns meanwhile
                double miss = SurfaceDistance(b, b.GetLatitude(ground), lon, siteLat, siteLon);
                if (miss < bestMiss - SiteGoodEnough) { bestMiss = miss; bestUt = ut; }
            }
            _burnAt = bestUt;
            _searches++;
            ctx.Say("Aiming for the Director's site: this orbit comes down " + Math.Round(bestMiss / 1000, 1) + " km from it, deorbit in " + TransferSupport.Countdown(_burnAt - ctx.Now) + "."
                + (bestMiss > SiteReach ? " Too far to slide: the orbit does not cross it, and a plane change is not built." : string.Empty));
        }

        /// <summary>
        /// Where a deorbit burn at ut comes down, and how long after. A weak lander brakes straight away, close under the burn. A strong one
        /// falls most of the way round first, whatever the shape of its orbit (2026-11-01: a quarter-orbit guess was 5.6 km out on Minmus).
        /// </summary>
        private static Vector3d PredictDown(Orbit o, double ut, CelestialBody b, double accel, double g, out double seconds)
        {
            Vector3d r = o.getPositionAtUT(ut) - b.position;
            if (accel > 0 && accel < 3 * g)
            {
                seconds = Math.Min(o.period * SiteLead, 0.6 * o.getOrbitalVelocityAtUT(ut).magnitude / accel);
                return (o.getPositionAtUT(ut + seconds) - b.position).normalized;
            }
            double rA = r.magnitude, rP = 0.9 * b.Radius, a = (rA + rP) / 2, e = (rA - rP) / (rA + rP), p = a * (1 - e * e);
            double cos = Math.Max(-1, Math.Min(1, (p / b.Radius - 1) / e)), f = Math.Acos(cos);
            double ea = 2 * Math.Atan(Math.Sqrt((1 - e) / (1 + e)) * Math.Tan(f / 2));
            double sinceLow = (ea - e * Math.Sin(ea)) * Math.Sqrt(a * a * a / b.gravParameter);
            seconds = Math.PI * Math.Sqrt(a * a * a / b.gravParameter) - sinceLow;
            Vector3d normal = Vector3d.Cross(r, o.getPositionAtUT(ut + 10) - b.position).normalized;
            Vector3d side = Vector3d.Cross(normal, r.normalized);
            double angle = Math.PI - f;
            return (r.normalized * Math.Cos(angle) + side * Math.Sin(angle)).normalized;
        }

        /// <summary>Great-circle distance between two points on a world, metres.</summary>
        private static double SurfaceDistance(CelestialBody b, double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon1 - lon2) * Math.PI / 180;
            double cos = Math.Sin(p1) * Math.Sin(p2) + Math.Cos(p1) * Math.Cos(p2) * Math.Cos(dl);
            return b.Radius * Math.Acos(Math.Max(-1, Math.Min(1, cos)));
        }

        /// <summary>The worst of the height change across a 1 km patch and the steepest step between samples: a crater rim scores high.</summary>
        private static double Roughness(CelestialBody b, double lat, double lon)
        {
            double step = SiteSpacing / b.Radius * 180 / Math.PI;
            double stepLon = step / Math.Max(0.2, Math.Cos(lat * Math.PI / 180));
            double low = double.PositiveInfinity, high = double.NegativeInfinity, steepest = 0;
            var heights = new double[5, 5];
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                {
                    double h = heights[i, j] = b.TerrainAltitude(lat + (i - 2) * step, lon + (j - 2) * stepLon);
                    low = Math.Min(low, h); high = Math.Max(high, h);
                    if (i > 0) steepest = Math.Max(steepest, Math.Abs(h - heights[i - 1, j]) / SiteSpacing);
                    if (j > 0) steepest = Math.Max(steepest, Math.Abs(h - heights[i, j - 1]) / SiteSpacing);
                }
            return Math.Max((high - low) / (4 * SiteSpacing), steepest / SteepStepAllowance);
        }

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
                    // Without air the look at 2 km finds flat ground; waving off from here threw away a good Minmus approach (2026-10-31).
                    if (_hasAir) AskCrewAboutSite(ctx, _surveySlope);
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
            // Physics warp is safe on a long steady descent, so the crew uses it, once (2026-10-31, T: "would be nice to time
            // compress here": a Terrier comes down from 30 km at 60 m/s, over eight minutes). Rails warp never: engines stop on rails.
            bool physicsWarp = warping && TimeWarp.WarpMode == TimeWarp.Modes.LOW;
            if (warping && !(physicsWarp && above > DescentWarpFloor)) { c.StopWarp(); return; }
            if (!warping && !_descentWarped && above > DescentWarpFloor + 1000 && Math.Abs(v.verticalSpeed + DescentMath.TargetDescentSpeed(above)) < 5)
            {
                _descentWarped = true;
                TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
                TimeWarp.SetRate(3, false); // 4x
            }
            bool inAir = _hasAir && v.altitude < v.mainBody.atmosphereDepth;
            if (inAir) c.DeployLanderChutes(ref line);
            c.AutoStage(ref line);
            if (accel <= 0) c.EnsureEngine(ref line);

            double hSpeed = v.horizontalSrfSpeed;
            double descentRate = -v.verticalSpeed;
            // Height held during a side-step, climbing a little if the ground rises towards it.
            double target = _stepping ? (above < SideStepFloor ? -5 : 0) : DescentMath.TargetDescentSpeed(above);
            // High in the air, fall as fast as the engine can still stop: every second at 60 m/s is a second of gravity paid for.
            if (!_stepping && _hasAir && above >= AirFallFloor && accel > g) target = Math.Max(target, AirFallShare * Math.Sqrt(2 * (accel - g) * above));
            // Never touch down moving sideways: hold the height until the drift is gone (2026-11-01: 3.1 m/s down, soft, with 2 m/s of
            // drift left over from a slide, and the lander tripped over its legs).
            if (!_stepping && above < DriftHoldBelow && hSpeed > DriftAllowed && ctx.Now - _driftHoldStart < DriftHoldLimit)
            {
                if (_driftHoldStart < 0) _driftHoldStart = ctx.Now;
                target = 0;
            }

            if (!_gearDown && above < 500) { _gearDown = true; c.Gear(true); c.Lights(true); ctx.Say("Gear down, lights on at " + Math.Round(above) + " m."); }

            // Attitude: retrograde while moving fast, then straight up, leaning a little against any sideways drift. Never the
            // radial marker: it stands square to the velocity, so on a vertical descent it is sideways, and slowing to a stop
            // laid the lander over and burned it into the Mun nose first at 27 m/s (2026-10-31, 39 m up, 1,887 m/s aboard).
            if (!_holdingUp && (above < UpHoldBelow || v.srfSpeed < UpHoldSpeed))
            {
                _holdingUp = true;
                c.PointAlong(() => StandAndSteer(v));
            }
            else if (!_holdingUp) c.Sas(VesselAutopilot.AutopilotMode.Retrograde, true);
            // Close to the ground, look at it: anything steeper than the limit, find flat ground nearby and slide over (2026-10-31, T: "abort
            // anything more than 7% and look for something nearby that is flat").
            if (!_sideChecked && above < SideStepAt) { _sideChecked = true; if (_holdingUp) LookForFlat(ctx, v); }
            if (_stepping && (ctx.Now - _stepStart > (_aimed ? AimedStepLimit : SideStepLimit) || HorizontalTo(v).magnitude < SideStepDone))
            {
                _stepping = false;
                ctx.Say("Over the flat spot: going down.");
            }
            // Pick a place: hovering burns the fuel home, so flat ground underneath ends the slide, and so does the reserve
            // (2026-10-31, T: "at some point needs to pick a place or abort and come home": 200 m/s gone over a flat crater floor).
            if (_stepping && ctx.Now - _lastStepCheck >= 3)
            {
                _lastStepCheck = ctx.Now;
                double fuel = v.VesselDeltaV != null && v.VesselDeltaV.IsReady ? v.VesselDeltaV.TotalDeltaVVac : double.PositiveInfinity;
                if (!_aimed && PatchSlope(v.mainBody, v.latitude, v.longitude) <= FairSlope && !InShadow(v.mainBody, v.latitude, v.longitude))
                {
                    _stepping = false;
                    ctx.Say("Flat ground right here: going down.");
                }
                else if (fuel < SideStepReserve)
                {
                    _stepping = false;
                    ctx.Say("Down to " + Math.Round(fuel) + " m/s: no more sliding, going down here.");
                }
            }
            if (ctx.Now - _lastAttitudeLog >= 5)
            {
                _lastAttitudeLog = ctx.Now;
                KsaCore.Log.Info("Descent: " + Math.Round(above) + " m, down " + Math.Round(descentRate, 1) + " m/s, sideways " + Math.Round(hSpeed, 1) + " m/s, nose " + Math.Round(c.AttitudeError(v.upAxis)) + " deg off up, " + (_holdingUp ? "up hold" : "retrograde"));
            }

            // Only with the throttle open: closed on purpose while the fall is slow enough, no thrust is not an empty tank
            // (2026-10-31: 32 km up with 2,711 m/s aboard the crew declared the tanks dry and fell onto the Mun at 278 m/s).
            if ((c.Throttle > 0.05f && c.NoThrustAvailable()) || c.OutOfDeltaV())
            {
                if (!_fuelWarned)
                {
                    _fuelWarned = true;
                    if (above > 500)
                    {
                        ctx.Say("Propellant exhausted at " + Math.Round(above) + " m above terrain; holding retrograde and hoping.");
                        ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted at " + Math.Round(above) + " m above terrain; holding retrograde and hoping.");
                        Status = "Out of propellant.";
                        State = LegState.Failed;
                        return;
                    }
                    ctx.Say("Propellant exhausted at " + Math.Round(above) + " m; riding it down.");
                    ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted at " + Math.Round(above) + " m; riding it down.");
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
                // Only when too fast and out of room. SafeBurnAltitude's 50 m reserve read as "behind" at any speed below 50 m: full
                // throttle at 1 m/s, up past 50, sink, again, a hover at 51 m with 1,600 m/s aboard (2026-10-31, T: "it is hovering").
                bool behind = descentRate > target && DescentMath.StoppingDistance(v.srfSpeed, accel, g) * 1.1 >= above;
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
            // Tipped over, the engine only drives it down faster: stand it up first.
            if (_holdingUp && c.AttitudeError(v.upAxis) > 45) throttle = 0f;
            c.Throttle = throttle;
            Status = "Descending " + Math.Round(descentRate, 1) + " m/s (target " + Math.Round(target, 1) + "), " + Math.Round(above) + " m.";
        }

        /// <summary>Straight up, tipped against the sideways speed it does not want: 2 degrees per m/s, at most 15. A side-step wants speed towards the spot.</summary>
        private Vector3d StandAndSteer(Vessel v)
        {
            Vector3d up = v.upAxis;
            Vector3d vel = v.srf_velocity;
            Vector3d side = vel - up * Vector3d.Dot(vel, up);
            // Stop first, then slide (2026-10-31: sliding while still falling hit the slope at 13 m/s sideways, 18 m up).
            if (_stepping && Math.Abs(v.verticalSpeed) < SideStepStopped)
            {
                Vector3d to = HorizontalTo(v);
                side -= to.normalized * Math.Min(SideStepSpeed, to.magnitude / 5);
            }
            if (side.magnitude < 0.2) return up;
            // Thrust is only about gravity's size in a hover: weak gravity needs a deeper lean to shift the craft at all (15 degrees on Minmus never stopped 9 m/s).
            double most = v.mainBody.GeeASL < 0.1 ? 40 : 15;
            double tilt = Math.Min(most, side.magnitude * (v.mainBody.GeeASL < 0.1 ? 6 : 2)) * Math.PI / 180;
            return (up * Math.Cos(tilt) - side.normalized * Math.Sin(tilt)).normalized;
        }

        /// <summary>
        /// Walks along the sunbeam from a point: high ground above the beam means the point is in shadow. The sun's angle alone cannot
        /// see a crater wall (2026-10-31, T: "they are not supposed to land in shadow right?").
        /// </summary>
        internal static bool InShadow(CelestialBody b, double lat, double lon)
        {
            Vector3d ground = b.GetWorldSurfacePosition(lat, lon, b.TerrainAltitude(lat, lon) + 2);
            Vector3d sun = (Planetarium.fetch.Sun.position - ground).normalized;
            for (double s = ShadowStep; s <= ShadowReach; s += ShadowStep)
            {
                Vector3d p = ground + sun * s;
                double height = (p - b.position).magnitude - b.Radius;
                if (b.TerrainAltitude(b.GetLatitude(p), b.GetLongitude(p)) > height) return true;
            }
            return false;
        }

        /// <summary>The steepest step across the 500 m patch round a point.</summary>
        internal static double PatchSlope(CelestialBody b, double lat, double lon)
        {
            double step = SideStepCell / b.Radius * 180 / Math.PI;
            double stepLon = step / Math.Max(0.2, Math.Cos(lat * Math.PI / 180));
            double slope = 0;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    double h = b.TerrainAltitude(lat + i * step, lon + j * stepLon);
                    if (i > -2) slope = Math.Max(slope, Math.Abs(h - b.TerrainAltitude(lat + (i - 1) * step, lon + j * stepLon)));
                    if (j > -2) slope = Math.Max(slope, Math.Abs(h - b.TerrainAltitude(lat + i * step, lon + (j - 1) * stepLon)));
                }
            return slope / SideStepCell;
        }

        /// <summary>From the craft to the spot it is sliding to, along the ground.</summary>
        private Vector3d HorizontalTo(Vessel v)
        {
            Vector3d up = v.upAxis;
            Vector3d to = v.mainBody.GetWorldSurfacePosition(_stepLat, _stepLon, 0) - v.GetWorldPos3D();
            return to - up * Vector3d.Dot(to, up);
        }

        /// <summary>The flattest ground within 1 km, judged on the 500 m patch round each point (2026-10-31, T): a target with room to miss it.</summary>
        private void LookForFlat(FlightContext ctx, Vessel v)
        {
            try
            {
                CelestialBody b = v.mainBody;
                if (b.pqsController == null) return;
                const int half = 8, patch = 2; // 8 cells of 125 m each way; 2 cells each way is a 500 m patch
                int n = 2 * half + 1;
                double step = SideStepCell / b.Radius * 180 / Math.PI;
                double stepLon = step / Math.Max(0.2, Math.Cos(v.latitude * Math.PI / 180));
                // The flattest ground near the Director's click, when it is in reach; near the craft otherwise.
                double midLat = v.latitude, midLon = v.longitude;
                var live = KsaFlightDirector.Live;
                if (live != null && live.HasSite && live.SiteBody == b.bodyName)
                {
                    double miss = SurfaceDistance(b, v.latitude, v.longitude, live.SiteLat, live.SiteLon);
                    if (miss <= SiteReach) { midLat = live.SiteLat; midLon = live.SiteLon; _aimed = true; }
                    else ctx.Say("The Director's site is " + Math.Round(miss / 1000, 1) + " km away: too far to slide, looking nearby.");
                }
                var h = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        h[i, j] = b.TerrainAltitude(midLat + (i - half) * step, midLon + (j - half) * stepLon);
                double here = 0, best = double.PositiveInfinity;
                int bestI = half, bestJ = half;
                for (int i = patch; i < n - patch; i++)
                    for (int j = patch; j < n - patch; j++)
                    {
                        double slope = 0;
                        for (int a = i - patch; a <= i + patch; a++)
                            for (int c2 = j - patch; c2 <= j + patch; c2++)
                            {
                                if (a > i - patch) slope = Math.Max(slope, Math.Abs(h[a, c2] - h[a - 1, c2]));
                                if (c2 > j - patch) slope = Math.Max(slope, Math.Abs(h[a, c2] - h[a, c2 - 1]));
                            }
                        slope /= SideStepCell;
                        if (i == half && j == half) here = slope;
                        // Ties go to the nearer cell: distance adds a little.
                        double score = slope + 0.00001 * ((i - half) * (i - half) + (j - half) * (j - half));
                        // The sunbeam test only for a cell that would win: each one takes forty height samples.
                        if (score < best && !InShadow(b, midLat + (i - half) * step, midLon + (j - half) * stepLon)) { best = score; bestI = i; bestJ = j; }
                    }
                bool dark = InShadow(b, v.latitude, v.longitude);
                if (double.IsPositiveInfinity(best)) { ctx.Say("All this ground is in shadow; going straight down."); return; }
                if (!_aimed && !dark && (here <= GoodSlope || (bestI == half && bestJ == half))) { ctx.Say("Ground below: " + LandingHelpers.Pct(here) + "; going straight down."); return; }
                if (!_aimed && !dark && here <= FairSlope && best > GoodSlope) { ctx.Say("Ground below: " + LandingHelpers.Pct(here) + "; good enough."); return; }
                // Nothing within reach is fit to land on: back to orbit, once, while the fuel allows (2026-10-31, T: "it should abort anything more than 7%").
                double fuel = v.VesselDeltaV != null && v.VesselDeltaV.IsReady ? v.VesselDeltaV.TotalDeltaVVac : 0;
                if (best > FairSlope && !_waveOffUsed && fuel > WaveOffFuel)
                {
                    _waveOffUsed = true;
                    _aimed = false;
                    _steepWaveOff = true;
                    _phase = Phases.WaveOff; _phaseStart = ctx.Now;
                    Status = "Waving off: climbing back to orbit.";
                    ctx.Say("Ground below: " + LandingHelpers.Pct(here) + ", best nearby " + LandingHelpers.Pct(best) + ": over the limit. Waving off, back to orbit.");
                    return;
                }
                _stepLat = midLat + (bestI - half) * step;
                _stepLon = midLon + (bestJ - half) * stepLon;
                _stepping = true;
                _stepStart = ctx.Now;
                ctx.Say("Ground below: " + LandingHelpers.Pct(here) + "; sliding " + Math.Round(HorizontalTo(v).magnitude) + " m to a " + LandingHelpers.Pct(best) + " spot.");
            }
            catch (Exception e) { KsaCore.Log.Warn("Side-step search failed: " + e.Message); }
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
        private const double SettledSpeed = 0.3;   // m/s over the ground
        private const double SettleSeconds = 5;    // that still, for this long
        private const double SettleLimit = 30;     // and no longer than this in all
        private const double MaxEvaLean = 30;      // degrees off upright
        private double _settledSince = -1;
        private bool _holdingUpright;

        internal void Tick()
        {
            if (State != LegState.Running || _ctx == null) return;
            Vessel v = _ctx.Vessel;
            double now = _ctx.Now;
            switch (_phase)
            {
                case Phases.Spawn:
                    // Let the lander settle first (2026-10-31: first Mun landing, 3.8 m/s, bouncing on its legs for 7 s; Jeb stepped out
                    // in the touchdown frame and was thrown clear at 13.6 m/s).
                    if (v != null && v.srfSpeed > SettledSpeed) _settledSince = now;
                    if (_settledSince < 0) _settledSince = now;
                    // The crew keeps flying through the bounces (2026-10-31: a 1% site, engine off at first touch, nine bounces and over it went).
                    if (now - _settledSince < SettleSeconds && now - _evaStart < SettleLimit)
                    {
                        if (!_holdingUpright && v != null) { _holdingUpright = true; _ctx.Control.PointAlong(() => v.upAxis); }
                        Status = "Waiting for the lander to settle.";
                        return;
                    }
                    if (_holdingUpright) { _holdingUpright = false; _ctx.Control.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, true); }
                    // Nobody steps out of a lander on its side: the hatch throws them clear (2026-10-31, Jeb, twice).
                    if (v != null && _ctx.Control.AttitudeError(v.upAxis) > MaxEvaLean)
                    {
                        _ctx.Say("The lander is " + Math.Round(_ctx.Control.AttitudeError(v.upAxis)) + " deg off upright: the crew stays aboard.");
                        Finish();
                        return;
                    }
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

        private const double MaxLiftoffLean = 30; // degrees off upright

        private readonly double _target;
        private Phases _phase;
        private double _phaseStart;
        private bool _gearUp;
        private const double KickPitch = 60;      // degrees above the horizon, heading east
        private const double AheadOfClimb = 10000; // metres: the ground looked at ahead of the climb
        private const double ClearanceAhead = 1500; // metres above it

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
                double verticalUntil = v.mainBody.atmosphere ? 10000 : 300;

                if (!_gearUp && _phase != Phases.Liftoff && above > 50 && !LandingHelpers.IsDown(v)) { _gearUp = true; c.Gear(false); }

                switch (_phase)
                {
                    case Phases.Liftoff:
                        if (warping) { c.StopWarp(); break; }
                        // Never light up lying down: the engine only drives the craft along the ground (2026-10-31: tipped over on a slope,
                        // the crew followed the plan, and the lander killed all three).
                        if (LandingHelpers.IsDown(v) && c.AttitudeError(v.upAxis) > MaxLiftoffLean)
                        {
                            State = LegState.Failed; Status = "The lander is on its side.";
                            ctx.Say("The lander is " + Math.Round(c.AttitudeError(v.upAxis)) + " deg off upright: no liftoff. Director, a rescue or a reload.");
                            break;
                        }
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
                        if (above > verticalUntil || v.srfSpeed > (v.mainBody.atmosphere ? 80 : 30))
                        {
                            // East by the compass, not by a yaw input: yaw follows however the lander happened to roll, and a kick that way
                            // put it in an orbit tilted 113 degrees, which no fuel aboard could bring home (2026-10-31).
                            c.PointAlong(() => ClimbDirection(v, KickPitch));
                            _phase = Phases.Kick; _phaseStart = ctx.Now;
                            Status = "Pitching east.";
                        }
                        break;

                    case Phases.Kick:
                        if (warping) { c.StopWarp(); break; }
                        c.AutoStage(ref line);
                        if (ctx.Now - _phaseStart > 2.5)
                        {
                            if (v.mainBody.atmosphere) c.Sas(VesselAutopilot.AutopilotMode.Prograde, true);
                            else c.PointAlong(() => ClimbDirection(v, AirlessPitch(v)));
                            _phase = Phases.Ascent; _phaseStart = ctx.Now;
                            Status = "Holding prograde.";
                            ctx.Say("Pitch program started; following prograde.");
                        }
                        break;

                    case Phases.Ascent:
                        if (warping) { c.StopWarp(); break; }
                        c.Throttle = 1f;
                        c.AutoStage(ref line);
                        if (v.mainBody.atmosphere) c.Sas(VesselAutopilot.AutopilotMode.Prograde, v.altitude < 20000);
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
                                ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted during ascent at " + LandingHelpers.Km(v.altitude) + " km; coasting on what we have.");
                            }
                            else
                            {
                                State = LegState.Failed; Status = "Out of propellant.";
                                ctx.Say("Propellant exhausted during ascent at " + LandingHelpers.Km(v.altitude) + " km; no orbit possible.");
                                ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted during ascent at " + LandingHelpers.Km(v.altitude) + " km; no orbit possible.");
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
                                ctx.Cause(KSA.Core.Flight.FailureCause.OutOfFuel, "Propellant exhausted before a stable orbit; periapsis " + LandingHelpers.Km(v.orbit.PeA) + " km. The lander will come back down.");
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
        /// <summary>East, raised by the given pitch above the horizon.</summary>
        private static Vector3d ClimbDirection(Vessel v, double pitch)
        {
            double p = pitch * Math.PI / 180;
            return ((Vector3d)v.upAxis * Math.Sin(p) + (Vector3d)v.east * Math.Cos(p)).normalized;
        }

        /// <summary>
        /// Off an airless world, lay over as the sideways speed builds: steep climbing wasted about 500 m/s off the Mun. Never so flat
        /// that a falling lander meets the ground.
        /// </summary>
        private static double AirlessPitch(Vessel v)
        {
            double r = v.mainBody.Radius + v.altitude;
            double circular = Math.Sqrt(v.mainBody.gravParameter / r);
            // From 40 degrees, not 70: at 70 the Mun climb reached 50 km with only 114 m/s of orbital speed (2026-10-31).
            double pitch = 5 + 35 * Math.Max(0, 1 - v.horizontalSrfSpeed / circular);
            // Keep over the ground ahead: the Mun has 7 km peaks.
            double ahead = AheadOfClimb / r * 180 / Math.PI / Math.Max(0.2, Math.Cos(v.latitude * Math.PI / 180));
            bool groundAhead = v.altitude - v.mainBody.TerrainAltitude(v.latitude, v.longitude + ahead) < ClearanceAhead;
            return v.verticalSpeed < 5 || groundAhead ? Math.Max(pitch, 45) : pitch;
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
