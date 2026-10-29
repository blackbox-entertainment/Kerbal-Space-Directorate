using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Data;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class AgencyDesignTests
    {
        private static Agency Staffed(FakeClock clock, ListLogger log)
        {
            Agency a = Make.Agency(clock, log);
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4),
                Make.Snapshot("Bob Kerman", CrewRole.Engineer, 1)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            return a;
        }

        private static void EnableAi(Agency a, ScriptedTransport transport)
        {
            a.Ai = new AiSettings { DesignMode = DesignMode.Api, ApiKey = "sk-test", DesignModel = "test/model", MaxCallsPerDay = 50, MonthlySpendCapUsd = 5 };
            a.Transport = transport;
            a.Parts = TestParts.Catalog();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
        }

        [Test]
        public void WithAiOffProjectsUseTemplatesOnly()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            string msg;
            Project p = a.CreateProject("Test", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design, Is.Null);
            Assert.That(a.AiDesignAvailable, Is.False);
        }

        [Test]
        public void ApprovedAiDesignFeedsTheSimulatorAndSaves()
        {
            var clock = new FakeClock();
            var log = new ListLogger();
            Agency a = Staffed(clock, log);
            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(ScriptedTransport.Chat(TestParts.GoodOrbitPlan()));
            EnableAi(a, transport);
            int started = 0, finished = 0, builds = 0;
            a.DesignStarted += p => started++;
            a.DesignFinished += (p, r) => finished++;
            a.BuildRequested += (p, r) => builds++;

            string msg;
            Project project = a.CreateProject("AI Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(project, Is.Not.Null, msg);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(finished, Is.EqualTo(1), "scripted transport answers synchronously");
            Assert.That(project.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(project.Design.DesignName, Is.EqualTo("Kerbal 1"));
            Assert.That(transport.Sent[0].Body, Does.Contain("Bill Kerman"), "the lead engineer is in the prompt");
            Assert.That(a.Costs.CallsOn(a.CurrentDay), Is.EqualTo(1));

            // Save/load keeps the record.
            DataNode saved = AgencyStateSerializer.Write(a);
            Agency b = Staffed(new FakeClock(), new ListLogger());
            AgencyStateSerializer.Read(saved, b);
            Assert.That(b.GetProject(project.Id).Design.TotalDeltaV, Is.EqualTo(project.Design.TotalDeltaV));
            Assert.That(b.Costs.CallsOn(a.CurrentDay), Is.EqualTo(1));

            // Design clock runs out → Build with the AI plan; host is asked to build.
            clock.AdvanceDays(project.DesignDays + 1);
            a.Tick();
            Assert.That(project.Status, Is.EqualTo(ProjectStatus.Build));
            Assert.That(builds, Is.EqualTo(1));
            Assert.That(project.Notes.Exists(n => n.Contains("Kerbal 1 (AI)")));

            // Fly it: the report carries the AI design score.
            clock.AdvanceDays(project.BuildDays + 60);
            a.Tick();
            Assert.That(project.Status, Is.EqualTo(ProjectStatus.Complete).Or.EqualTo(ProjectStatus.Failed));
            Assert.That(project.Report.DesignScore, Is.EqualTo(project.Design.DesignScore).Within(0.001));
        }

        [Test]
        public void FailedAiDesignFallsBackToTemplateAndHoldsDesignPhaseWhilePending()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(new LlmResponse { Ok = false, Status = 401, Body = "{\"error\":{\"message\":\"bad key\"}}" });
            EnableAi(a, transport);
            string msg;
            Project project = a.CreateProject("Bad Key", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(project.Design.Verdict, Is.EqualTo(DesignVerdict.Failed));
            Assert.That(project.Notes.Exists(n => n.Contains("Flying the template")));
            clock.AdvanceDays(project.DesignDays + project.BuildDays + 60);
            a.Tick();
            Assert.That(project.Report, Is.Not.Null);
            Assert.That(project.Report.DesignScore, Is.Not.EqualTo(0));
        }

        [Test]
        public void CostGuardStopsSessionsAndLeavesTemplate()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            EnableAi(a, transport);
            a.Ai.MaxCallsPerDay = 1;
            a.Costs.Record(a.CurrentDay, 0.01);
            string msg;
            Project project = a.CreateProject("Capped", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(project.Design, Is.Null);
            Assert.That(transport.Sent.Count, Is.EqualTo(0));
            Assert.That(project.Notes.Exists(n => n.Contains("cap")));
        }

        [Test]
        public void RedesignFromBuildRestartsDesign()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            string msg;
            Project project = a.CreateProject("Retry", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(project.Design.Verdict, Is.EqualTo(DesignVerdict.Short));
            clock.AdvanceDays(project.DesignDays + 1);
            a.Tick();
            Assert.That(project.Status, Is.EqualTo(ProjectStatus.Build));
            transport.Replies.Enqueue(ScriptedTransport.Chat(TestParts.GoodOrbitPlan()));
            Assert.That(a.RequestRedesign(project.Id, out msg), Is.True, msg);
            Assert.That(project.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(project.Status, Is.EqualTo(ProjectStatus.Design));
        }

        [Test]
        public void WithAiOnTheEngineersDesignAMissionThatHasNoBuildableTemplate()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.TemplateCheck = t => "needs parts not researched";
            KSA.Core.Missions.MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            Assert.That(a.MissionBlocker(orbit), Is.Not.Null, "AI off: no template, no mission");

            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(ScriptedTransport.Chat(TestParts.GoodOrbitPlan()));
            EnableAi(a, transport);
            Assert.That(a.MissionBlocker(orbit), Is.Null, "AI on: the engineers design it");
            string msg;
            Project p = a.CreateProject("From scratch", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.TemplateId, Is.Empty);
            Assert.That(p.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            clock.AdvanceDays(p.DesignDays + 1);
            a.Tick();
            Assert.That(p.Status, Is.Not.EqualTo(ProjectStatus.Design));
            Assert.That(a.DesignNameOf(p), Does.EndWith("(AI)"));
        }

        [Test]
        public void TheBriefDemandsInstrumentsAndAHeatShieldAndTheReviewChecksThem()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            var parts = (PartCatalog)a.Parts;
            parts.Add(new PartInfo { Name = "sensorThermometer", Title = "Thermometer", Kind = PartKind.Science, Mass = 0.005 });
            parts.Add(new PartInfo { Name = "HeatShield1", Title = "Heat Shield (1.25m)", Kind = PartKind.HeatShield, Mass = 0.1, Diameter = 1.25 });
            var shielded = new PartInfo { Name = "kv1Pod", Title = "KV-1 Onion", Kind = PartKind.Command, Mass = 1.4, CrewCapacity = 1 };
            shielded.Tags.Add("heatshielded");
            parts.Add(shielded);
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            DesignRequest req = a.BuildDesignRequest(p);
            Assert.That(req.MustInclude.Exists(r => r.Key.Contains("science") && r.Value.Contains("sensorThermometer")));
            Assert.That(req.MustInclude.Exists(r => r.Key.Contains("heat shield") && r.Value.Contains("HeatShield1") && r.Value.Contains("kv1Pod")));

            StagePlan plan;
            string error;
            Assert.That(StagePlan.TryParse(TestParts.GoodOrbitPlan(), out plan, out error), Is.True, error);
            Assert.That(DesignSession.MissingMustInclude(plan, req), Has.Count.EqualTo(1).And.Some.Contains("science"), "the plan has a shield but no instrument");
            plan.Payload.Extras.Remove("HeatShield1");
            Assert.That(DesignSession.MissingMustInclude(plan, req), Has.Count.EqualTo(2), "no instrument, no shield");
            plan.Payload.Extras.Add("sensorThermometer");
            plan.Payload.Command = "kv1Pod"; // a self-shielded pod satisfies the shield rule
            Assert.That(DesignSession.MissingMustInclude(plan, req), Is.Empty);

            // A suborbital hop comes in slowly: no shield demanded.
            Project hop = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, new string[0], null, MissionMode.Simulate, out msg);
            if (hop != null) Assert.That(a.BuildDesignRequest(hop).MustInclude.Exists(r => r.Key.Contains("heat shield")), Is.False);
        }

        [Test]
        public void WithKerbalismACrewedDesignMustCarryAnAntenna()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            ((PartCatalog)a.Parts).Add(new PartInfo { Name = "SurfAntenna", Title = "Communotron 16-S", Kind = PartKind.Antenna, Mass = 0.015 });
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(a.BuildDesignRequest(p).MustInclude.Exists(r => r.Key.Contains("antenna")), Is.False, "stock: the pod keeps its own results");
            a.LifeSupportMod = "Kerbalism";
            DesignRequest req = a.BuildDesignRequest(p);
            Assert.That(req.MustInclude.Exists(r => r.Key.Contains("antenna") && r.Value.Contains("SurfAntenna")));
            StagePlan plan; string error;
            Assert.That(StagePlan.TryParse(TestParts.GoodOrbitPlan(), out plan, out error), Is.True, error);
            Assert.That(DesignSession.MissingMustInclude(plan, req), Has.Some.Contains("antenna"));
            plan.Payload.Extras.Add("SurfAntenna");
            Assert.That(DesignSession.MissingMustInclude(plan, req), Has.None.Contains("antenna"));
        }

        [Test]
        public void ANoTemplateProjectRetriesItsDesignThenIsCalledOffWithItsMoneyBack()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.TemplateCheck = t => "needs parts not researched";
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            double before = a.Ledger.Balance;
            string msg;
            Project p = a.CreateProject("Too hard", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design.Verdict, Is.EqualTo(DesignVerdict.Short));
            for (int i = 0; i < 4; i++) { clock.AdvanceDays(p.DesignDays + 1); a.Tick(); }
            Assert.That(p.DesignAttempts, Is.EqualTo(Agency.MaxAiDesignAttempts));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Cancelled));
            Assert.That(a.Ledger.Balance, Is.EqualTo(before).Within(1));
            Assert.That(a.Crew.Get("Jeb Kerman").ProjectId, Is.Empty);
        }
    }
}
