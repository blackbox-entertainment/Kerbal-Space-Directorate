using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    public sealed class PlanPart
    {
        public string Part { get; set; }
        public int Count { get; set; }
        /// <summary>Radial parts only: 1 (none), 2, 3, 4, 6, 8.</summary>
        public int Symmetry { get; set; }

        public PlanPart() { Part = string.Empty; Count = 1; Symmetry = 1; }
    }

    public sealed class PlanStage
    {
        public int Stage { get; set; }
        /// <summary>launch, upper, transfer, lander, ascent, return ... free text for the model, "launch" matters to the validator.</summary>
        public string Role { get; set; }
        public List<PlanPart> Core { get; } = new List<PlanPart>();
        public List<PlanPart> Radial { get; } = new List<PlanPart>();
        public string Decoupler { get; set; }
        /// <summary>Launched with empty tanks and filled at the fuel depot in orbit (2026-10-29).</summary>
        public bool LaunchEmpty { get; set; }

        public PlanStage() { Role = string.Empty; Decoupler = string.Empty; }
    }

    public sealed class PlanPayload
    {
        public string Command { get; set; }
        public List<string> Extras { get; } = new List<string>();
        /// <summary>The payload's fuel tanks go up empty (a fuel depot's tank farm, 2026-10-29: tankers fill it).</summary>
        public bool TanksEmpty { get; set; }

        public PlanPayload() { Command = string.Empty; }
    }

    /// <summary>The LLM's output: decisions, not geometry (GDD 5.1, 5.4). C# turns it into a vessel.</summary>
    public sealed class StagePlan
    {
        public string DesignName { get; set; }
        public string Rationale { get; set; }
        /// <summary>Stage 0 is the launch stage (bottom of the stack); higher numbers sit above it.</summary>
        public List<PlanStage> Stages { get; } = new List<PlanStage>();
        public PlanPayload Payload { get; } = new PlanPayload();

        public StagePlan() { DesignName = string.Empty; Rationale = string.Empty; }

        private Dictionary<string, object> PayloadJson(List<object> extras)
        {
            var p = new Dictionary<string, object> { { "command", Payload.Command }, { "extras", extras } };
            if (Payload.TanksEmpty) p["tanks_empty"] = true;
            return p;
        }

        /// <summary>Every part name the plan references.</summary>
        public List<string> AllPartNames()
        {
            var names = new List<string>();
            if (Payload.Command.Length > 0) names.Add(Payload.Command);
            names.AddRange(Payload.Extras);
            foreach (PlanStage s in Stages)
            {
                foreach (PlanPart p in s.Core) names.Add(p.Part);
                foreach (PlanPart p in s.Radial) names.Add(p.Part);
                if (s.Decoupler.Length > 0) names.Add(s.Decoupler);
            }
            return names;
        }

        // ---------------------------------------------------------------- JSON

        public static bool TryParse(string text, out StagePlan plan, out string error)
        {
            plan = null;
            string json = Json.ExtractObject(text);
            if (json == null) { error = "No JSON object found in the reply."; return false; }
            object parsed;
            if (!Json.TryParse(json, out parsed, out error)) { error = "Invalid JSON: " + error; return false; }
            Dictionary<string, object> obj = Json.AsObject(parsed);
            if (obj == null) { error = "JSON root is not an object."; return false; }
            plan = FromJson(obj);
            error = null;
            return true;
        }

        public static StagePlan FromJson(Dictionary<string, object> obj)
        {
            var plan = new StagePlan
            {
                DesignName = Json.GetString(obj, "design_name", "Untitled"),
                Rationale = Json.GetString(obj, "rationale", string.Empty)
            };
            List<object> stages = Json.GetArray(obj, "stages");
            if (stages != null)
            {
                foreach (object so in stages)
                {
                    Dictionary<string, object> s = Json.AsObject(so);
                    if (s == null) continue;
                    var stage = new PlanStage
                    {
                        Stage = Json.GetInt(s, "stage", plan.Stages.Count),
                        Role = Json.GetString(s, "role", string.Empty),
                        Decoupler = Json.GetString(s, "decoupler", string.Empty),
                        LaunchEmpty = Json.GetBool(s, "launch_empty", false)
                    };
                    ReadParts(Json.GetArray(s, "core"), stage.Core, false);
                    ReadParts(Json.GetArray(s, "radial"), stage.Radial, true);
                    plan.Stages.Add(stage);
                }
            }
            plan.Stages.Sort((a, b) => a.Stage.CompareTo(b.Stage));
            Dictionary<string, object> payload = Json.GetObject(obj, "payload");
            if (payload != null)
            {
                plan.Payload.Command = Json.GetString(payload, "command", string.Empty);
                plan.Payload.TanksEmpty = Json.GetBool(payload, "tanks_empty", false);
                List<object> extras = Json.GetArray(payload, "extras");
                if (extras != null) foreach (object e in extras) if (e is string) plan.Payload.Extras.Add((string)e);
            }
            return plan;
        }

        private static void ReadParts(List<object> list, List<PlanPart> into, bool radial)
        {
            if (list == null) return;
            foreach (object po in list)
            {
                Dictionary<string, object> p = Json.AsObject(po);
                if (p == null)
                {
                    if (po is string) into.Add(new PlanPart { Part = (string)po, Count = 1, Symmetry = 1 });
                    continue;
                }
                into.Add(new PlanPart
                {
                    Part = Json.GetString(p, "part", string.Empty),
                    Count = Json.GetInt(p, "count", 1),
                    Symmetry = radial ? Json.GetInt(p, "symmetry", Json.GetInt(p, "count", 1)) : 1
                });
            }
        }

        public Dictionary<string, object> ToJson()
        {
            var stages = new List<object>();
            foreach (PlanStage s in Stages)
            {
                var core = new List<object>();
                foreach (PlanPart p in s.Core) core.Add(new Dictionary<string, object> { { "part", p.Part }, { "count", (double)p.Count } });
                var radial = new List<object>();
                foreach (PlanPart p in s.Radial) radial.Add(new Dictionary<string, object> { { "part", p.Part }, { "count", (double)p.Count }, { "symmetry", (double)p.Symmetry } });
                var st = new Dictionary<string, object>
                {
                    { "stage", (double)s.Stage }, { "role", s.Role }, { "core", core }, { "radial", radial }, { "decoupler", s.Decoupler }
                };
                if (s.LaunchEmpty) st["launch_empty"] = true;
                stages.Add(st);
            }
            var extras = new List<object>();
            foreach (string e in Payload.Extras) extras.Add(e);
            return new Dictionary<string, object>
            {
                { "design_name", DesignName },
                { "rationale", Rationale },
                { "stages", stages },
                { "payload", PayloadJson(extras) }
            };
        }

        public string ToJsonText(bool pretty = true) { return Json.Write(ToJson(), pretty); }
    }

    /// <summary>Structural checks before any physics: the part list is law (GDD 5.4).</summary>
    public static class StagePlanValidator
    {
        private static readonly int[] AllowedSymmetry = { 1, 2, 3, 4, 6, 8 };

        /// <summary>
        /// Drops non-essential parts the team was not offered (a decoupler on a single-stage plan, an extra) instead of
        /// failing the whole plan on them. Returns one note per removal; essential parts (command, cores, engines) are left
        /// for <see cref="Validate"/> to reject.
        /// </summary>
        public static List<string> Repair(StagePlan plan, ICollection<string> allowedNames)
        {
            var notes = new List<string>();
            if (plan == null || allowedNames == null) return notes;
            for (int i = 0; i < plan.Stages.Count; i++)
            {
                PlanStage s = plan.Stages[i];
                bool last = i == plan.Stages.Count - 1;
                if (s.Decoupler.Length > 0 && !allowedNames.Contains(s.Decoupler) && last)
                {
                    notes.Add("Dropped '" + s.Decoupler + "': not researched, and the top stage needs no decoupler.");
                    s.Decoupler = string.Empty;
                }
            }
            for (int i = plan.Payload.Extras.Count - 1; i >= 0; i--)
            {
                string e = plan.Payload.Extras[i];
                if (!allowedNames.Contains(e)) { notes.Add("Dropped extra '" + e + "': not researched."); plan.Payload.Extras.RemoveAt(i); }
            }
            return notes;
        }

        /// <summary>True when a radial (side) decoupler is among the allowed parts.</summary>
        private static bool HasRadialDecoupler(IPartCatalog catalog, ICollection<string> allowedNames)
        {
            if (catalog == null) return true;
            foreach (PartInfo p in catalog.All)
                if (p.Kind == PartKind.Decoupler && !p.IsStackable && (allowedNames == null || allowedNames.Contains(p.Name))) return true;
            return false;
        }

        public static List<string> Validate(StagePlan plan, IPartCatalog catalog, ICollection<string> allowedNames)
        {
            var problems = new List<string>();
            if (plan == null) { problems.Add("No plan."); return problems; }
            if (plan.Stages.Count == 0) problems.Add("The plan has no stages.");
            if (plan.Payload.Command.Length == 0) problems.Add("The payload has no command part.");

            foreach (string name in plan.AllPartNames())
            {
                if (allowedNames != null && !allowedNames.Contains(name))
                    problems.Add("Part '" + name + "' is not in the allowed part list.");
                else if (catalog != null && catalog.Get(name) == null)
                    problems.Add("Part '" + name + "' does not exist.");
            }
            if (plan.Payload.Command.Length > 0 && catalog != null)
            {
                PartInfo cmd = catalog.Get(plan.Payload.Command);
                if (cmd != null && cmd.Kind != PartKind.Command) problems.Add("Payload command part '" + cmd.Name + "' is not a command part.");
            }

            for (int i = 0; i < plan.Stages.Count; i++)
            {
                PlanStage s = plan.Stages[i];
                string label = "Stage " + s.Stage;
                if (s.Core.Count == 0 && s.Radial.Count == 0) problems.Add(label + " is empty.");
                foreach (PlanPart p in s.Core)
                {
                    if (p.Count <= 0) problems.Add(label + ": '" + p.Part + "' has count " + p.Count + ".");
                    if (catalog != null)
                    {
                        PartInfo info = catalog.Get(p.Part);
                        if (info != null && !info.IsStackable) problems.Add(label + ": '" + p.Part + "' cannot be stacked in the core; use it radially or as an extra.");
                    }
                }
                foreach (PlanPart p in s.Radial)
                {
                    if (p.Count <= 0) problems.Add(label + ": '" + p.Part + "' has count " + p.Count + ".");
                    if (Array.IndexOf(AllowedSymmetry, p.Symmetry) < 0) problems.Add(label + ": radial '" + p.Part + "' symmetry " + p.Symmetry + " is not 2, 3, 4, 6 or 8.");
                    if (catalog == null) continue;
                    PartInfo info = catalog.Get(p.Part);
                    if (info == null) continue;
                    // Side tanks cannot feed anything: no fuel lines are fitted (2026-10-28).
                    if (info.Kind == PartKind.Tank)
                        problems.Add(label + ": fuel tank '" + p.Part + "' is in \"radial\"; side tanks cannot feed the engines (no fuel lines). Stack tanks in the core.");
                    // Side boosters must be able to drop off on their own (2026-10-28: boosters were bolted to the core).
                    if (info.ProducesThrust && !HasRadialDecoupler(catalog, allowedNames))
                        problems.Add(label + ": side booster '" + p.Part + "' needs a radial decoupler to drop off, and none is researched. Use no side boosters: put the engine or booster in the core.");
                }
                bool hasThrust = false;
                if (catalog != null)
                {
                    // One engine or booster per stack (2026-10-28): stacked end to end, the upper ones fire into the parts
                    // below and KSP blocks their thrust, so the rocket crawls. Extra thrust goes round the side.
                    int coreThrusters = 0;
                    foreach (PlanPart p in s.Core) { PartInfo info = catalog.Get(p.Part); if (info != null && info.ProducesThrust) coreThrusters += Math.Max(1, p.Count); }
                    if (coreThrusters > 1)
                        problems.Add(label + ": " + coreThrusters + " engines or boosters are stacked in the core; only ONE can sit in a stack (the others fire into it and their thrust is blocked). Use one in the core and put extra boosters in \"radial\" with symmetry 2, 3 or 4.");
                    foreach (PlanPart p in s.Core) { PartInfo info = catalog.Get(p.Part); if (info != null && info.ProducesThrust) hasThrust = true; }
                    foreach (PlanPart p in s.Radial) { PartInfo info = catalog.Get(p.Part); if (info != null && info.ProducesThrust) hasThrust = true; }
                    if (!hasThrust && (i == 0 || string.Equals(s.Role, "launch", StringComparison.OrdinalIgnoreCase)))
                        problems.Add(label + " (launch) has no engine or booster.");
                    if (i < plan.Stages.Count - 1 && s.Decoupler.Length == 0)
                        problems.Add(label + " needs a decoupler to separate from the stage above.");
                    // A side decoupler has no top and bottom node, so it cannot sit between two stages (2026-10-28: two
                    // approved designs could not be assembled and their flights were simulated).
                    if (s.Decoupler.Length > 0)
                    {
                        PartInfo dec = catalog.Get(s.Decoupler);
                        if (dec != null && !dec.IsStackable)
                            problems.Add(label + ": decoupler '" + s.Decoupler + "' is a radial (side) decoupler and cannot sit between stages. Use a stack decoupler there; radial decouplers are fitted automatically under side boosters.");
                    }
                }
            }
            return problems;
        }
    }
}
