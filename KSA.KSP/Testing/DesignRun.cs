using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.KSP.Build;
using KSA.KSP.Parts;

namespace KSA.KSP.Testing
{
    /// <summary>
    /// The engineers draw the mission list with low-tech parts and save each rocket in the save's VAB folder, to look at, edit
    /// and fly on the bench (2026-10-31, T: "redesign the rockets with research 100 or less"). Each run replaces the "Engineers ..."
    /// craft, so a craft to keep is saved under another name (T: "tired of deleting").
    /// </summary>
    public static class DesignRun
    {
        public const double MaxNodeCost = 100;
        private static readonly string[] Rows =
        {
            "MunFlyby:1", "MunOrbit:1", "MunOrbit:2", "MunOrbit:3", "MunLanding:2", "MunLanding:3",
            "MinmusOrbit:1", "MinmusOrbit:2", "MinmusOrbit:3", "MinmusLanding:2", "MinmusLanding:3",
            "EveFlyby:2", "MohoLanding:3", "DunaLanding:3", "DresLanding:2", "JoolFlyby:2", "EelooLanding:3"
        };

        /// <summary>One line per rocket, from the last run.</summary>
        public static readonly List<string> Lines = new List<string>();
        public static string ReportPath { get { return Path.Combine(Path.GetDirectoryName(FlightTestBench.ReportPath), "designs.txt"); } }

        public static void Run()
        {
            Lines.Clear();
            string folder = KspPlayerDesigns.FolderFor("VAB");
            if (KsaCore.Agency == null || folder == null) { Lines.Add("No game loaded."); return; }
            foreach (string row in Rows)
            {
                string[] bits = row.Split(':');
                int crew = int.Parse(bits[1]);
                MissionType type = KsaCore.Agency.Catalogs.Missions.Get(bits[0]);
                if (type == null) continue;
                string name = "Engineers " + type.Name + " crew " + crew;
                string why; bool viaDepot;
                DesignRecord r = KsaCore.Agency.DesignWithinTech(type, crew, MaxNodeCost, out why, out viaDepot);
                if (r == null) { Lines.Add(name + ": cannot be done: " + why); continue; }
                string size = Math.Round(r.TotalDeltaV).ToString("#,##0") + " of " + Math.Round(r.RequiredDeltaV).ToString("#,##0") + " m/s, " + Math.Round(r.TotalMass) + " t, TWR " + r.LiftoffTwr + ", " + r.PartCount + " parts";
                if (viaDepot) { Lines.Add(name + ": only with a stop at a fuel depot (" + size + "), not saved"); continue; }
                string path = Path.Combine(folder, name + ".craft");
                CraftWriter.Result w = CraftWriter.WriteTo(r.Plan, KsaCore.Agency.Parts, name, path);
                Lines.Add(name + ": " + size + (w.Ok ? ", saved" : ", NOT saved: " + w.Error));
            }
            // The Director's lander, one craft for each crew pod (2026-10-31).
            foreach (Template t in KsaCore.Agency.Catalogs.Templates.Templates)
            {
                if (!t.Id.StartsWith("DirectorateLander", StringComparison.Ordinal) || !t.IsAvailable(KsaCore.Agency.InstalledMods)) continue;
                string plan = Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", t.PlanFile.Replace('/', Path.DirectorySeparatorChar)));
                StagePlan parsed; string error;
                if (!File.Exists(plan) || !StagePlan.TryParse(File.ReadAllText(plan), out parsed, out error)) { Lines.Add(t.Name + ": no plan"); continue; }
                string name = "Engineers " + t.Name;
                CraftWriter.Result w = CraftWriter.WriteTo(parsed, KsaCore.Agency.Parts, name, Path.Combine(folder, name + ".craft"));
                Lines.Add(name + (w.Ok ? ": saved" : ": NOT saved: " + w.Error));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllLines(ReportPath, Lines.ToArray());
            }
            catch (Exception e) { KsaCore.Log.Warn("Design report not written: " + e.Message); }
            foreach (string line in Lines) KsaCore.Log.Info("Design run: " + line);
        }
    }
}
