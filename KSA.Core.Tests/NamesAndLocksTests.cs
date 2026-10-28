using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Recruiting;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>KSA-66: the shipped name list, and programs locked until their parts are researched.</summary>
    [TestFixture]
    public class NamesAndLocksTests
    {
        [Test]
        public void ShippedNamesAreAFewHundredAndUnique()
        {
            var warnings = new List<string>();
            NameCatalog n = NameCatalog.Parse(ShippedConfigs.Load("names.cfg"), warnings);
            Assert.That(warnings, Is.Empty, string.Join(" | ", warnings));
            Assert.That(n.Male.Count, Is.GreaterThanOrEqualTo(150));
            Assert.That(n.Female.Count, Is.GreaterThanOrEqualTo(150));
            Assert.That(n.Family.Count, Is.GreaterThanOrEqualTo(100));
            Assert.That(n.DefaultFamily, Is.Not.Empty);
            foreach (string f in n.Male) Assert.That(f.Length, Is.GreaterThanOrEqualTo(4), f);
            foreach (string f in n.Female) Assert.That(f.Length, Is.GreaterThanOrEqualTo(4), f);
            Assert.That(n.Male, Has.None.AnyOf(n.Female.ToArray()), "a first name is either male or female, never both");
        }

        [Test]
        public void PickHonoursGenderTheAcceptRuleAndTheFamilyShare()
        {
            NameCatalog n = NameCatalog.Parse(ShippedConfigs.Load("names.cfg"));
            var rng = new System.Random(3);
            int traditional = 0;
            var taken = new HashSet<string>();
            for (int i = 0; i < 400; i++)
            {
                bool female = i % 2 == 0;
                string name = n.Pick(rng, female, x => !taken.Contains(x));
                Assert.That(name, Is.Not.Null);
                taken.Add(name);
                string first = name.Split(' ')[0];
                Assert.That(female ? n.Female : n.Male, Has.Member(first));
                if (name.EndsWith(" " + n.DefaultFamily)) traditional++;
            }
            Assert.That(traditional, Is.InRange(80, 200), "about a third keep the traditional family name");
            Assert.That(n.Pick(rng, true, x => false), Is.Null, "nothing acceptable: no name rather than a clash");
        }

        [Test]
        public void ExtraNameNodesMerge()
        {
            var a = new DataNode(NameCatalog.NodeName); a.AddValue("male", "Zorbert, Quaxley"); a.AddValue("family", "Testwell");
            var b = new DataNode(NameCatalog.NodeName); b.AddValue("female", "Blinora"); b.AddValue("male", "Quaxley");
            var warnings = new List<string>();
            NameCatalog n = NameCatalog.Parse(new[] { a, b }, warnings);
            Assert.That(n.Male, Is.EqualTo(new[] { "Zorbert", "Quaxley" }));
            Assert.That(n.Female, Is.EqualTo(new[] { "Blinora" }));
            Assert.That(warnings, Has.Count.EqualTo(1), "the duplicate is reported once");
        }

        [Test]
        public void AProgramIsLockedUntilEveryModuleCanBeBuilt()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Val Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 3) });
            a.Tick();
            a.Ledger.SetBalance(9000000);
            ProgramType mun = a.Catalogs.ProgramTypes.Get("MunBase");
            Assert.That(a.ProgramBlocker(mun, "Mun"), Is.Null, "nothing blocks before research gating is wired");

            a.TemplateCheck = t => "needs research: landing legs";   // career with nothing unlocked
            string why = a.ProgramBlocker(mun, "Mun");
            Assert.That(why, Does.Contain("module").And.Contains("research"));
            string msg;
            int before = a.Programs.Count;
            Assert.That(a.CreateProgram("Mun Base", "MunBase", "Mun", null, out msg), Is.Null);
            Assert.That(msg, Does.Contain("research"));
            Assert.That(a.Programs.Count, Is.EqualTo(before), "no half-built program is left behind");

            a.TemplateCheck = null;
            Assert.That(a.CreateProgram("Mun Base", "MunBase", "Mun", null, out msg), Is.Not.Null, msg);
        }
    }
}
