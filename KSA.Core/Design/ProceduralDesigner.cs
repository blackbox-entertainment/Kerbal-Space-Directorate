using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    /// <summary>
    /// The engineering team's own drawing board (2026-10-28): a rule-based designer that stacks researched engines, tanks
    /// and decouplers into 1 to 4 stages until the mission's delta-v and liftoff thrust are met, with the payload the brief
    /// asks for (pod, parachute, heat shield, instruments, antenna, battery, landing legs). No AI involved, so it decides
    /// whether a rocket can be built at all, steps in when the AI comes up short, and designs when AI is off.
    /// Designs are checked with the same analysis, validation and must-carry rules as AI designs.
    /// </summary>
    public static class ProceduralDesigner
    {
        private const double G0 = 9.80665;
        private const double Margin = 1.06;      // aim 6% over the requirement
        private const double LaunchTwr = 1.45;    // vacuum thrust figure; sea level is lower
        private const double UpperTwr = 0.45;     // in space, slow but workable
        private const int MaxTanks = 8;

        private sealed class Option
        {
            public PartInfo Engine;
            public PartInfo Tank;
            public int Tanks;
            public PartInfo Booster;    // radial solid boosters on the launch stage
            public int Boosters;
            public double Dry, Fuel, Thrust, IspNum;
        }

        /// <summary>
        /// Returns a plan that passes, or null. <paramref name="why"/> says what came closest when it fails.
        /// </summary>
        public static StagePlan Design(DesignRequest req, IPartCatalog catalog, MissionRequirements.Entry need, double gravity, out string why)
        {
            why = null;
            if (req == null || catalog == null) { why = "nothing to design with"; return null; }
            var allowed = req.AllowedParts;
            int crew = Math.Max(0, req.CrewSize);

            // Payload: the lightest pod that seats the crew, then one of each thing the brief must carry.
            PartInfo pod = null;
            foreach (PartInfo p in allowed)
                if (p.Kind == PartKind.Command && p.CrewCapacity >= Math.Max(1, crew) && (pod == null || p.Mass < pod.Mass)) pod = p;
            if (pod == null) { why = "no crew pod with " + Math.Max(1, crew) + " seats"; return null; }
            var plan = new StagePlan { DesignName = "Engineering team " + (req.Mission != null ? req.Mission.Name : "rocket"), Rationale = "Drawn up by the engineering team from the researched parts." };
            plan.Payload.Command = pod.Name;
            foreach (KeyValuePair<string, List<string>> rule in req.MustInclude)
            {
                if (rule.Value.Contains(pod.Name)) continue; // a self-shielded pod already counts
                PartInfo pickPart = null;
                foreach (string n in rule.Value)
                {
                    PartInfo x = catalog.Get(n);
                    if (x == null) continue;
                    // A heat shield must fit the pod; otherwise the lightest.
                    if (x.Kind == PartKind.HeatShield && pod.Diameter > 0 && x.Diameter > 0 && Math.Abs(x.Diameter - pod.Diameter) > 0.01) continue;
                    if (pickPart == null || x.Mass < pickPart.Mass) pickPart = x;
                }
                if (pickPart == null) foreach (string n in rule.Value) { PartInfo x = catalog.Get(n); if (x != null && (pickPart == null || x.Mass < pickPart.Mass)) pickPart = x; }
                if (pickPart == null) { why = "nothing researched for " + rule.Key; return null; }
                plan.Payload.Extras.Add(pickPart.Name);
            }
            double payload = pod.Mass + pod.FuelMass;
            foreach (string e in plan.Payload.Extras) { PartInfo x = catalog.Get(e); if (x != null) payload += x.Mass + x.FuelMass; }

            // Building blocks.
            var engines = new List<PartInfo>();
            var tanks = new List<PartInfo>();
            var boosters = new List<PartInfo>();
            PartInfo decoupler = null, legs = null;
            foreach (PartInfo p in allowed)
            {
                if (p.Kind == PartKind.Engine && p.ProducesThrust && p.IsStackable) engines.Add(p);
                else if (p.Kind == PartKind.Booster && p.ProducesThrust && p.FuelMass > 0) boosters.Add(p);
                else if (p.Kind == PartKind.Tank && p.FuelMass > 0 && p.IsStackable) tanks.Add(p);
                else if (p.Kind == PartKind.Decoupler && p.IsStackable && (decoupler == null || p.Mass < decoupler.Mass)) decoupler = p;
                else if (p.Kind == PartKind.LandingLeg && (legs == null || p.Mass < legs.Mass)) legs = p;
            }
            bool landing = req.Mission != null && (req.Mission.Profile == MissionProfile.Land || req.Mission.Profile == MissionProfile.Base);
            if (landing && legs == null) { why = "no landing legs researched"; return null; }
            if (engines.Count == 0 && boosters.Count == 0) { why = "no engine or booster researched"; return null; }

            double target = (need != null ? need.DeltaV : 3600) * Margin;
            double twrNeed = Math.Max(LaunchTwr, need != null ? need.MinTwr + 0.25 : LaunchTwr);
            var options = Options(engines, tanks, boosters, false);
            var launchOptions = Options(engines, tanks, boosters, true);

            StagePlan best = null;
            double bestMass = double.MaxValue, closest = 0;
            double[][] splits =
            {
                new[] { 1.0 },
                new[] { 0.5, 0.5 }, new[] { 0.6, 0.4 }, new[] { 0.4, 0.6 },
                new[] { 0.34, 0.33, 0.33 }, new[] { 0.45, 0.3, 0.25 }, new[] { 0.3, 0.35, 0.35 },
                new[] { 0.25, 0.25, 0.25, 0.25 }, new[] { 0.35, 0.25, 0.2, 0.2 },
            };
            foreach (double[] split in splits)
            {
                // Build from the top stage down: each stage carries everything above it.
                int n = split.Length;
                var chosen = new Option[n];
                double above = payload;
                bool ok = true;
                for (int k = n - 1; k >= 0; k--)
                {
                    bool launch = k == 0;
                    double stageTarget = target * split[k];
                    double extraDry = (decoupler != null ? decoupler.Mass : 0) + (landing && k == n - 1 ? legs.Mass * 4 : 0);
                    Option pick = null; double pickMass = double.MaxValue;
                    foreach (Option o in launch ? launchOptions : options)
                    {
                        double start = above + o.Dry + extraDry + o.Fuel;
                        double end = above + o.Dry + extraDry;
                        double isp = o.Thrust > 0 ? o.IspNum / o.Thrust : 0;
                        double dv = isp * G0 * Math.Log(start / end);
                        double twr = o.Thrust / (start * gravity);
                        if (dv < stageTarget || twr < (launch ? twrNeed : UpperTwr)) continue;
                        if (start < pickMass) { pick = o; pickMass = start; }
                    }
                    if (pick == null) { ok = false; break; }
                    chosen[k] = pick;
                    above = pickMass;
                }
                if (!ok || above >= bestMass) continue;
                StagePlan candidate = Build(plan, chosen, decoupler, landing ? legs : null);
                DesignAnalysis a = StageAnalyzer.Analyze(candidate, catalog, need, gravity);
                closest = Math.Max(closest, a.TotalDeltaV);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PartInfo p in allowed) names.Add(p.Name);
                if (!a.Passes || StagePlanValidator.Validate(candidate, catalog, names).Count > 0 || DesignSession.MissingMustInclude(candidate, req).Count > 0) continue;
                best = candidate; bestMass = above;
            }
            if (best == null) why = "no stack of the researched parts reaches " + Math.Round(need != null ? need.DeltaV : 0) + " m/s";
            return best;
        }

        private static List<Option> Options(List<PartInfo> engines, List<PartInfo> tanks, List<PartInfo> boosters, bool launch)
        {
            var list = new List<Option>();
            foreach (PartInfo e in engines)
                foreach (PartInfo t in tanks)
                    for (int c = 1; c <= MaxTanks; c++)
                    {
                        var o = new Option { Engine = e, Tank = t, Tanks = c };
                        Sum(o);
                        list.Add(o);
                        if (launch) foreach (PartInfo b in boosters) foreach (int nb in new[] { 2, 4 }) { var ob = new Option { Engine = e, Tank = t, Tanks = c, Booster = b, Boosters = nb }; Sum(ob); list.Add(ob); }
                    }
            // A solid booster alone makes a stage too (stacked, if it can be).
            foreach (PartInfo b in boosters) if (b.IsStackable) { var o = new Option { Booster = b, Boosters = 1 }; Sum(o); list.Add(o); }
            return list;
        }

        private static void Sum(Option o)
        {
            if (o.Engine != null) { o.Dry += o.Engine.Mass; o.Fuel += o.Engine.FuelMass; Thrust(o, o.Engine, 1); }
            if (o.Tank != null) { o.Dry += o.Tank.Mass * o.Tanks; o.Fuel += o.Tank.FuelMass * o.Tanks; }
            if (o.Booster != null) { o.Dry += o.Booster.Mass * o.Boosters; o.Fuel += o.Booster.FuelMass * o.Boosters; Thrust(o, o.Booster, o.Boosters); }
        }

        private static void Thrust(Option o, PartInfo p, int n)
        {
            o.Thrust += p.Thrust * n;
            o.IspNum += p.Thrust * n * (p.IspVacuum > 0 ? p.IspVacuum : p.IspSeaLevel);
        }

        private static StagePlan Build(StagePlan template, Option[] stages, PartInfo decoupler, PartInfo legs)
        {
            var plan = new StagePlan { DesignName = template.DesignName, Rationale = template.Rationale };
            plan.Payload.Command = template.Payload.Command;
            plan.Payload.Extras.AddRange(template.Payload.Extras);
            for (int k = 0; k < stages.Length; k++)
            {
                Option o = stages[k];
                var s = new PlanStage { Stage = k, Role = k == 0 ? "launch" : (k == stages.Length - 1 ? "upper" : "transfer"), Decoupler = decoupler != null ? decoupler.Name : string.Empty };
                if (o.Tank != null) s.Core.Add(new PlanPart { Part = o.Tank.Name, Count = o.Tanks });
                if (o.Engine != null) s.Core.Add(new PlanPart { Part = o.Engine.Name, Count = 1 });
                if (o.Booster != null)
                {
                    if (o.Engine == null && o.Boosters == 1) s.Core.Add(new PlanPart { Part = o.Booster.Name, Count = 1 });
                    else s.Radial.Add(new PlanPart { Part = o.Booster.Name, Count = o.Boosters, Symmetry = o.Boosters });
                }
                if (legs != null && k == stages.Length - 1) s.Radial.Add(new PlanPart { Part = legs.Name, Count = 4, Symmetry = 4 });
                plan.Stages.Add(s);
            }
            return plan;
        }
    }
}
