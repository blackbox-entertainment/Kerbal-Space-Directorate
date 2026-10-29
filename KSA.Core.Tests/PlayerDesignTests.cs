using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The Director's own VAB rockets fly projects when they fit the mission (2026-10-28).</summary>
    [TestFixture]
    public class PlayerDesignTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Val Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2) });
            a.Tick();
            a.Ledger.SetBalance(500000);
            return a;
        }

        private static PlayerDesign Orbiter() { return new PlayerDesign { Name = "Tim's Orbiter", CraftPath = "C:/saves/x/Ships/VAB/Orbiter.craft", Seats = 1, DeltaV = 4200, LiftoffTwr = 1.6, HasParachute = true, Mass = 12, PartCount = 14 }; }
        private static PlayerDesign Hopper() { return new PlayerDesign { Name = "Hopper", CraftPath = "C:/saves/x/Ships/VAB/Hopper.craft", Seats = 1, DeltaV = 2000, LiftoffTwr = 2.0, HasParachute = true }; }

        [Test]
        public void ADesignFitsOnlyWhatItCanFly()
        {
            Agency a = Staffed(new FakeClock());
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            MissionRequirements.Entry need = a.Requirements.For(orbit);
            Assert.That(Orbiter().Blocker(orbit, need), Is.Null);
            Assert.That(Hopper().Blocker(orbit, need), Does.Contain("m/s"));
            PlayerDesign noChute = Orbiter(); noChute.HasParachute = false;
            Assert.That(noChute.Blocker(orbit, need), Does.Contain("parachute"));
            PlayerDesign locked = Orbiter(); locked.ResearchBlocker = "needs research: Terrier";
            Assert.That(locked.Blocker(orbit, need), Does.Contain("research"));
        }

        [Test]
        public void ThePickedDesignIsRememberedAndOnlyUsedWhileItFits()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.PlayerDesignSource = () => new List<PlayerDesign> { Orbiter(), Hopper() };
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            a.SetPreferredDesign("KerbinOrbit", "Tim's Orbiter");
            Assert.That(a.PreferredDesignFor(orbit).Name, Is.EqualTo("Tim's Orbiter"));
            Assert.That(a.DirectorBriefing(), Does.Contain("wants their design 'Tim's Orbiter' flown"));
            a.SetPreferredDesign("KerbinOrbit", "Hopper");
            Assert.That(a.PreferredDesignFor(orbit), Is.Null, "the hopper cannot reach orbit, so it is not forced on the mission");

            a.SetPreferredDesign("KerbinOrbit", "Tim's Orbiter");
            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Assert.That(b.PreferredDesigns["KerbinOrbit"], Is.EqualTo("Tim's Orbiter"));
        }

        [Test]
        public void YourDesignFliesTheProjectWithoutAnAiDesign()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            a.PlayerDesignSource = () => new List<PlayerDesign> { Orbiter(), Hopper() };
            // No template can be built: only the Director's own rocket opens the mission.
            a.TemplateCheck = t => "needs research";
            MissionType orbit = a.Catalogs.Missions.Get("KerbinOrbit");
            Assert.That(a.MissionBlocker(orbit), Is.Not.Null);
            Assert.That(a.PlayerDesignsFor(orbit), Has.Count.EqualTo(1));

            string msg;
            Assert.That(a.CreateProject("Hop to orbit", "KerbinOrbit", null, null, null, null, MissionMode.Simulate, out msg, null, "Hopper"), Is.Null, "the hopper cannot reach orbit");
            Assert.That(msg, Does.Contain("m/s"));

            Project p = a.CreateProject("My orbit", "KerbinOrbit", null, null, null, null, MissionMode.WatchLive, out msg, null, "Tim's Orbiter");
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(p.Design.Source, Is.EqualTo(DesignSource.Player));
            Assert.That(p.Design.Verdict, Is.EqualTo(DesignVerdict.Approved));
            Assert.That(p.Design.CraftPath, Is.EqualTo(Orbiter().CraftPath));
            Assert.That(p.DesignDays, Is.EqualTo(1));
            Assert.That(a.DesignNameOf(p), Does.EndWith("(your design)"));
            Assert.That(p.Design.DesignScore, Is.GreaterThan(0.45));

            // Through design and build without an AI call or a call-off.
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 1);
            a.Tick();
            Assert.That(p.Status, Is.EqualTo(ProjectStatus.Ready).Or.EqualTo(ProjectStatus.InFlight));

            var store = new MemoryPersistence();
            a.Save(store);
            Agency b = Make.Agency(clock, new ListLogger());
            b.Load(store);
            Assert.That(b.GetProject(p.Id).Design.Source, Is.EqualTo(DesignSource.Player));
        }
    }
}
