using System;
using System.Collections.Generic;

namespace KSA.Core.Missions
{
    /// <summary>
    /// How far each stock body is from Kerbin, in km, for the exploration record of simulated missions (KSA-60):
    /// moons by their orbit radius around Kerbin, planets by the difference of their mean orbital radius from
    /// Kerbin's (a typical closest approach), moons of other planets by their parent's figure.
    /// Live flights measure the real distance instead.
    /// </summary>
    public static class BodyDistances
    {
        /// <summary>Kerbin's radius, km: live flights measure their farthest point from the surface (0.7.12).</summary>
        public const double HomeRadiusKm = 600;
        private static readonly Dictionary<string, double> Km = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Kerbin", 0 }, { "Mun", 12000 }, { "Minmus", 47000 },
            { "Moho", 8340000 }, { "Eve", 3780000 }, { "Gilly", 3780000 },
            { "Duna", 7130000 }, { "Ike", 7130000 }, { "Dres", 27200000 },
            { "Jool", 55100000 }, { "Laythe", 55100000 }, { "Vall", 55100000 }, { "Tylo", 55100000 }, { "Bop", 55100000 }, { "Pol", 55100000 },
            { "Eeloo", 76500000 }
        };

        public static double FromHomeKm(string body)
        {
            double d;
            return body != null && Km.TryGetValue(body, out d) ? d : 0;
        }

        /// <summary>Days a simulated landing mission spends on the surface: a fifth of the mission, at least one day.</summary>
        public static double SurfaceDaysFor(MissionType type)
        {
            if (type == null || !(type.Profile == MissionProfile.Land || type.Profile == MissionProfile.Base)) return 0;
            if (type.SurfaceDays > 0) return type.SurfaceDays;
            return Math.Max(1, Math.Round(type.DurationDays * 0.2));
        }
    }
}
