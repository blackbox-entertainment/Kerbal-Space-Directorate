using System.Collections.Generic;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class MoraleScalingTests
    {
        [Test]
        public void ShippedScalingLoadsAndSmallRostersHurtMore()
        {
            MoraleRules rules = ShippedConfigs.Morale();
            Assert.That(rules.ReferenceRoster, Is.EqualTo(12));
            Assert.That(rules.ScaledDelta(MoraleEvent.CrewDeath, 12), Is.EqualTo(-15), "the configured delta at the reference roster");
            Assert.That(rules.ScaledDelta(MoraleEvent.CrewDeath, 3), Is.EqualTo(-30), "four kerbals lose one: double");
            Assert.That(rules.ScaledDelta(MoraleEvent.CrewDeath, 99), Is.EqualTo(-5), "a hundred lose one: a third");
            Assert.That(rules.ScaledDelta(MoraleEvent.ColleagueFired, 1), Is.EqualTo(-12), "clamped at triple");
            Assert.That(rules.ScaledDelta(MoraleEvent.ColleagueFired, 3), Is.EqualTo(-8));
        }

        [Test]
        public void FiringInATinyAgencyScalesTheHitAndSaysSo()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist, 2),
                Make.Snapshot("Val Kerman", CrewRole.Pilot, 3)
            });
            a.Tick();
            int before = a.Crew.Get("Jeb Kerman").Morale;
            string msg;
            Assert.That(a.Fire("Bob Kerman", out msg), Is.True, msg);
            Assert.That(msg, Does.Contain("fell by 8"));
            Assert.That(a.Crew.Get("Jeb Kerman").Morale, Is.EqualTo(before - 8));

            before = a.Crew.Get("Jeb Kerman").Morale;
            List<string> notices = a.RecordDeath("Val Kerman", "test flight");
            Assert.That(notices, Has.Some.Contains("fell by 37"), "two survivors: sqrt(12/2) times 15");
            Assert.That(a.Crew.Get("Jeb Kerman").Morale, Is.EqualTo(before - 37));
        }
    }
}
