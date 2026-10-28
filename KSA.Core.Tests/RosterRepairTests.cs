using System.Collections.Generic;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Save repair for hires the old rename bug left in the game's roster under a random name (2026-10-28).</summary>
    [TestFixture]
    public class RosterRepairTests
    {
        [Test]
        public void AHireLostToARandomNameIsMatchedAndRevived()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            var start = new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 1), Make.Snapshot("Bob Kerman", CrewRole.Scientist, 1) };
            a.SyncRoster(start);
            a.Tick();
            clock.AdvanceDays(75); a.Tick();

            // What the bug did: the agency hired Herrick, the game added "Gleus" instead, and the sync dismissed Herrick.
            a.Crew.Add(new CrewProfile("Herrick Kerman", "Herrick Kerman") { Role = CrewRole.Scientist, HiredDay = a.CurrentDay });
            var buggy = new List<CrewSnapshot>(start) { Make.Snapshot("Gleus Kerman", CrewRole.Scientist, 0) };
            a.SyncRoster(buggy);
            Assert.That(a.Crew.Get("Herrick Kerman").Departed, Is.True);
            Assert.That(a.Crew.Get("Gleus Kerman"), Is.Not.Null);

            List<KeyValuePair<string, string>> pairs = a.MisnamedHires(buggy);
            Assert.That(pairs, Has.Count.EqualTo(1));
            Assert.That(pairs[0].Key, Is.EqualTo("Gleus Kerman"));
            Assert.That(pairs[0].Value, Is.EqualTo("Herrick Kerman"));

            // The host renames Gleus back to Herrick; the next sync revives Herrick and nobody is left dismissed.
            a.ForgetMisnamedHire("Gleus Kerman", "Herrick Kerman");
            a.SyncRoster(new List<CrewSnapshot>(start) { Make.Snapshot("Herrick Kerman", CrewRole.Scientist, 0) });
            Assert.That(a.Crew.Get("Herrick Kerman").Departed, Is.False);
            Assert.That(a.Crew.Get("Gleus Kerman"), Is.Null);
        }

        [Test]
        public void ARealDismissalOrAVeteranIsNeverMatched()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            var roster = new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 1), Make.Snapshot("Val Kerman", CrewRole.Pilot, 1) };
            a.SyncRoster(roster);
            a.Tick();
            clock.AdvanceDays(40); a.Tick();
            // Dismissed weeks after hiring: a real dismissal, not the bug.
            a.Crew.Add(new CrewProfile("Old Hand", "Old Hand") { Role = CrewRole.Pilot, HiredDay = 1, Departed = true, DepartedDay = 30, DepartureReason = DepartureReason.Fired });
            Assert.That(a.MisnamedHires(roster), Is.Empty, "Jeb and Val were hired on day 0 and are not strangers");
        }
    }
}
