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

    /// <summary>
    /// Morale deltas per event, from KSA_MORALE_RULE nodes with built-in defaults (Jira KSA-30). Losing a colleague
    /// (CrewDeath, ColleagueFired) scales with how small the agency is: the configured delta is the hit for a roster
    /// of ReferenceRoster, and it grows as the square root of how much smaller the remaining roster is
    /// (KSA_MORALE_SCALING node). Four kerbals losing one feel it far more than a hundred losing one.
    /// </summary>
    public sealed class MoraleRules
    {
        public const string NodeName = "KSA_MORALE_RULE";
        public const string ScalingNodeName = "KSA_MORALE_SCALING";

        /// <summary>Roster size at which a loss hits exactly the configured delta.</summary>
        public int ReferenceRoster { get; set; } = 12;
        public double MinScale { get; set; } = 0.25;
        public double MaxScale { get; set; } = 3.0;

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

        /// <summary>The scale factor for a loss felt by a roster of <paramref name="remaining"/> kerbals.</summary>
        public double LossScale(int remaining)
        {
            if (remaining < 1) remaining = 1;
            int reference = ReferenceRoster < 1 ? 1 : ReferenceRoster;
            double f = Math.Sqrt((double)reference / remaining);
            if (f < MinScale) f = MinScale;
            if (f > MaxScale) f = MaxScale;
            return f;
        }

        /// <summary>The delta for an event, scaled by how many kerbals are left to feel it.</summary>
        public int ScaledDelta(MoraleEvent e, int remaining)
        {
            return (int)Math.Round(Delta(e) * LossScale(remaining), MidpointRounding.AwayFromZero);
        }

        /// <summary>Applies an event to a profile, clamping 0..100. Returns the new morale.</summary>
        /// <summary>Optional: personality scaling of every morale change (Steady halves losses, Showboat amplifies gains).</summary>
        public PersonalityRules Personality { get; set; }

        public int Apply(CrewProfile p, MoraleEvent e)
        {
            return ApplyDelta(p, Scale(p, Delta(e)));
        }

        private int Scale(CrewProfile p, int delta)
        {
            if (Personality == null || p == null || delta == 0) return delta;
            double f = Personality.MoraleFactor(p, delta < 0);
            return (int)Math.Round(delta * f, MidpointRounding.AwayFromZero);
        }

        /// <summary>Applies a roster-scaled event (a colleague lost). Returns the new morale.</summary>
        public int ApplyScaled(CrewProfile p, MoraleEvent e, int remaining)
        {
            return ApplyDelta(p, Scale(p, ScaledDelta(e, remaining)));
        }

        private static int ApplyDelta(CrewProfile p, int delta)
        {
            if (p == null) return 0;
            int m = p.Morale + delta;
            p.Morale = m < 0 ? 0 : (m > 100 ? 100 : m);
            return p.Morale;
        }

        /// <summary>Parses KSA_MORALE_RULE and KSA_MORALE_SCALING nodes; other node types are ignored.</summary>
        public static MoraleRules Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var rules = new MoraleRules();
            if (nodes == null) return rules;
            foreach (DataNode node in nodes)
            {
                if (node == null) continue;
                if (string.Equals(node.Name, ScalingNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    rules.ReferenceRoster = node.GetInt("referenceRoster", rules.ReferenceRoster);
                    rules.MinScale = node.GetDouble("minScale", rules.MinScale);
                    rules.MaxScale = node.GetDouble("maxScale", rules.MaxScale);
                    if (rules.MaxScale < rules.MinScale) rules.MaxScale = rules.MinScale;
                    continue;
                }
                if (!string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
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
