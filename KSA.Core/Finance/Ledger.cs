using System;
using System.Collections.Generic;

namespace KSA.Core.Finance
{
    public enum LedgerCategory
    {
        Grant = 0,
        Salary = 1,
        Recruiting = 2,
        Hiring = 3,
        Cancellation = 4,
        Other = 5,
        /// <summary>Space center building upgrades (2026-10-30).</summary>
        Facilities = 6
    }

    public sealed class LedgerEntry
    {
        public int Day { get; set; }
        public int Year { get; set; }
        /// <summary>Positive = income, negative = expense.</summary>
        public double Amount { get; set; }
        public LedgerCategory Category { get; set; }
        public string Reason { get; set; }

        public LedgerEntry() { Reason = string.Empty; }
    }

    /// <summary>
    /// The host's money, if it has any. In KSP Career mode the agency balance mirrors stock Funds;
    /// in Sandbox there is nothing to mirror and the ledger is the only account.
    /// </summary>
    public interface IFunds
    {
        bool IsActive { get; }
        double Balance { get; }
        void Apply(double delta, string reason);
    }

    /// <summary>The agency's account book (Jira KSA-26).</summary>
    public sealed class Ledger
    {
        public const int MaxEntries = 400;

        public double Balance { get; private set; }
        public List<LedgerEntry> Entries { get; } = new List<LedgerEntry>();
        /// <summary>Optional host account to keep in step with this ledger.</summary>
        public IFunds Mirror { get; set; }

        /// <summary>Adopts the host's balance as the truth (Career mode on load).</summary>
        public void SyncFromMirror()
        {
            if (Mirror != null && Mirror.IsActive) Balance = Mirror.Balance;
        }

        public void SetBalance(double balance) { Balance = balance; }

        public void Deposit(double amount, LedgerCategory category, string reason, int day, int year)
        {
            if (amount <= 0) return;
            Record(amount, category, reason, day, year);
        }

        /// <summary>Returns false (and books nothing) if the balance would go negative and overdraft is not allowed.</summary>
        public bool Withdraw(double amount, LedgerCategory category, string reason, int day, int year, bool allowOverdraft = false)
        {
            if (amount <= 0) return true;
            if (!allowOverdraft && Balance - amount < 0) return false;
            Record(-amount, category, reason, day, year);
            return true;
        }

        public bool CanAfford(double amount) { return amount <= 0 || Balance >= amount; }

        public double TotalFor(int year, LedgerCategory? category = null)
        {
            double sum = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                LedgerEntry e = Entries[i];
                if (e.Year != year) continue;
                if (category.HasValue && e.Category != category.Value) continue;
                sum += e.Amount;
            }
            return sum;
        }

        public double IncomeFor(int year)
        {
            double sum = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Year == year && Entries[i].Amount > 0) sum += Entries[i].Amount;
            return sum;
        }

        public double ExpensesFor(int year)
        {
            double sum = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Year == year && Entries[i].Amount < 0) sum -= Entries[i].Amount;
            return sum;
        }

        private void Record(double amount, LedgerCategory category, string reason, int day, int year)
        {
            Balance += amount;
            Entries.Add(new LedgerEntry { Day = day, Year = year, Amount = amount, Category = category, Reason = reason ?? string.Empty });
            if (Entries.Count > MaxEntries) Entries.RemoveRange(0, Entries.Count - MaxEntries);
            if (Mirror != null && Mirror.IsActive) Mirror.Apply(amount, reason);
        }
    }

    /// <summary>Fiscal years on the agency calendar. Year 1 starts on day 0.</summary>
    public sealed class FiscalCalendar
    {
        public int DaysPerYear { get; }

        public FiscalCalendar(int daysPerYear)
        {
            DaysPerYear = daysPerYear > 0 ? daysPerYear : 426;
        }

        public int YearOf(int day) { return (day < 0 ? 0 : day) / DaysPerYear + 1; }

        /// <summary>1-based day within the fiscal year.</summary>
        public int DayOfYear(int day) { return (day < 0 ? 0 : day) % DaysPerYear + 1; }

        public int DaysLeftInYear(int day) { return DaysPerYear - DayOfYear(day); }

        public int FirstDayOfYear(int year) { return (year - 1) * DaysPerYear; }
    }
}
