using System.Collections.Generic;

namespace KSA.Core.Personnel
{
    /// <summary>
    /// A roster row plus the extra stock data the agency mirrors: traits and the host's achievement log.
    /// Produced by the host (KSA.KSP) from its own crew objects; consumed by <see cref="CrewRegistry"/>.
    /// </summary>
    public sealed class CrewSnapshot : CrewRecord
    {
        /// <summary>0..1</summary>
        public float Courage { get; }
        /// <summary>0..1</summary>
        public float Stupidity { get; }
        /// <summary>Achievement keys in "Type:Body" form (see <see cref="ServiceRecord.Achievements"/>).</summary>
        public IReadOnlyList<string> Achievements { get; }

        public CrewSnapshot(string name, CrewRole role, int stars, CrewStatus status,
                            float courage, float stupidity, IReadOnlyList<string> achievements)
            : base(name, role, stars, status)
        {
            Courage = courage < 0f ? 0f : (courage > 1f ? 1f : courage);
            Stupidity = stupidity < 0f ? 0f : (stupidity > 1f ? 1f : stupidity);
            Achievements = achievements ?? new string[0];
        }
    }
}
