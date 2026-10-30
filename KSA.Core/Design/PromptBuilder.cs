using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KSA.Core.Careers;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;

namespace KSA.Core.Design
{
    /// <summary>Everything a design request needs.</summary>
    public sealed class DesignRequest
    {
        public MissionType Mission { get; set; }
        public MissionRequirements.Entry Requirement { get; set; }
        public List<CrewProfile> Engineers { get; } = new List<CrewProfile>();
        public string LeadEngineerId { get; set; }
        public List<PartInfo> AllowedParts { get; } = new List<PartInfo>();
        public List<string> Lessons { get; } = new List<string>();
        /// <summary>Causes of earlier failures of this mission the design must fix (2026-10-30; FailureCauses.Fix).</summary>
        public List<Flight.FailureCause> Fixes { get; } = new List<Flight.FailureCause>();

        /// <summary>The delta-v margin the engineers aim for: 10%, plus 15% for each time the mission ran out of fuel or fell short of space (at most 55%).</summary>
        public double DeltaVMargin
        {
            get { return 1.10 + 0.15 * Math.Min(3, Flight.FailureCauses.Count(Fixes, Flight.FailureCause.OutOfFuel) + Flight.FailureCauses.Count(Fixes, Flight.FailureCause.BelowSpace)); }
        }

        /// <summary>Parachute capacity over landing mass: 1, or 1.5 after a hard landing (2.25 after two).</summary>
        public double ChuteFactor
        {
            get { return Math.Pow(1.5, Math.Min(2, Flight.FailureCauses.Count(Fixes, Flight.FailureCause.HardLanding))); }
        }
        public double Budget { get; set; }
        public int CrewSize { get; set; }
        public string ProjectName { get; set; }
        /// <summary>Hardware the craft must carry, one plain sentence each (KSA-67: industry modules, life support supplies).</summary>
        public List<string> MustCarry { get; } = new List<string>();
        /// <summary>Every small science instrument researched, one of each to carry (2026-10-29: designs carried only the lightest).</summary>
        public List<string> ScienceKit { get; } = new List<string>();
        /// <summary>Checked, not just asked for (2026-10-28): each entry is a reason and the parts, any one of which satisfies it.</summary>
        public List<KeyValuePair<string, List<string>>> MustInclude { get; } = new List<KeyValuePair<string, List<string>>>();
        /// <summary>How many projects of this mission type the agency has flown before: later designs are asked to differ.</summary>
        public int PriorDesigns { get; set; }
        /// <summary>A fuel depot's core (2026-10-29): tonnes of propellant its payload tanks (above the pod) must hold.</summary>
        public double DepotTanks { get; set; }
        /// <summary>Rank names for the engineers, by crew id (the catalog lives in the agency).</summary>
        public Dictionary<string, string> RankNames { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public DesignRequest() { LeadEngineerId = string.Empty; ProjectName = string.Empty; }
    }

    /// <summary>Turns a request into the system and user prompts (GDD 5.2, 5.3; Jira KSA-43).</summary>
    public static class PromptBuilder
    {
        public static string SystemPrompt(DesignRequest req)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the engineering team of a small space agency designing a rocket for a crewed mission.");
            sb.AppendLine("You decide WHAT parts to use and in which stages. You never place parts in 3D; the agency's software assembles the vehicle as a vertical stack from your plan.");
            sb.AppendLine();
            sb.AppendLine("The team:");
            foreach (CrewProfile e in req.Engineers)
            {
                string rank;
                if (!req.RankNames.TryGetValue(e.Id, out rank)) rank = "Engineer";
                bool lead = string.Equals(e.Id, req.LeadEngineerId, StringComparison.OrdinalIgnoreCase);
                sb.Append("- ").Append(e.Name).Append(", ").Append(rank).Append(lead ? " (lead)" : string.Empty);
                if (e.PersonalityTags.Count > 0) sb.Append(". Personality: ").Append(string.Join(", ", e.PersonalityTags.ToArray()));
                sb.AppendLine(".");
                foreach (string tag in e.PersonalityTags)
                {
                    string mod = PersonalityModifier(tag);
                    if (mod.Length > 0) sb.Append("  ").AppendLine(mod);
                }
            }
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("1. Use ONLY part names from the ALLOWED PARTS list, spelled exactly. Any other part name rejects the whole plan.");
            sb.AppendLine("2. Stage 0 is the launch stage at the bottom of the stack; higher stage numbers sit above it and fire later.");
            sb.AppendLine("3. Each stage lists core parts (stacked, from the bottom of that stage upward) and optional radial parts with symmetry 2, 3, 4, 6 or 8.");
            sb.AppendLine("4. Every stage except the top one must name a decoupler that separates it from the stage above.");
            sb.AppendLine("   Each stage's core stack holds at most ONE engine or solid booster, at its bottom: stacked engines fire into each other and lose their thrust. Put extra boosters in \"radial\" with symmetry 2, 3 or 4.");
            sb.AppendLine("5. The payload names one command part (it must seat the crew) and extras such as parachutes, heat shields, antennas.");
            sb.AppendLine("6. Prefer stock parts: use a part marked [DLC] only when no stock part does the job.");
            sb.AppendLine("7. Keep part counts realistic: a core tank list like [{\"part\":\"fuelTankLong\",\"count\":2}] stacks two of that tank. Prefer one big tank to several small ones, and short, wide stacks to tall, thin ones: a long thin rocket is hard to keep pointed.");
            sb.AppendLine("   Put four fins (radial, symmetry 4) on the launch stage when a fin is in the ALLOWED PARTS: without them the rocket turns sideways in the air.");
            sb.AppendLine("8. Reply with ONE JSON object and nothing else. No prose, no markdown fences.");
            sb.AppendLine();
            sb.AppendLine("JSON shape:");
            sb.AppendLine("{\"design_name\":\"...\",\"rationale\":\"one paragraph\",\"stages\":[{\"stage\":0,\"role\":\"launch\",\"core\":[{\"part\":\"name\",\"count\":1}],\"radial\":[{\"part\":\"name\",\"count\":4,\"symmetry\":4}],\"decoupler\":\"name\"}],\"payload\":{\"command\":\"name\",\"extras\":[\"name\"]}}");
            return sb.ToString();
        }

        public static string UserPrompt(DesignRequest req, StagePlan previous, string feedback)
        {
            var sb = new StringBuilder();
            MissionType m = req.Mission;
            sb.AppendLine("MISSION: " + (m != null ? m.Name : req.ProjectName));
            if (m != null)
            {
                sb.AppendLine("Destination: " + m.Body + ". Profile: " + ProfileText(m.Profile) + ". " + m.Description);
            }
            sb.AppendLine("Crew: " + Math.Max(1, req.CrewSize) + ". Budget: " + Math.Round(req.Budget) + " funds.");
            if (req.Requirement != null && req.Requirement.Legs != null)
            {
                // Leg by leg (2026-10-29): the design review flies the stages through these in order and checks each one.
                sb.AppendLine("Requirements: the rocket flies these legs in order, " + Math.Round(req.Requirement.DeltaV) + " m/s in all (vacuum figures). Stages fire from stage 0 up and each one drops away when empty, so give every leg to a stage that is still there:");
                foreach (FlightLeg l in req.Requirement.Legs)
                {
                    // Depot stops (2026-10-29) burn nothing: say what happens there instead.
                    if (l.Refuel) { sb.AppendLine("- " + l.Name + ": docked at the fuel depot in Kerbin orbit. Every stage that fires after this point should launch EMPTY (mark it \"launch_empty\": true) and is filled here, so the launch from Kerbin lifts only empty tanks. The stages without it must reach the depot."); continue; }
                    if (l.FuelDrop > 0) { sb.AppendLine("- " + l.Name + ": " + l.FuelDrop.ToString("0.#", CultureInfo.InvariantCulture) + " t of propellant is pumped into the depot here, so the stage flying it must still hold that much on arrival, on top of what the legs after it need."); continue; }
                    sb.AppendLine("- " + l.Name + ": " + Math.Round(l.DeltaV) + " m/s"
                        + (l.MinTwr > 0 ? ", thrust-to-weight at least " + l.MinTwr.ToString("0.0", CultureInfo.InvariantCulture) + (l.Gravity < 9 ? " in " + l.Body + "'s gravity (" + l.Gravity.ToString("0.00", CultureInfo.InvariantCulture) + " m/s2)" : l.Atmospheric ? " at sea level" : "") : "")
                        + (l.MaxTwr > 0 ? " and at most " + l.MaxTwr.ToString("0.0", CultureInfo.InvariantCulture) + " at lift-off (more wastes the fuel on drag)" : "")
                        + (l.Atmospheric ? "; flown in thick air, so count it at the engines' sea-level Isp" : "")
                        + (l.Landing ? "; this stage touches down and needs landing legs (radial, symmetry 4)" : "") + ".");
                }
                sb.AppendLine("Aim for 10-20% spare on every leg. Solid boosters only for the climb out of Kerbin's air: they cannot throttle for a landing.");
            }
            else if (req.Requirement != null)
                sb.AppendLine("Requirements: at least " + Math.Round(req.Requirement.DeltaV) + " m/s total delta-v across all stages, liftoff thrust-to-weight at least " + req.Requirement.MinTwr.ToString("0.0", CultureInfo.InvariantCulture) + ". Aim for a 15-25% delta-v margin.");
            foreach (string must in req.MustCarry) sb.AppendLine("Must carry: " + must);
            if (req.Lessons.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("LESSONS LEARNED from earlier flights:");
                int shown = 0;
                for (int i = req.Lessons.Count - 1; i >= 0 && shown < 8; i--, shown++) sb.AppendLine("- " + req.Lessons[i]);
            }
            if (req.Fixes.Count > 0)
            {
                var said = new List<string>();
                foreach (Flight.FailureCause c in req.Fixes) { string f = Flight.FailureCauses.Fix(c); if (f != null && !said.Contains(f)) said.Add(f); }
                sb.AppendLine("THIS MISSION FAILED BEFORE. Fix it: " + string.Join("; ", said.ToArray()) + ".");
            }
            sb.AppendLine();
            sb.AppendLine("ALLOWED PARTS (name | kind | dry mass t | propellant t | thrust kN | Isp vac | crew | cost):");
            foreach (PartInfo p in req.AllowedParts)
            {
                sb.Append(p.Name).Append(" | ").Append(p.Kind).Append(" | ")
                  .Append(p.Mass.ToString("0.###", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(p.FuelMass.ToString("0.###", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(p.Thrust.ToString("0", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(p.IspVacuum.ToString("0", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(p.CrewCapacity).Append(" | ")
                  .Append(p.Cost.ToString("0", CultureInfo.InvariantCulture));
                if (p.Title.Length > 0 && !string.Equals(p.Title, p.Name, StringComparison.OrdinalIgnoreCase)) sb.Append("  (").Append(p.Title).Append(')');
                if (p.RequiresDlc) sb.Append("  [DLC]");
                sb.AppendLine();
            }
            if (previous != null)
            {
                sb.AppendLine();
                sb.AppendLine("YOUR PREVIOUS PLAN:");
                sb.AppendLine(previous.ToJsonText(false));
                sb.AppendLine();
                sb.AppendLine("REVIEW OF THAT PLAN:");
                sb.AppendLine(feedback ?? string.Empty);
                sb.AppendLine("Revise the plan to fix every point above. Reply with the complete corrected JSON object only.");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("Produce the stage plan JSON now.");
            }
            return sb.ToString();
        }

        public static string PersonalityModifier(string tag)
        {
            switch ((tag ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "cautious": return "Cautious: prioritise 20%+ delta-v margins and redundancy over cost.";
                case "reckless": return "Reckless: minimise mass and cost; margins are optional.";
                case "perfectionist": return "Perfectionist: refine details; prefer the cleanest staging even if it takes another pass.";
                case "showboat": return "Showboat: favour impressive, larger vehicles when the budget allows.";
                case "steady": return "Steady: proven, conventional layouts; no surprises.";
                case "curious": return "Curious: willing to try unusual part combinations if the numbers work.";
                default: return string.Empty;
            }
        }

        private static string ProfileText(MissionProfile p)
        {
            switch (p)
            {
                case MissionProfile.Suborbital: return "suborbital flight above the atmosphere and back";
                case MissionProfile.Orbit: return "orbit the home world and return";
                case MissionProfile.Flyby: return "fly past the target body and return";
                case MissionProfile.OrbitBody: return "enter orbit around the target body and return";
                case MissionProfile.Land: return "land on the target body, EVA, and return";
                case MissionProfile.Station: return "deliver a station module to orbit";
                case MissionProfile.Base: return "deliver a base module to the surface";
                case MissionProfile.Atmospheric: return "fly an aircraft in the home world's air and land";
                default: return p.ToString();
            }
        }

        /// <summary>Revision passes allowed: 1 + lead rung, +2 for a Perfectionist lead, capped at 5 (GDD 5.3).</summary>
        public static int RevisionPasses(CrewProfile lead, int leadRung) { return RevisionPasses(lead, leadRung, true); }

        /// <param name="perfectionistBonus">False when the caller adds personality passes itself (PersonalityRules).</param>
        public static int RevisionPasses(CrewProfile lead, int leadRung, bool perfectionistBonus)
        {
            int passes = 2 + Math.Max(0, leadRung); // at least one chance to fix what the validator rejects
            if (perfectionistBonus && lead != null && lead.PersonalityTags.Exists(t => string.Equals(t, "Perfectionist", StringComparison.OrdinalIgnoreCase))) passes += 2;
            return Math.Min(5, passes);
        }
    }
}
