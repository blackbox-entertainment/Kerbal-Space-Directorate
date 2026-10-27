using System.Collections.Generic;
using KSA.Core.Analytics;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Rivals;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class RivalsAndAnalyticsTests
    {
        private static Agency Staffed(FakeClock clock, ListLogger log)
        {
            Agency a = Make.Agency(clock, log);
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2)
            });
            a.Tick();
            a.Ledger.SetBalance(400000);
            return a;
        }

        [Test]
        public void ShippedRivalsLoadAndLadderComesFromMissions()
        {
            List<RivalDefinition> defs = ShippedConfigs.Rivals();
            Assert.That(defs.Count, Is.EqualTo(3));
            Assert.That(defs.Exists(d => d.Name.Contains("Northern")));
            List<MilestoneLadder.Rung> ladder = MilestoneLadder.Build(ShippedConfigs.Missions());
            Assert.That(ladder.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(ladder[0].Achievement, Is.EqualTo("Suborbit:Kerbin"), "easiest first");
            Assert.That(ladder.Exists(r => r.Achievement == "Flyby:Mun"));
            Assert.That(ladder.Exists(r => r.Achievement == "EVA:Mun"), "a landing mission's purpose is the spacewalk on the surface");
            Assert.That(MilestoneLadder.Describe("Land:Mun"), Is.EqualTo("landing on Mun"));
        }

        [Test]
        public void RivalsFlyEveryYearClaimFirstsAndPersist()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.Rng = new System.Random(7);
            var rivalNews = new List<string>();
            a.RivalNews += rivalNews.Add;
            for (int i = 0; i < 6; i++) a.ForceYearEnd();
            Assert.That(a.Rivals.Count, Is.EqualTo(3));
            int totalMissions = 0, totalMilestones = 0;
            foreach (RivalAgency r in a.Rivals) { totalMissions += r.Missions; totalMilestones += r.Milestones.Count; Assert.That(r.Years.Count, Is.EqualTo(6)); }
            Assert.That(totalMissions, Is.GreaterThan(12));
            Assert.That(totalMilestones, Is.GreaterThan(0), "six years is plenty for someone to reach suborbit");
            Assert.That(a.Firsts.Count, Is.GreaterThan(0));
            Assert.That(a.Firsts.TrueForAll(f => f.ClaimedBy != Agency.PlayerId), "the player flew nothing");
            Assert.That(a.News.Items.Exists(n => n.Related.StartsWith("rival:")));
            Assert.That(rivalNews.Count, Is.GreaterThan(0));

            Agency b = Staffed(new FakeClock(), new ListLogger());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.Rivals.Count, Is.EqualTo(3));
            Assert.That(b.Rivals[0].Missions, Is.EqualTo(a.Rivals[0].Missions));
            Assert.That(b.Firsts.Count, Is.EqualTo(a.Firsts.Count));
            Assert.That(b.YearHistory.Count, Is.EqualTo(6));
        }

        [Test]
        public void PlayerClaimsAFirstWhenAheadAndLeagueRanksIt()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            a.Settings.RivalsEnabled = false;
            string msg;
            Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            a.Rng = new System.Random(3);
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 30);
            a.Tick();
            Assert.That(p.Report, Is.Not.Null);
            a.ForceYearEnd();
            if (a.HasAgencyAchievement("Suborbit:Kerbin"))
            {
                Assert.That(a.FirstFor("Suborbit:Kerbin").ClaimedBy, Is.EqualTo(Agency.PlayerId));
                Assert.That(a.League()[0].IsPlayer, Is.True, "no rivals flew, so a first puts the player on top");
            }
            List<LeagueRow> league = a.League();
            Assert.That(league.Count, Is.EqualTo(1), "rivals disabled: only the player row");
            a.Settings.RivalsEnabled = true;
            Assert.That(a.League().Count, Is.EqualTo(4));
        }

        [Test]
        public void YearSummariesTrackMoneyMissionsAndGoals()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            int year = a.CurrentYear;
            YearSummary live = a.SummaryFor(year);
            Assert.That(live.Closed, Is.False);
            Assert.That(live.Grant, Is.GreaterThan(0), "the founding grant landed this year");
            Assert.That(live.Band, Is.EqualTo("in progress"));

            string msg;
            Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(a.SummaryFor(year).Projects, Is.EqualTo(p.Budget));
            clock.AdvanceDays(p.DesignDays + p.BuildDays + 30);
            a.Tick();
            a.ForceYearEnd();
            YearSummary closed = a.SummaryFor(year);
            Assert.That(closed.Closed, Is.True);
            Assert.That(closed.Missions, Is.EqualTo(1));
            Assert.That(closed.Salaries, Is.GreaterThan(0));
            Assert.That(closed.Band, Is.Not.EqualTo("in progress"));
            Assert.That(closed.OpeningBalance + closed.Net, Is.EqualTo(closed.ClosingBalance).Within(0.01));
            List<YearSummary> all = a.AllYears();
            Assert.That(all.Count, Is.EqualTo(2), "the closed year plus the live one");
            Assert.That(all[0].Year, Is.EqualTo(year));
            Assert.That(all[1].Closed, Is.False);
        }
    }
}
