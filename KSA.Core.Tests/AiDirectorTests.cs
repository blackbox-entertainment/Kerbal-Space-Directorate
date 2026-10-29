using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-68: the AI Director reads a briefing, answers with actions, and every action goes through the agency's own checks.</summary>
    [TestFixture]
    public class AiDirectorTests
    {
        private sealed class FakeResearch : IResearchHost
        {
            public double Science { get; set; } = 30;
            public List<string> Bought { get; } = new List<string>();
            public List<TechOption> Options() { return new List<TechOption> { new TechOption { Id = "basicRocketry", Title = "Basic Rocketry", ScienceCost = 5, Unlocks = "liquid engine, fuel tank" } }; }
            public bool Research(string techId, out string message)
            {
                if (techId != "basicRocketry") { message = "no technology called '" + techId + "'"; return false; }
                if (Science < 5) { message = "not enough Science"; return false; }
                Science -= 5; Bought.Add(techId); message = "Basic Rocketry researched"; return true;
            }
        }

        private static Agency Staffed(FakeClock clock, ScriptedTransport transport)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 1), Make.Snapshot("Val Kerman", CrewRole.Pilot, 1),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 1), Make.Snapshot("Bob Kerman", CrewRole.Scientist, 1)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.Ai = new AiSettings { ApiKey = "sk-test", DirectorModel = "test/director", MaxCallsPerDay = 50, MonthlySpendCapUsd = 5 };
            a.Transport = transport;
            return a;
        }

        private static string Reply(string actions, string plan = "Fly the Flea, then reach space.")
        {
            return "{\"thoughts\": \"Cheap and safe first.\", \"plan\": \"" + plan + "\", \"actions\": [" + actions + "]}";
        }

        [Test]
        public void OffByDefaultAndSilentUntilSwitchedOn()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            Assert.That(a.DirectorOn, Is.False);
            clock.AdvanceDays(60); a.Tick();
            Assert.That(transport.Sent, Is.Empty, "no calls while the player is the Director");
        }

        [Test]
        public void ATurnCarriesOutTheActionsAndLogsWhy()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            a.ResearchHost = new FakeResearch();
            transport.Replies.Enqueue(ScriptedTransport.Chat(Reply(
                "{\"do\": \"lab\", \"kerbal\": \"Bob Kerman\", \"on\": true, \"why\": \"idle scientist\"}," +
                "{\"do\": \"start_project\", \"mission\": \"FleaHop\", \"why\": \"day-one hardware\"}," +
                "{\"do\": \"research\", \"tech\": \"Basic Rocketry\", \"why\": \"liquid engines\"}")));
            string msg;
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(transport.Sent, Has.Count.EqualTo(1));
            Assert.That(transport.Sent[0].Body, Does.Contain("test/director"));
            Assert.That(transport.Sent[0].Body, Does.Contain("MISSIONS YOU CAN START").And.Contains("FleaHop"));
            Assert.That(a.Crew.Get("Bob Kerman").InLab, Is.True);
            Assert.That(a.Projects, Has.Some.Matches<Project>(p => p.MissionTypeId == "FleaHop"));
            Assert.That(((FakeResearch)a.ResearchHost).Bought, Has.Member("basicRocketry"), "found by its title");
            Assert.That(a.DirectorPlan, Does.StartWith("Fly the Flea"));
            Assert.That(a.DirectorLog, Has.Some.Matches<DirectorLogEntry>(e => e.Kind == "Thought"));
            Assert.That(a.DirectorLog.FindAll(e => e.Kind == "Action"), Has.Count.EqualTo(3));
            Assert.That(a.DirectorLog, Has.Some.Matches<DirectorLogEntry>(e => e.Text.Contains("idle scientist")));
        }

        [Test]
        public void TheAiDirectorCannotCancelTheHumanDirectorsProject()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            string msg;
            Project mine = a.CreateProject("My hop", "FleaHop", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(mine, Is.Not.Null, msg);
            Assert.That(mine.ByAiDirector, Is.False);
            transport.Replies.Enqueue(ScriptedTransport.Chat(Reply("{\"do\": \"cancel_project\", \"project\": \"" + mine.Id + "\", \"why\": \"too cheap\"}")));
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(transport.Sent, Has.Count.EqualTo(1));
            Assert.That(transport.Sent[0].Body, Does.Contain("started by the human Director"));
            Assert.That(mine.IsActive, Is.True, "still running");
            Assert.That(a.DirectorLog, Has.Some.Matches<DirectorLogEntry>(e => e.Text.Contains("only they can cancel it")));
        }

        [Test]
        public void TheBriefingShowsTheCampaignLadder()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ScriptedTransport());
            List<string> next = a.CampaignNextSteps(3);
            Assert.That(next, Has.Count.EqualTo(3));
            Assert.That(next[0], Is.EqualTo("Kerbin: suborbital hop"));
            Assert.That(next[1], Is.EqualTo("Mun: flyby"));
            Assert.That(a.DirectorBriefing(), Does.Contain("CAMPAIGN LADDER").And.Contain("Kerbin: suborbital hop"));
        }

        [Test]
        public void BadActionsFailAndTheNextBriefingSaysWhy()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            transport.Replies.Enqueue(ScriptedTransport.Chat(Reply(
                "{\"do\": \"launch_the_moon\"}," +
                "{\"do\": \"start_project\", \"mission\": \"DunaLanding\"}," +
                "{\"do\": \"hire\", \"applicant\": \"A999\"}")));
            string msg;
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(a.DirectorLog.FindAll(e => e.Kind == "Failed"), Has.Count.EqualTo(3));
            string briefing = a.DirectorBriefing();
            Assert.That(briefing, Does.Contain("RESULTS OF YOUR LAST TURN"));
            Assert.That(briefing, Does.Contain("FAILED launch_the_moon"));
            Assert.That(a.Projects, Has.None.Matches<Project>(p => p.MissionTypeId == "DunaLanding"), "Duna needs stars and research the agency lacks");
        }

        [Test]
        public void AReplyThatIsNotJsonChangesNothing()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            transport.Replies.Enqueue(ScriptedTransport.Chat("I think we should fly to the Mun!"));
            string msg;
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(a.DirectorLog, Has.Some.Matches<DirectorLogEntry>(e => e.Kind == "Failed" && e.Text.Contains("JSON")));
            Assert.That(a.Projects, Is.Empty);
        }

        [Test]
        public void AtMostMaxActionsPerTurn()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            a.Settings.Director.MaxActionsPerTurn = 2;
            transport.Replies.Enqueue(ScriptedTransport.Chat(Reply(
                "{\"do\": \"lab\", \"kerbal\": \"Bob Kerman\"},{\"do\": \"lab\", \"kerbal\": \"Bill Kerman\"},{\"do\": \"start_project\", \"mission\": \"FleaHop\"}")));
            string msg;
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(a.Projects, Is.Empty, "the third action was beyond the limit");
            Assert.That(a.DirectorBriefing(), Does.Contain("Only 2 actions per turn"));
        }

        [Test]
        public void TurnsComeOnScheduleAndSoonerAfterEvents()
        {
            var transport = new ScriptedTransport { Fallback = ScriptedTransport.Chat(Reply("{\"do\": \"wait\"}")) };
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            string msg;
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(transport.Sent, Has.Count.EqualTo(1));
            Assert.That(a.DaysToDirectorTurn(), Is.EqualTo(a.Settings.Director.TurnIntervalDays));
            clock.AdvanceDays(10); a.Tick();
            Assert.That(transport.Sent, Has.Count.EqualTo(1), "nothing happened: wait for the interval");
            a.RequestDirectorTurn("a mission ended");
            Assert.That(a.DaysToDirectorTurn(), Is.EqualTo(0), "10 days since the last turn is past the 5-day minimum");
            a.Tick();
            Assert.That(transport.Sent, Has.Count.EqualTo(2));
            Assert.That(transport.Sent[1].Body, Does.Contain("a mission ended"));
            clock.AdvanceDays(30); a.Tick();
            Assert.That(transport.Sent, Has.Count.EqualTo(3));
        }

        [Test]
        public void TheSpendCapStopsTurns()
        {
            var transport = new ScriptedTransport { Fallback = ScriptedTransport.Chat(Reply("")) };
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            a.Ai.MonthlySpendCapUsd = 0.01;                 // 0 would mean "no cap"
            a.Costs.Record(a.CurrentDay, 0.02);              // already over it this month
            string msg;
            a.SetDirector(true, out msg);
            a.Tick();
            Assert.That(transport.Sent, Is.Empty);
            Assert.That(a.DirectorLog, Has.Some.Matches<DirectorLogEntry>(e => e.Text.StartsWith("Turn skipped")));
        }

        [Test]
        public void FlightsSettingDecidesLiveOrSimulated()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ScriptedTransport());
            a.LiveFlightAvailable = true;
            MissionType flea = a.Catalogs.Missions.Get("FleaHop");
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            a.SetDirectorFlights(DirectorFlights.Simulate);
            Assert.That(a.DirectorMode(orbit), Is.EqualTo(MissionMode.Simulate));
            a.SetDirectorFlights(DirectorFlights.All);
            Assert.That(a.DirectorMode(flea), Is.EqualTo(MissionMode.WatchLive), "everything live");
            a.SetDirectorFlights(DirectorFlights.Firsts);
            Assert.That(a.DirectorMode(orbit), Is.EqualTo(orbit.IsFirst ? MissionMode.WatchLive : MissionMode.Simulate));
            a.LiveFlightAvailable = false;
            Assert.That(a.DirectorMode(orbit), Is.EqualTo(MissionMode.Simulate), "no flight director: simulate");
        }

        [Test]
        public void DirectorStateSurvivesSaveAndLoad()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeClock();
            Agency a = Staffed(clock, transport);
            transport.Replies.Enqueue(ScriptedTransport.Chat(Reply("{\"do\": \"lab\", \"kerbal\": \"Bob Kerman\", \"why\": \"science {braces} and // slashes\"}", "Keep // it simple {always}")));
            string msg;
            a.SetDirector(true, out msg);
            a.SetDirectorFlights(DirectorFlights.All);
            a.Tick();
            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Assert.That(b.DirectorOn, Is.True);
            Assert.That(b.State.DirectorFlights, Is.EqualTo(DirectorFlights.All));
            Assert.That(b.DirectorPlan, Does.Contain("Keep").And.Not.Contain("{").And.Not.Contain("//"));
            Assert.That(b.DirectorLog.Count, Is.EqualTo(a.DirectorLog.Count));
            Assert.That(b.DirectorLastTurnDay, Is.EqualTo(a.DirectorLastTurnDay));
        }

        [Test]
        public void AFailedAttemptAtAnOpenGoalReadsAsTryAgain()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ScriptedTransport());
            a.ActiveGoals.Clear();
            a.ActiveGoals.Add(new KSA.Core.Goals.GoalState { GoalId = "ReachSpace", YearIssued = a.CurrentYear });
            Assert.That(a.DirectorBriefing(), Does.Contain("met by: SuborbitalHop").And.Not.Contains("try again"));
            var r = new MissionReport { MissionTypeId = "SuborbitalHop", Day = a.CurrentDay, Outcome = MissionOutcome.LossOfCrew, ProjectName = "Suborbital hop" };
            a.Reports.Add(r);
            string b = a.DirectorBriefing();
            Assert.That(b, Does.Contain("SuborbitalHop (tried 1, last failure"));
            Assert.That(b, Does.Contain("try again"));
            Assert.That(a.DirectorSystemPrompt(), Does.Contain("Try again"));
        }

        [Test]
        public void TheBriefingCoversTheWholeAgency()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ScriptedTransport());
            a.ResearchHost = new FakeResearch();
            string b = a.DirectorBriefing();
            foreach (string section in new[] { "DATE:", "MONEY:", "GOALS THIS YEAR:", "KERBALS", "DEPARTMENTS:", "APPLICANTS", "RECRUITING DRIVES:", "ACTIVE PROJECTS:", "MISSIONS YOU CAN START", "PROGRAMS", "RESEARCH YOU CAN BUY NOW", "RECENT RESULTS:" })
                Assert.That(b, Does.Contain(section));
            Assert.That(b, Does.Contain("Jeb Kerman"));
            Assert.That(b, Does.Contain("basicRocketry"));
        }
    }
}
