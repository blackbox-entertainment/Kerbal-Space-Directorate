using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The AI Director upgrades space center buildings (2026-10-30, T: "the runway needs to be upgraded").</summary>
    [TestFixture]
    public class FacilityTests
    {
        private sealed class FakeHost : IFacilityHost
        {
            public readonly List<FacilityOption> List = new List<FacilityOption>();
            public bool Refuse;
            public List<FacilityOption> Facilities() { return List; }
            public bool RaiseLevel(string id, out string message)
            {
                message = Refuse ? "not at the Space Center" : string.Empty;
                if (Refuse) return false;
                foreach (FacilityOption f in List) if (f.Id == id) { f.Level++; f.MassLimit *= 5; }
                return true;
            }
        }

        private static Agency Make1(out FakeHost host)
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            host = new FakeHost();
            host.List.Add(new FacilityOption { Id = "Runway", Title = "Runway", Level = 1, MaxLevel = 3, UpgradeCost = 100000, MassLimit = 10, LimitNow = "craft up to 10 t", NextLevel = "craft up to 50 t" });
            host.List.Add(new FacilityOption { Id = "LaunchPad", Title = "Launch Pad", Level = 3, MaxLevel = 3, MassLimit = 0, LimitNow = "craft up to any mass" });
            a.FacilityHost = host;
            return a;
        }

        [Test]
        public void AnUpgradeIsPaidFromTheLedgerAndRefundedIfTheGameRefuses()
        {
            FakeHost host;
            Agency a = Make1(out host);
            string msg;
            host.Refuse = true;
            Assert.That(a.UpgradeFacility("Runway", true, out msg), Is.False);
            Assert.That(msg, Does.Contain("refunded"));
            Assert.That(a.Ledger.Balance, Is.EqualTo(500000));
            host.Refuse = false;
            Assert.That(a.UpgradeFacility("runway", true, out msg), Is.True, msg);
            Assert.That(a.Ledger.Balance, Is.EqualTo(400000));
            Assert.That(host.List[0].Level, Is.EqualTo(2));
            Assert.That(a.UpgradeFacility("LaunchPad", true, out msg), Is.False);
            Assert.That(msg, Does.Contain("top level"));
        }

        [Test]
        public void APlaneTooHeavyForTheRunwayIsHeldBackAndTheBriefingSaysWhy()
        {
            FakeHost host;
            Agency a = Make1(out host);
            var plane = new PlayerDesign { Name = "Big Plane", CraftPath = "C:/x/SPH/Big.craft", Seats = 1, IsPlane = true, HasJets = true, HasWheels = true, HasParachute = true, Mass = 14 };
            a.PlayerDesignSource = () => new List<PlayerDesign> { plane };
            MissionType first = a.Catalogs.Missions.Get("FirstFlightKerbin");
            Assert.That(a.DesignBlocker(plane, first), Does.Contain("too heavy for the Runway"));
            Assert.That(a.PlayerDesignsFor(first), Is.Empty);
            string brief = a.DirectorBriefing();
            Assert.That(brief, Does.Contain("FACILITIES").And.Contain("HELD BACK BY A BUILDING: 'Big Plane'"));
            Assert.That(a.DirectorSystemPrompt(), Does.Contain("upgrade {facility}"));
            string msg;
            Assert.That(a.UpgradeFacility("Runway", false, out msg), Is.True, msg);
            Assert.That(a.PlayerDesignsFor(first), Has.Count.EqualTo(1), "50 t runway takes it");
        }
    }
}
