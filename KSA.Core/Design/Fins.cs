using System;
using System.Collections.Generic;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    /// <summary>
    /// Fins at the base of the launch stage (2026-10-29). A rocket without them turns sideways in the air once it is going
    /// fast (a finless Suborbital hop fell over at 3 km and flew flat). The biggest researched fin whose four weigh at most
    /// 2% of the rocket, four of them, or eight when even four of the biggest are lighter than that.
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

        /// <summary>
        /// How many of <paramref name="fin"/> a stack of <paramref name="tonnes"/> gets: four, or eight when four weigh less
        /// than 2% of it (2026-10-30: four basic fins could not hold a 2-stage Suborbital hop straight; T added a second set).
        /// </summary>
        public static int CountFor(PartInfo fin, double tonnes)
        {
            if (fin == null) return 0;
            return fin.Mass * Count >= tonnes * MassShare ? Count : 2 * Count;
        }

        /// <summary>True when the launch stage already carries fins.</summary>
        public static bool Has(StagePlan plan, IPartCatalog catalog) { return HasOn(plan, catalog, 0); }

        /// <summary>True when stage <paramref name="stage"/> (0 = the launch stage) carries fins.</summary>
        public static bool HasOn(StagePlan plan, IPartCatalog catalog, int stage)
        {
            if (plan == null || stage < 0 || stage >= plan.Stages.Count) return false;
            foreach (PlanPart rp in plan.Stages[stage].Radial)
            {
                PartInfo x = catalog.Get(rp.Part);
                if (x != null && x.Kind == PartKind.Fin) return true;
            }
            return false;
        }

        /// <summary>
        /// The stages that fly through thick air and so need fins: the launch stage, and the stage above it when the launch
        /// stage is solid boosters only (they burn out at 10-15 km and the next stage climbs on through the air; 2026-10-30:
        /// a finless liquid stage above a solid tumbled at 16 km).
        /// </summary>
        public static List<int> StagesNeedingFins(StagePlan plan, IPartCatalog catalog)
        {
            var list = new List<int>();
            if (plan == null || plan.Stages.Count == 0) return list;
            list.Add(0);
            if (plan.Stages.Count < 2) return list;
            bool liquid = false, solid = false;
            foreach (PlanPart p in plan.Stages[0].Core)
            {
                PartInfo x = catalog.Get(p.Part);
                if (x == null) continue;
                if (x.Kind == PartKind.Engine) liquid = true;
                if (x.Kind == PartKind.Booster) solid = true;
            }
            if (solid && !liquid) list.Add(1);
            return list;
        }

        /// <summary>
        /// Adds fins to every stage that flies through thick air and has none, when a fin is allowed (the AI often leaves
        /// them out). Returns a note for the design log, or null when nothing changed.
        /// </summary>
        public static string AddIfMissing(StagePlan plan, IEnumerable<PartInfo> allowed, IPartCatalog catalog)
        {
            if (plan == null || plan.Stages.Count == 0 || catalog == null) return null;
            var notes = new List<string>();
            foreach (int s in StagesNeedingFins(plan, catalog))
            {
                if (HasOn(plan, catalog, s)) continue;
                double above = MassFrom(plan, catalog, s);
                PartInfo fin = Pick(allowed, above);
                if (fin == null) return null;
                int n = CountFor(fin, above);
                plan.Stages[s].Radial.Add(new PlanPart { Part = fin.Name, Count = n, Symmetry = n });
                notes.Add(n + " " + (fin.Title ?? fin.Name) + " to the base of " + (s == 0 ? "the launch stage" : "stage " + (s + 1)));
            }
            return notes.Count > 0 ? "Added " + string.Join(" and ", notes.ToArray()) + ", so the rocket flies nose first." : null;
        }

        /// <summary>Rough lift-off mass, full tanks: enough to choose a fin size.</summary>
        public static double LiftoffMass(StagePlan plan, IPartCatalog catalog) { return MassFrom(plan, catalog, 0); }

        /// <summary>Rough mass, full tanks, of stage <paramref name="stage"/> and everything above it.</summary>
        public static double MassFrom(StagePlan plan, IPartCatalog catalog, int stage)
        {
            double m = 0;
            Func<string, int, double> add = (name, n) => { PartInfo x = catalog.Get(name); return x != null ? (x.Mass + x.FuelMass) * Math.Max(1, n) : 0; };
            m += add(plan.Payload.Command, 1);
            foreach (string e in plan.Payload.Extras) m += add(e, 1);
            for (int i = Math.Max(0, stage); i < plan.Stages.Count; i++)
            {
                PlanStage s = plan.Stages[i];
                foreach (PlanPart p in s.Core) m += add(p.Part, p.Count);
                foreach (PlanPart p in s.Radial) m += add(p.Part, p.Count > 0 && p.Symmetry > 1 && p.Count == p.Symmetry ? p.Count : Math.Max(1, p.Count) * Math.Max(1, p.Symmetry));
                if (!string.IsNullOrEmpty(s.Decoupler)) m += add(s.Decoupler, 1);
            }
            return m;
        }
    }
}
