using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Missions;
using KSA.Core.Parts;

namespace KSA.Core.Design
{
    public sealed class StageAnalysis
    {
        public int Stage { get; set; }
        public string Role { get; set; }
        public double StartMass { get; set; }
        public double EndMass { get; set; }
        public double Thrust { get; set; }
        public double Isp { get; set; }
        public double DeltaV { get; set; }
        public double Twr { get; set; }
        public double Cost { get; set; }

        public StageAnalysis() { Role = string.Empty; }
    }

    public sealed class DesignAnalysis
    {
        public List<StageAnalysis> Stages { get; } = new List<StageAnalysis>();
        public double TotalDeltaV { get; set; }
        public double LiftoffTwr { get; set; }
        public double TotalMass { get; set; }
        public double TotalCost { get; set; }
        public int PartCount { get; set; }
        public double RequiredDeltaV { get; set; }
        public double RequiredTwr { get; set; }
        public List<string> Problems { get; } = new List<string>();

        public double DeltaVMargin { get { return RequiredDeltaV > 0 ? (TotalDeltaV - RequiredDeltaV) / RequiredDeltaV : 1; } }
        public bool MeetsDeltaV { get { return TotalDeltaV >= RequiredDeltaV; } }
        public bool MeetsTwr { get { return RequiredTwr <= 0 || LiftoffTwr >= RequiredTwr; } }
        public bool Passes { get { return MeetsDeltaV && MeetsTwr && Problems.Count == 0; } }

        /// <summary>0..1 quality for the simulator: margin and TWR, capped so a 25% margin is "excellent".</summary>
        public double DesignScore
        {
            get
            {
                if (!MeetsDeltaV) return Math.Max(0.05, 0.4 * Math.Max(0, TotalDeltaV / Math.Max(1, RequiredDeltaV)));
                double margin = Math.Min(1.0, DeltaVMargin / 0.25);
                double twr = MeetsTwr ? 1.0 : Math.Max(0, LiftoffTwr / Math.Max(0.01, RequiredTwr));
                return Math.Min(1.0, 0.45 + 0.4 * margin + 0.15 * twr);
            }
        }
    }

    /// <summary>What a mission profile needs, from dv_requirements.cfg with built-in stock defaults (Jira KSA-44).</summary>
    public sealed class MissionRequirements
    {
        public const string NodeName = "KSA_DV_REQUIREMENT";

        public sealed class Entry
        {
            public MissionProfile Profile;
            public string Body;
            /// <summary>Optional: applies to one mission type id only, beating the profile/body entry.</summary>
            public string MissionType = string.Empty;
            public double DeltaV;
            public double MinTwr;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public MissionRequirements() { AddDefaults(); }

        private void AddDefaults()
        {
            Add(MissionProfile.Suborbital, "Kerbin", 1800, 1.2);
            Add(MissionProfile.Orbit, "Kerbin", 3600, 1.2);
            Add(MissionProfile.Station, "Kerbin", 3900, 1.2);
            Add(MissionProfile.Flyby, "Mun", 4600, 1.2);
            Add(MissionProfile.OrbitBody, "Mun", 5000, 1.2);
            Add(MissionProfile.Land, "Mun", 6500, 1.2);
            Add(MissionProfile.Base, "Mun", 6300, 1.2);
            Add(MissionProfile.Flyby, "Minmus", 4600, 1.2);
            Add(MissionProfile.OrbitBody, "Minmus", 4900, 1.2);
            Add(MissionProfile.Land, "Minmus", 5600, 1.2);
            Add(MissionProfile.Base, "Minmus", 5400, 1.2);
            Add(MissionProfile.Flyby, "Duna", 5200, 1.2);
            Add(MissionProfile.OrbitBody, "Duna", 6000, 1.2);
            Add(MissionProfile.Land, "Duna", 7800, 1.2);
            Add(MissionProfile.Base, "Duna", 7500, 1.2);
            Add(MissionProfile.Flyby, "Eve", 5100, 1.2);
            Add(MissionProfile.OrbitBody, "Eve", 6200, 1.2);
            Add(MissionProfile.Base, "Eve", 6600, 1.2);
            Add(MissionProfile.Flyby, "Jool", 6000, 1.2);
            Add(MissionProfile.OrbitBody, "Jool", 7500, 1.2);
        }

        public void Add(MissionProfile profile, string body, double deltaV, double minTwr)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Profile == profile && string.Equals(e.Body, body, StringComparison.OrdinalIgnoreCase)) { e.DeltaV = deltaV; e.MinTwr = minTwr; return; }
            }
            _entries.Add(new Entry { Profile = profile, Body = body, DeltaV = deltaV, MinTwr = minTwr });
        }

        public Entry For(MissionType type)
        {
            if (type == null) return new Entry { DeltaV = 3600, MinTwr = 1.2, Body = "Kerbin", Profile = MissionProfile.Orbit };
            foreach (Entry e in _entries)
                if (e.MissionType.Length > 0 && string.Equals(e.MissionType, type.Id, StringComparison.OrdinalIgnoreCase)) return e;
            foreach (Entry e in _entries)
                if (e.Profile == type.Profile && string.Equals(e.Body, type.Body, StringComparison.OrdinalIgnoreCase)) return e;
            // Unknown body: scale from the Kerbin orbit figure by profile.
            double dv = 3600;
            switch (type.Profile)
            {
                case MissionProfile.Suborbital: dv = 1800; break;
                case MissionProfile.Flyby: dv = 5500; break;
                case MissionProfile.OrbitBody: dv = 6500; break;
                case MissionProfile.Land: case MissionProfile.Base: dv = 8000; break;
            }
            return new Entry { Profile = type.Profile, Body = type.Body, DeltaV = dv, MinTwr = 1.2 };
        }

        public static MissionRequirements Parse(IEnumerable<DataNode> nodes)
        {
            var req = new MissionRequirements();
            if (nodes == null) return req;
            foreach (DataNode n in nodes)
            {
                if (n == null || !string.Equals(n.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                MissionProfile profile;
                if (!Enum.TryParse(n.GetValue("profile", "Orbit"), true, out profile)) continue;
                string missionType = n.GetValue("missionType", string.Empty);
                if (missionType.Length > 0) req._entries.Add(new Entry { MissionType = missionType, Profile = profile, Body = n.GetValue("body", "Kerbin"), DeltaV = n.GetDouble("deltaV", 3600), MinTwr = n.GetDouble("minTwr", 1.2) });
                else req.Add(profile, n.GetValue("body", "Kerbin"), n.GetDouble("deltaV", 3600), n.GetDouble("minTwr", 1.2));
            }
            return req;
        }
    }

    /// <summary>Rocket-equation analysis of a plan from the catalog's part stats. Stage 0 fires first, at the bottom.</summary>
    public static class StageAnalyzer
    {
        public const double G0 = 9.80665;

        public static DesignAnalysis Analyze(StagePlan plan, IPartCatalog catalog, MissionRequirements.Entry requirement, double surfaceGravity = 9.81)
        {
            var a = new DesignAnalysis();
            if (plan == null || catalog == null) { a.Problems.Add("Nothing to analyze."); return a; }
            if (requirement != null) { a.RequiredDeltaV = requirement.DeltaV; a.RequiredTwr = requirement.MinTwr; }

            // Payload sits on top of everything.
            double payloadMass = 0, payloadCost = 0;
            int parts = 0;
            PartInfo cmd = catalog.Get(plan.Payload.Command);
            if (cmd != null) { payloadMass += cmd.Mass + cmd.FuelMass; payloadCost += cmd.Cost; parts++; }
            foreach (string e in plan.Payload.Extras)
            {
                PartInfo x = catalog.Get(e);
                if (x != null) { payloadMass += x.Mass + x.FuelMass; payloadCost += x.Cost; parts++; }
            }

            // Per-stage dry mass, fuel, thrust, isp (thrust-weighted), cost.
            var stageDry = new double[plan.Stages.Count];
            var stageFuel = new double[plan.Stages.Count];
            var stageThrust = new double[plan.Stages.Count];
            var stageIspNum = new double[plan.Stages.Count];
            var stageCost = new double[plan.Stages.Count];
            for (int i = 0; i < plan.Stages.Count; i++)
            {
                PlanStage s = plan.Stages[i];
                double tankFuel = 0;
                bool liquidEngine = false;
                Accumulate(s.Core, catalog, false, ref stageDry[i], ref stageFuel[i], ref tankFuel, ref liquidEngine, ref stageThrust[i], ref stageIspNum[i], ref stageCost[i], ref parts);
                Accumulate(s.Radial, catalog, true, ref stageDry[i], ref stageFuel[i], ref tankFuel, ref liquidEngine, ref stageThrust[i], ref stageIspNum[i], ref stageCost[i], ref parts);
                // Tank propellant burns only in a liquid engine of the same stage (2026-10-28: tanks hung under a solid
                // booster were counted as fuel, so designs were approved on delta-v they could never have).
                if (liquidEngine) stageFuel[i] += tankFuel;
                else if (tankFuel > 0)
                {
                    stageDry[i] += tankFuel;
                    a.Problems.Add("Stage " + s.Stage + ": its fuel tanks have no liquid engine in the same stage to burn them, so their " + tankFuel.ToString("0.0") + " t of propellant is dead weight. Put a liquid engine under the tanks, or leave the tanks out (a solid booster carries its own fuel).");
                }
                PartInfo dec = catalog.Get(s.Decoupler);
                if (dec != null) { stageDry[i] += dec.Mass; stageCost[i] += dec.Cost; parts++; }
            }

            // Burn order: stage 0 first with everything above it still attached.
            double massAbove = payloadMass;
            double totalDv = 0;
            var analyses = new List<StageAnalysis>();
            for (int i = plan.Stages.Count - 1; i >= 0; i--)
            {
                // compute cumulative mass above stage i (stages i+1 .. n-1 full)
                massAbove = payloadMass;
                for (int j = i + 1; j < plan.Stages.Count; j++) massAbove += stageDry[j] + stageFuel[j];
                double start = massAbove + stageDry[i] + stageFuel[i];
                double end = massAbove + stageDry[i];
                double isp = stageThrust[i] > 0 ? stageIspNum[i] / stageThrust[i] : 0;
                double dv = isp > 0 && start > end && end > 0 ? isp * G0 * Math.Log(start / end) : 0;
                var sa = new StageAnalysis
                {
                    Stage = plan.Stages[i].Stage,
                    Role = plan.Stages[i].Role,
                    StartMass = start,
                    EndMass = end,
                    Thrust = stageThrust[i],
                    Isp = isp,
                    DeltaV = dv,
                    Twr = start > 0 ? stageThrust[i] / (start * surfaceGravity) : 0,
                    Cost = stageCost[i]
                };
                analyses.Add(sa);
                totalDv += dv;
                if (stageThrust[i] <= 0) a.Problems.Add("Stage " + sa.Stage + " has no thrust.");
                if (stageFuel[i] <= 0 && stageThrust[i] > 0) a.Problems.Add("Stage " + sa.Stage + " has engines but no propellant.");
            }
            analyses.Reverse();
            a.Stages.AddRange(analyses);
            a.TotalDeltaV = totalDv;
            a.LiftoffTwr = a.Stages.Count > 0 ? a.Stages[0].Twr : 0;
            a.TotalMass = a.Stages.Count > 0 ? a.Stages[0].StartMass : payloadMass;
            a.TotalCost = payloadCost;
            foreach (double c in stageCost) a.TotalCost += c;
            a.PartCount = parts;
            return a;
        }

        private static void Accumulate(List<PlanPart> list, IPartCatalog catalog, bool radial, ref double dry, ref double fuel, ref double tankFuel, ref bool liquidEngine, ref double thrust, ref double ispNum, ref double cost, ref int parts)
        {
            foreach (PlanPart p in list)
            {
                PartInfo info = catalog.Get(p.Part);
                if (info == null) continue;
                int n = Math.Max(1, p.Count) * (radial ? Math.Max(1, p.Symmetry) : 1);
                if (radial && p.Count > 0 && p.Symmetry > 1 && p.Count == p.Symmetry) n = p.Count; // "4 boosters in 4x symmetry" means 4 total
                dry += info.Mass * n;
                // A solid booster carries and burns its own propellant; everything else's feeds the liquid engines.
                if (info.Kind == PartKind.Booster) fuel += info.FuelMass * n; else tankFuel += info.FuelMass * n;
                if (info.Kind == PartKind.Engine && info.Thrust > 0) liquidEngine = true;
                cost += info.Cost * n;
                parts += n;
                if (info.ProducesThrust)
                {
                    thrust += info.Thrust * n;
                    ispNum += info.Thrust * n * (info.IspVacuum > 0 ? info.IspVacuum : info.IspSeaLevel);
                }
            }
        }

        /// <summary>Feedback text for the model when a design falls short (GDD 5.2 step 6).</summary>
        public static string Feedback(DesignAnalysis a)
        {
            var lines = new List<string>();
            if (a == null) return "No analysis.";
            foreach (string p in a.Problems) lines.Add(p);
            if (!a.MeetsDeltaV)
                lines.Add("Total delta-v is " + Math.Round(a.TotalDeltaV) + " m/s; the mission needs " + Math.Round(a.RequiredDeltaV) + " m/s. Short by " + Math.Round(a.RequiredDeltaV - a.TotalDeltaV) + " m/s.");
            if (!a.MeetsTwr)
                lines.Add("Liftoff TWR is " + a.LiftoffTwr.ToString("0.00") + "; it must be at least " + a.RequiredTwr.ToString("0.00") + ". Add thrust or shed mass in the launch stage.");
            for (int i = 0; i < a.Stages.Count; i++)
            {
                StageAnalysis s = a.Stages[i];
                lines.Add("Stage " + s.Stage + " (" + s.Role + "): " + Math.Round(s.DeltaV) + " m/s, TWR " + s.Twr.ToString("0.00") + ", " + s.StartMass.ToString("0.0") + " t at ignition.");
            }
            if (a.Passes) lines.Insert(0, "The design meets the requirements.");
            return string.Join("\n", lines.ToArray());
        }
    }
}
