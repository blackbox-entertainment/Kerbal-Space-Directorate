using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Missions;
using KSA.Core.Narrative;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class NarrativeTests
    {
        private static Agency Staffed(FakeClock clock, ListLogger log)
        {
            Agency a = Make.Agency(clock, log);
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            return a;
        }

        [Test]
        public void MissionsProjectsAndYearsMakeNews()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var published = new List<NewsItem>();
            a.NewsPublished += published.Add;
            string msg;
            Project p = a.CreateProject("Jeb's Hop", "SuborbitalHop", null, null, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Assert.That(published.Exists(n => n.Kind == NewsKind.Ticker && n.Headline.Contains("Jeb's Hop")));

            clock.AdvanceDays(p.DesignDays + p.BuildDays + 30);
            a.Tick();
            Assert.That(p.Report, Is.Not.Null);
            NewsItem release = published.Find(n => (n.Kind == NewsKind.PressRelease || n.Kind == NewsKind.Obituary) && n.Related == p.Id);
            Assert.That(release, Is.Not.Null);
            Assert.That(release.Body, Does.Contain("Jeb Kerman"));
            Assert.That(release.Byline, Does.Contain("Kerbal Space Directorate"));

            a.ForceYearEnd();
            Assert.That(published.Exists(n => n.Kind == NewsKind.Review));
            Assert.That(a.News.TickerLines(3).Count, Is.EqualTo(3));
            Assert.That(a.News.Unread, Is.GreaterThan(0));
            a.MarkAllRead();
            Assert.That(a.News.Unread, Is.EqualTo(0));
        }

        [Test]
        public void DeparturesAndHiresMakeNews()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            string msg;
            Assert.That(a.Fire("Bill Kerman", out msg), Is.True, msg);
            Assert.That(a.News.Latest(1)[0].Headline, Does.Contain("Bill Kerman"));
            Assert.That(a.Applicants.Applicants.Count, Is.GreaterThan(0), "walk-ins exist after the first tick");
            Assert.That(a.Hire(a.Applicants.Applicants[0].Id, out msg), Is.True, msg);
            Assert.That(a.News.Latest(1)[0].Headline, Does.Contain("joins"));
        }

        [Test]
        public void NarrativeModelRewritesInPlaceAndSaves()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock, new ListLogger());
            var transport = new ScriptedTransport();
            transport.Fallback = ScriptedTransport.Chat("Today the Directorate was pleased to confirm the mission flew well and everyone came home for snacks.");
            a.Ai = new AiSettings { DesignMode = DesignMode.Local, LocalModel = "llama3.1:8b" };
            a.Transport = transport;
            var item = a.Publish(new NewsItem { Kind = NewsKind.PressRelease, Headline = "Test", Body = "Canned body with facts." });
            Assert.That(item.Source, Is.EqualTo("AI"));
            Assert.That(item.Body, Does.Contain("snacks"));
            Assert.That(transport.Sent[0].Url, Does.Contain("localhost"));
            Assert.That(transport.Sent[0].Body, Does.Contain("Canned body with facts"));
            Assert.That(a.Costs.CallsOn(a.CurrentDay), Is.EqualTo(0), "local calls are free");

            Agency b = Staffed(new FakeClock(), new ListLogger());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), b);
            Assert.That(b.News.Get(item.Id).Body, Does.Contain("snacks"));
            Assert.That(b.News.Get(item.Id).Source, Is.EqualTo("AI"));
        }
    }
}
