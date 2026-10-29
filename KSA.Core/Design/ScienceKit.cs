using System.Collections.Generic;

namespace KSA.Core.Design
{
    /// <summary>
    /// One of each small science instrument researched goes on every crewed flight (2026-10-29): goo, thermometer,
    /// barometer, radiation counter and the rest. The live crew runs them all and recovery pays for each.
    /// </summary>
    public static class ScienceKit
    {
        /// <summary>Adds the instruments of the brief's kit the payload does not carry yet. Returns a note, or null.</summary>
        public static string AddMissing(StagePlan plan, DesignRequest req)
        {
            if (plan == null || req == null || req.ScienceKit.Count == 0) return null;
            var added = new List<string>();
            foreach (string name in req.ScienceKit)
            {
                if (plan.Payload.Extras.Contains(name)) continue;
                // Before any docking port, which stays the top of the stack.
                int at = plan.Payload.Extras.FindIndex(e => req.AllowedParts.Exists(p => p.Name == e && p.Kind == Parts.PartKind.Docking));
                if (at < 0) at = plan.Payload.Extras.Count;
                plan.Payload.Extras.Insert(at, name);
                added.Add(name);
            }
            return added.Count > 0 ? "Added the rest of the science kit: " + string.Join(", ", added.ToArray()) + "." : null;
        }
    }
}
