using System;
using System.Collections.Generic;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    /// <summary>
    /// Fins at the base of the launch stage (2026-10-29). A rocket without them turns sideways in the air once it is going
    /// fast (a finless Suborbital hop fell over at 3 km and flew flat). Four fins, the biggest researched fin whose four
    /// weigh at most 2% of the rocket; the lightest fin when none is that light.
    /// </summary>
    public static class Fins
    {
        public const int Count = 4;
        private const double MassShare = 0.02;

        /// <summary>The fin for a rocket of <paramref name="liftoffTonnes"/>, or null when no fin is researched.</summary>
        public static PartInfo Pick(IEnumerable<PartInfo> allowed, double liftoffTonnes)
        {
            PartInfo best = null, lightest = null;
            foreach (PartInfo p in allowed)
            {
                if (p.Kind != PartKind.Fin) continue;
                if (lightest == null || p.Mass < lightest.Mass) lightest = p;
                if (p.Mass * Count <= liftoffTonnes * MassShare && (best == null || p.Mass > best.Mass)) best = p;
            }
            return best ?? lightest;
        }

        /// <summary>True when the launch stage already carries fins.</summary>
        public static bool Has(StagePlan plan, IPartCatalog catalog)
        {
            if (plan == null || plan.Stages.Count == 0) return false;
            foreach (PlanPart rp in plan.Stages[0].Radial)
            {
                PartInfo x = catalog.Get(rp.Part);
                if (x != null && x.Kind == PartKind.Fin) return true;
            }
            return false;
        }

        /// <summary>
        /// Adds four fins to a plan's launch stage when it has none and a fin is allowed (the AI often leaves them out).
        /// Returns a note for the design log, or null when nothing changed.
        /// </summary>
        public static string AddIfMissing(StagePlan plan, IEnumerable<PartInfo> allowed, IPartCatalog catalog)
        {
            if (plan == null || plan.Stages.Count == 0 || catalog == null || Has(plan, catalog)) return null;
            PartInfo fin = Pick(allowed, LiftoffMass(plan, catalog));
            if (fin == null) return null;
            plan.Stages[0].Radial.Add(new PlanPart { Part = fin.Name, Count = Count, Symmetry = Count });
            return "Added " + Count + " " + (fin.Title ?? fin.Name) + " to the base of the launch stage, so the rocket flies nose first.";
        }

        /// <summary>Rough lift-off mass, full tanks: enough to choose a fin size.</summary>
        public static double LiftoffMass(StagePlan plan, IPartCatalog catalog)
        {
            double m = 0;
            Func<string, int, double> add = (name, n) => { PartInfo x = catalog.Get(name); return x != null ? (x.Mass + x.FuelMass) * Math.Max(1, n) : 0; };
            m += add(plan.Payload.Command, 1);
            foreach (string e in plan.Payload.Extras) m += add(e, 1);
            foreach (PlanStage s in plan.Stages)
            {
                foreach (PlanPart p in s.Core) m += add(p.Part, p.Count);
                foreach (PlanPart p in s.Radial) m += add(p.Part, p.Count > 0 && p.Symmetry > 1 && p.Count == p.Symmetry ? p.Count : Math.Max(1, p.Count) * Math.Max(1, p.Symmetry));
                if (!string.IsNullOrEmpty(s.Decoupler)) m += add(s.Decoupler, 1);
            }
            return m;
        }
    }
}
