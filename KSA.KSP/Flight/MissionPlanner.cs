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
            ctx.Say(_what + " delivered; the crew stays aboard.", "Deliver:" + body);
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

            switch (profile)
            {
                case MissionProfile.Suborbital:
                    prog.Add(new AscentLeg(ctx.ParkingAltitude, false)).Add(new ReentryLeg(pe));
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
                    prog.Add(new AscentLeg(ctx.ParkingAltitude, true));
                    Outbound(prog, ctx, target, interplanetary, Math.Max(60000, floor), Math.Max(200000, floor + 150000));
                    prog.Add(new PassPeriapsisLeg());
                    Homebound(prog, ctx, target, interplanetary, false, peLo, peHi);
                    break;
                case MissionProfile.OrbitBody:
                    prog.Add(new AscentLeg(ctx.ParkingAltitude, true));
                    Outbound(prog, ctx, target, interplanetary, Math.Max(20000, floor), Math.Max(80000, floor + 50000));
                    prog.Add(new CaptureLeg(target, orbitAlt));
                    prog.Add(new HoldOrbitLeg(type != null ? Math.Max(1, type.Orbits) : 1)); // a full orbit of the target counts as orbit
                    if (type != null && type.IsScience) prog.Add(new ScienceSweepLeg(1800));
                    Homebound(prog, ctx, target, interplanetary, true, peLo, peHi);
                    break;
                case MissionProfile.Land:
                case MissionProfile.Base:
                    prog.Add(new AscentLeg(ctx.ParkingAltitude, true));
                    Outbound(prog, ctx, target, interplanetary, Math.Max(15000, floor), Math.Max(60000, floor + 40000));
                    prog.Add(new CaptureLeg(target, orbitAlt)).Add(new LandingLeg(ctx.MaxSiteSlope));
                    if (profile == MissionProfile.Base && type != null && string.Equals(type.ModuleRole, "resupply", StringComparison.OrdinalIgnoreCase))
                    {
                        // Resupply: deliver beside the base, then the relief crew stays and the old crew flies the lander home.
                        prog.Add(new DeliverLeg("Base resupply")); break;
                    }
                    if (profile == MissionProfile.Base) { prog.Add(new DeliverLeg("Base module")); break; }
                    prog.Add(new SurfaceOpsLeg(type != null && type.IsScience ? 120 : 45));
                    if (type != null && type.SurfaceDays > 0.2) prog.Add(new ScienceSweepLeg(type.SurfaceDays * 21600.0));
                    prog.Add(new AirlessAscentLeg(orbitAlt));
                    Homebound(prog, ctx, target, interplanetary, true, peLo, peHi);
                    break;
            }
            return prog;
        }

        private static void Outbound(MissionProgram prog, FlightContext ctx, CelestialBody target, bool interplanetary, double peLo, double peHi)
        {
            if (interplanetary)
            {
                prog.Add(new InterplanetaryTransferLeg(target)).Add(new WaitForSoiLeg(ctx.Home.referenceBody, 30))
                    .Add(new CorrectionLeg(target, peLo, peHi, 400)).Add(new WaitForSoiLeg(target, 800))
                    .Add(new CorrectionLeg(target, peLo, peHi, 300));
            }
            else
            {
                prog.Add(new TransferLeg(target, (peLo + peHi) / 2)).Add(new CorrectionLeg(target, peLo, peHi, 300))
                    .Add(new WaitForSoiLeg(target, 30)).Add(new CorrectionLeg(target, peLo, peHi, 200));
            }
        }

        private static void Homebound(MissionProgram prog, FlightContext ctx, CelestialBody target, bool interplanetary, bool fromOrbit, double peLo, double peHi)
        {
            CelestialBody home = ctx.Home;
            double pe = (peLo + peHi) / 2;
            if (interplanetary)
            {
                if (fromOrbit) prog.Add(new ReturnLeg(home, pe)).Add(new WaitForSoiLeg(home.referenceBody, 60));
                else prog.Add(new WaitForSoiLeg(home.referenceBody, 60)).Add(new TransferLeg(home, pe));
                prog.Add(new CorrectionLeg(home, peLo, peHi, 400)).Add(new WaitForSoiLeg(home, 800)).Add(new CorrectionLeg(home, peLo, peHi, 300));
            }
            else
            {
                if (fromOrbit) prog.Add(new ReturnLeg(home, pe));
                prog.Add(new WaitForSoiLeg(home, 30)).Add(new CorrectionLeg(home, peLo, peHi, 300));
            }
            prog.Add(new ReentryLeg(pe));
        }

        /// <summary>After a reload: skip to the leg that fits where the vessel is now. Best effort; the legs re-check in Begin.</summary>
        public static void Resume(MissionProgram prog, FlightContext ctx, Func<string, bool> logged)
        {
            Vessel v = ctx.Vessel;
            if (v == null) return;
            CelestialBody body = v.mainBody;
            CelestialBody home = ctx.Home;
            CelestialBody target = ctx.Target ?? home;
            string tb = target.bodyName;
            bool landedOnTarget = (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED) && body == target && target != home;
            bool orbitingTarget = v.situation == Vessel.Situations.ORBITING && body == target && target != home;
            bool inTargetSoi = body == target && target != home;
            bool inSun = body.referenceBody == null || body == home.referenceBody;
            bool headingHome = target != home && (logged("Flyby:" + tb) || logged("Orbit:" + tb) || logged("Land:" + tb) || logged("EVA:" + tb));
            bool down = v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED || v.situation == Vessel.Situations.PRELAUNCH;
            bool descending = v.situation == Vessel.Situations.SUB_ORBITAL || v.situation == Vessel.Situations.FLYING;

            if (landedOnTarget) { if (logged("EVA:" + tb)) prog.SkipTo(l => l is AirlessAscentLeg || l is DeliverLeg); else prog.SkipTo(l => l is SurfaceOpsLeg || l is DeliverLeg); return; }
            if (orbitingTarget)
            {
                if (logged("EVA:" + tb) || logged("Land:" + tb)) prog.SkipTo(l => l is ReturnLeg);
                else prog.SkipTo(l => l is LandingLeg || l is HoldOrbitLeg || l is ReturnLeg);
                return;
            }
            if (inTargetSoi)
            {
                // Leaving after the return burn, or part-way down to the surface: do not re-run the arrival legs.
                if (v.situation == Vessel.Situations.ESCAPING && headingHome && Has(prog, l => l is ReturnLeg)) { prog.SkipTo(l => l is WaitForSoiLeg && IndexAfterHome(prog, l)); return; }
                if (descending && logged("Orbit:" + tb) && !logged("Land:" + tb) && Has(prog, l => l is LandingLeg)) { prog.SkipTo(l => l is LandingLeg); return; }
                prog.SkipTo(l => l is CaptureLeg || l is PassPeriapsisLeg || l is CorrectionLeg);
                return;
            }
            if (body == home)
            {
                if (down) { if (logged("Land:Kerbin") || logged("Suborbit:Kerbin")) prog.SkipTo(l => l is ReentryLeg); return; }
                if (headingHome) { prog.SkipTo(l => l is ReentryLeg || (l is CorrectionLeg && IndexAfterHome(prog, l))); return; }
                if (v.situation == Vessel.Situations.ORBITING && target == home) { prog.SkipTo(l => l is HoldOrbitLeg || l is RendezvousLeg || l is DeliverLeg || l is ReentryLeg); return; }
                if (v.situation == Vessel.Situations.ORBITING) { prog.SkipTo(l => l is TransferLeg || l is InterplanetaryTransferLeg); return; }
                // Still climbing or coming down from a hop: the ascent leg's Begin sorts that out.
                if (v.altitude < home.atmosphereDepth || v.orbit == null || v.orbit.ApA < ctx.ParkingAltitude * 0.9) return;
                if (v.situation == Vessel.Situations.ESCAPING) prog.SkipTo(l => l is CorrectionLeg || l is WaitForSoiLeg);
                return;
            }
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
