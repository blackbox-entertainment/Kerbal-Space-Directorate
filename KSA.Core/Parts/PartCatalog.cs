using System;
using System.Collections.Generic;

namespace KSA.Core.Parts
{
    public enum PartKind
    {
        Other = 0, Command, Tank, Engine, Booster, Decoupler, Parachute, HeatShield, Fairing, Docking,
        Habitat, Power, Rcs, LifeSupport, Science, Structural, LandingLeg, Wheel, Envelope, Antenna,
        /// <summary>Drills and ore converters (stock ISRU, Kerbalism chemical plants and harvesters). Appended so saved values stay put.</summary>
        Industry
    }

    public enum PartJob { LaunchVehicle = 0, StationModule = 1, BaseModule = 2, CloudCityModule = 3 }

    /// <summary>What the design pipeline needs to know about a part. Generic names; the host fills it from the game.</summary>
    public sealed class PartInfo
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public PartKind Kind { get; set; }
        /// <summary>Dry mass in tonnes.</summary>
        public double Mass { get; set; }
        /// <summary>Propellant mass in tonnes when full (tanks, boosters).</summary>
        public double FuelMass { get; set; }
        public double Cost { get; set; }
        public int CrewCapacity { get; set; }
        /// <summary>Vacuum thrust, kN.</summary>
        public double Thrust { get; set; }
        public double IspVacuum { get; set; }
        public double IspSeaLevel { get; set; }
        /// <summary>Stack diameter in metres (0 for radial-only parts).</summary>
        public double Diameter { get; set; }
        public bool HasTopNode { get; set; }
        public bool HasBottomNode { get; set; }
        public string Mod { get; set; }
        public bool RequiresDlc { get; set; }
        public List<string> Tags { get; } = new List<string>();

        public PartInfo() { Name = string.Empty; Title = string.Empty; Mod = "Stock"; }

        public bool IsStackable { get { return HasTopNode || HasBottomNode; } }
        public bool ProducesThrust { get { return Thrust > 0 && (Kind == PartKind.Engine || Kind == PartKind.Booster); } }

        public override string ToString() { return Name + " (" + Kind + ")"; }
    }

    public interface IPartCatalog
    {
        IReadOnlyList<PartInfo> All { get; }
        PartInfo Get(string name);
    }

    /// <summary>In-memory catalog, used by tests and as the host's cache.</summary>
    public sealed class PartCatalog : IPartCatalog
    {
        private readonly List<PartInfo> _parts = new List<PartInfo>();
        private readonly Dictionary<string, PartInfo> _byName = new Dictionary<string, PartInfo>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<PartInfo> All { get { return _parts; } }
        public int Count { get { return _parts.Count; } }

        public void Add(PartInfo p)
        {
            if (p == null || string.IsNullOrEmpty(p.Name)) return;
            if (_byName.ContainsKey(p.Name)) return;
            _parts.Add(p);
            _byName[p.Name] = p;
        }

        public PartInfo Get(string name)
        {
            PartInfo p;
            return name != null && _byName.TryGetValue(name, out p) ? p : null;
        }
    }

    /// <summary>Picks the parts a job needs, capped per kind, so prompts stay small (GDD 5.5, Jira KSA-42).</summary>
    public static class PartFilter
    {
        public static PartKind[] KindsFor(PartJob job)
        {
            switch (job)
            {
                case PartJob.StationModule:
                    return new[] { PartKind.Command, PartKind.Habitat, PartKind.Docking, PartKind.Power, PartKind.Rcs, PartKind.LifeSupport, PartKind.Science, PartKind.Structural, PartKind.Antenna,
                                   PartKind.Tank, PartKind.Engine, PartKind.Decoupler, PartKind.Booster, PartKind.Fairing };
                case PartJob.BaseModule:
                    return new[] { PartKind.Command, PartKind.Habitat, PartKind.Power, PartKind.LandingLeg, PartKind.Wheel, PartKind.LifeSupport, PartKind.Industry, PartKind.Science, PartKind.Structural, PartKind.Docking,
                                   PartKind.Tank, PartKind.Engine, PartKind.Decoupler, PartKind.Booster, PartKind.Fairing, PartKind.Antenna };
                case PartJob.CloudCityModule:
                    return new[] { PartKind.Envelope, PartKind.Command, PartKind.Habitat, PartKind.Power, PartKind.LifeSupport, PartKind.Structural, PartKind.Docking,
                                   PartKind.Tank, PartKind.Engine, PartKind.Decoupler, PartKind.Booster, PartKind.Fairing, PartKind.HeatShield };
                default:
                    return new[] { PartKind.Command, PartKind.Tank, PartKind.Engine, PartKind.Booster, PartKind.Decoupler, PartKind.Parachute, PartKind.HeatShield, PartKind.Fairing, PartKind.Structural, PartKind.Antenna, PartKind.Power };
            }
        }

        /// <summary>Parts for the job: allowed kinds only, DLC parts only if allowed, at most <paramref name="maxPerKind"/> of each kind (cheapest first).</summary>
        public static List<PartInfo> ForJob(IPartCatalog catalog, PartJob job, bool allowDlc, int maxPerKind = 12)
        {
            var result = new List<PartInfo>();
            if (catalog == null) return result;
            PartKind[] kinds = KindsFor(job);
            foreach (PartKind kind in kinds)
            {
                var ofKind = new List<PartInfo>();
                foreach (PartInfo p in catalog.All)
                {
                    if (p.Kind != kind) continue;
                    if (p.RequiresDlc && !allowDlc) continue;
                    ofKind.Add(p);
                }
                // Keep a spread of sizes: sort by diameter then cost so small and large options both survive the cap.
                ofKind.Sort((a, b) => a.Diameter != b.Diameter ? a.Diameter.CompareTo(b.Diameter) : a.Cost.CompareTo(b.Cost));
                if (ofKind.Count > maxPerKind)
                {
                    var kept = new List<PartInfo>();
                    double step = (double)ofKind.Count / maxPerKind;
                    for (int i = 0; i < maxPerKind; i++) kept.Add(ofKind[(int)Math.Floor(i * step)]);
                    ofKind = kept;
                }
                result.AddRange(ofKind);
            }
            return result;
        }

        public static HashSet<string> Names(IEnumerable<PartInfo> parts)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (parts != null) foreach (PartInfo p in parts) set.Add(p.Name);
            return set;
        }
    }
}
