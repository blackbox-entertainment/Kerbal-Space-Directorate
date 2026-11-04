using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;

namespace KSA.KSP.Flight
{
    /// <summary>The next transfer window from the home planet, by the same search the crew flies (InterplanetaryTransferLeg).</summary>
    internal static class TransferWindows
    {
        private const double NearDays = 30, NearPrice = 1.3;
        private const double ArrivalDays = 12; // capture, aerobraking passes and the landing at the far end
        private static readonly Dictionary<string, KeyValuePair<double, double>> Cache = new Dictionary<string, KeyValuePair<double, double>>();

        /// <summary>Days out, a wait for the way home and back, by the sums, or NaN for the home planet's own system.</summary>
        public static double RoundTrip(string bodyName)
        {
            CelestialBody home = FlightGlobals.GetHomeBody();
            CelestialBody planet = MissionPlanner.Body(bodyName);
            if (home == null || planet == null || home.referenceBody == null) return double.NaN;
            CelestialBody sun = home.referenceBody;
            while (planet.referenceBody != null && planet.referenceBody != sun && planet.referenceBody != planet) planet = planet.referenceBody;
            if (planet == home || planet == sun || planet.referenceBody != sun) return double.NaN;
            double flight;
            Ejection.Hohmann(sun.gravParameter, home.orbit.semiMajorAxis, planet.orbit.semiMajorAxis, out flight);
            // The stay for the window home (0.8.193): on arrival home sits n_h x flight - pi ahead of the planet; the Hohmann back
            // needs pi - n_h x flight, and the two drift apart at n_h - n_t. A quarter of a synodic period (228 days at Duna) had
            // stood in for it; the real wait there is about 450, and a crew left with 340 days of water on a 1,150-day trip.
            double nh = 2 * Math.PI / home.orbit.period, nt = 2 * Math.PI / planet.orbit.period, drift = nh - nt;
            double needed = 2 * Math.PI - 2 * nh * flight;
            if (drift < 0) { needed = -needed; drift = -drift; }
            double wait = ((needed % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI) / drift;
            return (2 * flight + wait) / TransferSupport.Day + ArrivalDays;
        }

        /// <summary>Days to the window for a world, or NaN for the home planet's own system or anything unknown.</summary>
        public static double Days(string bodyName)
        {
            CelestialBody home = FlightGlobals.GetHomeBody();
            CelestialBody target = MissionPlanner.Body(bodyName);
            if (home == null || target == null || home.referenceBody == null) return double.NaN;
            CelestialBody sun = home.referenceBody;
            CelestialBody planet = target;
            while (planet.referenceBody != null && planet.referenceBody != sun && planet.referenceBody != planet) planet = planet.referenceBody;
            if (planet == home || planet == sun || planet.referenceBody != sun) return double.NaN;

            double now = Planetarium.GetUniversalTime(), day = TransferSupport.Day;
            KeyValuePair<double, double> known;
            // A date worked out later in the game means nothing after loading an earlier save (2026-11-01: 9 days away shown as 2,763).
            if (Cache.TryGetValue(planet.bodyName, out known) && known.Key > now && known.Value <= now) return (known.Key - now) / day;

            double t1 = home.orbit.period, t2 = planet.orbit.period;
            double synodic = 1 / Math.Abs(1 / t1 - 1 / t2), flight;
            Ejection.Hohmann(sun.gravParameter, home.orbit.semiMajorAxis, planet.orbit.semiMajorAxis, out flight);
            TransferPlan plan = TransferPlanner.Plan(OrbitAdapter.Sampler(home.orbit), OrbitAdapter.Sampler(planet.orbit), sun.gravParameter,
                now + day, now + 1.1 * synodic, 0.5 * flight, 1.6 * flight, 60, 30, 0.5);
            if (plan == null) return double.NaN;
            // The same choice the crew makes (InterplanetaryTransferLeg): a window close by beats a slightly cheaper one years off (2026-11-01: 9 days shown as 914).
            TransferPlan near = TransferPlanner.Plan(OrbitAdapter.Sampler(home.orbit), OrbitAdapter.Sampler(planet.orbit), sun.gravParameter,
                now + 600, now + NearDays * day, 0.5 * flight, 1.6 * flight, 40, 30, 0.5);
            if (near != null && near.DeltaVMagnitude <= plan.DeltaVMagnitude * NearPrice) plan = near;
            Cache[planet.bodyName] = new KeyValuePair<double, double>(plan.DepartureUt, now);
            return (plan.DepartureUt - now) / day;
        }
    }
}
