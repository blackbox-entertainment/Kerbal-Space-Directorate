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
            Assert.That(a.AdoptHangarLaunch("SuborbitalHop", "2", "X", new List<string> { "Jeb Kerman" }, out msg), Is.Not.Null);
            Assert.That(a.AdoptHangarLaunch("KerbinOrbit", "3", "Y", new List<string> { "Jeb Kerman" }, out msg), Is.Null, "Jeb is already flying");
            Assert.That(msg, Does.Contain("already assigned"));
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
