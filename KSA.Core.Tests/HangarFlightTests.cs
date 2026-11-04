using System.Collections.Generic;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-62: a hangar-built rocket with agency kerbals aboard becomes a live project on the spot.</summary>
    [TestFixture]
    public class HangarFlightTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4)
            });
            a.Tick();
            a.Ledger.SetBalance(900000);
            a.LiveFlightAvailable = true;
            return a;
        }

        [Test]
        public void HangarLaunchCreatesAnInFlightProjectWithTheCrewAboard()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            double before = a.Ledger.Balance;
            string msg;
            Project p = a.AdoptHangarLaunch("SuborbitalHop", "4242", "My Rocket", new List<string> { "Jeb Kerman", "Stranger Kerman" }, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.InFlight));
            Assert.That(p.Mode, Is.EqualTo(MissionMode.WatchLive));
            Assert.That(p.Crew, Is.EqualTo(new[] { "Jeb Kerman" }));
            Assert.That(p.VesselId, Is.EqualTo("4242"));
            Assert.That(p.Name, Does.Contain("My Rocket"));
            Assert.That(a.Ledger.Balance, Is.EqualTo(before), "a hangar flight costs nothing");
            Assert.That(a.Crew.Get("Jeb Kerman").IsFlying, Is.True);
            Assert.That(a.ProjectForVessel("4242"), Is.SameAs(p));
        }

        [Test]
        public void HangarLaunchRefusesWithoutAnAgencyKerbalOrWithAnAssignedOne()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Assert.That(a.AdoptHangarLaunch("SuborbitalHop", "1", "X", new List<string> { "Stranger Kerman" }, out msg), Is.Null);
            Assert.That(msg, Does.Contain("No agency kerbal"));
            Project first = a.AdoptHangarLaunch("SuborbitalHop", "2", "X", new List<string> { "Jeb Kerman" }, out msg);
            Assert.That(first, Is.Not.Null);
            // Seated in a new rocket, he is not on the old flight, whatever a reloaded save says.
            Project second = a.AdoptHangarLaunch("KerbinOrbit", "3", "Y", new List<string> { "Jeb Kerman" }, out msg);
            Assert.That(second, Is.Not.Null, msg);
            Assert.That(first.Crew, Does.Not.Contain("Jeb Kerman"));
            second.Status = ProjectStatus.Build;
            Assert.That(a.AdoptHangarLaunch("KerbinOrbit", "4", "Z", new List<string> { "Jeb Kerman" }, out msg), Is.Null, "on a project still on the ground");
            Assert.That(msg, Does.Contain("already assigned"));
        }

        [Test]
        public void StaleDuplicateCrewlessHangarFlightsAreScrubbedOnLoad()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            // Fuel Station mk I (2026-11-04): one finished flight and ten crewless duplicates on the same vessel.
            a.Projects.Add(new Project { Id = "P13", Name = "Hangar flight: Fuel Station mk I", MissionTypeId = "TankerKerbin", Status = ProjectStatus.Complete, VesselId = "26066", LaunchUT = 100 });
            for (int i = 14; i <= 23; i++)
                a.Projects.Add(new Project { Id = "P" + i, Name = "Hangar flight: Fuel Station mk I", MissionTypeId = "TankerKerbin", Status = ProjectStatus.InFlight, VesselId = "26066", LaunchUT = 100 + i });
            // A probe still flying, with two extra clicks: the first stays.
            a.Projects.Add(new Project { Id = "Q1", Name = "Hangar flight: Probe", MissionTypeId = "ProbeOrbitKerbin", Status = ProjectStatus.InFlight, VesselId = "777", LaunchUT = 500 });
            a.Projects.Add(new Project { Id = "Q2", Name = "Hangar flight: Probe", MissionTypeId = "ProbeOrbitKerbin", Status = ProjectStatus.InFlight, VesselId = "777", LaunchUT = 503 });
            // A crewed flight is never touched.
            string msg;
            Project crewed = a.AdoptHangarLaunch("SuborbitalHop", "4242", "My Rocket", new List<string> { "Jeb Kerman" }, out msg);
            Assert.That(crewed, Is.Not.Null, msg);

            List<string> removed = a.ScrubDuplicateHangarFlights();
            Assert.That(removed.Count, Is.EqualTo(11));
            Assert.That(a.GetProject("P13"), Is.Not.Null);
            for (int i = 14; i <= 23; i++) Assert.That(a.GetProject("P" + i), Is.Null, "P" + i);
            Assert.That(a.GetProject("Q1"), Is.Not.Null);
            Assert.That(a.GetProject("Q2"), Is.Null);
            Assert.That(a.GetProject(crewed.Id), Is.Not.Null);
            Assert.That(a.ScrubDuplicateHangarFlights(), Is.Empty, "a second pass finds nothing");
        }

        [Test]
        public void HangarFlightResolvesLikeAnyLiveFlight()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            string msg;
            Project p = a.AdoptHangarLaunch("SuborbitalHop", "7", "Hopper", new List<string> { "Jeb Kerman" }, out msg);
            var result = new LiveFlightResult { Recovered = true, MaxAltitude = 90000, MaxSpeed = 1200, DaysInSpace = 0.1 };
            result.Achievements.Add("Suborbit:Kerbin");
            result.Achievements.Add("Recover:-");
            MissionReport r = a.ResolveLive(p.Id, result);
            Assert.That(r, Is.Not.Null);
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Complete));
            Assert.That(a.Crew.Get("Jeb Kerman").IsFlying, Is.False);
            Assert.That(a.Crew.Get("Jeb Kerman").ProjectId, Is.Empty);
        }
    }
}
