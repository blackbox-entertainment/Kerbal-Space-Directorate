using System;

namespace KSA.Core.Flight
{
    /// <summary>
    /// When to lift off so the ascent ends beside a station in low orbit (0.8.224, T: "it should have waited to lift off and
    /// increase the countdown after I hit launch if it needed time to align with the tanker"). Angles are in the orbit's
    /// plane, prograde positive; the pad turns with the world, the station round its orbit.
    /// </summary>
    public static class LaunchWindow
    {
        /// <summary>
        /// Seconds to wait before liftoff. <paramref name="stationAheadNow"/>: the station's angle ahead of the pad now.
        /// <paramref name="downrange"/>: the angle the ascent covers from the pad's position at liftoff to insertion, in
        /// <paramref name="ascentSeconds"/>. <paramref name="lead"/>: how far ahead of the ship the station should be at
        /// insertion. NaN when the station never comes round (it turns no faster than the world).
        /// </summary>
        public static double WaitSeconds(double stationAheadNow, double stationRate, double padRate, double downrange, double ascentSeconds, double lead)
        {
            double relRate = stationRate - padRate;
            if (relRate <= 0 || double.IsNaN(stationAheadNow)) return double.NaN;
            // At insertion the ship sits downrange of where the pad was at liftoff; the station has moved on by its rate times the ascent.
            double required = downrange + lead - stationRate * ascentSeconds;
            return Wrap(required - stationAheadNow) / relRate;
        }

        /// <summary>Into [0, 2 pi).</summary>
        public static double Wrap(double angle)
        {
            double turn = 2 * Math.PI;
            angle %= turn;
            if (angle < 0) angle += turn;
            return angle;
        }
    }
}
