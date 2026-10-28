using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Personnel
{
    /// <summary>
    /// What the personality tags do (built 2026-10-27). Every number comes from the PERSONALITY block of
    /// KSA_AGENCY_SETTINGS; a tag with no entry has no effect. Effects stack when a kerbal has two tags.
    /// </summary>
    public sealed class PersonalityRules
    {
        public const string NodeName = "PERSONALITY";

        public sealed class Effect
        {
            public string Tag = string.Empty;
            /// <summary>Added to the pilot's nerve (courage) when flying: + thinner margins.</summary>
            public double Nerve;
            /// <summary>Multiplier on the per-leg mistake chance.</summary>
            public double MistakeFactor = 1;
            /// <summary>Added to flight precision.</summary>
            public double Precision;
            /// <summary>Multiplier on morale changes that are losses / gains.</summary>
            public double MoraleLossFactor = 1;
            public double MoraleGainFactor = 1;
            /// <summary>Immune to the idle-year morale dip.</summary>
            public bool IgnoresIdle;
            /// <summary>Multiplier on lab output and science-mission yield for this kerbal.</summary>
            public double ScienceFactor = 1;
            /// <summary>Extra prestige when this kerbal's mission is a success / a First.</summary>
            public double PrestigeOnSuccess;
            public double PrestigeOnFirst;
            /// <summary>Morale change when this kerbal's mission is simulated rather than flown live.</summary>
            public int MoraleIfSimulated;
            /// <summary>Lead engineer: extra design passes, design score bonus, build/design time multiplier.</summary>
            public int DesignPasses;
            public double DesignScore;
            public double BuildTimeFactor = 1;
            /// <summary>Morale change for this kerbal when their mission aborts or is lost.</summary>
            public int MoraleOnFailure;
        }

        private readonly Dictionary<string, Effect> _effects = new Dictionary<string, Effect>(StringComparer.OrdinalIgnoreCase);

        public Effect Get(string tag)
        {
            Effect e;
            return tag != null && _effects.TryGetValue(tag, out e) ? e : null;
        }

        public IEnumerable<Effect> For(CrewProfile p)
        {
            if (p == null) yield break;
            foreach (string tag in p.PersonalityTags) { Effect e = Get(tag); if (e != null) yield return e; }
        }

        public double Nerve(CrewProfile p) { double v = 0; foreach (Effect e in For(p)) v += e.Nerve; return v; }
        public double MistakeFactor(CrewProfile p) { double v = 1; foreach (Effect e in For(p)) v *= e.MistakeFactor; return v; }
        public double Precision(CrewProfile p) { double v = 0; foreach (Effect e in For(p)) v += e.Precision; return v; }
        public double MoraleFactor(CrewProfile p, bool loss) { double v = 1; foreach (Effect e in For(p)) v *= loss ? e.MoraleLossFactor : e.MoraleGainFactor; return v; }
        public bool IgnoresIdle(CrewProfile p) { foreach (Effect e in For(p)) if (e.IgnoresIdle) return true; return false; }
        public double ScienceFactor(CrewProfile p) { double v = 1; foreach (Effect e in For(p)) v *= e.ScienceFactor; return v; }
        public double PrestigeOnSuccess(CrewProfile p) { double v = 0; foreach (Effect e in For(p)) v += e.PrestigeOnSuccess; return v; }
        public double PrestigeOnFirst(CrewProfile p) { double v = 0; foreach (Effect e in For(p)) v += e.PrestigeOnFirst; return v; }
        public int MoraleIfSimulated(CrewProfile p) { int v = 0; foreach (Effect e in For(p)) v += e.MoraleIfSimulated; return v; }
        public int DesignPasses(CrewProfile p) { int v = 0; foreach (Effect e in For(p)) v += e.DesignPasses; return v; }
        public double DesignScore(CrewProfile p) { double v = 0; foreach (Effect e in For(p)) v += e.DesignScore; return v; }
        public double BuildTimeFactor(CrewProfile p) { double v = 1; foreach (Effect e in For(p)) v *= e.BuildTimeFactor; return v; }
        public int MoraleOnFailure(CrewProfile p) { int v = 0; foreach (Effect e in For(p)) v += e.MoraleOnFailure; return v; }

        /// <summary>One line per tag for the Service Record and the recruiting screen.</summary>
        public string Describe(string tag)
        {
            Effect e = Get(tag);
            if (e == null) return tag + ": no effect.";
            var parts = new List<string>();
            if (e.Nerve != 0) parts.Add((e.Nerve > 0 ? "thinner" : "wider") + " margins in flight");
            if (Math.Abs(e.MistakeFactor - 1) > 0.001) parts.Add((e.MistakeFactor > 1 ? "more" : "fewer") + " mistakes");
            if (e.Precision != 0) parts.Add((e.Precision > 0 ? "+" : "") + (int)Math.Round(e.Precision * 100) + "% precision");
            if (Math.Abs(e.MoraleLossFactor - 1) > 0.001 || Math.Abs(e.MoraleGainFactor - 1) > 0.001) parts.Add("morale swings x" + e.MoraleLossFactor.ToString("0.#") + " down, x" + e.MoraleGainFactor.ToString("0.#") + " up");
            if (e.IgnoresIdle) parts.Add("never minds an idle year");
            if (Math.Abs(e.ScienceFactor - 1) > 0.001) parts.Add("science x" + e.ScienceFactor.ToString("0.##"));
            if (e.PrestigeOnSuccess != 0) parts.Add("+" + e.PrestigeOnSuccess.ToString("0.#") + " prestige per success");
            if (e.PrestigeOnFirst != 0) parts.Add("+" + e.PrestigeOnFirst.ToString("0.#") + " prestige per First");
            if (e.MoraleIfSimulated != 0) parts.Add(e.MoraleIfSimulated + " morale when flown simulated");
            if (e.DesignPasses != 0) parts.Add("+" + e.DesignPasses + " design passes as lead");
            if (e.DesignScore != 0) parts.Add("+" + (int)Math.Round(e.DesignScore * 100) + "% design score as lead");
            if (Math.Abs(e.BuildTimeFactor - 1) > 0.001) parts.Add("design/build time x" + e.BuildTimeFactor.ToString("0.##") + " as lead");
            if (e.MoraleOnFailure != 0) parts.Add(e.MoraleOnFailure + " morale on a failed mission");
            return tag + ": " + string.Join(", ", parts.ToArray()) + ".";
        }

        public static PersonalityRules Parse(DataNode node)
        {
            var r = new PersonalityRules();
            if (node == null) return Defaults();
            foreach (DataNode t in node.GetChildren("TAG"))
            {
                var e = new Effect
                {
                    Tag = t.GetValue("tag", string.Empty),
                    Nerve = t.GetDouble("nerve", 0),
                    MistakeFactor = t.GetDouble("mistakeFactor", 1),
                    Precision = t.GetDouble("precision", 0),
                    MoraleLossFactor = t.GetDouble("moraleLossFactor", 1),
                    MoraleGainFactor = t.GetDouble("moraleGainFactor", 1),
                    IgnoresIdle = t.GetBool("ignoresIdle", false),
                    ScienceFactor = t.GetDouble("scienceFactor", 1),
                    PrestigeOnSuccess = t.GetDouble("prestigeOnSuccess", 0),
                    PrestigeOnFirst = t.GetDouble("prestigeOnFirst", 0),
                    MoraleIfSimulated = t.GetInt("moraleIfSimulated", 0),
                    DesignPasses = t.GetInt("designPasses", 0),
                    DesignScore = t.GetDouble("designScore", 0),
                    BuildTimeFactor = t.GetDouble("buildTimeFactor", 1),
                    MoraleOnFailure = t.GetInt("moraleOnFailure", 0)
                };
                if (e.Tag.Length > 0) r._effects[e.Tag] = e;
            }
            return r._effects.Count > 0 ? r : Defaults();
        }

        /// <summary>The shipped values, used when agency.cfg has no PERSONALITY block.</summary>
        public static PersonalityRules Defaults()
        {
            var r = new PersonalityRules();
            r._effects["Cautious"] = new Effect { Tag = "Cautious", Nerve = -0.15, MistakeFactor = 0.7, Precision = 0.05, MoraleOnFailure = -3 };
            r._effects["Reckless"] = new Effect { Tag = "Reckless", Nerve = 0.2, MistakeFactor = 1.4, PrestigeOnSuccess = 1, MoraleOnFailure = -8 };
            r._effects["Showboat"] = new Effect { Tag = "Showboat", PrestigeOnFirst = 2, PrestigeOnSuccess = 0.5, MoraleIfSimulated = -6, MoraleGainFactor = 1.3 };
            r._effects["Steady"] = new Effect { Tag = "Steady", MoraleLossFactor = 0.5, MoraleGainFactor = 0.7, IgnoresIdle = true, MistakeFactor = 0.85 };
            r._effects["Curious"] = new Effect { Tag = "Curious", ScienceFactor = 1.25, Precision = -0.02 };
            r._effects["Perfectionist"] = new Effect { Tag = "Perfectionist", DesignPasses = 2, DesignScore = 0.05, BuildTimeFactor = 1.15, MistakeFactor = 0.8 };
            return r;
        }
    }
}
