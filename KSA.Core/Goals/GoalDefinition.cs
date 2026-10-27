using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Goals
{
    public enum GoalTier { Early = 0, Mid = 1, Late = 2, Program = 3, Conditional = 4 }

    public enum GoalConditionType
    {
        /// <summary>Any crew member (past or present) holds the achievement pattern, e.g. "Orbit:Kerbin".</summary>
        Achievement = 0,
        /// <summary>At least Threshold missions completed this fiscal year.</summary>
        MissionsThisYear = 1,
        /// <summary>No crew deaths this fiscal year.</summary>
        NoCrewLoss = 2,
        /// <summary>Year-end balance at or above year-start balance.</summary>
        UnderBudget = 3
    }

    public enum GoalStatus { Active = 0, Met = 1, Missed = 2 }

    /// <summary>A government Program Goal, loaded from config (Jira KSA-27).</summary>
    public sealed class GoalDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public GoalTier Tier { get; set; }
        public GoalConditionType Condition { get; set; }
        /// <summary>Achievement pattern for Achievement goals.</summary>
        public string Pattern { get; set; }
        public int Threshold { get; set; }
        /// <summary>Selection weight within its tier.</summary>
        public int Weight { get; set; }
        /// <summary>Can be issued again in later years (true for conditional goals, false for one-off firsts).</summary>
        public bool Repeatable { get; set; }
        /// <summary>Carries over to the next year if not met, instead of being marked missed.</summary>
        public bool MultiYear { get; set; }

        public GoalDefinition()
        {
            Id = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Pattern = string.Empty;
            Weight = 1;
        }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    /// <summary>One goal as issued in a given year.</summary>
    public sealed class GoalState
    {
        public string GoalId { get; set; }
        public int YearIssued { get; set; }
        public int YearResolved { get; set; }
        public GoalStatus Status { get; set; }

        public GoalState() { GoalId = string.Empty; YearResolved = -1; }
    }

    public sealed class GoalCatalog
    {
        public const string NodeName = "KSA_GOAL";

        private readonly List<GoalDefinition> _goals = new List<GoalDefinition>();

        public IReadOnlyList<GoalDefinition> Goals { get { return _goals; } }
        public int Count { get { return _goals.Count; } }

        public void Add(GoalDefinition goal)
        {
            if (goal == null) throw new ArgumentNullException(nameof(goal));
            _goals.Add(goal);
        }

        public GoalDefinition Get(string id)
        {
            for (int i = 0; i < _goals.Count; i++)
                if (string.Equals(_goals[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _goals[i];
            return null;
        }

        public string NameOf(string id)
        {
            GoalDefinition g = Get(id);
            return g != null ? g.Name : id;
        }

        public static GoalCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new GoalCatalog();
            if (nodes == null) return catalog;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0) { Warn(warnings, "KSA_GOAL without id skipped"); continue; }

                GoalTier tier;
                GoalConditionType condition;
                if (!TryEnum(node.GetValue("tier", "Early"), out tier)) { Warn(warnings, "Goal " + id + ": unknown tier"); continue; }
                if (!TryEnum(node.GetValue("condition", "Achievement"), out condition)) { Warn(warnings, "Goal " + id + ": unknown condition"); continue; }

                var g = new GoalDefinition
                {
                    Id = id,
                    Name = node.GetValue("name", id),
                    Description = node.GetValue("description", string.Empty),
                    Tier = tier,
                    Condition = condition,
                    Pattern = node.GetValue("pattern", string.Empty),
                    Threshold = node.GetInt("threshold", 1),
                    Weight = Math.Max(1, node.GetInt("weight", 1)),
                    Repeatable = node.GetBool("repeatable", condition != GoalConditionType.Achievement),
                    MultiYear = node.GetBool("multiYear", false)
                };
                if (g.Condition == GoalConditionType.Achievement && g.Pattern.Length == 0)
                {
                    Warn(warnings, "Goal " + id + ": Achievement condition needs a pattern; skipped");
                    continue;
                }
                catalog.Add(g);
            }
            return catalog;
        }

        private static bool TryEnum<T>(string text, out T value) where T : struct
        {
            return Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value);
        }

        private static void Warn(List<string> warnings, string message)
        {
            if (warnings != null) warnings.Add(message);
        }
    }
}
