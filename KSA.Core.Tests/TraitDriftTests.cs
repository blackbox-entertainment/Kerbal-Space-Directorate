using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class TraitDriftTests
    {
        [Test]
        public void ShippedSettingsCarryDriftBlock()
        {
            AgencySettings s = ShippedConfigs.Settings();
            Assert.That(s.Drift.Enabled, Is.True);
            Assert.That(s.Drift.SuccessStupidity, Is.LessThan(0));
            Assert.That(s.Drift.StupidityFloor, Is.GreaterThan(0));
        }

        [Test]
        public void MissionsMoveTraitsWithinBounds()
        {
            var d = new TraitDrift();
            var p = new CrewProfile("Jeb", "Jeb") { Courage = 0.5f, Stupidity = 0.5f };
            Assert.That(d.AfterMission(p, MissionOutcome.Success, false), Is.True);
            Assert.That(p.Courage, Is.EqualTo(0.53f).Within(0.001f));
            Assert.That(p.Stupidity, Is.EqualTo(0.47f).Within(0.001f));
            Assert.That(p.TraitsOwned, Is.True);
            Assert.That(d.AfterMission(p, MissionOutcome.LossOfVehicle, true), Is.True);
            Assert.That(p.Courage, Is.EqualTo(0.50f).Within(0.001f), "+0.02 close call, -0.05 crewmate death");
            Assert.That(p.Stupidity, Is.EqualTo(0.42f).Within(0.001f));
            for (int i = 0; i < 100; i++) d.AfterMission(p, MissionOutcome.Success, false);
            Assert.That(p.Courage, Is.EqualTo(d.CourageMax).Within(0.001f));
            Assert.That(p.Stupidity, Is.EqualTo(d.StupidityFloor).Within(0.001f));
            Assert.That(d.AfterMission(p, MissionOutcome.LossOfCrew, true), Is.True, "survivors of a fatal flight learn (the agency skips the dead before calling this)");
            Assert.That(d.AfterMission(p, MissionOutcome.InProgress, false), Is.False);

            // Out-of-band starts are left alone instead of being dragged to the band edge.
            var bob = new CrewProfile("Bob", "Bob") { Courage = 1.0f, Stupidity = 0.1f };
            Assert.That(d.AfterMission(bob, MissionOutcome.Success, false), Is.False);
            Assert.That(bob.Courage, Is.EqualTo(1.0f));
            Assert.That(bob.Stupidity, Is.EqualTo(0.1f));
            Assert.That(bob.TraitsOwned, Is.False);
            Assert.That(d.Apply(bob, -0.03f, 0f), Is.True, "moving back toward the band is allowed");
            Assert.That(bob.Courage, Is.EqualTo(0.97f).Within(0.001f));
        }

        [Test]
        public void AgencyDriftsAfterMissionsAndDeathsAndPushesToHost()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Val Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            var pushed = new List<string>();
            a.TraitsChanged += changed => pushed.Add(changed.Id);
            CrewProfile jeb = a.Crew.Get("Jeb Kerman");
            CrewProfile val = a.Crew.Get("Val Kerman");
            float jebCourage = jeb.Courage, jebStupidity = jeb.Stupidity, valCourage = val.Courage;

            string msg;
            Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            a.Rng = new System.Random(1);
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 30);
            a.Tick();
            Assert.That(p.Report, Is.Not.Null);
            if (p.Report.Outcome != MissionOutcome.LossOfCrew)
            {
                Assert.That(jeb.Stupidity, Is.LessThan(jebStupidity), "every survived mission teaches something");
                Assert.That(pushed, Does.Contain("Jeb Kerman"));
                Assert.That(jeb.TraitsOwned, Is.True);
            }

            // The host re-syncs with the stock values: the agency's drifted values survive.
            float drifted = jeb.Stupidity;
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Val Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2) });
            Assert.That(jeb.Stupidity, Is.EqualTo(drifted));

            // A colleague's death shakes everyone else.
            a.RecordDeath("Bill Kerman", "test");
            Assert.That(val.Courage, Is.LessThan(valCourage));
            Assert.That(pushed, Does.Contain("Val Kerman"));

            // Save/load keeps ownership.
            Agency b = Make.Agency(new FakeClock(), new ListLogger());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.Crew.Get("Val Kerman").TraitsOwned, Is.True);
            Assert.That(b.Crew.Get("Val Kerman").Courage, Is.EqualTo(val.Courage).Within(0.0001f));
        }

        [Test]
        public void IdleYearLowersCourage()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3) });
            a.Tick();
            CrewProfile jeb = a.Crew.Get("Jeb Kerman");
            float before = jeb.Courage;
            a.ForceYearEnd();
            Assert.That(jeb.Courage, Is.LessThan(before));
        }
    }
}
