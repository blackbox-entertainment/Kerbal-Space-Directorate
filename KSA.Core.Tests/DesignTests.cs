using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>A transport that answers from a script, synchronously (the real one is async).</summary>
    public sealed class ScriptedTransport : ILlmTransport
    {
        public Queue<LlmResponse> Replies { get; } = new Queue<LlmResponse>();
        public List<LlmRequest> Sent { get; } = new List<LlmRequest>();
        /// <summary>Used when the queue is empty.</summary>
        public LlmResponse Fallback { get; set; }

        public void Send(LlmRequest request, Action<LlmResponse> callback)
        {
            Sent.Add(request);
            callback(Replies.Count > 0 ? Replies.Dequeue() : (Fallback ?? new LlmResponse { Ok = false, Status = 0, Error = "no scripted reply" }));
        }

        public static LlmResponse Chat(string content)
        {
            var msg = new Dictionary<string, object> { { "role", "assistant" }, { "content", content } };
            var choice = new Dictionary<string, object> { { "message", msg } };
            var body = new Dictionary<string, object> { { "choices", new List<object> { choice } } };
            return new LlmResponse { Ok = true, Status = 200, Body = Data.Json.Write(body) };
        }
    }

    public static class TestParts
    {
        /// <summary>A tiny stock-like catalog with realistic numbers.</summary>
        public static PartCatalog Catalog()
        {
            var c = new PartCatalog();
            c.Add(new PartInfo { Name = "mk1pod", Title = "Mk1 Command Pod", Kind = PartKind.Command, Mass = 0.84, CrewCapacity = 1, Cost = 600, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "parachuteSingle", Title = "Mk16 Parachute", Kind = PartKind.Parachute, Mass = 0.1, Cost = 422, HasBottomNode = true });
            c.Add(new PartInfo { Name = "parachuteRadial", Title = "Mk2-R Radial-Mount Parachute", Kind = PartKind.Parachute, Mass = 0.1, Cost = 400 });
            c.Add(new PartInfo { Name = "HeatShield1", Kind = PartKind.HeatShield, Mass = 0.3, Cost = 300, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "stackDecoupler", Title = "TD-12 Decoupler", Kind = PartKind.Decoupler, Mass = 0.04, Cost = 400, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "fuelTankSmall", Title = "FL-T200", Kind = PartKind.Tank, Mass = 0.125, FuelMass = 1.0, Cost = 275, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "fuelTankLong", Title = "FL-T800", Kind = PartKind.Tank, Mass = 0.5, FuelMass = 4.0, Cost = 800, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "liquidEngine3", Title = "LV-909 Terrier", Kind = PartKind.Engine, Mass = 0.5, Thrust = 60, IspVacuum = 345, IspSeaLevel = 85, Cost = 390, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "liquidEngine2", Title = "LV-T45 Swivel", Kind = PartKind.Engine, Mass = 1.5, Thrust = 215, IspVacuum = 320, IspSeaLevel = 250, Cost = 1200, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "solidBooster", Title = "RT-10 Hammer", Kind = PartKind.Booster, Mass = 0.75, FuelMass = 2.8, Thrust = 227, IspVacuum = 195, IspSeaLevel = 170, Cost = 400, Diameter = 1.25, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "radialDecoupler", Title = "TT-38K", Kind = PartKind.Decoupler, Mass = 0.025, Cost = 600 });
            c.Add(new PartInfo { Name = "dlcEngine", Kind = PartKind.Engine, Mass = 1, Thrust = 100, IspVacuum = 300, Cost = 1, RequiresDlc = true, HasTopNode = true, HasBottomNode = true });
            c.Add(new PartInfo { Name = "stationHab", Kind = PartKind.Habitat, Mass = 2, Cost = 2000, CrewCapacity = 4, HasTopNode = true, HasBottomNode = true });
            return c;
        }

        public static string GoodOrbitPlan()
        {
            return "{\"design_name\":\"Kerbal 1\",\"rationale\":\"simple two stage\",\"stages\":[" +
                   "{\"stage\":0,\"role\":\"launch\",\"core\":[{\"part\":\"liquidEngine2\",\"count\":1},{\"part\":\"fuelTankLong\",\"count\":2}],\"radial\":[{\"part\":\"solidBooster\",\"count\":2,\"symmetry\":2}],\"decoupler\":\"stackDecoupler\"}," +
                   "{\"stage\":1,\"role\":\"upper\",\"core\":[{\"part\":\"liquidEngine3\",\"count\":1},{\"part\":\"fuelTankLong\",\"count\":1}],\"radial\":[],\"decoupler\":\"stackDecoupler\"}]," +
                   "\"payload\":{\"command\":\"mk1pod\",\"extras\":[\"parachuteSingle\",\"HeatShield1\"]}}";
        }

        public static string WeakPlan()
        {
            return "{\"design_name\":\"Too small\",\"stages\":[{\"stage\":0,\"role\":\"launch\",\"core\":[{\"part\":\"liquidEngine3\",\"count\":1},{\"part\":\"fuelTankSmall\",\"count\":1}],\"radial\":[]}]," +
                   "\"payload\":{\"command\":\"mk1pod\",\"extras\":[\"parachuteSingle\"]}}";
        }
    }

    public class DesignTests
    {
        private static MissionType Orbit() { return ShippedConfigs.Missions().Get("KerbinOrbit"); }

        [Test]
        public void StagePlanParsesAndRoundTrips()
        {
            StagePlan plan; string err;
            Assert.That(StagePlan.TryParse("Here you go:\n" + TestParts.GoodOrbitPlan(), out plan, out err), Is.True, err);
            Assert.That(plan.Stages.Count, Is.EqualTo(2));
            Assert.That(plan.Stages[0].Radial[0].Symmetry, Is.EqualTo(2));
            Assert.That(plan.Payload.Extras.Count, Is.EqualTo(2));
            StagePlan back; string err2;
            Assert.That(StagePlan.TryParse(plan.ToJsonText(), out back, out err2), Is.True);
            Assert.That(back.AllPartNames(), Is.EqualTo(plan.AllPartNames()));
        }

        [Test]
        public void ValidatorRejectsUnknownPartsAndMissingPieces()
        {
            PartCatalog catalog = TestParts.Catalog();
            HashSet<string> allowed = PartFilter.Names(PartFilter.ForJob(catalog, PartJob.LaunchVehicle, false));
            StagePlan plan; string err;
            StagePlan.TryParse(TestParts.GoodOrbitPlan(), out plan, out err);
            Assert.That(StagePlanValidator.Validate(plan, catalog, allowed), Is.Empty);

            plan.Stages[0].Core.Add(new PlanPart { Part = "warpDrive" });
            plan.Stages[0].Core.Add(new PlanPart { Part = "dlcEngine" });
            plan.Stages[0].Decoupler = string.Empty;
            plan.Stages[1].Radial.Add(new PlanPart { Part = "solidBooster", Count = 5, Symmetry = 5 });
            List<string> problems = StagePlanValidator.Validate(plan, catalog, allowed);
            Assert.That(problems.Exists(p => p.Contains("warpDrive")));
            Assert.That(problems.Exists(p => p.Contains("dlcEngine")), "DLC part excluded from the allowed list");
            Assert.That(problems.Exists(p => p.Contains("decoupler")));
            Assert.That(problems.Exists(p => p.Contains("symmetry 5")));
        }

        [Test]
        public void PartFilterCapsPerKindAndRespectsJob()
        {
            PartCatalog catalog = TestParts.Catalog();
            List<PartInfo> launch = PartFilter.ForJob(catalog, PartJob.LaunchVehicle, false);
            Assert.That(launch.Exists(p => p.Name == "stationHab"), Is.False);
            Assert.That(launch.Exists(p => p.Name == "dlcEngine"), Is.False);
            Assert.That(PartFilter.ForJob(catalog, PartJob.LaunchVehicle, true).Exists(p => p.Name == "dlcEngine"), Is.True);
            Assert.That(PartFilter.ForJob(catalog, PartJob.StationModule, false).Exists(p => p.Name == "stationHab"), Is.True);
            Assert.That(PartFilter.ForJob(catalog, PartJob.LaunchVehicle, false, 1).FindAll(p => p.Kind == PartKind.Tank).Count, Is.EqualTo(1));
        }

        [Test]
        public void AnalyzerComputesDeltaVAndTwr()
        {
            PartCatalog catalog = TestParts.Catalog();
            StagePlan plan; string err;
            StagePlan.TryParse(TestParts.GoodOrbitPlan(), out plan, out err);
            var req = new MissionRequirements().For(Orbit());
            DesignAnalysis a = StageAnalyzer.Analyze(plan, catalog, req);
            Assert.That(a.Stages.Count, Is.EqualTo(2));
            Assert.That(a.TotalDeltaV, Is.GreaterThan(3600), "two stages with boosters should reach orbit: " + a.TotalDeltaV);
            Assert.That(a.LiftoffTwr, Is.GreaterThan(1.2));
            Assert.That(a.Passes, Is.True, StageAnalyzer.Feedback(a));
            Assert.That(a.DesignScore, Is.GreaterThan(0.6));

            StagePlan weak; StagePlan.TryParse(TestParts.WeakPlan(), out weak, out err);
            DesignAnalysis w = StageAnalyzer.Analyze(weak, catalog, req);
            Assert.That(w.MeetsDeltaV, Is.False);
            Assert.That(StageAnalyzer.Feedback(w), Does.Contain("m/s short"));
            Assert.That(w.DesignScore, Is.LessThan(0.4));
        }

        [Test]
        public void ShippedRequirementsLoadAndCoverEveryMissionType()
        {
            MissionRequirements req = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            foreach (MissionType t in ShippedConfigs.Missions().Types)
            {
                MissionRequirements.Entry e = req.For(t);
                if (t.IsAircraft) { Assert.That(e.DeltaV, Is.EqualTo(0), t.Id); continue; }
                Assert.That(e.DeltaV, Is.GreaterThan(t.Id == "FleaHop" ? 300 : 1000), t.Id);
            }
            Assert.That(req.For(ShippedConfigs.Missions().Get("MunLanding")).DeltaV, Is.EqualTo(6040)); // 3400 up, 860 + 310 out, 580 down, 580 up, 310 home
        }

        private static DesignRequest Request(PartCatalog catalog)
        {
            var lead = new CrewProfile("eng1", "Bill Kerman");
            lead.PersonalityTags.Add("Cautious");
            var req = new DesignRequest { Mission = Orbit(), Requirement = new MissionRequirements().For(Orbit()), LeadEngineerId = "eng1", Budget = 20000, CrewSize = 1 };
            req.Engineers.Add(lead);
            req.RankNames["eng1"] = "Senior Engineer";
            req.AllowedParts.AddRange(PartFilter.ForJob(catalog, PartJob.LaunchVehicle, false));
            req.Lessons.Add("Last flight ran out of fuel before circularising.");
            return req;
        }

        [Test]
        public void PromptListsPartsTeamAndLessons()
        {
            DesignRequest req = Request(TestParts.Catalog());
            string system = PromptBuilder.SystemPrompt(req);
            string user = PromptBuilder.UserPrompt(req, null, null);
            Assert.That(system, Does.Contain("Bill Kerman"));
            Assert.That(system, Does.Contain("Cautious"));
            Assert.That(user, Does.Contain("liquidEngine2"));
            Assert.That(user, Does.Contain("LESSONS LEARNED"));
            Assert.That(user, Does.Contain("climb to Kerbin orbit"));
            Assert.That(user, Does.Not.Contain("stationHab"));
            Assert.That(PromptBuilder.RevisionPasses(req.Engineers[0], 2), Is.EqualTo(4));
            req.Engineers[0].PersonalityTags.Add("Perfectionist");
            Assert.That(PromptBuilder.RevisionPasses(req.Engineers[0], 4), Is.EqualTo(5));
        }

        [Test]
        public void SessionRevisesUntilTheDesignPasses()
        {
            PartCatalog catalog = TestParts.Catalog();
            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(ScriptedTransport.Chat("```json\n" + TestParts.WeakPlan() + "\n```"));
            transport.Replies.Enqueue(ScriptedTransport.Chat(TestParts.GoodOrbitPlan()));
            var log = new ListLogger();
            var endpoint = new LlmEndpoint { Url = "http://test", Model = "test-model", ApiKey = "k" };
            var session = new DesignSession(new LlmClient(transport, log), endpoint, catalog, log);
            DesignRecord result = null;
            session.Run(Request(catalog), 3, r => result = r);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(result.PassesUsed, Is.EqualTo(2));
            Assert.That(result.DesignName, Is.EqualTo("Kerbal 1"));
            Assert.That(result.DesignScore, Is.GreaterThan(0.6));
            Assert.That(transport.Sent[1].Body, Does.Contain("m/s short"), "second pass carries the review");
            Assert.That(transport.Sent[0].Headers["Authorization"], Is.EqualTo("Bearer k"));
            Assert.That(string.Join("\n", log.Lines.ToArray()), Does.Not.Contain("Bearer k"), "keys never reach the log");

            DesignRecord back = DesignRecord.FromDataNode(result.ToDataNode());
            Assert.That(back.Plan, Is.Not.Null);
            Assert.That(back.TotalDeltaV, Is.EqualTo(result.TotalDeltaV));
        }

        [Test]
        public void SessionReportsShortWhenPassesRunOut()
        {
            PartCatalog catalog = TestParts.Catalog();
            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(ScriptedTransport.Chat(TestParts.WeakPlan()));
            transport.Replies.Enqueue(ScriptedTransport.Chat("I cannot help with that."));
            var log = new ListLogger();
            var session = new DesignSession(new LlmClient(transport, log), new LlmEndpoint { Url = "http://t", Model = "m" }, catalog, log);
            DesignRecord result = null;
            session.Run(Request(catalog), 2, r => result = r);
            Assert.That(result.Verdict, Is.EqualTo(DesignVerdict.Short));
            Assert.That(result.PassesUsed, Is.EqualTo(2));
            Assert.That(result.Feedback[1], Does.Contain("unreadable"));
        }

        [Test]
        public void ClientRetriesTransientErrorsThenFails()
        {
            var transport = new ScriptedTransport();
            transport.Replies.Enqueue(new LlmResponse { Ok = false, Status = 429, Body = "{\"error\":{\"message\":\"rate limited\"}}" });
            transport.Replies.Enqueue(new LlmResponse { Ok = false, Status = 500 });
            transport.Replies.Enqueue(new LlmResponse { Ok = false, Status = 401, Body = "{\"error\":{\"message\":\"bad key\"}}" });
            var client = new LlmClient(transport, new ListLogger());
            LlmResult result = null;
            client.Chat(new LlmEndpoint { Url = "http://t", Model = "m" }, "sys", "user", 0.2, true, r => result = r);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Attempts, Is.EqualTo(3));
            Assert.That(result.Error, Does.Contain("bad key"));
            Assert.That(transport.Sent[1].DelaySeconds, Is.GreaterThan(0));
            Assert.That(transport.Sent[0].Body, Does.Contain("json_object"));
        }

        [Test]
        public void RepairDropsUnresearchedDecouplerAndExtras()
        {
            var plan = new StagePlan { DesignName = "Flea" };
            plan.Payload.Command = "mk1pod_v2";
            plan.Payload.Extras.Add("parachuteSingle");
            plan.Payload.Extras.Add("ladder");
            var s = new PlanStage { Stage = 0, Role = "launch", Decoupler = "mk2Decoupler" };
            s.Core.Add(new PlanPart { Part = "solidBooster_sm_v2", Count = 1 });
            plan.Stages.Add(s);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mk1pod_v2", "parachuteSingle", "solidBooster_sm_v2" };
            List<string> notes = StagePlanValidator.Repair(plan, allowed);
            Assert.That(notes.Count, Is.EqualTo(2));
            Assert.That(plan.Stages[0].Decoupler, Is.Empty);
            Assert.That(plan.Payload.Extras, Is.EqualTo(new[] { "parachuteSingle" }));
            Assert.That(StagePlanValidator.Validate(plan, null, allowed), Is.Empty);
        }
    }
}
