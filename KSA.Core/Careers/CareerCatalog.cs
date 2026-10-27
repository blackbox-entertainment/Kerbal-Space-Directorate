using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Personnel;

namespace KSA.Core.Careers
{
    /// <summary>
    /// All career tracks and ranks, parsed from config nodes named KSA_CAREER_TRACK.
    /// The host (KSA.KSP) hands over the nodes; this class has no idea where they came from.
    /// </summary>
    public sealed class CareerCatalog
    {
        public const string TrackNodeName = "KSA_CAREER_TRACK";
        public const string RankNodeName = "RANK";

        private readonly Dictionary<CareerTrack, CareerTrackDefinition> _tracks = new Dictionary<CareerTrack, CareerTrackDefinition>();

        public IEnumerable<CareerTrackDefinition> Tracks { get { return _tracks.Values; } }
        public int TrackCount { get { return _tracks.Count; } }

        public void Add(CareerTrackDefinition track)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            track.SortRanks();
            _tracks[track.Track] = track;
        }

        public CareerTrackDefinition GetTrack(CareerTrack track)
        {
            CareerTrackDefinition t;
            return _tracks.TryGetValue(track, out t) ? t : null;
        }

        /// <summary>The track a newly hired crew member of this role starts on, or None.</summary>
        public CareerTrack TrackForRole(CrewRole role)
        {
            foreach (CareerTrackDefinition t in _tracks.Values)
                if (t.EntryRoles.Contains(role)) return t.Track;
            return CareerTrack.None;
        }

        public RankDefinition GetRank(CareerTrack track, string rankId)
        {
            CareerTrackDefinition t = GetTrack(track);
            return t == null ? null : t.GetRank(rankId);
        }

        public string RankName(CareerTrack track, string rankId)
        {
            RankDefinition r = GetRank(track, rankId);
            return r != null ? r.Name : (string.IsNullOrEmpty(rankId) ? "Unranked" : rankId);
        }

        // --- Parsing ---

        /// <summary>Builds a catalog from every KSA_CAREER_TRACK node. Malformed nodes are skipped and reported via <paramref name="warnings"/>.</summary>
        public static CareerCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new CareerCatalog();
            if (nodes == null) return catalog;

            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, TrackNodeName, StringComparison.OrdinalIgnoreCase)) continue;

                string trackId = node.GetValue("id", string.Empty);
                CareerTrack track;
                if (!TryParseEnum(trackId, out track) || track == CareerTrack.None)
                {
                    Warn(warnings, "Career track with unknown id '" + trackId + "' skipped");
                    continue;
                }

                var def = new CareerTrackDefinition { Track = track, Name = node.GetValue("name", trackId) };
                foreach (string roleName in node.GetList("roles"))
                {
                    CrewRole role;
                    if (TryParseEnum(roleName, out role)) def.EntryRoles.Add(role);
                    else Warn(warnings, "Track " + trackId + ": unknown role '" + roleName + "'");
                }

                int autoOrder = 0;
                foreach (DataNode rankNode in node.GetChildren(RankNodeName))
                {
                    var rank = new RankDefinition
                    {
                        Id = rankNode.GetValue("id", string.Empty),
                        Name = rankNode.GetValue("name", rankNode.GetValue("id", string.Empty)),
                        Order = rankNode.GetInt("order", autoOrder),
                        MinStars = rankNode.GetInt("minStars", 0),
                        MinMissions = rankNode.GetInt("minMissions", 0),
                        MaxSlots = rankNode.GetInt("maxSlots", -1),
                        DirectorSelected = rankNode.GetBool("directorSelected", false),
                        Salary = rankNode.GetInt("salary", 0)
                    };
                    if (rank.Id.Length == 0)
                    {
                        Warn(warnings, "Track " + trackId + ": RANK without id skipped");
                        continue;
                    }
                    rank.RequiredAchievements.AddRange(rankNode.GetValues("requiresAchievement"));
                    rank.Perks.AddRange(rankNode.GetValues("perk"));
                    def.Ranks.Add(rank);
                    autoOrder++;
                }

                if (def.Ranks.Count == 0)
                {
                    Warn(warnings, "Track " + trackId + " has no ranks; skipped");
                    continue;
                }
                catalog.Add(def);
            }
            return catalog;
        }

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            return Enum.TryParse(value, true, out result) && Enum.IsDefined(typeof(T), result);
        }

        private static void Warn(List<string> warnings, string message)
        {
            if (warnings != null) warnings.Add(message);
        }
    }
}
