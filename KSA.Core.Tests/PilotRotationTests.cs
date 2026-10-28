using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>The agency spreads flights across its pilots (2026-10-28): whoever has flown least goes next.</summary>
    [TestFixture]
    public class PilotRotationTests
    {
        [Test]
        public void ThePilotWhoHasFlownLeastIsPickedFirst()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Val Kerman", CrewRole.Pilot, 1) });
            a.Tick();
            MissionType hop = a.Catalogs.Missions.Get("FleaHop");
            Assert.That(a.AutoCrew(hop), Is.EqualTo(new[] { "Jeb Kerman" }), "nobody has flown: the more experienced first");
            a.Crew.Get("Jeb Kerman").Record.Missions.Add(new MissionEntry { MissionId = "M1", Name = "Hop", StartDay = 1, Outcome = MissionOutcome.Success });
            Assert.That(a.AutoCrew(hop), Is.EqualTo(new[] { "Val Kerman" }), "Jeb has flown, so Val goes next");
            a.Crew.Get("Val Kerman").Record.Missions.Add(new MissionEntry { MissionId = "M2", Name = "Hop", StartDay = 5, Outcome = MissionOutcome.Success });
            Assert.That(a.AutoCrew(hop), Is.EqualTo(new[] { "Jeb Kerman" }), "one flight each: whoever flew longest ago");
        }
    }
}
