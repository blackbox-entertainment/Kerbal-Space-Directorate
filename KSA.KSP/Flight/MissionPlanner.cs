using System;
using KSA.Core;
using KSA.Core.Missions;
using KSA.Core.Projects;

namespace KSA.KSP.Flight
{
    /// <summary>Marks the vessel as delivered (station core in orbit, module docked, base module down) and ends the program.</summary>
    public sealed class DeliverLeg : IFlightLeg
    {
        private readonly string _what;
        public DeliverLeg(string what) { _what = what; }
        public string Name { get { return "On station"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }
        public void Begin(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            ctx.Delivered = true;
            Status = _what + " delivered.";
            string body = ctx.Vessel != null && ctx.Vessel.mainBody != null ? ctx.Vessel.mainBody.bodyName : "Kerbin";
            ctx.Say(_what + (ctx.Vessel != null && ctx.Vessel.GetCrewCount() > 0 ? " delivered; the crew stays aboard." : " on station."), "Deliver:" + body);
            State = LegState.Done;
        }
        public string Step(FlightContext ctx) { State = LegState.Done; return null; }
        public void End(FlightContext ctx) { }
    }

    /// <summary>
    /// Builds the leg list for a mission type (KSA-51/52/53) and finds where to resume after a reload.
    /// Kerbin: ascent, hold, reentry. Moons and planets: transfer, correction, capture, landing, surface EVA,
    /// ascent, return, reentry. Stations: ascent, rendezvous, docking.
    /// </summary>
    public static class MissionPlanner
    {
        public static CelestialBody Body(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (CelestialBody b in FlightGlobals.Bodies) if (string.Equals(b.bodyName, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        private static bool WantsEva(MissionType t)
        {
            if (t == null) return false;
            foreach (string a in t.Achievements) if (a.StartsWith("EVA:", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static MissionProgram Build(FlightContext ctx, MissionType type)
        {
            var prog = new MissionProgram(ctx);
            CelestialBody home = ctx.Home;
            CelestialBody target = ctx.Target ?? home;
            double pe = ctx.ReentryPeriapsis;
            double peLo = pe - 5000, peHi = pe + 5000;
            bool interplanetary = target != home && target.referenceBody != home;
            MissionProfile profile = type != null ? type.Profile : MissionProfile.Suborbital;
            bool core = type != null && string.Equals(type.ModuleRole, "core", StringComparison.OrdinalIgnoreCase);
            // Never aim a periapsis or an orbit inside the target's atmosphere (Eve 90 km, Duna 50 km, Jool 200 km).
            double floor = target.atmosphere ? target.atmosphereDepth + 10000 : 0;
            double orbitAlt = Math.Max(ctx.TargetOrbitAltitude, floor + 10000);

            if (type != null && type.IsRescue && (profile == MissionProfile.Orbit || profile == MissionProfile.OrbitBody))
            {
                // KSA-65: meet the stranded vessel, bring the crew across, come home. Surface rescues are simulated.
                if (target == home)
                {
                    prog.Add(new AscentLeg(Math.Max(75000, ctx.ParkingAltitude - 15000), true)).Add(new RendezvousLeg()).Add(new RescueTransferLeg()).Add(new ReentryLeg(pe));
                }
                else
                {
                    prog.Add(new AscentLeg(ctx.ParkingAltitude, true));
                    Outbound(prog, ctx, target, interplanetary, Math.Max(20000, floor), Math.Max(80000, floor + 50000));
                    prog.Add(new CaptureLeg(target, orbitAlt)).Add(new RendezvousLeg()).Add(new RescueTransferLeg());
                    Homebound(prog, ctx, target, interplanetary, true, peLo, peHi);
                }
                return prog;
            }

            // Aircraft missions (2026-10-30): takeoff, a flight in the air, home and land.
            if (profile == MissionProfile.Atmospheric) { prog.Add(new AircraftLeg(type)); return prog; }

            // Spaceplanes (2026-10-28): runway takeoff, jets then rockets, nose-high reentry, glide home.
            if (ctx.IsPlane && target == home && (profile == MissionProfile.Suborbital || profile == MissionProfile.Orbit))
            {
                if (profile == MissionProfile.Suborbital) prog.Add(new PlaneAscentLeg(ctx.ParkingAltitude, false)).Add(new PlaneReturnLeg(pe));
                else
                {
                    prog.Add(new PlaneAscentLeg(ctx.ParkingAltitude, true));
                    prog.Add(new HoldOrbitLeg(type != null ? Math.Max(1, type.Orbits) : 1));
                    if (type != null && type.IsScience) prog.Add(new ScienceSweepLeg(1800));
                    prog.Add(new PlaneReturnLeg(pe));
                }
                return prog;
            }

            // A tanker (2026-10-29): meet the depot, pump the cargo across, relieve the keeper, come home.
            if (ctx.CargoFuel > 0 && target == home)
            {
                prog.Add(new AscentLeg(Math.Max(75000, ctx.ParkingAltitude - 15000), true)).Add(new TargetDepotLeg()).Add(new RendezvousLeg()).Add(new DockingLeg())
                    .Add(new PumpCargoLeg()).Add(new KeeperSwapLeg()).Add(new UndockLeg()).Add(new ReentryLeg(pe));
                return prog;
            }

            // A probe does not come home (2026-11-01, T: probe missions to every world): it goes, does its work and stays.
            if (type != null && type.IsUncrewed && profile != MissionProfile.Suborbital)
            {
                if (profile == MissionProfile.Orbit) prog.Add(new AscentLeg(ctx.ParkingAltitude, true)).Add(new HoldOrbitLeg(Math.Max(1, type.Orbits)));
                else
                {
                    Up(prog, ctx, false);
                    bool airbrakes = profile == MissionProfile.Land && Aerobrakes(target, home);
                    double air = KSA.Core.Flight.Sim.AirbrakeRules.PassShare * target.atmosphereDepth;
                    if (profile == MissionProfile.Flyby) Outbound(prog, ctx, target, interplanetary, Math.Max(20000, floor), Math.Max(200000, floor + 150000));
                    else if (airbrakes) Outbound(prog, ctx, target, interplanetary, air - 3000, air + 3000);
                    else Outbound(prog, ctx, target, interplanetary, Math.Max(15000, floor), Math.Max(60000, floor + 40000));
                    if (profile == MissionProfile.Flyby) prog.Add(new PassPeriapsisLeg());
                    else if (profile == MissionProfile.Land)
                    {
                        if (airbrakes) prog.Add(new CaptureLeg(target, orbitAlt, LooseApoapsis(target))).Add(new AirbrakeLeg(target, orbitAlt));
                        else prog.Add(new CaptureLeg(target, orbitAlt));
                        prog.Add(new LandingLeg(ctx.MaxSiteSlope));
                    }
                    else prog.Add(new CaptureLeg(target, orbitAlt)).Add(new HoldOrbitLeg(Math.Max(1, type.Orbits)));
                }
                if (type.IsScience && profile != MissionProfile.Flyby) prog.Add(new ScienceSweepLeg(1800));
                prog.Add(new DeliverLeg(profile == MissionProfile.Land ? "Lander" : "Probe"));
                return prog;
            }

            // Cloud cities dock like stations (KSA-91, 2026-11-03): enter, inflate, climb to the city altitude; the core anchors
            // there as the city, each later module flies to it and docks.
            if (type != null && type.Floating && target.atmosphere)
            {
                Up(prog, ctx);
                Outbound(prog, ctx, target, interplanetary, Math.Max(15000, floor), Math.Max(60000, floor + 40000));
                prog.Add(new CaptureLeg(target, orbitAlt));
                double cloud = ctx.CloudAltitude > 0 ? ctx.CloudAltitude : 0.2 * target.atmosphereDepth;
                prog.Add(new EntryLeg(target, cloud + 15000)).Add(new AirshipLeg(cloud, core));
                if (core) prog.Add(new DeliverLeg("Cloud city core"));
                else prog.Add(new AirRendezvousLeg(cloud)).Add(new DockingLeg()).Add(new DeliverLeg("Cloud city module"));
                return prog;
            }

            switch (profile)
            {
                case MissionProfile.Suborbital:
                    // With the delta-v for orbit the crew goes for it: one orbit, then home (2026-10-30).
                    if (ctx.TryOrbit) prog.Add(new AscentLeg(ctx.ParkingAltitude, true)).Add(new HoldOrbitLeg(1)).Add(new ReentryLeg(pe));
                    else prog.Add(new AscentLeg(ctx.ParkingAltitude, false)).Add(new ReentryLeg(pe));
                    break;
                case MissionProfile.Orbit:
                    prog.Add(new AscentLeg(ctx.ParkingAltitude, true));
                    // One full orbit at least (2026-10-28: "600" picked the orbits overload and held for 600 orbits).
                    prog.Add(new HoldOrbitLeg(type != null ? Math.Max(1, type.Orbits) : 1));
                    if (type != null && type.IsScience) prog.Add(new ScienceSweepLeg(1800));
                    if (WantsEva(type)) prog.Add(new SurfaceOpsLeg(40, false, true));
                    prog.Add(new ReentryLeg(pe));
                    break;
                case MissionProfile.Station:
                    if (core) prog.Add(new AscentLeg(ctx.ParkingAltitude, true)).Add(new DeliverLeg("Station core"));
                    else prog.Add(new AscentLeg(Math.Max(75000, ctx.ParkingAltitude - 15000), true)).Add(new RendezvousLeg()).Add(new DockingLeg()).Add(new DeliverLeg("Station module"));
                    break;
                case MissionProfile.Flyby:
                    Up(prog, ctx);
                    // A moon flyby flies a free return: a wider band of heights, and the side that drops the craft back home.
                    Outbound(prog, ctx, target, interplanetary, Math.Max(60000, floor), interplanetary ? Math.Max(200000, floor + 150000) : Math.Max(1200000, floor + 150000), !interplanetary);
                    prog.Add(new PassPeriapsisLeg());
                    Homebound(prog, ctx, target, interplanetary, false, peLo, peHi);
                    break;
                case MissionProfile.OrbitBody:
                    Up(prog, ctx);
                    Outbound(prog, ctx, target, interplanetary, Math.Max(20000, floor), Math.Max(80000, floor + 50000));
                    prog.Add(new CaptureLeg(target, orbitAlt));
                    prog.Add(new HoldOrbitLeg(type != null ? Math.Max(1, type.Orbits) : 1)); // a full orbit of the target counts as orbit
                    if (type != null && type.IsScience) prog.Add(new ScienceSweepLeg(1800));
                    Homebound(prog, ctx, target, interplanetary, true, peLo, peHi);
                    break;
                case MissionProfile.Land:
                case MissionProfile.Base:
                    Up(prog, ctx);
                    if (profile == MissionProfile.Land && Aerobrakes(target, home))
                    {
                        double airPe = KSA.Core.Flight.Sim.AirbrakeRules.PassShare * target.atmosphereDepth;
                        Outbound(prog, ctx, target, interplanetary, airPe - 3000, airPe + 3000);
                        prog.Add(new CaptureLeg(target, orbitAlt, LooseApoapsis(target))).Add(new AirbrakeLeg(target, orbitAlt)).Add(new TankerStopLeg()).Add(new LandingLeg(ctx.MaxSiteSlope));
                    }
                    else
                    {
                        Outbound(prog, ctx, target, interplanetary, Math.Max(15000, floor), Math.Max(60000, floor + 40000));
                        prog.Add(new CaptureLeg(target, orbitAlt));
                        if (interplanetary) prog.Add(new TankerStopLeg());
                        prog.Add(new LandingLeg(ctx.MaxSiteSlope));
                    }
                    if (profile == MissionProfile.Base && type != null && string.Equals(type.ModuleRole, "resupply", StringComparison.OrdinalIgnoreCase))
                    {
                        // Resupply: deliver beside the base, then the relief crew stays and the old crew flies the lander home.
                        prog.Add(new DeliverLeg("Base resupply")); break;
                    }
                    if (profile == MissionProfile.Base) { prog.Add(new DeliverLeg("Base module")); break; }
                    // The crew stays long enough to plant the flag and run the EVA science (2026-11-03, T); "Lift off now" on the HUD ends it early.
                    prog.Add(new SurfaceOpsLeg(ctx.SurfaceStaySeconds > 0 ? ctx.SurfaceStaySeconds : (type != null && type.IsScience ? 120 : 45)));
                    if (type != null && type.SurfaceDays > 0.2) prog.Add(new ScienceSweepLeg(type.SurfaceDays * 21600.0));
                    prog.Add(new AirlessAscentLeg(orbitAlt));
                    Homebound(prog, ctx, target, interplanetary, true, peLo, peHi);
                    break;
            }
            return prog;
        }

        /// <summary>
        /// The climb to a parking orbit, and for a flight that tops up at the fuel depot (2026-10-29) the stop there: a
        /// lower orbit to catch the depot up, rendezvous, docking, filling the tanks that went up empty, and undocking.
        /// </summary>
        private static void Up(MissionProgram prog, FlightContext ctx, bool crewed = true)
        {
            if (!ctx.Refuel)
            {
                prog.Add(new AscentLeg(ctx.ParkingAltitude, true));
                // A fuel station above home (2026-11-01, T): a crew leaving Kerbin fills up there first.
                if (crewed && ctx.Target != null && ctx.Target != ctx.Home) prog.Add(new TankerStopLeg());
                return;
            }
            prog.Add(new AscentLeg(Math.Max(75000, ctx.ParkingAltitude - 15000), true)).Add(new TargetDepotLeg()).Add(new RendezvousLeg()).Add(new DockingLeg())
                .Add(new RefuelLeg()).Add(new UndockLeg());
        }

        private static void Outbound(MissionProgram prog, FlightContext ctx, CelestialBody target, bool interplanetary, double peLo, double peHi, bool freeReturn = false)
        {
            if (interplanetary)
            {
                // A moon of another planet (Laythe, Ike): the planet first, a high orbit of it, then across to the moon. The transfer refused these outright until 2026-11-01.
                CelestialBody planet = PlanetOf(target, ctx.Home);
                bool moon = planet != target;
                double lo = moon ? 0.9 * StagingAltitude(target) : peLo, hi = moon ? 1.1 * StagingAltitude(target) : peHi;
                // A mid-course check 30 days out (0.8.173): the deep-space burn's error is a nudge there and 12,000 km at the edge of the planet's space.
                prog.Add(new InterplanetaryTransferLeg(planet)).Add(new WaitForSoiLeg(ctx.Home.referenceBody, 30))
                    .Add(new CorrectionLeg(planet, lo, hi, 400)).Add(new WaitForSoiLeg(planet, 4000, MidCourseDays)).Add(new CorrectionLeg(planet, lo, hi, 300))
                    .Add(new WaitForSoiLeg(planet, 4000)).Add(new CorrectionLeg(planet, lo, hi, 300));
                if (moon)
                    prog.Add(new CaptureLeg(planet, StagingAltitude(target))).Add(new TransferLeg(target, (peLo + peHi) / 2)).Add(new CorrectionLeg(target, peLo, peHi, 300))
                        .Add(new WaitForSoiLeg(target, 30)).Add(new CorrectionLeg(target, peLo, peHi, 200));
            }
            else
            {
                CelestialBody back = freeReturn ? ctx.Home : null;
                prog.Add(new TransferLeg(target, (peLo + peHi) / 2)).Add(new CorrectionLeg(target, peLo, peHi, 300, back))
                    .Add(new WaitForSoiLeg(target, 30)).Add(new CorrectionLeg(target, peLo, peHi, 200, back));
            }
        }

        /// <summary>Days short of a planet's space for the mid-course check (0.8.173).</summary>
        private const double MidCourseDays = 30;

        /// <summary>The planet a flight must reach: the world itself, or the one a moon goes around.</summary>
        private static CelestialBody PlanetOf(CelestialBody target, CelestialBody home)
        {
            CelestialBody sun = home.referenceBody, b = target;
            while (b.referenceBody != null && b.referenceBody != sun && b.referenceBody != b) b = b.referenceBody;
            return b;
        }

        /// <summary>Half way up to the moon's orbit: cheap to capture into and clear of the planet's air and inner moons.</summary>
        private static double StagingAltitude(CelestialBody moon)
        {
            return 0.5 * moon.orbit.semiMajorAxis - moon.referenceBody.Radius * 0.5;
        }

        private const double ReturnAirShare = 0.43; // 30 km on Kerbin
        private const double LooseCapture = 0.3;    // of the sphere of influence: the apoapsis of a capture the air will finish

        /// <summary>
        /// The apoapsis (altitude) of a loose capture: 0.3 of the sphere, or under the innermost moon's reach where there is one
        /// (0.8.181, T: a capture at 14,746 km over Duna crossed Ike's path and needed two dodges; 1,735 km costs about 64 m/s more
        /// and Ike can never reach it). Never under five times the air, so the air still has an orbit to round.
        /// </summary>
        private static double LooseApoapsis(CelestialBody target)
        {
            double alt = LooseCapture * target.sphereOfInfluence;
            if (target.orbitingBodies != null)
                foreach (CelestialBody moon in target.orbitingBodies)
                    if (moon != null && moon.orbit != null) alt = Math.Min(alt, moon.orbit.PeR - moon.sphereOfInfluence - target.Radius);
            return Math.Max(alt, 5 * Math.Max(target.atmosphereDepth, 50000));
        }
        private const double ThinAir = 20;          // kPa at the surface: thin enough to pass through for days (Duna 6.8; Laythe and Eve are not)

        /// <summary>A world whose air can finish a capture without a crew being crushed or burned on a low pass.</summary>
        private static bool Aerobrakes(CelestialBody target, CelestialBody home)
        {
            return target != home && target.atmosphere && target.atmospherePressureSeaLevel < ThinAir;
        }

        private static void Homebound(MissionProgram prog, FlightContext ctx, CelestialBody target, bool interplanetary, bool fromOrbit, double peLo, double peHi)
        {
            CelestialBody home = ctx.Home;
            // Back from a moon or a planet the capsule is fast: too high a periapsis skips off the air (2026-11-01: 41 km at 3.2 km/s took two passes).
            double pe = Math.Min((peLo + peHi) / 2, ReturnAirShare * home.atmosphereDepth);
            peLo = pe - 4000;
            peHi = pe + 4000;
            if (interplanetary)
            {
                // From a moon of another planet: down to the planet's staging orbit, then home from there.
                CelestialBody planet = PlanetOf(target, home);
                if (planet != target)
                {
                    prog.Add(new ReturnLeg(planet, StagingAltitude(target))).Add(new WaitForSoiLeg(planet, 30)).Add(new CaptureLeg(planet, StagingAltitude(target)));
                    fromOrbit = true;
                }
                if (fromOrbit) prog.Add(new ReturnLeg(home, pe)).Add(new WaitForSoiLeg(home.referenceBody, 60));
                else prog.Add(new WaitForSoiLeg(home.referenceBody, 60)).Add(new TransferLeg(home, pe));
                prog.Add(new CorrectionLeg(home, peLo, peHi, 400) { Homebound = true }).Add(new WaitForSoiLeg(home, 800, MidCourseDays)).Add(new CorrectionLeg(home, peLo, peHi, 300) { Homebound = true })
                    .Add(new WaitForSoiLeg(home, 800)).Add(new CorrectionLeg(home, peLo, peHi, 300) { Homebound = true });
            }
            else
            {
                if (fromOrbit) prog.Add(new ReturnLeg(home, pe));
                prog.Add(new WaitForSoiLeg(home, 30)).Add(new CorrectionLeg(home, peLo, peHi, 300) { Homebound = true });
            }
            prog.Add(new ReentryLeg(pe));
        }

        /// <summary>After a reload: skip to the leg that fits where the vessel is now. Best effort; the legs re-check in Begin.</summary>
        public static void Resume(MissionProgram prog, FlightContext ctx, Func<string, bool> logged)
        {
            Vessel v = ctx.Vessel;
            if (v == null) return;
            foreach (IFlightLeg l in prog.Legs)
            {
                AircraftLeg air = l as AircraftLeg;
                if (air != null) { air.TurnedForHome = logged("Aircraft:" + v.mainBody.bodyName); return; }
            }
            // A cloud module (KSA-91): in orbit of the target it enters; in the air it floats; floating already, it flies to the city.
            if (Has(prog, l => l is AirshipLeg) && ctx.Target != null && v.mainBody == ctx.Target)
            {
                if (v.situation == Vessel.Situations.ORBITING) { prog.SkipTo(l => l is EntryLeg); return; }
                if (logged("Float:" + v.mainBody.bodyName)) { prog.SkipTo(l => l is AirRendezvousLeg || l is DeliverLeg); return; }
                if (v.altitude < v.mainBody.atmosphereDepth) { prog.SkipTo(l => l is AirshipLeg); return; }
            }
            CelestialBody body = v.mainBody;
            CelestialBody home = ctx.Home;
            CelestialBody target = ctx.Target ?? home;
            string tb = target.bodyName;
            bool landedOnTarget = (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED) && body == target && target != home;
            bool orbitingTarget = v.situation == Vessel.Situations.ORBITING && body == target && target != home;
            bool inTargetSoi = body == target && target != home;
            bool inSun = body.referenceBody == null || body == home.referenceBody;
            // Heading home only once the mission's own milestone is logged (0.8.173: a flyby credited on entering Duna's space sent a
            // halted lander, 12,443 km out with nothing done, onto the return coast to nowhere).
            bool lands = Has(prog, l => l is LandingLeg), orbits = Has(prog, l => l is CaptureLeg || l is AirbrakeLeg);
            bool headingHome = target != home && (logged("Land:" + tb) || logged("EVA:" + tb) || (!lands && logged("Orbit:" + tb)) || (!lands && !orbits && logged("Flyby:" + tb)));
            bool down = v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED || v.situation == Vessel.Situations.PRELAUNCH;
            bool descending = v.situation == Vessel.Situations.SUB_ORBITAL || v.situation == Vessel.Situations.FLYING;

            if (landedOnTarget) { if (logged("EVA:" + tb)) prog.SkipTo(l => l is AirlessAscentLeg || l is DeliverLeg); else prog.SkipTo(l => l is SurfaceOpsLeg || l is DeliverLeg); return; }
            if (orbitingTarget)
            {
                if (logged("EVA:" + tb) || logged("Land:" + tb)) prog.SkipTo(l => l is ReturnLeg);
                else if (logged("Refuel:" + tb)) prog.SkipTo(l => l is LandingLeg || l is HoldOrbitLeg);
                else prog.SkipTo(l => l is AirbrakeLeg || l is LandingLeg || l is HoldOrbitLeg || l is ReturnLeg);
                return;
            }
            if (inTargetSoi)
            {
                // Leaving after the return burn, or part-way down to the surface: do not re-run the arrival legs.
                if (v.situation == Vessel.Situations.ESCAPING && headingHome && Has(prog, l => l is ReturnLeg)) { prog.SkipTo(l => l is WaitForSoiLeg && IndexAfterHome(prog, l)); return; }
                // No need for the orbit to be logged: it only counts after a full turn, and a lander goes straight down (2026-10-31).
                // Still orbiting through the air is aerobraking; a path that stays down is a landing (2026-11-01: a lander out of fuel at 2 km was sent back to aerobrake).
                bool stillOrbiting = v.orbit.ApA > target.atmosphereDepth && v.orbit.PeA > 0.45 * target.atmosphereDepth; // a deorbit path runs far lower than any pass
                if (descending && !logged("Land:" + tb) && v.orbit.eccentricity < 1 && Has(prog, l => l is LandingLeg)) { prog.SkipTo(l => (stillOrbiting && l is AirbrakeLeg) || l is LandingLeg); return; }
                prog.SkipTo(l => l is CaptureLeg || l is PassPeriapsisLeg || l is CorrectionLeg);
                return;
            }
            if (body == home)
            {
                if (down) { if (logged("Land:Kerbin") || logged("Suborbit:Kerbin")) prog.SkipTo(l => l is ReentryLeg); return; }
                if (headingHome) { prog.SkipTo(l => l is ReentryLeg || (l is CorrectionLeg && IndexAfterHome(prog, l))); return; }
                // Been to orbit and coming down (the Director burned for home and handed back): home, never the ascent again
                // (2026-10-30: the crew tried to circularise a capsule with no fuel, which also held time warp at 1x).
                // A periapsis dipped into the air on the way to the tanker (0.8.165): the crew raise it and carry on, never reentry.
                if (descending && v.orbit != null && v.orbit.ApA > home.atmosphereDepth + 20000 && Has(prog, l => l is TankerStopLeg) && !logged("Refuel:" + home.bodyName) && TankerStopLeg.FindTanker(v, home) != null) { prog.SkipTo(l => l is TankerStopLeg); return; }
                if (descending && logged("Orbit:Kerbin") && v.orbit != null && v.orbit.PeA < home.atmosphereDepth) { prog.SkipTo(l => l is ReentryLeg); return; }
                // The fuel depot (2026-10-29): a tanker that has pumped goes home; a ship that has not refuelled goes back to the depot.
                if (v.situation == Vessel.Situations.ORBITING && ctx.CargoFuel > 0 && logged("Tanker:Kerbin")) { prog.SkipTo(l => l is UndockLeg); return; }
                if (v.situation == Vessel.Situations.ORBITING && target == home) { prog.SkipTo(l => l is HoldOrbitLeg || l is TargetDepotLeg || l is RendezvousLeg || l is DeliverLeg || l is ReentryLeg); return; }
                if (v.situation == Vessel.Situations.ORBITING && ctx.Refuel && !logged("Refuel:Kerbin") && Has(prog, l => l is TargetDepotLeg)) { prog.SkipTo(l => l is TargetDepotLeg); return; }
                if (v.situation == Vessel.Situations.ORBITING && ctx.Refuel && DepotOps.Docked(ctx)) { prog.SkipTo(l => l is UndockLeg); return; }
                if (v.situation == Vessel.Situations.ORBITING) { prog.SkipTo(l => (l is TankerStopLeg && !logged("Refuel:" + home.bodyName)) || l is TransferLeg || l is InterplanetaryTransferLeg); return; }
                // Still climbing or coming down from a hop: the ascent leg's Begin sorts that out.
                if (v.altitude < home.atmosphereDepth || v.orbit == null || v.orbit.ApA < ctx.ParkingAltitude * 0.9) return;
                if (v.situation == Vessel.Situations.ESCAPING) prog.SkipTo(l => l is CorrectionLeg || l is WaitForSoiLeg);
                return;
            }
            // At the planet on the way to or from one of its moons.
            CelestialBody planetOfMoon = target != home ? PlanetOf(target, home) : null;
            if (planetOfMoon != null && planetOfMoon != target && planetOfMoon != home && body == planetOfMoon)
            {
                if (headingHome) prog.SkipTo(l => l is CaptureLeg && IndexAfterHome(prog, l) || l is ReturnLeg && IndexAfterHome(prog, l));
                else if (v.situation == Vessel.Situations.ORBITING) prog.SkipTo(l => l is TransferLeg);
                else prog.SkipTo(l => l is CaptureLeg || l is CorrectionLeg);
                return;
            }
            // Pulled in by a moon of the world they came for (2026-11-01: nothing matched, so the crew ran the ascent from Kerbin in Ike's space and burned every drop).
            if (body.referenceBody == target && target != home && Has(prog, l => l is AirbrakeLeg) && !logged("Land:" + tb)) { prog.SkipTo(l => l is AirbrakeLeg); return; }
            if (inSun) { if (headingHome) prog.SkipTo(l => l is TransferLeg || (l is CorrectionLeg && IndexAfterHome(prog, l))); else prog.SkipTo(l => l is CorrectionLeg || l is WaitForSoiLeg); }
        }

        private static bool Has(MissionProgram prog, Func<IFlightLeg, bool> match)
        {
            foreach (IFlightLeg l in prog.Legs) if (match(l)) return true;
            return false;
        }

        private static bool IndexAfterHome(MissionProgram prog, IFlightLeg leg)
        {
            // A leg placed after the return/transfer-home legs belongs to the homebound half.
            bool seenReturn = false;
            foreach (IFlightLeg l in prog.Legs)
            {
                if (l is ReturnLeg || l is PassPeriapsisLeg) seenReturn = true;
                if (l == leg) return seenReturn;
            }
            return false;
        }
    }
}
