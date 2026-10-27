using System.Collections.Generic;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Department heads (M2 decision, built 2026-10-27): Management-track kerbals run Finances, Science, PR and Operations.</summary>
    public class DepartmentTests
    {
        private static Agency Staffed(FakeClock clock)
        {
            Agency a = Make.Agency(clock, new ListLogger());
            a.SyncRoster(new List<CrewSnapshot>
            {
                Make.Snapshot("Jeb Kerman", CrewRole.Pilot, 3),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer, 4),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist, 3),
                Make.Snapshot("Val Kerman", CrewRole.Pilot, 2)
            });
            a.Tick();
            a.Ledger.SetBalance(500000);
            return a;
        }

        private static void MakeManager(Agency a, string id)
        {
            CrewProfile c = a.Crew.Get(id);
            c.Track = CareerTrack.Management;
            c.RankId = "ProgramManager";
        }

        [Test]
        public void OnlyManagementTrackCanHoldAPost_AndHeadsLeaveTheCrewPool()
        {
            Agency a = Staffed(new FakeClock());
            string msg;
            Assert.That(a.Appoint(Department.Finances, "Jeb Kerman", out msg), Is.False);
            Assert.That(msg, Does.Contain("Management"));
            MakeManager(a, "Bill Kerman");
            Assert.That(a.Appoint(Department.Finances, "Bill Kerman", out msg), Is.True, msg);
            Assert.That(a.HeadOf(Department.Finances).Name, Is.EqualTo("Bill Kerman"));
            Assert.That(a.IsDepartmentHead("Bill Kerman"), Is.True);
            Assert.That(a.AvailableFor(CrewRole.Engineer), Has.None.Matches<CrewProfile>(c => c.Id == "Bill Kerman"), "a head is not assignable");
            Assert.That(a.DepartmentRung(Department.Finances), Is.EqualTo(1.25).Within(0.001), "Program Manager rung 1 plus a quarter for the fourth star");
            Assert.That(a.SalaryFactor(), Is.LessThan(1.0));
            Assert.That(a.GrantFactor(), Is.GreaterThan(1.0));
            Assert.That(a.BuildTimeFactor(), Is.EqualTo(1.0), "Operations is vacant");

            // Moving to another post vacates the first.
            Assert.That(a.Appoint(Department.Operations, "Bill Kerman", out msg), Is.True, msg);
            Assert.That(a.HeadOf(Department.Finances), Is.Null);
            Assert.That(a.BuildTimeFactor(), Is.LessThan(1.0));

            // Leaving the agency vacates the post.
            Assert.That(a.Fire("Bill Kerman", out msg), Is.True, msg);
            Assert.That(a.HeadOf(Department.Operations), Is.Null);
        }

        [Test]
        public void HeadsShapeSalariesGrantsAndBuildTimes_AndSurviveSaves()
        {
            var clock = new FakeClock();
            Agency a = Staffed(clock);
            MakeManager(a, "Bob Kerman");
            string msg;
            Assert.That(a.Appoint(Department.Operations, "Bob Kerman", out msg), Is.True, msg);
            Project p = a.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, null, out msg);
            Assert.That(p, Is.Not.Null, msg);
            Agency b = Staffed(new FakeClock());
            Project q = b.CreateProject("Hop", "SuborbitalHop", null, new[] { "Jeb Kerman" }, null, null, null, out msg);
            Assert.That(p.BuildDays + p.DesignDays, Is.LessThanOrEqualTo(q.BuildDays + q.DesignDays), "Operations trims design and build time");

            Agency c = Staffed(new FakeClock());
            AgencyStateSerializer.Read(AgencyStateSerializer.Write(a), c);
            Assert.That(c.HeadOf(Department.Operations), Is.Not.Null);
            Assert.That(c.HeadOf(Department.Operations).Name, Is.EqualTo("Bob Kerman"));

            // Finances cuts the payroll at year end.
            MakeManager(a, "Val Kerman");
            Assert.That(a.Appoint(Department.Finances, "Val Kerman", out msg), Is.True, msg);
            List<string> notices = a.ForceYearEnd();
            Assert.That(notices, Has.Some.Contains("trimmed the payroll"));
            Assert.That(notices, Has.Some.Contains("Head of Finances"));
        }
    }
}
