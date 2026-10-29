using System;
using System.Collections.Generic;

namespace KSA.Core.Design
{
    /// <summary>
    /// Flies a stack of stages through a mission's legs on paper (2026-10-29): each stage burns from the bottom up, the
    /// legs are paid for in flight order with that stage's Isp (the mean of sea level and vacuum in thick air), and the
    /// thrust-to-weight is checked at the start of every leg in the gravity of the world it is flown at. Stages marked
    /// launch-empty start dry and are filled at a refuel leg (the fuel depot); a tanker's cargo leaves at its delivery leg.
    /// </summary>
    public static class LegWalk
    {
        private const double G0 = 9.80665;

        public sealed class Stage
        {
            public int Label;
            public double Dry, Fuel, Thrust, IspVac, IspSl;
            /// <summary>Launched dry, filled at the depot.</summary>
            public bool LaunchEmpty;
        }

        public sealed class Result
        {
            public List<string> Problems = new List<string>();
            public List<string> Lines = new List<string>();
            public double LiftoffTwr;
            /// <summary>Delta-v still owed when the fuel ran out (0 when the whole mission is paid for).</summary>
            public double Shortfall;
            /// <summary>Index of the stage burning when each leg ends (-1 if it never ends).</summary>
            public int[] StageAtLegEnd;
            /// <summary>Propellant taken on at the fuel depot, tonnes.</summary>
            public double DepotFuel;
            /// <summary>A tanker's cargo pumped into the depot, tonnes.</summary>
            public double CargoDelivered;
        }

        /// <param name="stages">Bottom (first to fire) first.</param>
        /// <param name="payload">Everything above the top stage, tonnes.</param>
        public static Result Run(Stage[] stages, double payload, List<FlightLeg> legs)
        {
            var r = new Result { StageAtLegEnd = new int[legs.Count] };
            for (int k = 0; k < legs.Count; k++) r.StageAtLegEnd[k] = -1;
            int n = stages.Length;
            var fuels = new double[n];
            double mass = payload;
            for (int j = 0; j < n; j++)
            {
                fuels[j] = stages[j].LaunchEmpty ? 0 : stages[j].Fuel;
                mass += stages[j].Dry + fuels[j];
            }
            if (n > 0) r.LiftoffTwr = SeaLevelThrust(stages[0]) / (mass * DeltaVMap.KerbinGravity);

            int leg = 0, i = 0, checkedKey = -1;
            double owed = legs.Count > 0 ? legs[0].DeltaV : 0;
            while (leg < legs.Count)
            {
                FlightLeg l = legs[leg];
                if (l.Refuel)
                {
                    // Docked at the depot: the stages that launched empty are filled to the brim (the one that flew up keeps
                    // what it has left, and drops off when it is spent or too weak).
                    double add = 0;
                    for (int j = i; j < n; j++) { if (!stages[j].LaunchEmpty) continue; add += stages[j].Fuel - fuels[j]; fuels[j] = stages[j].Fuel; }
                    mass += add;
                    r.DepotFuel += add;
                    r.Lines.Add("At the depot: " + add.ToString("0.0") + " t of propellant taken on.");
                    r.StageAtLegEnd[leg] = i;
                    leg++;
                    owed = leg < legs.Count ? legs[leg].DeltaV : 0;
                    continue;
                }
                if (l.FuelDrop > 0)
                {
                    // A tanker pumps its cargo across, from the top stage down.
                    double want = l.FuelDrop, taken = 0;
                    for (int j = n - 1; j >= i && taken < want; j--)
                    {
                        double t = Math.Min(fuels[j], want - taken);
                        fuels[j] -= t;
                        taken += t;
                    }
                    mass -= taken;
                    r.CargoDelivered = taken;
                    if (taken < want - 1e-3)
                        r.Problems.Add("Only " + taken.ToString("0.0") + " of the " + want.ToString("0.0") + " t of cargo fuel is left on arrival at the depot. Carry more fuel in the top stage.");
                    else r.Lines.Add("At the depot: " + taken.ToString("0.0") + " t of cargo pumped across.");
                    r.StageAtLegEnd[leg] = i;
                    leg++;
                    owed = leg < legs.Count ? legs[leg].DeltaV : 0;
                    continue;
                }
                if (i >= n) break;
                Stage s = stages[i];
                if (s.Thrust <= 0 || s.IspVac <= 0 || fuels[i] <= 1e-6)
                {
                    if (s.LaunchEmpty && legs.FindIndex(leg, x => x.Refuel) >= 0)
                    {
                        r.Problems.Add("Stage " + s.Label + " is still empty (it fills at the depot), but the stages below it run dry before the " + l.Name + ". The stages that launch full must reach the depot.");
                        break;
                    }
                    mass -= s.Dry + fuels[i];
                    fuels[i] = 0;
                    i++;
                    continue;
                }

                // A stage that takes over a climb through the air part-way is already high in thin air: vacuum figures,
                // and the thrust a climb to orbit needs (2026-10-29: a Terrier finishing the last 30 m/s was judged at sea level).
                bool midAir = l.Atmospheric && owed < l.DeltaV - 1;
                bool thick = l.Atmospheric && !midAir;
                double isp = thick ? 0.5 * (s.IspVac + (s.IspSl > 0 ? s.IspSl : s.IspVac)) : s.IspVac;
                double thrust = thick ? SeaLevelThrust(s) : s.Thrust;
                double minTwr = midAir ? Math.Min(l.MinTwr, 0.6) : l.MinTwr;
                if (checkedKey != i * 1000 + leg)
                {
                    checkedKey = i * 1000 + leg;
                    double twr = thrust / (mass * l.Gravity);
                    // Fuel left over from an earlier leg but too weak for this one: stage it away, as a pilot would.
                    if (minTwr > 0 && twr < minTwr && fuels[i] < s.Fuel && i < n - 1)
                    {
                        r.Lines.Add("Stage " + s.Label + " is dropped with fuel left before the " + l.Name + " (too weak for it).");
                        mass -= s.Dry + fuels[i];
                        fuels[i] = 0;
                        i++;
                        continue;
                    }
                    if (minTwr > 0 && twr < minTwr)
                        r.Problems.Add("Stage " + s.Label + " flies the " + l.Name + " at thrust-to-weight " + twr.ToString("0.00") + (midAir ? "" : Where(l)) + "; it needs at least " + minTwr.ToString("0.0") + ". Give that stage a stronger engine (or more of them round the side), or carry less above it.");
                    if (leg == 0 && i == 0 && l.MaxTwr > 0 && twr > l.MaxTwr)
                        r.Problems.Add("Lift-off thrust-to-weight is " + twr.ToString("0.00") + " at sea level, over " + l.MaxTwr.ToString("0.0") + ": the rocket punches through the thick air so fast that drag eats the fuel. Use a gentler launch engine or fewer side boosters, and put the delta-v in the stages above.");
                }
                double end = mass * Math.Exp(-owed / (isp * G0));
                double used = mass - end;
                if (used <= fuels[i])
                {
                    fuels[i] -= used;
                    mass = end;
                    r.Lines.Add("The " + l.Name + " (" + Math.Round(l.DeltaV) + " m/s): stage " + s.Label + ", thrust-to-weight " + (thrust / (Math.Max(0.001, mass + used) * l.Gravity)).ToString("0.00") + Where(l) + ".");
                    r.StageAtLegEnd[leg] = i;
                    leg++;
                    owed = leg < legs.Count ? legs[leg].DeltaV : 0;
                }
                else
                {
                    owed -= isp * G0 * Math.Log(mass / (mass - fuels[i]));
                    mass -= fuels[i];
                    fuels[i] = 0;
                }
            }
            if (leg < legs.Count)
            {
                double shortBy = owed;
                var left = new List<string>();
                for (int k = leg; k < legs.Count; k++) { if (k > leg) shortBy += legs[k].DeltaV; left.Add(legs[k].Name); }
                r.Shortfall = Math.Max(1, shortBy);
                if (!r.Problems.Exists(p => p.Contains("still empty")))
                    r.Problems.Add("The fuel runs out during the " + legs[leg].Name + ": about " + Math.Round(shortBy) + " m/s short (still to fly: " + string.Join(", ", left.ToArray()) + "). Add fuel or a stage, or use more efficient engines in the upper stages.");
            }
            return r;
        }

        public static double SeaLevelThrust(Stage s)
        {
            if (s.IspVac <= 0) return s.Thrust;
            return s.Thrust * (s.IspSl > 0 ? Math.Min(1, s.IspSl / s.IspVac) : 1);
        }

        private static string Where(FlightLeg l)
        {
            return string.Equals(l.Body, "Kerbin", StringComparison.OrdinalIgnoreCase) && l.Gravity >= 9 ? (l.Atmospheric ? " at sea level" : "") : " in " + l.Body + "'s gravity";
        }
    }
}
