using System;
using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.Core.Recruiting
{
    /// <summary>Supplies names for generated applicants. The host owns the name style; tests use a counter.</summary>
    public interface INameSource
    {
        string NextName();
    }

    /// <summary>A candidate the Director can interview and hire. True stats are hidden until revealed.</summary>
    public sealed class Applicant
    {
        public const string RevealStars = "stars";
        public const string RevealCourage = "courage";
        public const string RevealStupidity = "stupidity";
        public const string RevealTags = "tags";

        public string Id { get; set; }
        public string Name { get; set; }
        public CrewRole Role { get; set; }
        public int Stars { get; set; }
        public float Courage { get; set; }
        public float Stupidity { get; set; }
        public List<string> Tags { get; } = new List<string>();
        public List<string> Achievements { get; } = new List<string>();
        public string SourceId { get; set; }
        public int ArrivedDay { get; set; }
        public int Interviews { get; set; }
        public HashSet<string> Revealed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>For rejected applicants: the day they may come back. -1 when not applicable.</summary>
        public int ReturnDay { get; set; }
        public double HireFee { get; set; }

        public Applicant() { Id = string.Empty; Name = string.Empty; SourceId = string.Empty; ReturnDay = -1; }

        public bool IsRevealed(string what) { return Revealed.Contains(what); }
        public bool FullyRevealed { get { return Revealed.Count >= 4; } }
    }

    /// <summary>Applicants waiting to be hired, plus rejected ones that may return (Jira KSA-29).</summary>
    public sealed class ApplicantPool
    {
        public List<Applicant> Applicants { get; } = new List<Applicant>();
        public List<Applicant> Returning { get; } = new List<Applicant>();
        public int NextId { get; set; }

        public ApplicantPool() { NextId = 1; }

        public Applicant Get(string id)
        {
            for (int i = 0; i < Applicants.Count; i++)
                if (string.Equals(Applicants[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Applicants[i];
            return null;
        }

        /// <summary>Creates applicants from a source. Prestige (0..100) nudges stars up or down within the source's range.</summary>
        public List<Applicant> Generate(RecruitSource source, int count, double prestige, int day, Random rng, INameSource names,
                                        IList<string> tagPool, ICollection<string> takenNames)
        {
            var made = new List<Applicant>();
            if (source == null || count <= 0) return made;
            rng = rng ?? new Random();
            for (int i = 0; i < count; i++)
            {
                string name = UniqueName(names, takenNames);
                var a = new Applicant
                {
                    Id = "A" + NextId++,
                    Name = name,
                    Role = source.Roles[rng.Next(source.Roles.Count)],
                    SourceId = source.Id,
                    ArrivedDay = day,
                    HireFee = source.HireFee
                };
                // Prestige biases the roll: high prestige pulls toward StarsMax.
                double bias = (prestige - 50.0) / 100.0; // -0.5 .. +0.5
                double roll = Clamp01(rng.NextDouble() + bias * 0.5);
                a.Stars = source.StarsMin + (int)Math.Round(roll * (source.StarsMax - source.StarsMin));
                a.Courage = Lerp(source.CourageMin, source.CourageMax, (float)rng.NextDouble());
                a.Stupidity = Lerp(source.StupidityMin, source.StupidityMax, (float)rng.NextDouble());
                a.Achievements.AddRange(source.Achievements);
                if (tagPool != null && tagPool.Count > 0)
                {
                    a.Tags.Add(tagPool[rng.Next(tagPool.Count)]);
                    if (rng.NextDouble() < 0.3)
                    {
                        string second = tagPool[rng.Next(tagPool.Count)];
                        if (!a.Tags.Contains(second)) a.Tags.Add(second);
                    }
                }
                Applicants.Add(a);
                made.Add(a);
                if (takenNames != null) takenNames.Add(name);
            }
            return made;
        }

        /// <summary>
        /// One interview reveals one more thing: stars, then courage, then stupidity, then tags.
        /// A senior panel (panelBonus >= 1) reveals two per interview.
        /// </summary>
        public bool Interview(string id, int panelBonus)
        {
            Applicant a = Get(id);
            if (a == null) return false;
            a.Interviews++;
            int reveals = 1 + (panelBonus > 0 ? 1 : 0);
            string[] order = { Applicant.RevealStars, Applicant.RevealCourage, Applicant.RevealStupidity, Applicant.RevealTags };
            for (int i = 0; i < order.Length && reveals > 0; i++)
            {
                if (a.Revealed.Add(order[i])) reveals--;
            }
            return true;
        }

        public Applicant Reject(string id, int day, int returnAfterDays)
        {
            Applicant a = Get(id);
            if (a == null) return null;
            Applicants.Remove(a);
            a.ReturnDay = day + returnAfterDays;
            Returning.Add(a);
            return a;
        }

        public Applicant Remove(string id)
        {
            Applicant a = Get(id);
            if (a != null) Applicants.Remove(a);
            return a;
        }

        /// <summary>Brings back rejected applicants whose time has come. Returns how many returned.</summary>
        public int Refresh(int day)
        {
            int n = 0;
            for (int i = Returning.Count - 1; i >= 0; i--)
            {
                Applicant a = Returning[i];
                if (a.ReturnDay >= 0 && a.ReturnDay <= day)
                {
                    Returning.RemoveAt(i);
                    a.ReturnDay = -1;
                    a.ArrivedDay = day;
                    a.Stars = Math.Min(5, a.Stars + 1); // a year older and wiser
                    Applicants.Add(a);
                    n++;
                }
            }
            return n;
        }

        private static string UniqueName(INameSource names, ICollection<string> taken)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string n = names != null ? names.NextName() : "Applicant " + Guid.NewGuid().ToString("N").Substring(0, 6);
                if (string.IsNullOrEmpty(n)) continue;
                if (taken == null || !taken.Contains(n)) return n;
            }
            return "Applicant " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    }
}
