using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Analytics
{
    /// <summary>One fiscal year of the player's agency, frozen at year end (the current year is computed live).</summary>
    public sealed class YearSummary
    {
        public int Year { get; set; }
        public bool Closed { get; set; }
        public double Grant { get; set; }
        public double OtherIncome { get; set; }
        public double Salaries { get; set; }
        public double Recruiting { get; set; }
        public double Projects { get; set; }
        public double Refunds { get; set; }
        public double OpeningBalance { get; set; }
        public double ClosingBalance { get; set; }
        public double Prestige { get; set; }
        public int Crew { get; set; }
        public int Missions { get; set; }
        public int Successes { get; set; }
        public int Deaths { get; set; }
        public int GoalsIssued { get; set; }
        public int GoalsMet { get; set; }
        public string Band { get; set; }
        public List<string> Milestones { get; } = new List<string>();

        public YearSummary() { Band = string.Empty; }

        public double Income { get { return Grant + OtherIncome + Refunds; } }
        public double Spend { get { return Salaries + Recruiting + Projects; } }
        public double Net { get { return Income - Spend; } }

        public DataNode ToDataNode()
        {
            var n = new DataNode("YEAR_SUMMARY");
            n.AddValue("year", Year).AddValue("closed", Closed).AddValue("grant", Grant).AddValue("otherIncome", OtherIncome)
             .AddValue("salaries", Salaries).AddValue("recruiting", Recruiting).AddValue("projects", Projects).AddValue("refunds", Refunds)
             .AddValue("opening", OpeningBalance).AddValue("closing", ClosingBalance).AddValue("prestige", Prestige).AddValue("crew", Crew)
             .AddValue("missions", Missions).AddValue("successes", Successes).AddValue("deaths", Deaths)
             .AddValue("goalsIssued", GoalsIssued).AddValue("goalsMet", GoalsMet).AddValue("band", Band);
            foreach (string m in Milestones) n.AddValue("milestone", m);
            return n;
        }

        public static YearSummary FromDataNode(DataNode n)
        {
            var s = new YearSummary
            {
                Year = n.GetInt("year", 0), Closed = n.GetBool("closed", true), Grant = n.GetDouble("grant", 0), OtherIncome = n.GetDouble("otherIncome", 0),
                Salaries = n.GetDouble("salaries", 0), Recruiting = n.GetDouble("recruiting", 0), Projects = n.GetDouble("projects", 0), Refunds = n.GetDouble("refunds", 0),
                OpeningBalance = n.GetDouble("opening", 0), ClosingBalance = n.GetDouble("closing", 0), Prestige = n.GetDouble("prestige", 0), Crew = n.GetInt("crew", 0),
                Missions = n.GetInt("missions", 0), Successes = n.GetInt("successes", 0), Deaths = n.GetInt("deaths", 0),
                GoalsIssued = n.GetInt("goalsIssued", 0), GoalsMet = n.GetInt("goalsMet", 0), Band = n.GetValue("band", string.Empty)
            };
            s.Milestones.AddRange(n.GetValues("milestone"));
            return s;
        }
    }

    /// <summary>One row of the league table: the player or a rival.</summary>
    public sealed class LeagueRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Style { get; set; }
        public bool IsPlayer { get; set; }
        public double Prestige { get; set; }
        public double Budget { get; set; }
        public int Missions { get; set; }
        public int Successes { get; set; }
        public int Deaths { get; set; }
        public int Milestones { get; set; }
        public int Firsts { get; set; }
        public string LatestMilestone { get; set; }

        public LeagueRow() { Id = string.Empty; Name = string.Empty; Style = string.Empty; LatestMilestone = string.Empty; }

        /// <summary>Ranking score: milestones and firsts weigh most, prestige breaks ties, deaths cost.</summary>
        public double Score { get { return Milestones * 10 + Firsts * 6 + Prestige * 0.2 + Successes - Deaths * 4; } }
    }
}
