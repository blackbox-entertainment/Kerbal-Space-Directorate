using System;
using System.Collections.Generic;
using KSA.Core.Missions;

namespace KSA.Core
{
    /// <summary>What the Director asks for when defining a mission (KSA-59).</summary>
    public sealed class CustomMissionSpec
    {
        public string Name { get; set; }
        public string Body { get; set; }
        public MissionProfile Profile { get; set; }
        /// <summary>Orbit / OrbitBody: full orbits to complete before coming home (at least 1).</summary>
        public int Orbits { get; set; }
        /// <summary>Land: days to stay on the surface (at least 0.1).</summary>
        public double SurfaceDays { get; set; }
        public int CrewSize { get; set; }
        /// <summary>Run every experiment aboard and bring the data home.</summary>
        public bool Science { get; set; }

        public CustomMissionSpec() { Name = string.Empty; Body = "Kerbin"; Profile = MissionProfile.Orbit; Orbits = 1; SurfaceDays = 1; CrewSize = 1; }
    }

    /// <summary>
    /// Director-defined missions: a synthetic mission type is built from the spec, priced from the body and profile,
    /// added to the catalog and saved with the game so the project survives reloads. Templates are matched by profile
    /// and body (any catalogue mission with the same shape).
    /// </summary>
    public sealed partial class Agency
    {
        public static readonly string[] CustomBodies =
        {
            "Kerbin", "Mun", "Minmus", "Duna", "Ike", "Eve", "Gilly", "Moho", "Dres", "Jool", "Laythe", "Vall", "Tylo", "Bop", "Pol", "Eeloo"
        };

        private static MissionTier TierFor(string body)
        {
            if (string.Equals(body, "Kerbin", StringComparison.OrdinalIgnoreCase)) return MissionTier.Early;
            if (string.Equals(body, "Mun", StringComparison.OrdinalIgnoreCase) || string.Equals(body, "Minmus", StringComparison.OrdinalIgnoreCase)) return MissionTier.Mid;
            return MissionTier.Late;
        }

        private static bool IsMoonOfHome(string body)
        {
            return string.Equals(body, "Mun", StringComparison.OrdinalIgnoreCase) || string.Equals(body, "Minmus", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Why the spec cannot be flown, or null.</summary>
        public string ValidateCustom(CustomMissionSpec spec)
        {
            if (spec == null) return "No mission.";
            bool home = string.Equals(spec.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            if (Array.IndexOf(CustomBodies, spec.Body) < 0) return "Unknown body " + spec.Body + ".";
            if (home && (spec.Profile == MissionProfile.Flyby || spec.Profile == MissionProfile.OrbitBody || spec.Profile == MissionProfile.Land)) return "At Kerbin choose Suborbital or Orbit.";
            if (!home && (spec.Profile == MissionProfile.Suborbital || spec.Profile == MissionProfile.Orbit)) return "Away from Kerbin choose Flyby, Orbit the body, or Land.";
            if (spec.Profile == MissionProfile.Station || spec.Profile == MissionProfile.Base) return "Stations and bases are Programs.";
            if (spec.Profile == MissionProfile.Land && (string.Equals(spec.Body, "Eve", StringComparison.OrdinalIgnoreCase) || string.Equals(spec.Body, "Jool", StringComparison.OrdinalIgnoreCase))) return "Nobody lands on " + spec.Body + "; cloud cities are a Program.";
            if (spec.CrewSize < 1 || spec.CrewSize > 6) return "Crew size must be 1 to 6.";
            if (spec.Orbits < 1 || spec.Orbits > 50) return "Orbits must be 1 to 50.";
            if (spec.SurfaceDays < 0 || spec.SurfaceDays > 400) return "Surface stay must be 0 to 400 days.";
            return null;
        }

        /// <summary>Builds the mission type (without adding it) so the UI can show cost and days before committing.</summary>
        public MissionType PreviewCustom(CustomMissionSpec spec)
        {
            bool home = string.Equals(spec.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            bool moon = IsMoonOfHome(spec.Body);
            MissionTier tier = TierFor(spec.Body);
            double baseCost; int days; double difficulty;
            switch (spec.Profile)
            {
                case MissionProfile.Suborbital: baseCost = 8000; days = 1; difficulty = 0.15; break;
                case MissionProfile.Orbit: baseCost = 15000 + 1500 * (spec.Orbits - 1); days = 1 + (int)Math.Ceiling(spec.Orbits / 12.0); difficulty = 0.3; break;
                case MissionProfile.Flyby: baseCost = moon ? 30000 : 90000; days = moon ? 6 : 90; difficulty = moon ? 0.4 : 0.6; break;
                case MissionProfile.OrbitBody: baseCost = (moon ? 40000 : 110000) + 1000 * (spec.Orbits - 1); days = (moon ? 8 : 100) + (int)Math.Ceiling(spec.Orbits / 8.0); difficulty = moon ? 0.5 : 0.65; break;
                default: baseCost = (moon ? 60000 : 150000) + 1200 * spec.SurfaceDays; days = (moon ? 10 : 120) + (int)Math.Ceiling(spec.SurfaceDays); difficulty = moon ? 0.65 : 0.8; break;
            }
            if (!home && !moon)
            {
                // Farther worlds cost and take more: scale by distance class.
                double far = BodyDistances.FromHomeKm(spec.Body);
                double scale = far > 50000000 ? 2.2 : far > 20000000 ? 1.6 : far > 7000000 ? 1.2 : 1.0;
                baseCost *= scale; days = (int)Math.Round(days * scale); difficulty = Math.Min(0.95, difficulty + (scale - 1) * 0.15);
            }
            baseCost += (spec.CrewSize - 1) * 4000;
            if (spec.Science) baseCost += 3000;
            var t = new MissionType
            {
                Id = "Custom-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = spec.Name.Length > 0 ? spec.Name : DefaultName(spec),
                Description = "Director-defined mission: " + DefaultName(spec) + ".",
                Tier = tier,
                Body = spec.Body,
                Profile = spec.Profile,
                CrewSize = spec.CrewSize,
                MinStars = tier == MissionTier.Late ? 2 : tier == MissionTier.Mid ? 1 : 0,
                DurationDays = Math.Max(1, days),
                BaseCost = Math.Round(baseCost),
                Difficulty = difficulty,
                Orbits = spec.Orbits,
                SurfaceDays = spec.Profile == MissionProfile.Land ? Math.Max(0.1, spec.SurfaceDays) : 0,
                Purpose = spec.Science ? "Science" : string.Empty,
                ScienceYield = spec.Science ? (tier == MissionTier.Late ? 200 : tier == MissionTier.Mid ? 90 : spec.Profile == MissionProfile.Orbit ? 40 : 20) : 0,
                IsCustom = true
            };
            t.Achievements.Add("Flight:Kerbin");
            if (spec.Profile != MissionProfile.Suborbital) t.Achievements.Add("Orbit:Kerbin"); else t.Achievements.Add("Suborbit:Kerbin");
            if (!home && !moon) t.Achievements.Add("Escape:Kerbin");
            if (!home) t.Achievements.Add("Flyby:" + spec.Body);
            if (spec.Profile == MissionProfile.OrbitBody || spec.Profile == MissionProfile.Land) t.Achievements.Add("Orbit:" + spec.Body);
            if (spec.Profile == MissionProfile.Land) { t.Achievements.Add("Land:" + spec.Body); t.Achievements.Add("EVA:" + spec.Body); }
            if (spec.Science) t.Achievements.Add("Science:" + spec.Body);
            t.Achievements.Add("Recover:-");
            return t;
        }

        public static string DefaultName(CustomMissionSpec spec)
        {
            switch (spec.Profile)
            {
                case MissionProfile.Suborbital: return "Kerbin suborbital flight";
                case MissionProfile.Orbit: return spec.Orbits + " orbit" + (spec.Orbits == 1 ? "" : "s") + " of Kerbin";
                case MissionProfile.Flyby: return spec.Body + " flyby";
                case MissionProfile.OrbitBody: return spec.Orbits + " orbit" + (spec.Orbits == 1 ? "" : "s") + " of " + spec.Body;
                default: return spec.Body + " landing, " + spec.SurfaceDays.ToString("0.#") + " day" + (Math.Abs(spec.SurfaceDays - 1) < 0.01 ? "" : "s") + " on the surface";
            }
        }

        /// <summary>Adds the custom mission type to the catalog (it is saved with the game). Returns it, or null with a reason.</summary>
        public MissionType DefineCustomMission(CustomMissionSpec spec, out string message)
        {
            message = ValidateCustom(spec);
            if (message != null) return null;
            MissionType t = PreviewCustom(spec);
            string blocked = MissionBlocker(t);
            if (blocked != null) { message = t.Name + " " + blocked + "."; return null; }
            Catalogs.Missions.Add(t);
            message = "Mission defined: " + t.Name + " (" + Money(t.BaseCost) + ", " + t.DurationDays + " days in flight).";
            return t;
        }

        /// <summary>Templates able to fly a mission type: by id for catalogue types, by shape (profile and body, then profile) for custom ones.</summary>
        public List<Template> TemplatesFor(MissionType type, string moduleRole = null)
        {
            if (type == null) return new List<Template>();
            if (!type.IsCustom) return Catalogs.Templates.For(type.Id, InstalledMods, moduleRole);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byBody = new List<Template>();
            var byProfile = new List<Template>();
            foreach (MissionType other in Catalogs.Missions.Types)
            {
                if (other.IsCustom || other.Profile != type.Profile || !string.IsNullOrEmpty(other.ModuleRole)) continue;
                bool sameBody = string.Equals(other.Body, type.Body, StringComparison.OrdinalIgnoreCase);
                foreach (Template t in Catalogs.Templates.For(other.Id, InstalledMods))
                {
                    if (!ids.Add(t.Id)) continue;
                    if (sameBody) byBody.Add(t); else byProfile.Add(t);
                }
            }
            byBody.AddRange(byProfile);
            // Too few seats for the crew and the kerbals a rescue brings home: not a fit (2026-10-28).
            byBody.RemoveAll(t => t.Seats > 0 && t.Seats < type.Seats);
            return byBody;
        }
    }
}
