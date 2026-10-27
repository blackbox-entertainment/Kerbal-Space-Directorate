using System.Collections.Generic;
using System.IO;
using KSA.Core.Ai;
using KSA.Core.Data;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class AiSettingsTests
    {
        private static DataNode ExampleSettings()
        {
            string path = Path.Combine(Path.GetDirectoryName(ShippedConfigs.ConfigDir), "PluginData", "ksa_settings.example.cfg");
            List<DataNode> nodes = DataNodeText.ParseAll(File.ReadAllText(path));
            return nodes.Find(n => n.Name == AiSettings.NodeName);
        }

        [Test]
        public void ExampleFileParsesWithAiOffAndNoKey()
        {
            AiSettings s = AiSettings.Parse(ExampleSettings());
            Assert.That(s.HasApiKey, Is.False, "the example file must not carry a real key");
            Assert.That(s.AiEnabled, Is.False);
            Assert.That(s.EndpointFor(AiTask.Design), Is.Null);
        }

        [Test]
        public void HybridRoutesDesignToApiAndNarrativeToLocal()
        {
            var s = new AiSettings { DesignMode = DesignMode.Hybrid, ApiKey = "sk-test", DesignModel = "m/design", PostMortemModel = "m/pm", LocalModel = "llama3.1:8b" };
            Assert.That(s.AiEnabled, Is.True);
            Assert.That(s.EndpointFor(AiTask.Design).IsLocal, Is.False);
            Assert.That(s.EndpointFor(AiTask.PostMortem).Model, Is.EqualTo("m/pm"));
            Assert.That(s.EndpointFor(AiTask.Narrative).IsLocal, Is.True);
        }

        [Test]
        public void HybridFallsBackToLocalWithoutKey()
        {
            var s = new AiSettings { DesignMode = DesignMode.Hybrid, LocalModel = "llama3.1:8b" };
            Assert.That(s.EndpointFor(AiTask.Design).IsLocal, Is.True);
        }

        [Test]
        public void RoundTripsThroughDataNode()
        {
            var s = new AiSettings { DesignMode = DesignMode.Api, ApiKey = "sk-abc", DesignModel = "x/y", MaxCallsPerDay = 7, MonthlySpendCapUsd = 2.5, LogPrompts = true };
            AiSettings back = AiSettings.Parse(s.ToDataNode());
            Assert.That(back.DesignMode, Is.EqualTo(DesignMode.Api));
            Assert.That(back.ApiKey, Is.EqualTo("sk-abc"));
            Assert.That(back.MaxCallsPerDay, Is.EqualTo(7));
            Assert.That(back.MonthlySpendCapUsd, Is.EqualTo(2.5));
            Assert.That(back.LogPrompts, Is.True);
        }

        [Test]
        public void ShippedModelListHasDefaultsPerTask()
        {
            List<ModelOption> models = ModelOption.Parse(ShippedConfigs.Load("ai_models.cfg"));
            Assert.That(models.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(models.Exists(m => m.Task == AiTask.Design && m.Default && m.Tier == "budget"));
            Assert.That(models.Exists(m => m.Task == AiTask.Design && m.Tier == "flagship"));
            Assert.That(models.Exists(m => m.Task == AiTask.Narrative && m.Default && m.Local));
        }

        [Test]
        public void CostGuardCapsCallsAndSpend()
        {
            var s = new AiSettings { MaxCallsPerDay = 2, MonthlySpendCapUsd = 1.0 };
            var g = new CostGuard();
            string why;
            Assert.That(g.Allows(s, false, 10, out why), Is.True);
            g.Record(10, 0.1); g.Record(10, 0.1);
            Assert.That(g.Allows(s, false, 10, out why), Is.False);
            Assert.That(g.Allows(s, false, 11, out why), Is.True);
            Assert.That(g.Allows(s, true, 10, out why), Is.True, "local calls are never capped");
            g.Record(12, 0.9);
            Assert.That(g.Allows(s, false, 13, out why), Is.False);
            Assert.That(why, Does.Contain("spend"));
            Assert.That(g.Allows(s, false, 13 + CostGuard.MonthDays, out why), Is.True, "spend window rolls off");

            var back = new CostGuard();
            back.Load(g.ToDataNode());
            Assert.That(back.CallsOn(10), Is.EqualTo(2));
        }
    }
}
