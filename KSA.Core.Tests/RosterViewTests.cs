using System.Collections.Generic;
using KSA.Core.Personnel;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class RosterViewTests
    {
        [Test]
        public void Sort_GroupsByStatusThenName()
        {
            var crew = new List<CrewRecord>
            {
                new CrewRecord("Valentina", CrewRole.Pilot, 5, CrewStatus.Assigned),
                new CrewRecord("bob", CrewRole.Engineer, 1, CrewStatus.Available),
                new CrewRecord("Jebediah", CrewRole.Pilot, 3, CrewStatus.Dead),
                new CrewRecord("Bill", CrewRole.Engineer, 2, CrewStatus.Available),
                new CrewRecord("Dilsby", CrewRole.Scientist, 0, CrewStatus.Missing),
            };

            List<CrewRecord> sorted = RosterView.Sort(crew);

            Assert.That(sorted.Count, Is.EqualTo(5));
            Assert.That(sorted[0].Name, Is.EqualTo("Bill"));       // Available, B...
            Assert.That(sorted[1].Name, Is.EqualTo("bob"));        // Available, case-insensitive after Bill
            Assert.That(sorted[2].Name, Is.EqualTo("Valentina"));  // Assigned
            Assert.That(sorted[3].Name, Is.EqualTo("Dilsby"));     // Missing
            Assert.That(sorted[4].Name, Is.EqualTo("Jebediah"));   // Dead last
        }

        [Test]
        public void Sort_EmptyInput_ReturnsEmptyList()
        {
            Assert.That(RosterView.Sort(new CrewRecord[0]), Is.Empty);
        }

        [TestCase(0, "-----")]
        [TestCase(3, "***--")]
        [TestCase(5, "*****")]
        [TestCase(-2, "-----")]
        [TestCase(9, "*****")]
        public void StarsText_IsFixedWidthAndClamped(int stars, string expected)
        {
            Assert.That(RosterView.StarsText(stars), Is.EqualTo(expected));
        }

        [Test]
        public void CrewRecord_ClampsStarsToRange()
        {
            Assert.That(new CrewRecord("A", CrewRole.Pilot, 42, CrewStatus.Available).Stars, Is.EqualTo(5));
            Assert.That(new CrewRecord("A", CrewRole.Pilot, -1, CrewStatus.Available).Stars, Is.EqualTo(0));
        }

        [Test]
        public void RoleAndStatusText_CoverEveryValue()
        {
            foreach (CrewRole role in System.Enum.GetValues(typeof(CrewRole)))
                Assert.That(RosterView.RoleText(role), Is.Not.Empty);
            foreach (CrewStatus status in System.Enum.GetValues(typeof(CrewStatus)))
                Assert.That(RosterView.StatusText(status), Is.Not.Empty);
        }
    }
}
