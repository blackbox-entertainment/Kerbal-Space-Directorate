using KSA.Core.Finance;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class FinanceTests
    {
        private sealed class FakeFunds : IFunds
        {
            public bool IsActive { get; set; } = true;
            public double Balance { get; set; }
            public double LastDelta { get; private set; }
            public void Apply(double delta, string reason) { Balance += delta; LastDelta = delta; }
        }

        [Test]
        public void Ledger_DepositWithdraw_AndTotals()
        {
            var l = new Ledger();
            l.Deposit(1000, LedgerCategory.Grant, "grant", 0, 1);
            Assert.That(l.Withdraw(300, LedgerCategory.Salary, "pay", 10, 1), Is.True);
            Assert.That(l.Withdraw(5000, LedgerCategory.Hiring, "too much", 11, 1), Is.False, "no overdraft by default");
            Assert.That(l.Balance, Is.EqualTo(700));
            Assert.That(l.Withdraw(5000, LedgerCategory.Salary, "forced", 12, 1, allowOverdraft: true), Is.True);
            Assert.That(l.Balance, Is.EqualTo(-4300));
            Assert.That(l.IncomeFor(1), Is.EqualTo(1000));
            Assert.That(l.ExpensesFor(1), Is.EqualTo(5300));
            Assert.That(l.TotalFor(1, LedgerCategory.Salary), Is.EqualTo(-5300));
            Assert.That(l.TotalFor(2), Is.EqualTo(0));
        }

        [Test]
        public void Ledger_MirrorsToHostFunds_WhenActive()
        {
            var funds = new FakeFunds { Balance = 25000 };
            var l = new Ledger { Mirror = funds };
            l.SyncFromMirror();
            Assert.That(l.Balance, Is.EqualTo(25000));
            l.Deposit(100, LedgerCategory.Grant, "g", 0, 1);
            Assert.That(funds.Balance, Is.EqualTo(25100));
            Assert.That(funds.LastDelta, Is.EqualTo(100));
            funds.IsActive = false;
            l.Withdraw(50, LedgerCategory.Other, "x", 0, 1);
            Assert.That(funds.Balance, Is.EqualTo(25100), "inactive mirror is left alone");
            Assert.That(l.Balance, Is.EqualTo(25050));
        }

        [Test]
        public void Ledger_TrimsOldEntries()
        {
            var l = new Ledger();
            for (int i = 0; i < Ledger.MaxEntries + 25; i++) l.Deposit(1, LedgerCategory.Other, "e" + i, i, 1);
            Assert.That(l.Entries.Count, Is.EqualTo(Ledger.MaxEntries));
            Assert.That(l.Entries[0].Reason, Is.EqualTo("e25"));
            Assert.That(l.Balance, Is.EqualTo(Ledger.MaxEntries + 25), "balance is not affected by trimming");
        }

        [Test]
        public void FiscalCalendar_YearsAndDays()
        {
            var c = new FiscalCalendar(426);
            Assert.That(c.YearOf(0), Is.EqualTo(1));
            Assert.That(c.YearOf(425), Is.EqualTo(1));
            Assert.That(c.YearOf(426), Is.EqualTo(2));
            Assert.That(c.DayOfYear(0), Is.EqualTo(1));
            Assert.That(c.DayOfYear(426), Is.EqualTo(1));
            Assert.That(c.DaysLeftInYear(0), Is.EqualTo(425));
            Assert.That(c.FirstDayOfYear(3), Is.EqualTo(852));
            Assert.That(new FiscalCalendar(0).DaysPerYear, Is.EqualTo(426), "guards against a zero-length year");
        }
    }
}
