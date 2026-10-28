using System.Collections.Generic;

namespace KSA.Core.Parts
{
    /// <summary>A technology that can be researched right now: its prerequisites are done and it is not yet bought.</summary>
    public sealed class TechOption
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public double ScienceCost { get; set; }
        /// <summary>What it unlocks, in plain words ("liquid engine, fuel tanks, decoupler").</summary>
        public string Unlocks { get; set; }
        public int PartCount { get; set; }

        public TechOption() { Id = string.Empty; Title = string.Empty; Unlocks = string.Empty; }
    }

    /// <summary>
    /// The host's research tree (KSA-68): what can be researched now, and spending Science to research it (parts are
    /// bought with funds where the game requires it). Null in sandbox and in tests that do not need it.
    /// </summary>
    public interface IResearchHost
    {
        double Science { get; }
        List<TechOption> Options();
        bool Research(string techId, out string message);
    }
}
