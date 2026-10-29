using System;
using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.Core.Careers
{
    /// <summary>Starting ranks and promotion rules (Jira KSA-19). Pure logic over the catalog and registry.</summary>
    public sealed class PromotionEngine
    {
        private readonly CareerCatalog _catalog;
        private readonly CrewRegistry _registry;

        public PromotionEngine(CareerCatalog catalog, CrewRegistry registry)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _catalog = catalog;
            _registry = registry;
        }

        /// <summary>Puts a new hire on the track for their role at its entry rank. No-op if already placed.</summary>
        public bool AssignStartingRank(CrewProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Track != CareerTrack.None && profile.RankId.Length > 0) return false;

            CareerTrack track = _catalog.TrackForRole(profile.Role);
            CareerTrackDefinition def = _catalog.GetTrack(track);
            if (def == null || def.EntryRank == null) return false;

            profile.Track = track;
            profile.RankId = def.EntryRank.Id;
            if (profile.Salary == 0) profile.Salary = def.EntryRank.Salary;
            return true;
        }

        /// <summary>True when the profile meets every requirement of the rank (ignores Director selection and slot caps).</summary>
        public static bool MeetsRequirements(CrewProfile profile, RankDefinition rank)
        {
            if (profile == null || rank == null) return false;
            if (profile.Stars < rank.MinStars) return false;
            if (MissionCredit(profile) < rank.MinMissions) return false;
            for (int i = 0; i < rank.RequiredAchievements.Count; i++)
                if (!profile.Record.HasAchievement(rank.RequiredAchievements[i])) return false;
            return true;
        }

        /// <summary>
        /// Completed missions, plus ground work for the tracks that do their work on the ground (2026-10-28): an engineer
        /// counts each mission flown on a design they led, a scientist each full year in the lab.
        /// </summary>
        public static int MissionCredit(CrewProfile profile)
        {
            if (profile == null) return 0;
            int n = profile.Record.CompletedMissionCount;
            if (profile.Track == CareerTrack.Engineering) n += profile.Record.DesignsFlown;
            else if (profile.Track == CareerTrack.Science) n += profile.Record.LabYears;
            return n;
        }

        public bool HasOpenSlot(RankDefinition rank)
        {
            if (rank == null) return false;
            if (rank.MaxSlots < 0) return true;
            return _registry.CountAtRank(rank.Id) < rank.MaxSlots;
        }

        /// <summary>
        /// The next rank the profile could be promoted to right now, or null.
        /// Director-selected ranks are never returned here; see <see cref="CanSelect"/> and <see cref="Select"/>.
        /// </summary>
        public RankDefinition NextAutomaticRank(CrewProfile profile)
        {
            if (profile == null || profile.Departed || profile.Status == CrewStatus.Dead) return null;
            CareerTrackDefinition def = _catalog.GetTrack(profile.Track);
            if (def == null) return null;
            RankDefinition next = def.NextRank(profile.RankId);
            if (next == null || next.DirectorSelected) return null;
            if (!MeetsRequirements(profile, next)) return null;
            if (!HasOpenSlot(next)) return null;
            return next;
        }

        /// <summary>Applies every automatic promotion the profile qualifies for, one rung at a time. Returns the ranks granted.</summary>
        public List<RankDefinition> PromoteAutomatically(CrewProfile profile)
        {
            var granted = new List<RankDefinition>();
            if (profile == null) return granted;
            for (int guard = 0; guard < 16; guard++)
            {
                RankDefinition next = NextAutomaticRank(profile);
                if (next == null) break;
                profile.RankId = next.Id;
                profile.Salary = Math.Max(profile.Salary, next.Salary);
                granted.Add(next);
            }
            return granted;
        }

        /// <summary>True when the next rank is Director-selected and the profile qualifies for it (e.g. Test Pilot ready to become Candidate).</summary>
        public bool CanSelect(CrewProfile profile)
        {
            RankDefinition next = PendingSelection(profile);
            return next != null;
        }

        /// <summary>The Director-selected rank waiting on a decision for this profile, or null.</summary>
        public RankDefinition PendingSelection(CrewProfile profile)
        {
            if (profile == null || profile.Departed || profile.Status == CrewStatus.Dead) return null;
            CareerTrackDefinition def = _catalog.GetTrack(profile.Track);
            if (def == null) return null;
            RankDefinition next = def.NextRank(profile.RankId);
            if (next == null || !next.DirectorSelected) return null;
            if (!MeetsRequirements(profile, next) || !HasOpenSlot(next)) return null;
            return next;
        }

        /// <summary>The Director grants the pending selected rank. Returns the rank granted, or null if nothing was pending.</summary>
        public RankDefinition Select(CrewProfile profile)
        {
            RankDefinition next = PendingSelection(profile);
            if (next == null) return null;
            profile.RankId = next.Id;
            profile.Salary = Math.Max(profile.Salary, next.Salary);
            return next;
        }
    }
}
