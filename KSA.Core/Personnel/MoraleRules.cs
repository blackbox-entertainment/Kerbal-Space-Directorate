using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Personnel
{
    public enum MoraleEvent
    {
        MissionSuccess,
        MissionPartial,
        MissionFailure,
        CrewDeath,
        OwnMedal,
        OwnPromotion,
        PassedOver,
        LeanYear,
        CrisisYear,
        GoodYear,
        Idle,
        ColleagueFired,
        Commendation
    }

    public enum DepartureReason { None = 0, Quit = 1, Retired = 2, Fired = 3, Died = 4 }

    /// <summary>Morale deltas per event, from KSA_MORALE_RULE nodes with built-in defaults (Jira KSA-30).</summary>
    public sealed class MoraleRules
    {
        public const string NodeName = "KSA_MORALE_RULE";

        private readonly Dictionary<MoraleEvent, int> _deltas = new Dictionary<MoraleEvent, int>
        {
            { MoraleEvent.MissionSuccess, 5 },
            { MoraleEvent.MissionPartial, 1 },
            { MoraleEvent.MissionFailure, -8 },
            { MoraleEvent.CrewDeath, -15 },
            { MoraleEvent.OwnMedal, 8 },
            { MoraleEvent.OwnPromotion, 10 },
            { MoraleEvent.PassedOver, -12 },
            { MoraleEvent.LeanYear, -10 },
            { MoraleEvent.CrisisYear, -20 },
            { MoraleEvent.GoodYear, 5 },
            { MoraleEvent.Idle, -6 },
            { MoraleEvent.ColleagueFired, -4 },
            { MoraleEvent.Commendation, 12 }
        };

        public int Delta(MoraleEvent e)
        {
            int d;
            return _deltas.TryGetValue(e, out d) ? d : 0;
        }

        public void Set(MoraleEvent e, int delta) { _deltas[e] = delta; }

        /// <summary>Applies an event to a profile, clamping 0..100. Returns the new morale.</summary>
        public int Apply(CrewProfile p, MoraleEvent e)
        {
            if (p == null) return 0;
            int m = p.Morale + Delta(e);
            p.Morale = m < 0 ? 0 : (m > 100 ? 100 : m);
            return p.Morale;
        }

        public static MoraleRules Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var rules = new MoraleRules();
            if (nodes == null) return rules;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string eventName = node.GetValue("event", string.Empty);
                MoraleEvent e;
                if (!Enum.TryParse(eventName, true, out e) || !Enum.IsDefined(typeof(MoraleEvent), e))
                {
                    if (warnings != null) warnings.Add("Morale rule with unknown event '" + eventName + "' skipped");
                    continue;
                }
                rules.Set(e, node.GetInt("delta", rules.Delta(e)));
            }
            return rules;
        }
    }
}
