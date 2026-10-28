using System;
using System.Collections.Generic;
using KSA.Core.Recruiting;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class PersonalityTagTests
    {
        [Test]
        public void ApplicantsNeverGetContradictoryTags()
        {
            Assert.That(ApplicantPool.Conflicts("Reckless", "Steady"), Is.True);
            Assert.That(ApplicantPool.Conflicts("steady", "RECKLESS"), Is.True);
            Assert.That(ApplicantPool.Conflicts("Cautious", "Perfectionist"), Is.False);
            var tags = new List<string> { "Cautious", "Reckless", "Perfectionist", "Showboat", "Steady", "Curious" };
            var agency = Make.Agency(new FakeClock(), new ListLogger());
            RecruitSource src = agency.Catalogs.Recruiting.Sources[0];
            var rng = new Random(11);
            for (int round = 0; round < 60; round++)
            {
                var pool = new ApplicantPool();
                pool.Generate(src, 4, 50, 1, rng, agency.Names, tags, new HashSet<string>());
                foreach (Applicant a in pool.Applicants)
                {
                    Assert.That(a.Tags.Count, Is.InRange(1, 2));
                    if (a.Tags.Count == 2) Assert.That(ApplicantPool.Conflicts(a.Tags[0], a.Tags[1]), Is.False, string.Join("+", a.Tags.ToArray()));
                }
            }
        }
    }
}
