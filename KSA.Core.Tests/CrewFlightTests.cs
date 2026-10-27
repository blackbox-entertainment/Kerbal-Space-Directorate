using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The kerbals fly (decision 21): crew numbers shape live flights, and the crew makes judgement calls.</summary>
    public class CrewFlightTests
    {
        private static CrewProfile Pilot(string name, int stars, float courage, float stupidity, int morale, CrewRole role = CrewRole.Pilot)
        {
            return new CrewProfile(name, name) { Role = role, Stars = stars, Courage = courage, Stupidity = stupidity, Morale = morale };
        }

        [Test]
        public void ShippedRulesLoadAndBetterPilotsFlyBetter()
        {
            FlightCrewRules rules = ShippedConfigs.Settings().FlightCrew;
            Assert.That(rules.Enabled, Is.True);
            CrewFlightProfile ace = CrewFlightProfile.Build(new List<CrewProfile> { Pilot("Val", 5, 0.9f, 0.1f, 90) }, rules);
            CrewFlightProfile rookie = CrewFlightProfile.Build(new List<CrewProfile> { Pilot("Bob", 0, 0.2f, 0.9f, 30) }, rules);
            Assert.That(ace.Precision, Is.GreaterThan(rookie.Precision));
            Assert.That(ace.MistakeChance, Is.LessThan(rookie.MistakeChance));
            Assert.That(ace.BurnTolerance, Is.LessThan(rookie.BurnTolerance));
            Assert.That(ace.ApproachFactor, Is.GreaterThan(rookie.ApproachFactor));
            Assert.That(ace.MarginFactor, Is.LessThan(rookie.MarginFactor), "brave pilots cut the margin finer");
            Assert.That(rookie.MistakeChance, Is.LessThanOrEqualTo(rules.MistakeMax));
            Assert.That(ace.Describe(), Does.Contain("Val"));
        }

        [Test]
        public void PilotIsPreferredOverHigherStarredEngineer()
        {
            var crew = new List<CrewProfile> { Pilot("Bill", 5, 0.5f, 0.5f, 70, CrewRole.Engineer), Pilot("Jeb", 2, 0.5f, 0.5f, 70) };
            CrewFlightProfile p = CrewFlightProfile.Build(crew, new FlightCrewRules());
            Assert.That(p.PilotName, Is.EqualTo("Jeb"));
            CrewFlightProfile none = CrewFlightProfile.Build(new List<CrewProfile>(), new FlightCrewRules());
            Assert.That(none.Enabled, Is.False);
            Assert.That(none.MistakeChance, Is.EqualTo(0));
        }

        [Test]
        public void RuleDecisionsFollowNerve()
        {
            var req = new DecisionRequest { Kind = DecisionKind.LandingSite, Situation = "Survey: 28% slope, limit 30%.", Risk = 0.8 };
            CrewProfile brave = Pilot("Jeb", 3, 1.0f, 0.0f, 80);
            CrewProfile timid = Pilot("Bob", 3, 0.0f, 0.0f, 80);
            CrewDecision go = CrewDecider.Rule(CrewFlightProfile.Build(new List<CrewProfile> { brave }, new FlightCrewRules()), brave, req, new Random(1));
            CrewDecision no = CrewDecider.Rule(CrewFlightProfile.Build(new List<CrewProfile> { timid }, new FlightCrewRules()), timid, req, new Random(1));
            Assert.That(go.Choice, Is.EqualTo(DecisionChoice.Proceed));
            Assert.That(no.Choice, Is.EqualTo(DecisionChoice.Wait));
            Assert.That(go.Line, Does.StartWith("Jeb"));
        }

        [Test]
        public void ModelReplyIsParsedAndFallbackCoversSilence()
        {
            CrewDecision d = CrewDecider.ParseReply("Sure thing: ```json\n{\"decision\": \"abort\", \"say\": \"Nope. Not today.\"}\n```");
            Assert.That(d.Choice, Is.EqualTo(DecisionChoice.Abort));
            Assert.That(d.Line, Is.EqualTo("Nope. Not today."));
            Assert.That(CrewDecider.ParseReply("I think we should go"), Is.Null);

            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.LiveFlightAvailable = true;
            var transport = new ScriptedTransport();
            transport.Fallback = new LlmResponse { Ok = false, Status = 500, Body = "{}" };
            a.Ai = new AiSettings { DesignMode = DesignMode.Local, LocalEnabled = true, LocalModel = "llama" };
            a.Transport = transport;
            string msg;
            Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, MissionMode.WatchLive, out msg);
            Assert.That(p, Is.Not.Null, msg);
            for (int i = 0; i < 40 && p.Status != ProjectStatus.Ready; i++) { clock.AdvanceDays(5); a.Tick(); }
            LaunchSpec spec = null;
            a.LiveLaunchRequested += s => spec = s;
            Assert.That(a.LaunchLive(p.Id, out msg), Is.True, msg);
            Assert.That(spec.FlightCrew.PilotName, Is.EqualTo("Jeb Kerman"));

            // A model that answers in character.
            transport.Replies.Enqueue(ScriptedTransport.Chat("{\"decision\": \"proceed\", \"say\": \"Looks flat enough from up here.\"}"));
            CrewDecision got = null;
            a.AskCrew(p.Id, new DecisionRequest { Kind = DecisionKind.LandingSite, Situation = "Survey: 12% slope.", Risk = 0.3 }, x => got = x);
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Source, Is.EqualTo("AI"));
            Assert.That(got.Line, Does.Contain("flat enough"));
            Assert.That(p.FlightLog[p.FlightLog.Count - 1], Does.Contain("Jeb Kerman"));

            // A model that fails: the rule answers at once.
            got = null;
            a.AskCrew(p.Id, new DecisionRequest { Kind = DecisionKind.ProgramHalt, Situation = "No return trajectory found.", Risk = 0.5 }, x => got = x);
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Source, Is.EqualTo("Rule"));

            // crewDecisions = Rule never touches the model.
            a.Settings.CrewDecisions = "Rule";
            int sent = transport.Sent.Count;
            a.AskCrew(p.Id, new DecisionRequest { Kind = DecisionKind.ThinMargin, Situation = "x", Risk = 0.1 }, x => got = x);
            Assert.That(transport.Sent.Count, Is.EqualTo(sent));
            Assert.That(got.Choice, Is.EqualTo(DecisionChoice.Proceed));
        }
    }
}
