using KSA.Core.Finance;

namespace KSA.KSP
{
    /// <summary>
    /// Core's money account backed by stock Funds in Career mode. In Sandbox and Science modes there is no
    /// Funding instance, so the ledger runs on its own (IsActive = false).
    /// </summary>
    public sealed class KspFunds : IFunds
    {
        public bool IsActive
        {
            get
            {
                return Funding.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER;
            }
        }

        public double Balance { get { return Funding.Instance != null ? Funding.Instance.Funds : 0; } }

        public void Apply(double delta, string reason)
        {
            if (!IsActive || delta == 0) return;
            Funding.Instance.AddFunds(delta, TransactionReasons.None);
        }
    }
}
