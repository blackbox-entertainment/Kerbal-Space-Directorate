using System;
using System.Collections.Generic;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    /// <summary>
    /// Enough parachute for the capsule that comes down (2026-10-29: a single small radial chute was snapped on whatever the
    /// pod weighed). A main parachute lands about 22 times its own mass at a survivable 7 m/s or so: a 0.1 t Mk16 or Mk2-R
    /// about 2.2 t, a 0.3 t Mk16-XL about 6.6 t. Drogues slow a capsule high up but land nothing, so they count for nothing.
    /// </summary>
    public static class Parachutes
    {
        public const double TonnesPerChuteTonne = 22;

        /// <summary>The landing mass one parachute brings down softly, tonnes (0 for a drogue or anything else).</summary>
        public static double Capacity(PartInfo p)
        {
            if (p == null || p.Kind != PartKind.Parachute || p.Tags.Contains("drogue")) return 0;
            return Math.Max(0.05, p.Mass) * TonnesPerChuteTonne;
        }

        /// <summary>
        /// What comes down under the chutes: the pod and everything in the payload (stages are dropped first). Payload
        /// tanks count dry when they launch empty, full otherwise.
        /// </summary>
        public static double LandingMass(StagePlan plan, IPartCatalog catalog)
        {
            if (plan == null || catalog == null) return 0;
            double m = 0;
            PartInfo pod = catalog.Get(plan.Payload.Command);
            if (pod != null) m += pod.Mass + pod.FuelMass;
            foreach (string e in plan.Payload.Extras)
            {
                PartInfo x = catalog.Get(e);
                if (x == null) continue;
                m += x.Mass + (plan.Payload.TanksEmpty && x.Kind == PartKind.Tank ? 0 : x.FuelMass);
            }
            return m;
        }

        /// <summary>The parachute capacity in a plan's payload, tonnes.</summary>
        public static double CapacityOf(StagePlan plan, IPartCatalog catalog)
        {
            double c = 0;
            if (plan == null || catalog == null) return 0;
            foreach (string e in plan.Payload.Extras) c += Capacity(catalog.Get(e));
            return c;
        }

        /// <summary>
        /// Null when the payload's parachutes land it softly, else what is wrong. <paramref name="factor"/> asks for more
        /// than the bare need: 1.5 after the mission landed too hard before (2026-10-30).
        /// </summary>
        public static string Shortfall(StagePlan plan, IPartCatalog catalog, double factor = 1)
        {
            double land = LandingMass(plan, catalog) * Math.Max(1, factor), cap = CapacityOf(plan, catalog);
            if (cap + 1e-6 >= land) return null;
            return "The parachutes are too small: they land about " + cap.ToString("0.0") + " t softly, and the capsule coming down weighs "
                   + land.ToString("0.0") + " t. Add parachutes (radial ones round the pod) or a bigger one; each main parachute lands about "
                   + TonnesPerChuteTonne.ToString("0") + " times its own mass.";
        }

        /// <summary>
        /// Adds parachutes to a payload that already has one until they can land it: more of the biggest radial chute, or the
        /// biggest stack chute in place of a smaller one when no radial chute is allowed. Returns a note, or null if nothing
        /// changed.
        /// </summary>
        public static string Fit(StagePlan plan, IEnumerable<PartInfo> allowed, IPartCatalog catalog, bool radialOnly = false, double factor = 1)
        {
            if (plan == null || catalog == null) return null;
            factor = Math.Max(1, factor);
            if (!plan.Payload.Extras.Exists(e => { PartInfo x = catalog.Get(e); return x != null && x.Kind == PartKind.Parachute; })) return null;
            PartInfo radial = null, stack = null;
            foreach (PartInfo p in allowed)
            {
                if (Capacity(p) <= 0) continue;
                if (!p.IsStackable) { if (radial == null || Capacity(p) > Capacity(radial)) radial = p; }
                else if (!radialOnly && (stack == null || Capacity(p) > Capacity(stack))) stack = p;
            }
            int added = 0;
            // A bigger stack chute first, when there is no radial one to add.
            if (radial == null && stack != null)
            {
                int at = plan.Payload.Extras.FindIndex(e => { PartInfo x = catalog.Get(e); return x != null && x.Kind == PartKind.Parachute && x.IsStackable; });
                if (at >= 0 && Capacity(catalog.Get(plan.Payload.Extras[at])) < Capacity(stack) && CapacityOf(plan, catalog) < LandingMass(plan, catalog) * factor)
                { plan.Payload.Extras[at] = stack.Name; added++; }
            }
            // Radial chutes go on in pairs, opposite each other, so the drag pulls straight; before any docking port, which
            // stays the top of the stack.
            while (radial != null && CapacityOf(plan, catalog) < LandingMass(plan, catalog) * factor && added < 8)
            {
                int at = plan.Payload.Extras.FindIndex(e => { PartInfo x = catalog.Get(e); return x != null && x.Kind == PartKind.Docking; });
                if (at < 0) at = plan.Payload.Extras.Count;
                plan.Payload.Extras.Insert(at, radial.Name);
                plan.Payload.Extras.Insert(at, radial.Name);
                added += 2;
            }
            // Never one radial chute on its own: it would pull the capsule over (2026-10-30, a probe's single Mk2-R).
            var lone = new Dictionary<string, int>();
            foreach (string e in plan.Payload.Extras) { PartInfo x = catalog.Get(e); if (x != null && x.Kind == PartKind.Parachute && !x.IsStackable) lone[e] = (lone.ContainsKey(e) ? lone[e] : 0) + 1; }
            foreach (KeyValuePair<string, int> kv in lone)
                if (kv.Value % 2 == 1) { plan.Payload.Extras.Insert(plan.Payload.Extras.IndexOf(kv.Key), kv.Key); added++; }
            return added > 0 ? "Parachutes sized for a " + LandingMass(plan, catalog).ToString("0.0") + " t capsule (" + CapacityOf(plan, catalog).ToString("0.0") + " t of soft landing)." : null;
        }
    }
}
