using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Ai
{
    /// <summary>Which flights the AI Director flies live (KSA-68). The player can still switch any one project in Mission Control.</summary>
    public enum DirectorFlights { Simulate = 0, Firsts = 1, All = 2 }

    /// <summary>
    /// How the AI Director runs (KSA-68): the AI_DIRECTOR block of KSA_AGENCY_SETTINGS. It takes a turn every
    /// turnIntervalDays of game time, sooner (but no closer than minGapDays) when something happens: a mission
    /// resolves, an applicant arrives, the year ends, a crew is stranded.
    /// </summary>
    public sealed class DirectorRules
    {
        public const string NodeName = "AI_DIRECTOR";

        public int TurnIntervalDays { get; set; } = 30;
        public int MinGapDays { get; set; } = 5;
        public int MaxActionsPerTurn { get; set; } = 6;
        public int TimeoutSeconds { get; set; } = 120;
        /// <summary>Seconds the launch hold counts down before the AI gives the go (the player can press LAUNCH sooner).</summary>
        public int GoSeconds { get; set; } = 10;
        /// <summary>Seconds after a live flight ends without a recovery before the game returns to the Space Center.</summary>
        public int ReturnSeconds { get; set; } = 15;
        /// <summary>Rough USD per turn for the spend cap when the model's price is unknown.</summary>
        public double CostPerTurnUsd { get; set; } = 0.002;
        /// <summary>Log entries kept in the save.</summary>
        public int LogKept { get; set; } = 120;
        /// <summary>
        /// Once every goal of the year is met, save money (2026-10-30): spend only what was earned above the year's opening
        /// balance; new projects, programs, drives and hires wait for next year unless they fit in that.
        /// </summary>
        public bool ConserveWhenGoalsMet { get; set; } = true;

        public static DirectorRules Parse(DataNode node)
        {
            var r = new DirectorRules();
            if (node == null) return r;
            r.TurnIntervalDays = node.GetInt("turnIntervalDays", r.TurnIntervalDays);
            r.MinGapDays = node.GetInt("minGapDays", r.MinGapDays);
            r.MaxActionsPerTurn = node.GetInt("maxActionsPerTurn", r.MaxActionsPerTurn);
            r.TimeoutSeconds = node.GetInt("timeoutSeconds", r.TimeoutSeconds);
            r.GoSeconds = node.GetInt("goSeconds", r.GoSeconds);
            r.ReturnSeconds = node.GetInt("returnSeconds", r.ReturnSeconds);
            r.CostPerTurnUsd = node.GetDouble("costPerTurnUSD", r.CostPerTurnUsd);
            r.LogKept = node.GetInt("logKept", r.LogKept);
            r.ConserveWhenGoalsMet = node.GetBool("conserveWhenGoalsMet", r.ConserveWhenGoalsMet);
            return r;
        }
    }

    /// <summary>One line in the Director's log.</summary>
    public sealed class DirectorLogEntry
    {
        public int Day { get; set; }
        public int Year { get; set; }
        /// <summary>Thought (the AI's reasoning), Action (what it did and why), Failed (refused, with the reason), Note (system).</summary>
        public string Kind { get; set; }
        public string Text { get; set; }

        public DirectorLogEntry() { Kind = "Note"; Text = string.Empty; }
    }

    /// <summary>The model presets offered in the Director window (OpenRouter ids). Prices checked 2026-10-28.</summary>
    public static class DirectorModels
    {
        public sealed class Preset { public string Id; public string Label; public string Note; }

        public static readonly List<Preset> Presets = new List<Preset>
        {
            new Preset { Id = "anthropic/claude-haiku-5.5", Label = "Claude Haiku 5.5", Note = "recommended: about 1 cent per game year" },
            new Preset { Id = "deepseek/deepseek-v4-flash", Label = "DeepSeek V4 Flash", Note = "cheapest: about 1 cent per game year" },
            new Preset { Id = "google/gemini-3.1-flash-lite", Label = "Gemini 3.1 Flash Lite", Note = "about 3 cents per game year" },
            new Preset { Id = "anthropic/claude-sonnet-5.5", Label = "Claude Sonnet 5.5", Note = "premium planner: about 26 cents per game year" }
        };
    }
}
