using System.Collections.Generic;
using KSA.Core.Design;
using KSA.Core.Missions;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>
    /// dv_requirements.cfg sanity (2026-10-28: a second "from Kerbin orbit" entry for Minmus orbit, 2400 m/s, overrode the
    /// 4900 m/s round trip, and a rocket that could not reach Kerbin orbit was approved for Minmus).
    /// </summary>
    [TestFixture]
    public class RequirementsTests
    {
        [Test]
        public void EveryOtherWorldNeedsAtLeastWhatKerbinOrbitNeeds()
        {
            Agency a = Make.Agency(new FakeClock(), new ListLogger());
            MissionRequirements req = MissionRequirements.Parse(ShippedConfigs.Load("dv_requirements.cfg"));
            double orbit = req.For(a.Catalogs.Missions.Get("KerbinOrbit")).DeltaV;
            Assert.That(orbit, Is.GreaterThanOrEqualTo(3400));
            var low = new List<string>();
            foreach (MissionType t in a.Catalogs.Missions.Types)
            {
                if (string.Equals(t.Body, "Kerbin", System.StringComparison.OrdinalIgnoreCase)) continue;
                double dv = req.For(t).DeltaV;
                if (dv < orbit) low.Add(t.Id + " " + dv);
            }
            Assert.That(low, Is.Empty, "requirements below Kerbin orbit's " + orbit + " m/s");
            Assert.That(req.For(a.Catalogs.Missions.Get("MinmusOrbit")).DeltaV, Is.EqualTo(4760)); // 3400 up, 1000 + 160 out, 200 home
        }

        [Test]
        public void EachProfileAndBodyIsListedOnce()
        {
            var seen = new HashSet<string>();
            var dups = new List<string>();
            foreach (KSA.Core.Data.DataNode n in ShippedConfigs.Load("dv_requirements.cfg"))
            {
                if (n.Name != MissionRequirements.NodeName) continue;
                string key = n.GetValue("profile", "") + "/" + n.GetValue("body", "") + "/" + n.GetValue("missionType", "");
                if (!seen.Add(key)) dups.Add(key);
            }
            Assert.That(dups, Is.Empty, "a later entry silently overrides an earlier one");
        }
    }
}
