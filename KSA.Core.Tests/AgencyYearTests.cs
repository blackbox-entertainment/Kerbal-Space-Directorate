using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Finance;
using KSA.Core.Goals;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using KSA.Core.Review;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>Fiscal years, reviews, recruiting, morale, departures, manual medals, and v2 persistence (M2).</summary>
    [TestFixture]
    public class AgencyYearTests
    {
        private FakeClock _clock;
        private ListLogger _log;
        private Agency _agency;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            _log = new ListLogger();
            _agency = Make.Agency(_clock, _log);
        }

        private void SyncStarters()
        {
            _agency.SyncRoster(new[]
            {
                Make.Snapshot("Jebediah Kerman", CrewRole.Pilot),
                Make.Snapshot("Bill Kerman", CrewRole.Engineer),
                Make.Snapshot("Bob Kerman", CrewRole.Scientist),
                Make.Snapshot("Valentina Kerman", CrewRole.Pilot),
            });
        }

        [Test]
        public void FirstTick_OpensTheBooks()
        {
            List<string> notices = _agency.Tick();

            Assert.That(_agency.State.Started, Is.True);
            Assert.That(_agency.State.CurrentYear, Is.EqualTo(1));
            Assert.That(_agency.Ledger.Balance, Is.EqualTo(50000 + 150000), "founding balance plus year-1 grant at prestige 50");
            Assert.That(_agency.ActiveGoals.Count, Is.EqualTo(3));
            int conditional = 0;
            foreach (GoalState g in _agency.ActiveGoals)
                if (_agency.Catalogs.Goals.Get(g.GoalId).Tier == GoalTier.Conditional) conditional++;
            Assert.That(conditional, Is.EqualTo(1));
            Assert.That(notices, Has.Some.Contains("Fiscal year 1 opens"));
            Assert.That(_agency.Tick(), Is.Empty, "second tick in the same quarter does nothing");
        }

        [Test]
        public void YearEnd_ChargesSalaries_ReviewsAndIssuesNewGoals()
        {
            SyncStarters();
            _agency.Tick();
            double balanceBefore = _agency.Ledger.Balance;

            _clock.AdvanceDays(426);
            List<string> notices = _agency.Tick();

            Assert.That(_agency.State.CurrentYear, Is.EqualTo(2));
            Assert.That(_agency.Reviews.Count, Is.EqualTo(1));
            ReviewResult r = _agency.Reviews[0];
            Assert.That(r.Year, Is.EqualTo(1));
            Assert.That(r.GoalsIssued, Is.EqualTo(3));
            Assert.That(_agency.Ledger.TotalFor(1, LedgerCategory.Salary), Is.EqualTo(-(8000 + 7000 + 7000 + 8000)));
            Assert.That(_agency.Ledger.TotalFor(2, LedgerCategory.Grant), Is.EqualTo(r.NextGrant));
            Assert.That(_agency.ActiveGoals.Count, Is.EqualTo(3), "a fresh set of goals for year 2");
            Assert.That(_agency.GoalHistory.Count, Is.EqualTo(3));
            Assert.That(notices, Has.Some.Contains("Government Review, year 1"));
            Assert.That(_agency.Crew.Get("Bill Kerman").Morale, Is.LessThan(70), "idle all year, and a poor review");
            Assert.That(_agency.Crew.Get("Jebediah Kerman").Morale, Is.LessThan(_agency.Crew.Get("Bill Kerman").Morale), "test pilots passed over for selection lose extra morale");
        }

        [Test]
        public void YearEnd_AllGoalsMet_IsExcellent()
        {
            SyncStarters();
            _agency.Tick();
            _agency.AddAchievements("Jebediah Kerman", new[] { "Suborbit:Kerbin", "Orbit:Kerbin", "EVA:Kerbin", "Recover:-", "Flyby:Mun", "Orbit:Mun" });
            for (int i = 0; i < 3; i++)
            {
                _agency.BeginMission(new[] { "Jebediah Kerman" }, "Hop " + i);
                _clock.AdvanceDays(1);
                _agency.EndMission("Jebediah Kerman", MissionOutcome.Success);
            }

            _agency.ForceYearEnd();

            ReviewResult r = _agency.Reviews[0];
            Assert.That(r.GoalsMet, Is.EqualTo(r.GoalsIssued), string.Join(", ", r.MissedGoals));
            Assert.That(r.Band, Is.EqualTo(ReviewBand.Excellent));
            Assert.That(_agency.State.Prestige, Is.EqualTo(60));
            Assert.That(_agency.State.HiringFreeze, Is.False);
            Assert.That(_agency.Crew.Get("Jebediah Kerman").RankId, Is.EqualTo("TestPilot"), "still needs the Director's selection");
        }

        [Test]
        public void YearEnd_NoGoalsMet_IsCrisis_WithLayoffsAndProbation()
        {
            SyncStarters();
            _agency.Tick();
            // Make sure the conditional goal fails too: lose someone.
            _agency.BeginMission(new[] { "Valentina Kerman" }, "Doomed");
            _agency.RecordDeath("Valentina Kerman", "Explosion");
            // and spend into the red so UnderBudget fails as well
            _agency.Ledger.Withdraw(_agency.Ledger.Balance, LedgerCategory.Other, "waste", 1, 1, true);

            _agency.ForceYearEnd();

            ReviewResult r = _agency.Reviews[0];
            Assert.That(r.Band, Is.EqualTo(ReviewBand.Crisis), string.Join(", ", r.MetGoals));
            Assert.That(_agency.State.Probation, Is.True);
            Assert.That(_agency.State.HiringFreeze, Is.True);
            Assert.That(_agency.State.PendingLayoffs, Is.EqualTo(1), "25% of 3 active crew, rounded up");
            string why;
            Assert.That(_agency.HiringAllowed(out why), Is.False);

            string msg;
            Assert.That(_agency.Fire("Bill Kerman", out msg), Is.True);
            Assert.That(_agency.State.PendingLayoffs, Is.EqualTo(0));
            Assert.That(_agency.Crew.Get("Bill Kerman").DepartureReason, Is.EqualTo(DepartureReason.Fired));
        }

        [Test]
        public void Retirement_AtYearEnd_RaisesDeparture()
        {
            SyncStarters();
            _agency.Tick();
            CrewProfile jeb = _agency.Crew.Get("Jebediah Kerman");
            jeb.BornDay = -56 * 426;
            CrewProfile departed = null;
            DepartureReason reason = DepartureReason.None;
            _agency.CrewDeparted += (p, r) => { departed = p; reason = r; };

            _agency.ForceYearEnd();

            Assert.That(departed, Is.SameAs(jeb));
            Assert.That(reason, Is.EqualTo(DepartureReason.Retired));
            Assert.That(jeb.Departed, Is.True);
            Assert.That(jeb.DepartedDay, Is.GreaterThanOrEqualTo(0));
            Assert.That(_agency.Crew.Count, Is.EqualTo(4), "kept for the hall of fame");
        }

        [Test]
        public void LowMorale_ForTwoYearEnds_MeansResignation()
        {
            SyncStarters();
            _agency.Tick();
            CrewProfile bob = _agency.Crew.Get("Bob Kerman");
            bob.Morale = 0;

            _agency.ForceYearEnd();
            Assert.That(bob.Departed, Is.False);
            Assert.That(bob.LowMoraleYears, Is.EqualTo(1));
            bob.Morale = 0;
            _agency.ForceYearEnd();
            Assert.That(bob.Departed, Is.True);
            Assert.That(bob.DepartureReason, Is.EqualTo(DepartureReason.Quit));
        }

        [Test]
        public void Recruiting_DriveInterviewHire()
        {
            SyncStarters();
            _agency.Tick();
            double before = _agency.Ledger.Balance;
            int baseline = _agency.Applicants.Applicants.Count; // a quarterly walk-in may already be waiting
            string msg;

            Assert.That(_agency.RunDrive("University", out msg), Is.True, msg);
            Assert.That(_agency.Applicants.Applicants.Count, Is.EqualTo(baseline + 4));
            Assert.That(_agency.Ledger.Balance, Is.EqualTo(before - 12000));

            Applicant a = _agency.Applicants.Applicants[baseline];
            Assert.That(_agency.Interview(a.Id, out msg), Is.True);
            Assert.That(a.IsRevealed(Applicant.RevealStars), Is.True);

            Applicant hiredApplicant = null;
            CrewProfile hiredProfile = null;
            _agency.ApplicantHired += (ap, p) => { hiredApplicant = ap; hiredProfile = p; };
            Assert.That(_agency.Hire(a.Id, out msg), Is.True, msg);
            Assert.That(hiredApplicant, Is.SameAs(a));
            Assert.That(hiredProfile.Name, Is.EqualTo(a.Name));
            Assert.That(hiredProfile.RankId, Is.AnyOf("JuniorEngineer", "ResearchAssistant"));
            Assert.That(hiredProfile.PersonalityTags, Is.EqualTo(a.Tags));
            Assert.That(_agency.Ledger.Balance, Is.EqualTo(before - 12000 - 4000));
            Assert.That(_agency.Applicants.Applicants.Count, Is.EqualTo(baseline + 3));

            _agency.State.HiringFreeze = true;
            Assert.That(_agency.Hire(_agency.Applicants.Applicants[0].Id, out msg), Is.False);
            Assert.That(msg, Does.Contain("frozen"));
        }

        [Test]
        public void Recruiting_RejectAndReturn_AndQuarterlyWalkIns()
        {
            _agency.Tick();
            string msg;
            _agency.RunDrive("WalkIns", out msg);
            int count = _agency.Applicants.Applicants.Count;
            Applicant a = _agency.Applicants.Applicants[0];
            Assert.That(_agency.Reject(a.Id, out msg), Is.True);
            Assert.That(_agency.Applicants.Applicants.Count, Is.EqualTo(count - 1));

            _clock.AdvanceDays(110); // next quarter
            List<string> notices = _agency.Tick();
            Assert.That(notices, Has.Some.Contains("Walk-in applicant"));

            _clock.AdvanceDays(330); // past the rejected applicant's return day and the year end
            notices = _agency.Tick();
            Assert.That(notices, Has.Some.Contains("reapplied"));
        }

        [Test]
        public void ManualMedal_RespectsYearlyLimit()
        {
            SyncStarters();
            _agency.Tick();
            string msg;
            Assert.That(_agency.ManualAwardsRemaining("ChiefsCommendation"), Is.EqualTo(2));
            Assert.That(_agency.AwardManual("Jebediah Kerman", "ChiefsCommendation", "Nice flying", out msg), Is.True, msg);
            Assert.That(_agency.AwardManual("Jebediah Kerman", "ChiefsCommendation", "again", out msg), Is.False, "already holds it");
            Assert.That(_agency.AwardManual("Bill Kerman", "ChiefsCommendation", "", out msg), Is.True);
            Assert.That(_agency.AwardManual("Bob Kerman", "ChiefsCommendation", "", out msg), Is.False);
            Assert.That(msg, Does.Contain("No more"));
            Assert.That(_agency.AwardManual("Bob Kerman", "OrbitalPioneer", "", out msg), Is.False, "not a Director-awarded medal");
            Assert.That(_agency.Crew.Get("Jebediah Kerman").Morale, Is.EqualTo(82));

            _agency.ForceYearEnd();
            Assert.That(_agency.ManualAwardsRemaining("ChiefsCommendation"), Is.EqualTo(2), "quota resets each year");
        }

        [Test]
        public void SaveAndLoad_V2_RoundTripsAgencyState()
        {
            SyncStarters();
            _agency.Tick();
            string msg;
            _agency.RunDrive("AirCorps", out msg);
            _agency.Interview(_agency.Applicants.Applicants[0].Id, out msg);
            _agency.Reject(_agency.Applicants.Applicants[1].Id, out msg);
            _agency.AwardManual("Jebediah Kerman", "ChiefsCommendation", "x", out msg);
            _agency.Fire("Bob Kerman", out msg);
            _clock.AdvanceDays(426);
            _agency.Tick();

            var store = new MemoryPersistence();
            _agency.Save(store);
            string text = DataNodeText.Write(store.Stored);

            Agency reloaded = Make.Agency(_clock, _log);
            Assert.That(reloaded.Load(new MemoryPersistence { Stored = DataNodeText.ParseAll(text)[0] }), Is.True);

            Assert.That(reloaded.State.Started, Is.True);
            Assert.That(reloaded.State.CurrentYear, Is.EqualTo(2));
            Assert.That(reloaded.State.Prestige, Is.EqualTo(_agency.State.Prestige));
            Assert.That(reloaded.State.HiringFreeze, Is.EqualTo(_agency.State.HiringFreeze));
            Assert.That(reloaded.Ledger.Balance, Is.EqualTo(_agency.Ledger.Balance));
            Assert.That(reloaded.Ledger.Entries.Count, Is.EqualTo(_agency.Ledger.Entries.Count));
            Assert.That(reloaded.ActiveGoals.Count, Is.EqualTo(_agency.ActiveGoals.Count));
            Assert.That(reloaded.GoalHistory.Count, Is.EqualTo(_agency.GoalHistory.Count));
            Assert.That(reloaded.Reviews.Count, Is.EqualTo(1));
            Assert.That(reloaded.Reviews[0].Band, Is.EqualTo(_agency.Reviews[0].Band));
            Assert.That(reloaded.Reviews[0].Events.Count, Is.EqualTo(_agency.Reviews[0].Events.Count));
            Assert.That(reloaded.Applicants.Applicants.Count, Is.EqualTo(_agency.Applicants.Applicants.Count));
            Assert.That(reloaded.Applicants.Applicants[0].IsRevealed(Applicant.RevealStars), Is.True);
            Assert.That(reloaded.Applicants.Returning.Count + reloaded.Applicants.Applicants.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(reloaded.Crew.Get("Bob Kerman").DepartureReason, Is.EqualTo(DepartureReason.Fired));
            Assert.That(reloaded.Crew.Get("Jebediah Kerman").Record.HasMedal("ChiefsCommendation"), Is.True);
            Assert.That(reloaded.Tick(), Is.Empty, "nothing to catch up on after a faithful reload");
        }

        [Test]
        public void Load_V1Save_StartsBooksOnNextTick()
        {
            var v1 = new DataNode("KSA_AGENCY").AddValue("version", 1).AddValue("nextMissionNumber", 3);
            v1.AddChild("CREW").AddValue("id", "Jebediah Kerman").AddValue("name", "Jebediah Kerman").AddValue("role", "Pilot")
              .AddValue("track", "Flight").AddValue("rank", "AstronautCandidate").AddValue("status", "Dead");

            Assert.That(_agency.Load(new MemoryPersistence { Stored = v1 }), Is.True);
            Assert.That(_agency.State.Started, Is.False);
            Assert.That(_agency.Crew.Get("Jebediah Kerman").DepartureReason, Is.EqualTo(DepartureReason.Died), "dead crew from v1 become departed");

            _clock.AdvanceDays(500);
            _agency.Tick();
            Assert.That(_agency.State.Started, Is.True);
            Assert.That(_agency.State.CurrentYear, Is.EqualTo(2), "starts in the calendar's current year, no back-dated reviews");
            Assert.That(_agency.Reviews, Is.Empty);
            Assert.That(_agency.ActiveGoals.Count, Is.EqualTo(3));
        }
    }
}
