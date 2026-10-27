using System;
using System.Collections.Generic;

namespace KSA.Core.Review
{
    public enum ReviewBand { Excellent = 0, Good = 1, Flat = 2, Lean = 3, Crisis = 4 }

    /// <summary>The government's year-end verdict (Jira KSA-28).</summary>
    public sealed class ReviewResult
    {
        public int Year { get; set; }
        public int GoalsIssued { get; set; }
        public int GoalsMet { get; set; }
        public List<string> MetGoals { get; } = new List<string>();
        public List<string> MissedGoals { get; } = new List<string>();
        public int Deaths { get; set; }
        public ReviewBand Band { get; set; }
        public double BudgetChangePercent { get; set; }
        public double NextGrant { get; set; }
        public double PrestigeBefore { get; set; }
        public double PrestigeAfter { get; set; }
        public bool ProbationBefore { get; set; }
        public bool ProbationAfter { get; set; }
        public bool DirectorReplaced { get; set; }
        public bool HiringFreeze { get; set; }
        public int Layoffs { get; set; }
        public List<string> Events { get; } = new List<string>();

        public string BandText
        {
            get
            {
                switch (Band)
                {
                    case ReviewBand.Excellent: return "Excellent";
                    case ReviewBand.Good: return "Good";
                    case ReviewBand.Flat: return "Adequate";
                    case ReviewBand.Lean: return "Lean Year";
                    case ReviewBand.Crisis: return "Crisis Year";
                    default: return Band.ToString();
                }
            }
        }
    }

    public static class GovernmentReview
    {
        /// <summary>
        /// Scores the year. Deterministic: same inputs, same verdict. Budget percentages follow GDD 4.5.
        /// </summary>
        public static ReviewResult Evaluate(int year, int goalsIssued, int goalsMet, int deaths,
                                            bool probationBefore, double prestigeBefore, double baseGrant,
                                            int activeCrew, double crisisLayoffFraction)
        {
            var r = new ReviewResult
            {
                Year = year,
                GoalsIssued = goalsIssued,
                GoalsMet = goalsMet,
                Deaths = deaths,
                ProbationBefore = probationBefore,
                PrestigeBefore = prestigeBefore
            };

            double ratio = goalsIssued <= 0 ? 1.0 : (double)goalsMet / goalsIssued;
            ReviewBand band;
            if (ratio >= 0.999) band = ReviewBand.Excellent;
            else if (ratio >= 0.66) band = ReviewBand.Good;
            else if (ratio >= 0.5) band = ReviewBand.Flat;
            else if (ratio > 0) band = ReviewBand.Lean;
            else band = ReviewBand.Crisis;

            double prestigeDelta;
            switch (band)
            {
                case ReviewBand.Excellent: r.BudgetChangePercent = 25; prestigeDelta = 10; break;
                case ReviewBand.Good: r.BudgetChangePercent = 8; prestigeDelta = 4; break;
                case ReviewBand.Flat: r.BudgetChangePercent = 0; prestigeDelta = 0; break;
                case ReviewBand.Lean: r.BudgetChangePercent = -20; prestigeDelta = -8; break;
                default: r.BudgetChangePercent = -40; prestigeDelta = -15; break;
            }

            if (deaths > 0)
            {
                // A fatal year drops one band and costs extra prestige.
                if (band < ReviewBand.Crisis) band = (ReviewBand)((int)band + 1);
                prestigeDelta -= 5;
                r.Events.Add(deaths == 1 ? "A crew member was lost this year." : deaths + " crew members were lost this year.");
                switch (band)
                {
                    case ReviewBand.Good: r.BudgetChangePercent = 8; break;
                    case ReviewBand.Flat: r.BudgetChangePercent = 0; break;
                    case ReviewBand.Lean: r.BudgetChangePercent = -20; break;
                    case ReviewBand.Crisis: r.BudgetChangePercent = -40; break;
                }
            }
            r.Band = band;

            r.PrestigeAfter = Clamp(prestigeBefore + prestigeDelta, 0, 100);
            double prestigeFactor = 0.75 + r.PrestigeAfter / 200.0; // 0.75 .. 1.25
            r.NextGrant = Math.Round(baseGrant * (1 + r.BudgetChangePercent / 100.0) * prestigeFactor);

            switch (band)
            {
                case ReviewBand.Excellent:
                    r.Events.Add("Every program goal met. The government is delighted; expect better applicants.");
                    break;
                case ReviewBand.Good:
                    r.Events.Add("Most program goals met. Budget increased.");
                    break;
                case ReviewBand.Flat:
                    r.Events.Add("Half the program goals met. Budget held flat.");
                    break;
                case ReviewBand.Lean:
                    r.HiringFreeze = true;
                    r.Events.Add("Most program goals missed. Budget cut; hiring frozen for the year.");
                    r.Events.Add("Agency morale has dropped.");
                    break;
                case ReviewBand.Crisis:
                    r.HiringFreeze = true;
                    r.Layoffs = Math.Max(1, (int)Math.Ceiling(activeCrew * crisisLayoffFraction));
                    r.Events.Add("Program goals missed across the board. Budget slashed; hiring frozen.");
                    r.Events.Add("The government demands " + r.Layoffs + " layoff" + (r.Layoffs == 1 ? "" : "s") + ". The Director chooses who goes.");
                    r.Events.Add("A public hearing has been scheduled.");
                    break;
            }

            // Probation (GDD 14.1 decision 11): a Crisis puts the Director on probation; a second bad year replaces them.
            bool badYear = band >= ReviewBand.Lean;
            if (band == ReviewBand.Crisis)
            {
                if (probationBefore)
                {
                    r.DirectorReplaced = true;
                    r.ProbationAfter = false;
                    r.Events.Add("The Director has been replaced. The agency continues under new leadership.");
                }
                else
                {
                    r.ProbationAfter = true;
                    r.Events.Add("The Director is on probation. One more year like this ends their tenure.");
                }
            }
            else if (probationBefore)
            {
                if (badYear)
                {
                    r.DirectorReplaced = true;
                    r.ProbationAfter = false;
                    r.Events.Add("A second poor year while on probation. The Director has been replaced.");
                }
                else
                {
                    r.ProbationAfter = false;
                    r.Events.Add("Probation lifted.");
                }
            }
            return r;
        }

        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
