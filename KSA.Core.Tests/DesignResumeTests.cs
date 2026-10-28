using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class DesignResumeTests
    {
        [Test]
        public void PendingDesignRestartsAfterReload()
        {
            var clock = new FakeClock();
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2) });
            a.Tick();
            a.Ledger.SetBalance(400000);
            string msg;
            Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, MissionMode.Simulate, out msg);
            Assert.That(p, Is.Not.Null, msg);
            // Pretend the game was saved while the team was still working.
            p.Design = new DesignRecord { Source = DesignSource.Ai, Verdict = DesignVerdict.Pending, Model = "m" };
            KSA.Core.Data.DataNode saved = AgencyStateSerializer.Write(a);

            Agency b = Make.Agency(new FakeClock(), new ListLogger());
            b.SyncRoster(new List<CrewSnapshot> { Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3), Make.Snapshot("Bill Kerman", CrewRole.Engineer, 2) });
            var transport = new ScriptedTransport { Fallback = new LlmResponse { Ok = false, Status = 500, Body = "{}" } };
            b.Ai = new AiSettings { DesignMode = DesignMode.Api, ApiKey = "k", DesignModel = "m" };
            b.Transport = transport;
            b.Parts = TestParts.Catalog();
            AgencyStateSerializer.Read(saved, b);
            Project q = b.GetProject(p.Id);
            Assert.That(q.Design.Verdict, Is.EqualTo(DesignVerdict.Pending), "not written off by the load");
            int sent = transport.Sent.Count;
            b.Tick();
            Assert.That(transport.Sent.Count, Is.GreaterThan(sent), "a new design session asked the model again");
            Assert.That(q.Notes, Has.Some.Contains("resumed"));
        }
    }
}
