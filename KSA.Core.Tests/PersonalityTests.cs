using System.Collections.Generic;
using KSA.Core.Flight;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Personality tags have mechanical effects (built 2026-10-27).</summary>
    public class PersonalityTests
    {
        private static CrewProfile Pilot(string name, params string[] tags)
        {
            var p = new CrewProfile(name, name) { Role = CrewRole.Pilot, Stars = 3, Courage = 0.5f, Stupidity = 0.5f, Morale = 70 };
            p.PersonalityTags.AddRange(tags);
            return p;
        }

        [Test]
        public void ShippedRulesParseAndEveryTagDoesSomething()
        {
            PersonalityRules rules = ShippedConfigs.Settings().Personality;
            foreach (string tag in new[] { "Cautious", "Reckless", "Perfectionist", "Showboat", "Steady", "Curious" })
            {
                Assert.That(rules.Get(tag), Is.Not.Null, tag);
                Assert.That(rules.Describe(tag), Does.Not.Contain("no effect"), tag);
            }
            Assert.That(rules.MistakeFactor(Pilot("a", "Reckless")), Is.GreaterThan(1));
            Assert.That(rules.MistakeFactor(Pilot("b", "Cautious")), Is.LessThan(1));
            Assert.That(rules.IgnoresIdle(Pilot("c", "Steady")), Is.True);
            Assert.That(rules.ScienceFactor(Pilot("d", "Curious")), Is.GreaterThan(1));
            Assert.That(rules.DesignPasses(Pilot("e", "Perfectionist")), Is.EqualTo(2));
            Assert.That(rules.PrestigeOnFirst(Pilot("f", "Showboat")), Is.GreaterThan(0));
        }

        [Test]
        public void TagsTiltTheFlightProfileAndMorale()
        {
            PersonalityRules rules = PersonalityRules.Defaults();
            var fc = new FlightCrewRules();
            CrewFlightProfile reckless = CrewFlightProfile.Build(new List<CrewProfile> { Pilot("r", "Reckless") }, fc, rules);
            CrewFlightProfile cautious = CrewFlightProfile.Build(new List<CrewProfile> { Pilot("c", "Cautious") }, fc, rules);
            Assert.That(reckless.Nerve, Is.GreaterThan(cautious.Nerve));
            Assert.That(reckless.MistakeChance, Is.GreaterThan(cautious.MistakeChance));
            Assert.That(reckless.MarginFactor, Is.LessThan(cautious.MarginFactor), "the reckless cut it finer");

            var morale = new MoraleRules { Personality = rules };
            CrewProfile steady = Pilot("s", "Steady");
            CrewProfile plain = Pilot("p");
            morale.Apply(steady, MoraleEvent.CrisisYear);
            morale.Apply(plain, MoraleEvent.CrisisYear);
            Assert.That(70 - steady.Morale, Is.LessThan(70 - plain.Morale), "Steady kerbals shrug off half of a bad year");
        }
    }
}
