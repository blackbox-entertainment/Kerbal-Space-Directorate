using System;
using System.IO;
using KSA.Core;
using KSA.Core.Design;
using KSA.Core.Missions;

namespace KSA.KSP.Build
{
    /// <summary>
    /// KSA-67: templates that ship as a stage plan (planFile) instead of a finished craft. They are assembled here with
    /// the parts that are actually loaded, so a part config changed by another mod (Kerbalism rewrites drills,
    /// converters and pods) can never leave a hand-made craft file out of date. The craft goes to the template's
    /// craft path (KSA/PluginData/Templates); it is rebuilt when missing or older than its plan or KSA.KSP.dll.
    /// A template whose parts are not loaded is simply not built, and the research check reports it.
    /// </summary>
    public static class PlanTemplates
    {
        public static void BuildAll(Agency agency)
        {
            if (agency == null || agency.Parts == null) return;
            DateTime writerTime;
            try { writerTime = File.GetLastWriteTimeUtc(typeof(PlanTemplates).Assembly.Location); }
            catch { writerTime = DateTime.MinValue; }
            int built = 0;
            foreach (Template t in agency.Catalogs.Templates.Templates)
            {
                if (string.IsNullOrEmpty(t.PlanFile) || string.IsNullOrEmpty(t.Craft)) continue;
                if (!t.IsAvailable(agency.InstalledMods)) continue;
                try { if (Build(agency, t, writerTime)) built++; }
                catch (Exception e) { KsaCore.Log.Warn("Template " + t.Id + " could not be built from its plan: " + e.Message); }
            }
            if (built > 0) CraftWriter.PublishTemplates();
        }

        private static bool Build(Agency agency, Template t, DateTime writerTime)
        {
            string root = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            string planPath = Path.Combine(root, t.PlanFile.Replace('/', Path.DirectorySeparatorChar));
            string craftPath = Path.Combine(root, t.Craft.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(planPath)) { KsaCore.Log.Warn("Template " + t.Id + ": plan file " + t.PlanFile + " is missing."); return false; }
            if (File.Exists(craftPath))
            {
                DateTime craftTime = File.GetLastWriteTimeUtc(craftPath);
                if (craftTime >= File.GetLastWriteTimeUtc(planPath) && craftTime >= writerTime) return false; // up to date
            }
            StagePlan plan;
            string error;
            if (!StagePlan.TryParse(File.ReadAllText(planPath), out plan, out error)) { KsaCore.Log.Warn("Template " + t.Id + ": " + error); return false; }
            CraftWriter.Result r = CraftWriter.WriteTo(plan, agency.Parts, "KSA " + t.Name, craftPath);
            if (!r.Ok) { KsaCore.Log.Warn("Template " + t.Id + " was not assembled: " + r.Error); return false; }
            foreach (string w in r.Warnings) KsaCore.Log.Warn("Template " + t.Id + ": " + w);
            KsaCore.Log.Info("Template " + t.Id + " assembled from its plan: " + r.Parts + " parts, " + t.Craft);
            Parts.KspTemplateCheck.Forget(t.Id);
            return true;
        }
    }
}
