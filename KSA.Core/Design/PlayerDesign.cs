using System;
using KSA.Core.Missions;

namespace KSA.Core.Design
{
    /// <summary>
    /// A rocket the Director built and saved in the vehicle editor, read by the host from the craft file (2026-10-28).
    /// The crews can fly it on projects instead of an AI or template design, when it fits the mission.
    /// </summary>
    public sealed class PlayerDesign
    {
        /// <summary>Craft name as saved.</summary>
        public string Name { get; set; }
        /// <summary>Full path of the craft file; also its identity.</summary>
        public string CraftPath { get; set; }
        public int Seats { get; set; }
        /// <summary>Total vacuum delta-v over all stages, m/s, worked out from the craft file.</summary>
        public double DeltaV { get; set; }
        /// <summary>Thrust-to-weight at liftoff, at sea level on the home world.</summary>
        public double LiftoffTwr { get; set; }
        public double Mass { get; set; }
        public double Cost { get; set; }
        public int PartCount { get; set; }
        public bool HasParachute { get; set; }
        /// <summary>Built in the spaceplane hangar: takes off from the runway (2026-10-28).</summary>
        public bool IsPlane { get; set; }
        /// <summary>Has landing gear (wheels): a plane lands on them instead of under a parachute.</summary>
        public bool HasWheels { get; set; }
        /// <summary>Has air-breathing engines; their thrust is not counted in DeltaV (rocket delta-v only).</summary>
        public bool HasJets { get; set; }

        /// <summary>
        /// Delta-v a plane's jets save the rockets on the way up (to about 20 km and 1,400 m/s), credited against the
        /// mission's requirement. Rough; the rest has to come from rocket engines.
        /// </summary>
        public const double JetCredit = 1200;
        /// <summary>Null when every part is researched (and bought), else why it cannot be built yet.</summary>
        public string ResearchBlocker { get; set; }

        public PlayerDesign() { Name = string.Empty; CraftPath = string.Empty; }

        /// <summary>
        /// Null when the design can fly the mission, else why not: research, seats, a parachute for a crew that comes home,
        /// and the mission's delta-v and liftoff thrust (the same yardstick the AI designs are held to).
        /// </summary>
        public string Blocker(MissionType type, MissionRequirements.Entry need)
        {
            if (ResearchBlocker != null) return ResearchBlocker;
            if (type == null) return "unknown mission";
            if (type.CrewSize > Seats) return "seats " + Seats + ", the mission needs " + type.CrewSize;
            if (IsPlane)
            {
                // Planes (2026-10-28): Kerbin missions from the runway, landing on their wheels; the Director flies them.
                if (!string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase)) return "planes fly Kerbin missions only";
                if (!HasWheels && !HasParachute) return "no landing gear or parachute: it could not land";
                double dv = DeltaV + (HasJets ? JetCredit : 0);
                if (need != null && dv < need.DeltaV) return Math.Round(dv) + " of " + Math.Round(need.DeltaV) + " m/s (rockets" + (HasJets ? " + jet climb" : string.Empty) + ")";
                return null;
            }
            if (type.CrewSize > 0 && type.Profile != MissionProfile.Base && !HasParachute) return "no parachute: the crew could not land";
            if (need != null && DeltaV < need.DeltaV) return Math.Round(DeltaV) + " of " + Math.Round(need.DeltaV) + " m/s";
            if (need != null && need.MinTwr > 0 && LiftoffTwr < need.MinTwr) return "liftoff TWR " + LiftoffTwr.ToString("0.00") + " of " + need.MinTwr.ToString("0.0");
            return null;
        }

        /// <summary>The design as an approved project design record, scored like an AI design for simulated flights.</summary>
        public DesignRecord ToRecord(MissionRequirements.Entry need)
        {
            var a = new DesignAnalysis { TotalDeltaV = DeltaV + (IsPlane && HasJets ? JetCredit : 0), LiftoffTwr = IsPlane ? Math.Max(LiftoffTwr, 1.5) : LiftoffTwr, RequiredDeltaV = need != null ? need.DeltaV : 0, RequiredTwr = need != null ? need.MinTwr : 0 };
            var r = new DesignRecord
            {
                Source = DesignSource.Player,
                Verdict = DesignVerdict.Approved,
                DesignName = Name,
                Rationale = "Designed by the Director in the vehicle editor.",
                CraftPath = CraftPath,
                TotalDeltaV = Math.Round(DeltaV),
                RequiredDeltaV = need != null ? need.DeltaV : 0,
                LiftoffTwr = Math.Round(LiftoffTwr, 2),
                RequiredTwr = need != null ? need.MinTwr : 0,
                TotalMass = Math.Round(Mass, 2),
                TotalCost = Math.Round(Cost),
                PartCount = PartCount,
                DesignScore = Math.Round(a.DesignScore, 3),
                IsPlane = IsPlane
            };
            return r;
        }
    }
}
