using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class LiveFlightTests
    {
        private static Agency Staffed(FakeClock clock, ListLogger log)
        {
            Agency a = Make.Agency(clock, log);
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Val Kerman", CrewRole.Pilot, 2),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            a.LiveFlightAvailable = true;
            return a;
        }

        private static Project ReadyProject(Agency a, FakeClock clock, string typeId = "KerbinOrbit")
        {
            string msg;
            Project p = a.CreateProject("Live " + typeId, typeId, null, null, null, null, MissionMode.WatchLive, out msg);
            Assert.That(p, Is.Not.Null, msg);
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 1);
            a.Tick();
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Ready), "live projects wait on the pad for the host");
            return p;
        }

        [Test]
        public void ResolverGradesFlightsFromFacts()
        {
            MissionType orbit = ShippedConfigs.Missions().Get("KerbinOrbit");
            var crew = new List<string> { "Jeb Kerman" };
            var good = new LiveFlightResult { Recovered = true, DaysInSpace = 0.5 };
            good.Achievements.Add("Orbit:Kerbin");
            MissionReport r1 = LiveFlightResolver.Resolve(new Project { Id = "P1", Name = "Test" }, orbit, crew, good, 10, 0.7, 0.6);
            Assert.That(r1.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(r1.Mode, Is.EqualTo(MissionMode.WatchLive));
            Assert.That(r1.Achievements, Does.Contain("Orbit:Kerbin"));

            var sub = new LiveFlightResult { Recovered = true };
            sub.Achievements.Add("Suborbit:Kerbin");
            Assert.That(LiveFlightResolver.Resolve(null, orbit, crew, sub, 10, 0.5, 0.5).Outcome, Is.EqualTo(MissionOutcome.PartialSuccess));

            var crash = new LiveFlightResult { Recovered = false, Crashed = true };
            Assert.That(LiveFlightResolver.Resolve(null, orbit, crew, crash, 10, 0.5, 0.5).Outcome, Is.EqualTo(MissionOutcome.LossOfVehicle));

            var dead = new LiveFlightResult { Recovered = false, Crashed = true };
            dead.Deaths.Add("Jeb Kerman");
            MissionReport r4 = LiveFlightResolver.Resolve(null, orbit, crew, dead, 10, 0.5, 0.5);
            Assert.That(r4.Outcome, Is.EqualTo(MissionOutcome.LossOfCrew));
            Assert.That(r4.Deaths, Does.Contain("Jeb Kerman"));

            var director = new LiveFlightResult { Recovered = true, DirectorFlown = true };
            director.Achievements.Add("Orbit:Kerbin");
            Assert.That(LiveFlightResolver.Resolve(null, orbit, crew, director, 10, 0.5, 0.5).Lesson, Does.Contain("Director"));
        }

        [Test]
        public void LaunchLiveAsksHostThenResolvesFromLog()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            LaunchSpec spec = null;
            a.LiveLaunchRequested += s => spec = s;
            Project p = ReadyProject(a, clock);

            string msg;
            Assert.That(a.LaunchLive(p.Id, out msg), Is.True, msg);
            Assert.That(spec, Is.Not.Null);
            Assert.That(spec.CrewNames, Does.Contain("Jeb Kerman"));
            Assert.That(spec.TemplateCraft, Does.StartWith("stock:"));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.InFlight));
            Assert.That(a.Crew.Get("Jeb Kerman").IsFlying, Is.True);

            a.BindVessel(p.Id, "vessel-42");
            Assert.That(a.ProjectForVessel("vessel-42"), Is.SameAs(p));
            a.LogFlightEvent(p.Id, "Liftoff.");
            a.LogFlightEvent(p.Id, "Orbit achieved, 85 x 82 km.", "Orbit:Kerbin");
            a.SetDirectorFlown(p.Id, true);
            clock.AdvanceDays(0.5);
            a.Tick();
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.InFlight), "live flights never auto-resolve");

            MissionReport r = a.ResolveLive(p.Id, new LiveFlightResult { Recovered = true });
            Assert.That(r.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(r.DirectorFlown, Is.True);
            Assert.That(r.DaysInSpace, Is.EqualTo(0.5).Within(0.01));
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Complete));
            Assert.That(a.Crew.Get("Jeb Kerman").Record.Achievements, Does.Contain("Orbit:Kerbin"));
            Assert.That(a.Crew.Get("Jeb Kerman").IsFlying, Is.False);
            Assert.That(a.Reports[a.Reports.Count - 1].Mode, Is.EqualTo(MissionMode.WatchLive));
        }

        [Test]
        public void LiveBlockerAndSimulateInstead()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.LiveFlightCheck = t => t.Body == "Kerbin" ? null : "Live flights beyond Kerbin need MechJeb.";
            Project p = ReadyProject(a, clock, "MunFlyby");
            string msg;
            Assert.That(a.LaunchLive(p.Id, out msg), Is.False);
            Assert.That(msg, Does.Contain("MechJeb"));
            Assert.That(a.SimulateInstead(p.Id, out msg), Is.True, msg);
            Assert.That(p.Report, Is.Not.Null);
            Assert.That(p.Mode, Is.EqualTo(MissionMode.Simulate));
        }

        [Test]
        public void PostMortemArrivesAsynchronouslyAndSaves()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            a.Ai = new AiSettings { DesignMode = DesignMode.Api, ApiKey = "k", DesignModel = "m", PostMortemModel = "pm" };
            a.Transport = transport;
            a.Parts = TestParts.Catalog();
            // Block the design session (empty queue would fail it; that's fine) and script the post-mortem.
            transport.Fallback = new LlmResponse { Ok = false, Status = 401, Body = "{}" };
            Project p = ReadyProject(a, clock);
            transport.Replies.Enqueue(ScriptedTransport.Chat("Jeb flew a clean profile. The second stage ran lean. Next time carry ten percent more fuel."));
            string msg;
            Assert.That(a.LaunchLive(p.Id, out msg), Is.True, msg);
            a.LogFlightEvent(p.Id, "Orbit.", "Orbit:Kerbin");
            MissionReport updated = null;
            a.ReportUpdated += r => updated = r;
            MissionReport report = a.ResolveLive(p.Id, new LiveFlightResult { Recovered = true });
            Assert.That(updated, Is.SameAs(report));
            Assert.That(report.PostMortem, Does.Contain("clean profile"));
            Assert.That(report.Lesson, Does.Contain("more fuel"));
            Assert.That(transport.Sent[transport.Sent.Count - 1].Body, Does.Contain("\"pm\""));

            Agency b = Staffed(new FakeClock(), new ListLogger());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.Reports[b.Reports.Count - 1].PostMortem, Does.Contain("clean profile"));
            Assert.That(b.GetProject(p.Id).FlightLog, Does.Contain("Orbit."));
        }
    }
}
