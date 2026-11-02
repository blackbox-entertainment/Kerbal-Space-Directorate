using System;
using System.Collections.Generic;
using KSA.Core.Flight.Orbital;

namespace KSA.KSP.Flight
{
    /// <summary>The next transfer window from the home planet, by the same search the crew flies (InterplanetaryTransferLeg).</summary>
    internal static class TransferWindows
    {
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
            double flight, synodic = 1 / Math.Abs(1 / home.orbit.period - 1 / planet.orbit.period);
            Ejection.Hohmann(sun.gravParameter, home.orbit.semiMajorAxis, planet.orbit.semiMajorAxis, out flight);
            return (2 * flight + 0.25 * synodic) / TransferSupport.Day;
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
            if (Cache.TryGetValue(planet.bodyName, out known) && known.Key > now) return (known.Key - now) / day;

            double t1 = home.orbit.period, t2 = planet.orbit.period;
            double synodic = 1 / Math.Abs(1 / t1 - 1 / t2), flight;
            Ejection.Hohmann(sun.gravParameter, home.orbit.semiMajorAxis, planet.orbit.semiMajorAxis, out flight);
            TransferPlan plan = TransferPlanner.Plan(OrbitAdapter.Sampler(home.orbit), OrbitAdapter.Sampler(planet.orbit), sun.gravParameter,
                now + day, now + 1.1 * synodic, 0.5 * flight, 1.6 * flight, 60, 30, 0.5);
            if (plan == null) return double.NaN;
            Cache[planet.bodyName] = new KeyValuePair<double, double>(plan.DepartureUt, 0);
            return (plan.DepartureUt - now) / day;
        }
    }
}
