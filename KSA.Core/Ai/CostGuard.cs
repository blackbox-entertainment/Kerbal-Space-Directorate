using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Ai
{
    /// <summary>Call and spend caps (GDD 6.6). Days are agency days; a "month" is a rolling 30-day window.</summary>
    public sealed class CostGuard
    {
        public const int MonthDays = 30;

        public sealed class Entry
        {
            public int Day;
            public int Calls;
            public double Usd;
        }

        public List<Entry> Entries { get; } = new List<Entry>();

        public int CallsOn(int day)
        {
            foreach (Entry e in Entries) if (e.Day == day) return e.Calls;
            return 0;
        }

        public double SpendLast30Days(int day)
        {
            double sum = 0;
            foreach (Entry e in Entries) if (e.Day > day - MonthDays && e.Day <= day) sum += e.Usd;
            return sum;
        }

        public int CallsLast30Days(int day)
        {
            int sum = 0;
            foreach (Entry e in Entries) if (e.Day > day - MonthDays && e.Day <= day) sum += e.Calls;
            return sum;
        }

        /// <summary>True when one more cloud call is allowed. Local calls are always free.</summary>
        public bool Allows(AiSettings settings, bool isLocal, int day, out string reason)
        {
            reason = string.Empty;
            if (isLocal || settings == null) return true;
            if (settings.MaxCallsPerDay > 0 && CallsOn(day) >= settings.MaxCallsPerDay)
            {
                reason = "Daily AI call cap reached (" + settings.MaxCallsPerDay + ").";
                return false;
            }
            if (settings.MonthlySpendCapUsd > 0 && SpendLast30Days(day) >= settings.MonthlySpendCapUsd)
            {
                reason = "Monthly AI spend cap reached ($" + settings.MonthlySpendCapUsd.ToString("0.00") + ").";
                return false;
            }
            return true;
        }

        public void Record(int day, double usd)
        {
            foreach (Entry e in Entries)
            {
                if (e.Day == day) { e.Calls++; e.Usd += usd; Trim(day); return; }
            }
            Entries.Add(new Entry { Day = day, Calls = 1, Usd = usd });
            Trim(day);
        }

        private void Trim(int day)
        {
            Entries.RemoveAll(e => e.Day <= day - MonthDays * 2);
        }

        public DataNode ToDataNode()
        {
            var n = new DataNode("COST_GUARD");
            foreach (Entry e in Entries) n.AddChild("DAY").AddValue("day", e.Day).AddValue("calls", e.Calls).AddValue("usd", e.Usd);
            return n;
        }

        public void Load(DataNode n)
        {
            Entries.Clear();
            if (n == null) return;
            foreach (DataNode d in n.GetChildren("DAY"))
                Entries.Add(new Entry { Day = d.GetInt("day", 0), Calls = d.GetInt("calls", 0), Usd = d.GetDouble("usd", 0) });
        }
    }
}
