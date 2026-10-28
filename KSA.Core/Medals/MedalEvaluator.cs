using System;
using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.Core.Medals
{
    /// <summary>Decides which medals a profile has newly earned. Awarding (adding to the record) is done here too.</summary>
    public sealed class MedalEvaluator
    {
        private readonly MedalCatalog _catalog;
        private readonly CrewRegistry _registry;
        /// <summary>Name of the home world, for the worlds-visited count.</summary>
        public string HomeBody { get; set; } = "Kerbin";

        public MedalEvaluator(MedalCatalog catalog, CrewRegistry registry)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _catalog = catalog;
            _registry = registry;
        }

        /// <summary>True when the medal's rule is satisfied by the record, ignoring whether it was already awarded.</summary>
        public bool Qualifies(CrewProfile profile, MedalDefinition medal)
        {
            if (profile == null || medal == null) return false;
            bool dead = profile.Status == CrewStatus.Dead;
            if (dead && !medal.Posthumous) return false;
            if (!dead && medal.Posthumous) return false;
            if (medal.RequiresAlive && dead) return false;

            switch (medal.Trigger)
            {
                case MedalTrigger.Achievement:
                    for (int i = 0; i < medal.Achievements.Count; i++)
                        if (!profile.Record.HasAchievement(medal.Achievements[i])) return false;
                    return true;
                case MedalTrigger.DaysInSpace:
                    return profile.Record.DaysInSpace >= medal.Threshold;
                case MedalTrigger.Incident:
                    return profile.Record.HasIncident(medal.IncidentKind);
                case MedalTrigger.FarthestDistance:
                    return profile.Record.FarthestKm >= medal.Threshold;
                case MedalTrigger.DaysOnOtherWorlds:
                    return profile.Record.DaysOnOtherWorlds >= medal.Threshold;
                case MedalTrigger.WorldsVisited:
                    return profile.Record.WorldsVisited(HomeBody) >= medal.Threshold;
                case MedalTrigger.LongestMission:
                    return profile.Record.LongestMissionDays >= medal.Threshold;
                default:
                    return false;
            }
        }

        /// <summary>Awards every medal the profile newly qualifies for. Returns the medals granted this call.</summary>
        public List<MedalDefinition> Evaluate(CrewProfile profile, int day, string missionId = null)
        {
            var granted = new List<MedalDefinition>();
            if (profile == null) return granted;

            foreach (MedalDefinition medal in _catalog.Medals)
            {
                if (medal.Trigger == MedalTrigger.Manual) continue;
                if (profile.Record.HasMedal(medal.Id)) continue;
                if (medal.FirstInAgency && _registry.AnyHasMedal(medal.Id)) continue;
                if (!Qualifies(profile, medal)) continue;

                profile.Record.Medals.Add(new MedalAward
                {
                    MedalId = medal.Id,
                    Day = day,
                    MissionId = missionId ?? string.Empty,
                    Note = medal.FirstInAgency ? "First in the agency" : string.Empty
                });
                granted.Add(medal);
            }
            return granted;
        }
    }
}
