using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    /// <summary>
    /// The engineering team's own drawing board: a rule-based designer, no AI. Rebuilt 2026-10-29 around the mission's
    /// legs (<see cref="FlightBudget"/>): it tries every way of sharing the legs out among 1 to 5 stages, sizes each stage
    /// from the top down (each one carries everything above it) with the lightest engine, tank stack and side boosters that
    /// fly its legs with a 10% margin and enough thrust for the gravity each leg is flown in, and keeps the lightest
    /// rocket. Solid boosters only help climb out of Kerbin (they cannot throttle for a landing); landing legs go on the
    /// stage that touches down. Every design is then checked by the same analysis as an AI design.
    /// </summary>
    public static class ProceduralDesigner
    {
        private const double G0 = 9.80665;
        private const double Margin = 1.10;      // aim 10% over every leg
        private const int MaxTanks = 8;
        private const int MaxStages = 8;
        private static readonly int[] SideCounts = { 2, 3, 4, 6, 8 };

        private sealed class Option
        {
            public PartInfo Engine;
            public PartInfo Tank;
            public int Tanks;
            public PartInfo Booster;    // a core solid booster (Boosters == 1, no engine) or side boosters
            public int Boosters;
            public double Dry, Fuel, Thrust, IspNum, IspSlNum;
            public double Wet { get { return Dry + Fuel; } }
        }

        private sealed class Pick
        {
            public Option Option;
            public double Start;   // mass at ignition, everything above included
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
                if (p.Kind == PartKind.Command && p.CrewCapacity >= Math.Max(1, crew) && (pod == null || (pod.RequiresDlc && !p.RequiresDlc) || (pod.RequiresDlc == p.RequiresDlc && p.Mass < pod.Mass))) pod = p; // stock before DLC pods
            // No pod seats everyone (a rescue brings the stranded home): the roomiest pod plus crew cabins (2026-10-28).
            var cabins = new List<PartInfo>();
            if (pod == null)
            {
                foreach (PartInfo p in allowed)
                    if (p.Kind == PartKind.Command && p.CrewCapacity > 0 && (pod == null || p.CrewCapacity > pod.CrewCapacity || (p.CrewCapacity == pod.CrewCapacity && p.Mass < pod.Mass))) pod = p;
                PartInfo cabin = null;
                foreach (PartInfo p in allowed)
                    if (p.Kind == PartKind.Habitat && p.CrewCapacity > 0 && (cabin == null || p.Mass / p.CrewCapacity < cabin.Mass / cabin.CrewCapacity)) cabin = p;
                int seats = pod != null ? pod.CrewCapacity : 0;
                while (pod != null && cabin != null && seats < crew && cabins.Count < 4) { cabins.Add(cabin); seats += cabin.CrewCapacity; }
                if (pod == null || seats < Math.Max(1, crew)) { why = "no crew pod (or pod and crew cabin) with " + Math.Max(1, crew) + " seats"; return null; }
            }
            var plan = new StagePlan { DesignName = "Engineering team " + (req.Mission != null ? req.Mission.Name : "rocket"), Rationale = "Drawn up by the engineering team from the researched parts." };
            plan.Payload.Command = pod.Name;
            foreach (PartInfo c in cabins) plan.Payload.Extras.Add(c.Name); // first, so they stack straight on the pod
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
            // RCS blocks come in fours, round the pod (2026-10-29).
            string block = plan.Payload.Extras.Find(e => { PartInfo x = catalog.Get(e); return x != null && x.Kind == PartKind.Rcs && !x.IsStackable; });
            if (block != null) for (int k = 0; k < 3; k++) plan.Payload.Extras.Add(block);
            // A docking port goes on the very top: nothing may sit on it (2026-10-29).
            for (int k = plan.Payload.Extras.Count - 1, moved = 0; k >= 0 + moved; k--)
            {
                PartInfo x = catalog.Get(plan.Payload.Extras[k]);
                if (x == null || x.Kind != PartKind.Docking) continue;
                string port = plan.Payload.Extras[k];
                plan.Payload.Extras.RemoveAt(k);
                plan.Payload.Extras.Add(port);
                moved++;
            }
            if (req.DepotTanks > 0)
            {
                // A fuel depot's core: the tank farm stacks on the pod, under the docking port (2026-10-29). The tank with
                // the most propellant per tonne of tank, enough of them to hold what the depot is meant to.
                PartInfo farm = null;
                foreach (PartInfo p in allowed)
                    if (p.Kind == PartKind.Tank && p.FuelMass > 0 && p.HasTopNode && p.HasBottomNode && (farm == null || p.FuelMass / Math.Max(0.01, p.Mass) > farm.FuelMass / Math.Max(0.01, farm.Mass) || (Math.Abs(p.FuelMass / Math.Max(0.01, p.Mass) - farm.FuelMass / Math.Max(0.01, farm.Mass)) < 0.01 && p.FuelMass > farm.FuelMass))) farm = p;
                if (farm == null) { why = "no fuel tank researched for the depot"; return null; }
                int at = plan.Payload.Extras.FindIndex(e => { PartInfo x = catalog.Get(e); return x != null && x.Kind == PartKind.Docking; });
                if (at < 0) at = plan.Payload.Extras.Count;
                int count = Math.Max(1, (int)Math.Ceiling(req.DepotTanks / farm.FuelMass - 1e-6));
                for (int k = 0; k < Math.Min(count, 8); k++) plan.Payload.Extras.Insert(at, farm.Name);
                plan.Payload.TanksEmpty = true; // it goes up empty; tankers fill it
            }
            double payload = pod.Mass + pod.FuelMass;
            foreach (string e in plan.Payload.Extras) { PartInfo x = catalog.Get(e); if (x != null) payload += x.Mass + (plan.Payload.TanksEmpty && x.Kind == PartKind.Tank ? 0 : x.FuelMass); }

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
            if (engines.Count == 0 && boosters.Count == 0) { why = "no engine or booster researched"; return null; }

            // The legs to fly, each with the margin. A hop with no map gets one climb from the requirement.
            var flight = new List<FlightLeg>();
            if (need != null && need.Legs != null)
                foreach (FlightLeg leg in need.Legs)
                    flight.Add(new FlightLeg { Name = leg.Name, Body = leg.Body, DeltaV = leg.DeltaV * Margin, Gravity = leg.Gravity, MinTwr = leg.MinTwr, MaxTwr = leg.MaxTwr, Atmospheric = leg.Atmospheric, Landing = leg.Landing, Refuel = leg.Refuel, FuelDrop = leg.FuelDrop });
            else
            {
                // One figure: a hop is one climb; anything bigger climbs through the air first, then burns in thin air.
                double total = (need != null ? need.DeltaV : 1800) * Margin;
                double twr = Math.Max(1.45, need != null ? need.MinTwr + 0.25 : 1.45);
                if (total <= 2500) flight.Add(new FlightLeg { Name = "climb", DeltaV = total, Gravity = gravity, MinTwr = twr, Atmospheric = true });
                else
                {
                    flight.Add(new FlightLeg { Name = "lift-off and climb through the air", DeltaV = 1800 * Margin, Gravity = gravity, MinTwr = twr, Atmospheric = true });
                    flight.Add(new FlightLeg { Name = "rest of the flight", DeltaV = total - 1800 * Margin, Gravity = gravity, MinTwr = 0.3 });
                }
            }
            int landingLeg = flight.FindIndex(x => x.Landing);
            if (landingLeg >= 0 && legs == null) { why = "no landing legs researched"; return null; }
            // Only the climb out of Kerbin may use solid boosters: they cannot throttle for a landing or restart.
            int kerbinLegs = need != null && need.Legs != null ? Math.Min(2, flight.Count) : flight.Count;

            bool radialDecoupler = allowed.Exists(x => x.Kind == PartKind.Decoupler && !x.IsStackable);
            List<Option> liquid = Options(engines, tanks, boosters, false, false);
            List<Option> withSolids = Options(engines, tanks, boosters, true, radialDecoupler);
            double decouplerMass = decoupler != null ? decoupler.Mass : 0;
            double legsMass = legs != null ? legs.Mass * 4 : 0;

            // Every way to cut the legs into consecutive stages: bit k set = a new stage starts after leg k.
            int legCount = flight.Count;
            int refuelLeg = flight.FindIndex(x => x.Refuel);
            var memo = new Dictionary<string, Pick>();
            Pick[] bestStages = null;
            int[][] bestChunks = null;
            double bestMass = double.MaxValue;
            // For the "why not": the leg where the try that got furthest (most stages sized) ran out of options.
            string shortOn = null;
            int furthest = -1;
            for (int mask = 0; mask < (1 << (legCount - 1)); mask++)
            {
                var chunks = new List<int[]>();
                int from = 0;
                for (int k = 0; k < legCount - 1; k++) if ((mask & (1 << k)) != 0) { chunks.Add(new[] { from, k }); from = k + 1; }
                chunks.Add(new[] { from, legCount - 1 });
                if (chunks.Count > MaxStages) continue;
                // A stage either reaches the depot or leaves it (it cannot be both full at launch and filled there), and
                // every stage burns something.
                if (chunks.Exists(ch => ch[0] <= refuelLeg && ch[1] > refuelLeg) || chunks.Exists(ch => !Burns(flight, ch[0], ch[1]))) continue;

                // Top stage first: it carries the payload, the one below carries it, and so on. Stages after the depot
                // launch empty, so below the depot they weigh only their dry mass.
                var picks = new Pick[chunks.Count];
                double above = payload, postFuel = 0;
                bool ok = true, dry = false;
                for (int c = chunks.Count - 1; c >= 0; c--)
                {
                    int a = chunks[c][0], b = chunks[c][1];
                    if (refuelLeg >= 0 && b <= refuelLeg && !dry) { above -= postFuel; dry = true; }
                    bool solidsOk = b < kerbinLegs;
                    double extra = decouplerMass + (landingLeg >= a && landingLeg <= b ? legsMass : 0);
                    string key = a + "-" + b + "-" + Math.Round(above, 3);
                    Pick pick;
                    if (!memo.TryGetValue(key, out pick))
                    {
                        pick = Size(flight, a, b, above + extra, solidsOk ? withSolids : liquid);
                        memo[key] = pick;
                    }

                    if (pick == null)
                    {
                        ok = false;
                        int sized = chunks.Count - 1 - c;
                        if (sized > furthest) { furthest = sized; shortOn = flight[a].Name + (b > a ? " to the " + flight[b].Name : ""); }
                        break;
                    }
                    picks[c] = pick;
                    above = pick.Start;
                    if (refuelLeg >= 0 && a > refuelLeg) postFuel += pick.Option.Fuel;
                }
                if (!ok || above >= bestMass) continue;
                bestMass = above;
                bestStages = picks;
                bestChunks = chunks.ToArray();
            }
            if (bestStages == null)
            {
                why = "no stack of the researched parts flies the whole mission (" + Math.Round(need != null ? need.DeltaV : 0) + " m/s): with everything above it, no stage manages the " + (shortOn ?? "climb");
                return null;
            }

            StagePlan best = Build(plan, bestStages, bestChunks, flight, decoupler, legs, landingLeg, refuelLeg);
            DesignAnalysis check = StageAnalyzer.Analyze(best, catalog, need, gravity);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PartInfo p in allowed) names.Add(p.Name);
            List<string> invalid = StagePlanValidator.Validate(best, catalog, names);
            string tanksShort = DesignSession.MissingDepotTanks(best, req, catalog);
            if (!check.Passes || invalid.Count > 0 || DesignSession.MissingMustInclude(best, req).Count > 0 || tanksShort != null)
            {

                List<string> missing = DesignSession.MissingMustInclude(best, req);
                why = check.Problems.Count > 0 ? check.Problems[0] : invalid.Count > 0 ? invalid[0] : tanksShort ?? (missing.Count > 0 ? missing[0] : "the design misses something the brief asks for");
                return null;
            }
            return best;
        }

        /// <summary>The lightest stage that flies legs a..b (inclusive) under <paramref name="above"/> tonnes, or null.</summary>
        private static Pick Size(List<FlightLeg> flight, int a, int b, double above, List<Option> options)
        {
            foreach (Option o in options) // lightest first
            {
                double start = above + o.Wet;
                double mass = start;
                bool ok = true;
                double ispVac = o.IspNum / o.Thrust, ispSl = o.IspSlNum / o.Thrust;
                for (int k = a; k <= b && ok; k++)
                {
                    FlightLeg l = flight[k];
                    double isp = l.Atmospheric ? 0.5 * (ispVac + ispSl) : ispVac;
                    double thrust = l.Atmospheric ? o.Thrust * Math.Min(1, ispSl / ispVac) : o.Thrust;
                    double twr = thrust / (mass * l.Gravity);
                    if (twr < l.MinTwr) ok = false;
                    else if (k == 0 && l.MaxTwr > 0 && twr > l.MaxTwr) ok = false; // lift-off: too hard in thick air
                    mass *= Math.Exp(-l.DeltaV / (isp * G0));
                    mass -= l.FuelDrop; // a tanker's cargo is fuel the stage carries and hands over
                }
                if (!ok || start - mass > o.Fuel) continue;
                return new Pick { Option = o, Start = start };
            }
            return null;
        }

        private static List<Option> Options(List<PartInfo> engines, List<PartInfo> tanks, List<PartInfo> boosters, bool solids, bool sideBoosters)
        {
            var list = new List<Option>();
            foreach (PartInfo e in engines)
                foreach (PartInfo t in tanks)
                    for (int c = 1; c <= MaxTanks; c++)
                    {
                        var o = new Option { Engine = e, Tank = t, Tanks = c };
                        Sum(o);
                        list.Add(o);
                        if (solids && sideBoosters)
                            foreach (PartInfo b in boosters)
                                foreach (int nb in SideCounts) { var ob = new Option { Engine = e, Tank = t, Tanks = c, Booster = b, Boosters = nb }; Sum(ob); list.Add(ob); }
                    }
            // A solid booster alone makes a stage too (stacked, if it can be).
            if (solids) foreach (PartInfo b in boosters) if (b.IsStackable) { var o = new Option { Booster = b, Boosters = 1 }; Sum(o); list.Add(o); }
            list.RemoveAll(o => o.Thrust <= 0 || o.IspNum <= 0);
            list.Sort((x, y) => x.Wet.CompareTo(y.Wet));
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
            o.IspSlNum += p.Thrust * n * (p.IspSeaLevel > 0 ? p.IspSeaLevel : p.IspVacuum);
        }

        /// <summary>True when legs a..b ask for some burning or carrying (not only a refuel stop).</summary>
        private static bool Burns(List<FlightLeg> flight, int a, int b)
        {
            for (int k = a; k <= b; k++) if (flight[k].DeltaV > 0 || flight[k].FuelDrop > 0) return true;
            return false;
        }

        private static StagePlan Build(StagePlan template, Pick[] stages, int[][] chunks, List<FlightLeg> flight, PartInfo decoupler, PartInfo legs, int landingLeg, int refuelLeg)
        {
            var plan = new StagePlan { DesignName = template.DesignName };
            plan.Payload.Command = template.Payload.Command;
            plan.Payload.Extras.AddRange(template.Payload.Extras);
            plan.Payload.TanksEmpty = template.Payload.TanksEmpty;
            var why = new List<string>();
            for (int k = 0; k < stages.Length; k++)
            {
                Option o = stages[k].Option;
                int a = chunks[k][0], b = chunks[k][1];
                var s = new PlanStage { Stage = k, Role = k == 0 ? "launch" : RoleOf(flight, a, b), Decoupler = decoupler != null ? decoupler.Name : string.Empty, LaunchEmpty = refuelLeg >= 0 && a > refuelLeg };
                if (o.Tank != null) s.Core.Add(new PlanPart { Part = o.Tank.Name, Count = o.Tanks });
                if (o.Engine != null) s.Core.Add(new PlanPart { Part = o.Engine.Name, Count = 1 });
                if (o.Booster != null)
                {
                    if (o.Engine == null && o.Boosters == 1) s.Core.Add(new PlanPart { Part = o.Booster.Name, Count = 1 });
                    else s.Radial.Add(new PlanPart { Part = o.Booster.Name, Count = o.Boosters, Symmetry = o.Boosters });
                }
                if (legs != null && landingLeg >= a && landingLeg <= b) s.Radial.Add(new PlanPart { Part = legs.Name, Count = 4, Symmetry = 4 });
                plan.Stages.Add(s);
                var names = new List<string>();
                for (int i = a; i <= b; i++) names.Add(flight[i].Name);
                why.Add("stage " + k + " flies the " + string.Join(", then the ", names.ToArray()) + (s.LaunchEmpty ? " (it launches empty and fills at the depot)" : ""));
            }
            plan.Rationale = "Drawn up by the engineering team from the researched parts, leg by leg with a 10% margin: " + string.Join("; ", why.ToArray()) + ".";
            return plan;
        }

        /// <summary>A short stage role from the legs it flies (the validator only cares about "launch").</summary>
        private static string RoleOf(List<FlightLeg> flight, int a, int b)
        {
            var tags = new List<string>();
            for (int i = a; i <= b; i++)
            {
                string n = flight[i].Name;
                if (flight[i].Refuel) continue;
                string t = flight[i].FuelDrop > 0 ? "cargo" : n.StartsWith("climb to") ? "ascent" : n.StartsWith("transfer") ? "transfer" : n.StartsWith("capture") ? "capture"
                    : n.StartsWith("rendezvous") ? "rendezvous" : n.StartsWith("descent") ? "lander"
                    : n.StartsWith("take-off") || n.StartsWith("climb out") ? "ascent stage" : "return";
                if (!tags.Contains(t)) tags.Add(t);
            }
            return string.Join("+", tags.ToArray());
        }
    }
}
