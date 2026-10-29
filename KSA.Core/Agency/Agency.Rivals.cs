using System;
using System.Collections.Generic;
using KSA.Core.Analytics;
using KSA.Core.Finance;
using KSA.Core.Missions;
using KSA.Core.Narrative;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Review;
using KSA.Core.Rivals;

namespace KSA.Core
{
    /// <summary>Who claimed a milestone first, and when.</summary>
    public sealed class FirstClaim
    {
        public string Achievement { get; set; }
        /// <summary>"player" or a rival id.</summary>
        public string ClaimedBy { get; set; }
        public int Year { get; set; }

        public FirstClaim() { Achievement = string.Empty; ClaimedBy = string.Empty; }
    }

    /// <summary>
    /// Rival agencies and year-over-year analytics (decision 19, 2026-10-27). Rivals are simulated once per fiscal
    /// year at the Government Review; the player's year is frozen into a YearSummary at the same moment.
    /// </summary>
    public sealed partial class Agency
    {
        public const string PlayerId = "player";

        public List<RivalAgency> Rivals { get; } = new List<RivalAgency>();
        public List<YearSummary> YearHistory { get; } = new List<YearSummary>();
        public List<FirstClaim> Firsts { get; } = new List<FirstClaim>();

        /// <summary>A rival did something newsworthy (headline text).</summary>
        public event Action<string> RivalNews;

        public RivalDefinition RivalDefinition(string id)
        {
            foreach (RivalDefinition d in Catalogs.Rivals) if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        private void EnsureRivals()
        {
            if (!Settings.RivalsEnabled) return;
            foreach (RivalDefinition d in Catalogs.Rivals)
            {
                bool present = false;
                foreach (RivalAgency r in Rivals) if (string.Equals(r.Id, d.Id, StringComparison.OrdinalIgnoreCase)) { present = true; break; }
                if (!present) Rivals.Add(new RivalAgency { Id = d.Id, Prestige = d.StartPrestige });
            }
        }

        public FirstClaim FirstFor(string achievement)
        {
            foreach (FirstClaim f in Firsts) if (string.Equals(f.Achievement, achievement, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /// <summary>Records the player's claim on any ladder milestone they now hold and nobody claimed before.</summary>
        internal void ClaimPlayerFirsts(List<string> notices)
        {
            foreach (MilestoneLadder.Rung rung in MilestoneLadder.Build(Catalogs.Missions))
            {
                if (FirstFor(rung.Achievement) != null) continue;
                if (!HasAgencyAchievement(rung.Achievement)) continue;
                Firsts.Add(new FirstClaim { Achievement = rung.Achievement, ClaimedBy = PlayerId, Year = CurrentYear });
                Push(notices, AgencyName + " is first to " + MilestoneLadder.Describe(rung.Achievement));
            }
        }

        /// <summary>Year end: any of the closing year's rival flights not yet flown go now, so every year is complete.</summary>
        private void TickRivals(int year, List<string> notices)
        {
            FlyRivalMissions(year, int.MaxValue, notices);
        }

        /// <summary>
        /// Rivals fly through the year (2026-10-28): each one's flights are spread across the fiscal year and go as their
        /// day comes, with firsts, notices and wire stories at the time. Called every tick with today's day of the year.
        /// </summary>
        private void TickRivalMissions(List<string> notices)
        {
            if (!State.Started) return;
            FlyRivalMissions(CurrentYear, DayOfYear, notices);
        }

        private void FlyRivalMissions(int year, int dayOfYear, List<string> notices)
        {
            EnsureRivals();
            if (!Settings.RivalsEnabled || Rivals.Count == 0) return;
            List<MilestoneLadder.Rung> ladder = null;
            int dpy = Math.Max(10, Calendar.DaysPerYear);
            for (int i = 0; i < Rivals.Count; i++)
            {
                RivalAgency r = Rivals[i];
                RivalDefinition def = RivalDefinition(r.Id);
                if (def == null) continue;
                if (r.FlownYear != year)
                {
                    if (r.FlownYear > year) continue; // already past this year
                    r.FlownYear = year; r.FlownCount = 0;
                }
                int n = RivalSimulator.MissionsPerYear(def);
                while (r.FlownCount < n && RivalSimulator.MissionDay(r.FlownCount, n, dpy, i) <= dayOfYear)
                {
                    if (ladder == null) ladder = MilestoneLadder.Build(Catalogs.Missions);
                    var milestones = new List<string>();
                    var headlines = new List<string>();
                    RivalSimulator.RunMission(def, r, ladder, r.YearRecord(year), Rng, milestones, headlines);
                    r.FlownCount++;
                    foreach (string m in milestones)
                    {
                        if (FirstFor(m) != null) continue;
                        Firsts.Add(new FirstClaim { Achievement = m, ClaimedBy = r.Id, Year = year });
                        Push(notices, def.Name + " beat us to " + MilestoneLadder.Describe(m));
                    }
                    foreach (string h in headlines)
                    {
                        Action<string> handler = RivalNews;
                        if (handler != null) handler(h);
                        Publish(new NewsItem { Kind = NewsKind.Dispatch, Byline = "Wire service", Related = "rival:" + r.Id, Headline = h, Body = h + (string.IsNullOrEmpty(def.Blurb) ? string.Empty : " " + def.Blurb) }, false);
                    }
                }
            }
        }

        /// <summary>For the Analytics window: when a rival flies next this year, or null when its year is done.</summary>
        public string RivalNextFlight(RivalAgency r)
        {
            int i = Rivals.IndexOf(r);
            RivalDefinition def = r != null ? RivalDefinition(r.Id) : null;
            if (def == null || i < 0) return null;
            int n = RivalSimulator.MissionsPerYear(def);
            int flown = r.FlownYear == CurrentYear ? r.FlownCount : 0;
            if (flown >= n) return null;
            int day = RivalSimulator.MissionDay(flown, n, Math.Max(10, Calendar.DaysPerYear), i);
            int wait = Math.Max(0, day - DayOfYear);
            return "next flight in about " + wait + " day" + (wait == 1 ? "" : "s");
        }

        // ------------------------------------------------------------------ analytics

        /// <summary>The player's figures for a year. Closed years come from history; the running year is computed live.</summary>
        public YearSummary SummaryFor(int year)
        {
            foreach (YearSummary s in YearHistory) if (s.Year == year && s.Closed) return s;
            return BuildYearSummary(year, false, null);
        }

        /// <summary>Every year from the founding year to the current one, oldest first.</summary>
        public List<YearSummary> AllYears()
        {
            var list = new List<YearSummary>();
            foreach (YearSummary s in YearHistory) if (s.Closed) list.Add(s);
            int current = CurrentYear;
            if (!list.Exists(s => s.Year == current)) list.Add(BuildYearSummary(current, false, null));
            list.Sort((a, b) => a.Year.CompareTo(b.Year));
            return list;
        }

        private YearSummary BuildYearSummary(int year, bool closed, ReviewResult review)
        {
            var s = new YearSummary { Year = year, Closed = closed };
            s.Grant = Ledger.TotalFor(year, LedgerCategory.Grant);
            s.Refunds = Ledger.TotalFor(year, LedgerCategory.Cancellation);
            s.Salaries = -Ledger.TotalFor(year, LedgerCategory.Salary);
            s.Recruiting = -(Ledger.TotalFor(year, LedgerCategory.Recruiting) + Ledger.TotalFor(year, LedgerCategory.Hiring));
            double other = Ledger.TotalFor(year, LedgerCategory.Other);
            if (other < 0) s.Projects = -other; else s.OtherIncome = other;
            s.ClosingBalance = Ledger.Balance;
            double later = 0;
            foreach (LedgerEntry e in Ledger.Entries) if (e.Year > year) later += e.Amount;
            s.ClosingBalance -= later;
            s.OpeningBalance = s.ClosingBalance - s.Net;
            s.Prestige = review != null ? review.PrestigeAfter : State.Prestige;
            int crew = 0;
            foreach (CrewProfile p in Crew.Active) crew++;
            s.Crew = crew;
            foreach (MissionReport r in Reports)
            {
                if (Calendar.YearOf(r.Day) != year) continue;
                s.Missions++;
                if (r.IsSuccess) s.Successes++;
            }
            // Deaths come from the crew records, so a kerbal killed on a Director-flown stock vessel counts too (as the review does).
            foreach (CrewProfile p in Crew.All)
                if (p.Departed && p.DepartureReason == DepartureReason.Died && Calendar.YearOf(p.DepartedDay) == year) s.Deaths++;
            if (review != null) { s.GoalsIssued = review.GoalsIssued; s.GoalsMet = review.GoalsMet; s.Band = review.BandText; }
            else { s.GoalsIssued = ActiveGoals.Count; foreach (Goals.GoalState gs in ActiveGoals) if (IsGoalCurrentlyMet(gs)) s.GoalsMet++; s.Band = "in progress"; }
            foreach (FirstClaim f in Firsts) if (f.ClaimedBy == PlayerId && f.Year == year) s.Milestones.Add(f.Achievement);
            return s;
        }

        private void RecordYear(int year, ReviewResult review)
        {
            YearHistory.RemoveAll(s => s.Year == year);
            YearHistory.Add(BuildYearSummary(year, true, review));
            YearHistory.Sort((a, b) => a.Year.CompareTo(b.Year));
        }

        /// <summary>The league table, best first. The player row is always present.</summary>
        public List<LeagueRow> League()
        {
            EnsureRivals();
            var rows = new List<LeagueRow>();
            var player = new LeagueRow { Id = PlayerId, Name = AgencyName, Style = "Director-led", IsPlayer = true, Prestige = State.Prestige, Budget = Settings.BaseGrant };
            // Lifetime totals from the frozen years plus the running one (Reports is capped, rivals' counters are not).
            foreach (YearSummary ys in AllYears()) { player.Missions += ys.Missions; player.Successes += ys.Successes; }
            foreach (CrewProfile c in Crew.All) if (c.Departed && c.DepartureReason == DepartureReason.Died) player.Deaths++;
            foreach (MilestoneLadder.Rung rung in MilestoneLadder.Build(Catalogs.Missions))
                if (HasAgencyAchievement(rung.Achievement)) { player.Milestones++; player.LatestMilestone = MilestoneLadder.Describe(rung.Achievement); }
            foreach (FirstClaim f in Firsts) if (f.ClaimedBy == PlayerId) player.Firsts++;
            rows.Add(player);
            if (Settings.RivalsEnabled) foreach (RivalAgency r in Rivals)
            {
                RivalDefinition def = RivalDefinition(r.Id);
                var row = new LeagueRow
                {
                    Id = r.Id, Name = def != null ? def.Name : r.Id, Style = def != null ? def.Style : string.Empty, Prestige = r.Prestige, Budget = def != null ? def.Budget : 0,
                    Missions = r.Missions, Successes = r.Successes, Deaths = r.Deaths, Milestones = r.Milestones.Count,
                    LatestMilestone = r.Milestones.Count > 0 ? MilestoneLadder.Describe(r.Milestones[r.Milestones.Count - 1]) : string.Empty
                };
                foreach (FirstClaim f in Firsts) if (f.ClaimedBy == r.Id) row.Firsts++;
                rows.Add(row);
            }
            rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            return rows;
        }
    }
}
