using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Data;
using KSA.Core.Design;
using KSA.Core.Missions;
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
    }
}
