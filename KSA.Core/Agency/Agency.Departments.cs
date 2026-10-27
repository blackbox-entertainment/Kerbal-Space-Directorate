using System;
using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.Core
{
    /// <summary>
    /// Department heads (M2 decision, built 2026-10-27): Management-track kerbals hold Finances, Science, Public
    /// Relations and Operations, replacing the stock Administration faces in function. A head keeps drawing salary,
    /// cannot be assigned to projects or crews, and gives the agency a rank-scaled bonus.
    /// </summary>
    public sealed partial class Agency
    {
        public List<DepartmentPost> Departments { get; } = new List<DepartmentPost>();

        /// <summary>A post changed hands.</summary>
        public event Action<DepartmentPost> DepartmentChanged;

        private DepartmentRules DeptRules { get { return Settings.Departments ?? new DepartmentRules(); } }

        public DepartmentPost Post(Department d)
        {
            foreach (DepartmentPost p in Departments) if (p.Department == d) return p;
            var post = new DepartmentPost { Department = d };
            Departments.Add(post);
            return post;
        }

        public CrewProfile HeadOf(Department d)
        {
            DepartmentPost p = Post(d);
            return p.Filled ? Crew.Get(p.CrewId) : null;
        }

        public bool IsDepartmentHead(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return false;
            foreach (DepartmentPost p in Departments) if (string.Equals(p.CrewId, crewId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public Department? PostOf(string crewId)
        {
            foreach (DepartmentPost p in Departments) if (p.Filled && string.Equals(p.CrewId, crewId, StringComparison.OrdinalIgnoreCase)) return p.Department;
            return null;
        }

        /// <summary>Management-track kerbals who could take a post right now (not flying, not on a project).</summary>
        public List<CrewProfile> DepartmentCandidates()
        {
            var list = new List<CrewProfile>();
            foreach (CrewProfile c in Crew.Active)
            {
                if (c.Track != CareerTrack.Management || c.Status == CrewStatus.Dead || c.IsFlying || c.ProjectId.Length > 0) continue;
                list.Add(c);
            }
            list.Sort((a, b) => b.Stars != a.Stars ? b.Stars.CompareTo(a.Stars) : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return list;
        }

        public bool Appoint(Department d, string crewId, out string message)
        {
            CrewProfile c = Crew.Get(crewId);
            if (c == null || c.Departed) { message = "No such active crew member."; return false; }
            if (c.Track != CareerTrack.Management) { message = c.Name + " is not on the Management track. Select them for Program Manager first."; return false; }
            if (c.IsFlying || c.ProjectId.Length > 0) { message = c.Name + " is busy on a project."; return false; }
            Department? current = PostOf(crewId);
            if (current.HasValue && current.Value != d) Post(current.Value).CrewId = string.Empty;
            DepartmentPost post = Post(d);
            string previous = post.Filled && post.CrewId != crewId ? Crew.Get(post.CrewId) != null ? Crew.Get(post.CrewId).Name : post.CrewId : null;
            post.CrewId = crewId;
            post.SinceDay = CurrentDay;
            message = c.Name + " is now " + DepartmentRules.Title(d) + (previous != null ? ", replacing " + previous : "") + ". " + DepartmentRules.Describe(d, DeptRules, DepartmentRung(d));
            Push(null, message);
            Action<DepartmentPost> h = DepartmentChanged;
            if (h != null) h(post);
            return true;
        }

        public bool Vacate(Department d, out string message)
        {
            DepartmentPost post = Post(d);
            if (!post.Filled) { message = DepartmentRules.Title(d) + " is already vacant."; return false; }
            CrewProfile c = Crew.Get(post.CrewId);
            post.CrewId = string.Empty;
            message = (c != null ? c.Name : "The head") + " steps down as " + DepartmentRules.Title(d) + ".";
            Push(null, message);
            Action<DepartmentPost> h = DepartmentChanged;
            if (h != null) h(post);
            return true;
        }

        /// <summary>Called when a crew member leaves the agency for any reason.</summary>
        private void VacatePostsOf(string crewId)
        {
            foreach (DepartmentPost p in Departments)
                if (string.Equals(p.CrewId, crewId, StringComparison.OrdinalIgnoreCase)) { p.CrewId = string.Empty; Action<DepartmentPost> h = DepartmentChanged; if (h != null) h(p); }
        }

        /// <summary>Effective rung of the post holder: rank rung (Program Manager 1, Deputy Director 2) plus a star bonus; 0 when vacant.</summary>
        public double DepartmentRung(Department d)
        {
            if (!DeptRules.Enabled) return 0;
            CrewProfile c = HeadOf(d);
            if (c == null || c.Departed) return 0;
            int rung = RankRung(c) + 1;
            if (rung <= 0) rung = 1;
            return rung + Math.Max(0, c.Stars - 3) * DeptRules.StarWeight;
        }

        public double SalaryFactor() { return Math.Max(0.5, 1 - DeptRules.SalaryCutPerRung * DepartmentRung(Department.Finances)); }
        public double GrantFactor() { return 1 + DeptRules.GrantBonusPerRung * DepartmentRung(Department.Finances); }
        public double SuccessBonus() { return DeptRules.SuccessBonusPerRung * DepartmentRung(Department.Science); }
        public double PrestigeGainFactor() { return 1 + DeptRules.PrestigeBonusPerRung * DepartmentRung(Department.PublicRelations); }
        public int YearEndMoraleBonus() { return (int)Math.Round(DeptRules.MoralePerRung * DepartmentRung(Department.PublicRelations)); }
        public double BuildTimeFactor() { return Math.Max(0.5, 1 - DeptRules.BuildCutPerRung * DepartmentRung(Department.Operations)); }

        /// <summary>One line per filled post for the year-end notices.</summary>
        private void DepartmentYearNotes(List<string> notices)
        {
            foreach (DepartmentPost p in Departments)
            {
                if (!p.Filled) continue;
                CrewProfile c = Crew.Get(p.CrewId);
                if (c == null) continue;
                Push(notices, DepartmentRules.Title(p.Department) + " " + c.Name + ": " + DepartmentRules.Describe(p.Department, DeptRules, DepartmentRung(p.Department)));
            }
        }
    }
}
