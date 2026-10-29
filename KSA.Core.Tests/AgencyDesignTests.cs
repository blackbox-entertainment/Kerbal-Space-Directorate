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

            // A battery, once one is researched.
            Assert.That(req.MustInclude.Exists(r => r.Key.Contains("battery")), Is.EqualTo(new List<PartInfo>(((PartCatalog)a.Parts).All).Exists(x => x.Kind == PartKind.Power && x.Mass <= 0.3)));
            ((PartCatalog)a.Parts).Add(new PartInfo { Name = "batteryPack", Title = "Z-100", Kind = PartKind.Power, Mass = 0.005 });
            req = a.BuildDesignRequest(p);
            Assert.That(req.MustInclude.Exists(r => r.Key.Contains("battery") && r.Value.Contains("batteryPack")));
        }

        [Test]
        public void ProjectsReadyTogetherLaunchThreeDaysApart()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Val Kerman", CrewRole.Pilot, 3), Make.Snapshot("Ludo Kerman", CrewRole.Pilot, 3) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            string msg;
            Project p1 = a.CreateProject("Hop 1", "FleaHop", null, new[] { "Jeb Kerman" }, new string[0], null, MissionMode.Simulate, out msg);
            Project p2 = a.CreateProject("Hop 2", "FleaHop", null, new[] { "Val Kerman" }, new string[0], null, MissionMode.Simulate, out msg);
            Project p3 = a.CreateProject("Hop 3", "FleaHop", null, new[] { "Ludo Kerman" }, new string[0], null, MissionMode.Simulate, out msg);
            Assert.That(p3, Is.Not.Null, msg);
            Assert.That(a.Settings.LaunchSpacingDays, Is.EqualTo(3));
            // Long catch-up: all three were ready on the same day; they take the pad in turn.
            clock.AdvanceDays(p1.DesignDays + p1.BuildDays + 30);
            a.Tick();
            Assert.That(p2.LaunchDay - p1.LaunchDay, Is.EqualTo(3));
            Assert.That(p3.LaunchDay - p2.LaunchDay, Is.EqualTo(3));
        }

        [Test]
        public void ACrewedDesignMustCarryAParachute()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            DesignRequest req = a.BuildDesignRequest(p);
            Assert.That(req.MustInclude.Exists(r => r.Key.Contains("parachute") && r.Value.Contains("parachuteSingle")));
            StagePlan plan; string error;
            Assert.That(StagePlan.TryParse(TestParts.GoodOrbitPlan(), out plan, out error), Is.True, error);
            Assert.That(DesignSession.MissingMustInclude(plan, req), Has.None.Contains("parachute"));
            plan.Payload.Extras.Remove("parachuteSingle");
            Assert.That(DesignSession.MissingMustInclude(plan, req), Has.Some.Contains("parachute"), "no chute, no approval (a pilot died, 2026-10-28)");
        }

        [Test]
        public void AProvenDesignIsReusedUntilNewPartsAreResearched()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(ScriptedTransport.Chat(TestParts.GoodOrbitPlan()));
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            string msg;
            Project first = a.CreateProject("Orbit 1", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(first.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(a.CancelProject(first.Id, out msg), Is.True, msg); // frees the crew; pretend it flew well
            first.Report = new MissionReport { MissionTypeId = "KerbinOrbit", Outcome = MissionOutcome.Success };
            int calls = transport.Sent.Count;

            Project second = a.CreateProject("Orbit 2", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(second, Is.Not.Null, msg);
            Assert.That(second.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(second.Design.DesignName, Is.EqualTo(first.Design.DesignName));
            Assert.That(second.Notes, Has.Some.StartsWith("Reusing the proven design"));
            Assert.That(transport.Sent.Count, Is.EqualTo(calls), "no new AI call for a proven design");

            // New parts researched: the team takes a fresh look.
            Assert.That(a.CancelProject(second.Id, out msg), Is.True, msg);
            ((PartCatalog)a.Parts).Add(new PartInfo { Name = "newTank", Title = "New tank", Kind = PartKind.Tank, Mass = 0.1 });
            Project third = a.CreateProject("Orbit 3", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(third.Notes, Has.None.StartsWith("Reusing the proven design"));
        }

        private sealed class HangingTransport : KSA.Core.Ai.ILlmTransport
        {
            public System.Action<KSA.Core.Ai.LlmResponse> Pending;
            public void Send(KSA.Core.Ai.LlmRequest request, System.Action<KSA.Core.Ai.LlmResponse> callback) { Pending = callback; }
        }

        [Test]
        public void ASlowAiDesignIsAbandonedForTheFallbackAndItsLateAnswerIgnored()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new HangingTransport();
            a.Ai = new AiSettings { DesignMode = DesignMode.Api, ApiKey = "sk-test", DesignModel = "test/model", MaxCallsPerDay = 50, MonthlySpendCapUsd = 5 };
            a.Transport = transport;
            a.Parts = TestParts.Catalog();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            clock.AdvanceDays(p.DesignDays + 1);
            a.Tick();
            Assert.That(a.WaitingForAiDesign(p), Is.True, "design days over, the engineers still thinking");
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Design));

            Assert.That(a.AbandonSlowDesign(p.Id, "no answer after 120 s"), Is.True);
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Build), "the fallback design is built");
            Assert.That(a.DesignNameOf(p), Does.Not.EndWith("(AI)"));

            // The late answer changes nothing.
            transport.Pending(ScriptedTransport.Chat(TestParts.GoodOrbitPlan()));
            Assert.That(p.Design.Verdict, Is.EqualTo(DesignVerdict.Failed));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Build));
        }

        [Test]
        public void BoostersStackedInOneColumnAreRejected()
        {
            PartCatalog c = TestParts.Catalog();
            string json = "{\"design_name\":\"Tower\",\"rationale\":\"\",\"stages\":[" +
                          "{\"stage\":0,\"role\":\"launch\",\"core\":[{\"part\":\"solidBooster\",\"count\":4}],\"radial\":[],\"decoupler\":\"stackDecoupler\"}," +
                          "{\"stage\":1,\"role\":\"upper\",\"core\":[{\"part\":\"fuelTankLong\",\"count\":1},{\"part\":\"liquidEngine3\",\"count\":1}],\"radial\":[],\"decoupler\":\"\"}]," +
                          "\"payload\":{\"command\":\"mk1pod\",\"extras\":[\"parachuteSingle\"]}}";
            StagePlan plan; string error;
            Assert.That(StagePlan.TryParse(json, out plan, out error), Is.True, error);
            var names = new HashSet<string>();
            foreach (PartInfo p in c.All) names.Add(p.Name);
            Assert.That(StagePlanValidator.Validate(plan, c, names), Has.Some.Contains("only ONE can sit in a stack"));
        }

        [Test]
        public void SideTanksAndUndroppableSideBoostersAreRejected()
        {
            PartCatalog c = TestParts.Catalog();
            string json = "{\"design_name\":\"Odd\",\"rationale\":\"\",\"stages\":[" +
                          "{\"stage\":0,\"role\":\"launch\",\"core\":[{\"part\":\"solidBooster\",\"count\":1}],\"radial\":[{\"part\":\"solidBooster\",\"count\":4,\"symmetry\":4},{\"part\":\"fuelTankLong\",\"count\":2,\"symmetry\":2}],\"decoupler\":\"\"}]," +
                          "\"payload\":{\"command\":\"mk1pod\",\"extras\":[\"parachuteSingle\"]}}";
            StagePlan plan; string error;
            Assert.That(StagePlan.TryParse(json, out plan, out error), Is.True, error);
            var names = new HashSet<string>();
            foreach (PartInfo p in c.All) if (!(p.Kind == PartKind.Decoupler && !p.IsStackable)) names.Add(p.Name); // no radial decoupler researched
            List<string> problems = StagePlanValidator.Validate(plan, c, names);
            Assert.That(problems, Has.Some.Contains("side tanks cannot feed"));
            Assert.That(problems, Has.Some.Contains("needs a radial decoupler"));
        }

        [Test]
        public void AMissionOutsideTheCatalogCanBeCheckedForADesign()
        {
            // A rescue mission is built on the fly and is not in the catalog (2026-10-28: this crashed every tick).
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.Parts = TestParts.Catalog();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            var rescue = new MissionType { Id = "rescue-test", Name = "Rescue at Minmus", Body = "Minmus", Profile = MissionProfile.Orbit, CrewSize = 1 };
            Assert.DoesNotThrow(() => a.DesignableBlocker(rescue));
        }

        [Test]
        public void RadialDecouplerBetweenStagesIsRejected()
        {
            PartCatalog c = TestParts.Catalog();
            string json = "{\"design_name\":\"Loose\",\"rationale\":\"\",\"stages\":[" +
                          "{\"stage\":0,\"role\":\"launch\",\"core\":[{\"part\":\"solidBooster\",\"count\":1}],\"radial\":[],\"decoupler\":\"radialDecoupler\"}," +
                          "{\"stage\":1,\"role\":\"upper\",\"core\":[{\"part\":\"fuelTankLong\",\"count\":1},{\"part\":\"liquidEngine3\",\"count\":1}],\"radial\":[],\"decoupler\":\"\"}]," +
                          "\"payload\":{\"command\":\"mk1pod\",\"extras\":[\"parachuteSingle\"]}}";
            StagePlan plan; string error;
            Assert.That(StagePlan.TryParse(json, out plan, out error), Is.True, error);
            var names = new HashSet<string>();
            foreach (PartInfo p in c.All) names.Add(p.Name);
            Assert.That(StagePlanValidator.Validate(plan, c, names), Has.Some.Contains("cannot sit between stages"));
        }

        [Test]
        public void WhenTheAiFallsShortTheEngineersDesignItByHand()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.TemplateCheck = t => "needs parts not researched";
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat(TestParts.WeakPlan());
            EnableAi(a, transport);
            KSA.Core.Missions.MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            Assert.That(a.MissionBlocker(orbit), Is.Null, "the researched parts can build an orbital rocket");
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design.Verdict, Is.EqualTo(DesignVerdict.Short), "the AI comes up short");
            for (int i = 0; i < 4; i++) { clock.AdvanceDays(p.DesignDays + 1); a.Tick(); }
            Assert.That(p.DesignAttempts, Is.EqualTo(Agency.MaxAiDesignAttempts));
            Assert.That(p.Design.Model, Is.EqualTo(Agency.EngineersModel), "drawn up by hand, not called off");
            Assert.That(p.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(p.Design.TotalDeltaV, Is.GreaterThanOrEqualTo(p.Design.RequiredDeltaV));
            Assert.That(p.Status, Is.Not.EqualTo(ProjectStatus.Cancelled));
            Assert.That(a.DesignNameOf(p), Does.EndWith("(engineering team)"));
        }

        [Test]
        public void ARocketThatCannotBeAssembledIsNeverSimulated()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.TemplateCheck = t => "needs parts not researched";
            a.Parts = TestParts.Catalog();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design.Model, Is.EqualTo(Agency.EngineersModel));
            double before = a.Ledger.Balance;
            // Even the engineers' hand design would not go together in the VAB: called off, money back, no flight.
            a.DesignWontAssemble(p.Id, "no top node");
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Cancelled));
            Assert.That(a.Ledger.Balance, Is.GreaterThan(before));
            Assert.That(p.MissionId, Is.Empty.Or.Null, "nothing flew");
        }

        [Test]
        public void WithAiOffTheEngineersStillDesignTheRocket()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.TemplateCheck = t => "needs parts not researched";
            a.Parts = TestParts.Catalog();
            a.Requirements = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            Assert.That(a.AiDesignAvailable, Is.False);
            string msg;
            Project p = a.CreateProject("Orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design, Is.Not.Null);
            Assert.That(p.Design.Model, Is.EqualTo(Agency.EngineersModel));
            Assert.That(p.Design.Plan.Payload.Extras, Has.Member("parachuteSingle"), "the brief's must-carry parts are on it");
        }

        [Test]
        public void AMissionNoRocketCanFlyStaysShut()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.TemplateCheck = t => "needs parts not researched";
            var weak = new PartCatalog();
            weak.Add(new PartInfo { Name = "mk1pod", Title = "Mk1 pod", Kind = PartKind.Command, Mass = 0.84, CrewCapacity = 1, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            weak.Add(new PartInfo { Name = "parachuteSingle", Title = "Mk16", Kind = PartKind.Parachute, Mass = 0.1, HasBottomNode = true });
            weak.Add(new PartInfo { Name = "flea", Title = "Flea", Kind = PartKind.Booster, Mass = 0.45, FuelMass = 1.05, Thrust = 192, IspVacuum = 165, IspSeaLevel = 140, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            a.Parts = weak;
            KSA.Core.Missions.MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            Assert.That(a.MissionBlocker(orbit), Does.Contain("no rocket"));
            string msg;
            Assert.That(a.CreateProject("Too soon", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg), Is.Null);
        }
    }
}
