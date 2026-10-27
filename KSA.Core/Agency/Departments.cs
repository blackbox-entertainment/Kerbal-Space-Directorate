using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core
{
    /// <summary>The four department-head posts a Management-track kerbal can hold (decision 2026-10-26: hireable staff replace the stock faces).</summary>
    public enum Department { Finances = 0, Science = 1, PublicRelations = 2, Operations = 3 }

    /// <summary>Who holds a post and since when.</summary>
    public sealed class DepartmentPost
    {
        public Department Department { get; set; }
        public string CrewId { get; set; }
        public int SinceDay { get; set; }
        public DepartmentPost() { CrewId = string.Empty; }
        public bool Filled { get { return CrewId.Length > 0; } }
    }

    /// <summary>
    /// What a department head does for the agency, per rank rung (Program Manager = 1, Deputy Director = 2) with a
    /// star bonus. From the DEPARTMENTS block of KSA_AGENCY_SETTINGS. Effects are multiplicative fractions.
    /// </summary>
    public sealed class DepartmentRules
    {
        public const string NodeName = "DEPARTMENTS";

        public bool Enabled { get; set; } = true;
        /// <summary>Finances: salaries cut by this fraction per rung.</summary>
        public double SalaryCutPerRung { get; set; } = 0.03;
        /// <summary>Finances: grant raised by this fraction per rung.</summary>
        public double GrantBonusPerRung { get; set; } = 0.02;
        /// <summary>Science: simulated mission success chance raised by this per rung (absolute).</summary>
        public double SuccessBonusPerRung { get; set; } = 0.02;
        /// <summary>Public relations: prestige gains multiplied by 1 + this per rung.</summary>
        public double PrestigeBonusPerRung { get; set; } = 0.05;
        /// <summary>Public relations: morale added to everyone at year end, per rung.</summary>
        public int MoralePerRung { get; set; } = 2;
        /// <summary>Operations: design and build days cut by this fraction per rung.</summary>
        public double BuildCutPerRung { get; set; } = 0.05;
        /// <summary>Extra effective rung per star above three (a 5-star head counts as rung + 0.5).</summary>
        public double StarWeight { get; set; } = 0.25;

        public static DepartmentRules Parse(DataNode node)
        {
            var r = new DepartmentRules();
            if (node == null) return r;
            r.Enabled = node.GetBool("enabled", r.Enabled);
            r.SalaryCutPerRung = node.GetDouble("salaryCutPerRung", r.SalaryCutPerRung);
            r.GrantBonusPerRung = node.GetDouble("grantBonusPerRung", r.GrantBonusPerRung);
            r.SuccessBonusPerRung = node.GetDouble("successBonusPerRung", r.SuccessBonusPerRung);
            r.PrestigeBonusPerRung = node.GetDouble("prestigeBonusPerRung", r.PrestigeBonusPerRung);
            r.MoralePerRung = node.GetInt("moralePerRung", r.MoralePerRung);
            r.BuildCutPerRung = node.GetDouble("buildCutPerRung", r.BuildCutPerRung);
            r.StarWeight = node.GetDouble("starWeight", r.StarWeight);
            return r;
        }

        public static string Title(Department d)
        {
            switch (d)
            {
                case Department.Finances: return "Head of Finances";
                case Department.Science: return "Head of Science";
                case Department.PublicRelations: return "Head of Public Relations";
                default: return "Head of Operations";
            }
        }

        public static string Describe(Department d, DepartmentRules r, double rung)
        {
            if (rung <= 0) return "Vacant: no effect.";
            switch (d)
            {
                case Department.Finances: return "Salaries -" + Pct(r.SalaryCutPerRung * rung) + ", grant +" + Pct(r.GrantBonusPerRung * rung) + ".";
                case Department.Science: return "Simulated mission success +" + Pct(r.SuccessBonusPerRung * rung) + ".";
                case Department.PublicRelations: return "Prestige gains +" + Pct(r.PrestigeBonusPerRung * rung) + ", morale +" + (int)Math.Round(r.MoralePerRung * rung) + " at year end.";
                default: return "Design and build time -" + Pct(r.BuildCutPerRung * rung) + ".";
            }
        }

        private static string Pct(double f) { return (int)Math.Round(f * 100) + "%"; }
    }
}
