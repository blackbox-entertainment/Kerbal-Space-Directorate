using System;
using System.Collections.Generic;
using Contracts;
using KSA.Core;

namespace KSA.KSP
{
    /// <summary>
    /// Keeps the stock systems KSA replaces out of the way (agency.cfg): offered Mission Control contracts are
    /// withdrawn as they appear (KSA goals are the agency's tasking), and the stock Astronaut Complex applicant list
    /// is kept empty so hiring goes through KSA Recruiting at KSA prices. Accepted contracts and hired kerbals are never touched.
    /// </summary>
    public static class StockSystems
    {
        private static bool _hooked;
        private static readonly Listener Handler = new Listener();

        /// <summary>KSP GameEvents reject static handlers (null target), so the hook lives on an instance.</summary>
        private sealed class Listener { public void ContractOffered(Contract c) { OnContractOffered(c); } }
        private static int _withdrawn;
        private static int _purged;

        public static void Enforce(AgencySettings settings)
        {
            if (settings == null) return;
            if (!_hooked)
            {
                _hooked = true;
                GameEvents.Contract.onOffered.Add(Handler.ContractOffered);
            }
            if (!settings.StockContracts) WithdrawOffered();
        }

        private static void OnContractOffered(Contract c)
        {
            if (KsaCore.Agency == null || KsaCore.Agency.Settings.StockContracts || c == null) return;
            try
            {
                if (c.ContractState == Contract.State.Offered) { c.Withdraw(); _withdrawn++; }
            }
            catch (Exception e) { KsaCore.Log.Warn("Could not withdraw a stock contract: " + e.Message); }
        }

        private static void WithdrawOffered()
        {
            ContractSystem cs = ContractSystem.Instance;
            if (cs == null || cs.Contracts == null) return;
            var offered = new List<Contract>();
            foreach (Contract c in cs.Contracts) if (c != null && c.ContractState == Contract.State.Offered) offered.Add(c);
            foreach (Contract c in offered)
            {
                try { c.Withdraw(); _withdrawn++; }
                catch (Exception e) { KsaCore.Log.Warn("Could not withdraw a stock contract: " + e.Message); }
            }
            if (offered.Count > 0) KsaCore.Log.Info("Withdrew " + offered.Count + " stock contract(s); KSA goals replace them (agency.cfg stockContracts = false)");
        }

        private static void PurgeApplicants()
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null) return;
            var names = new List<string>();
            foreach (ProtoCrewMember pcm in roster.Applicants) if (pcm != null) names.Add(pcm.name);
            foreach (string n in names)
            {
                try { roster.Remove(n); _purged++; }
                catch (Exception e) { KsaCore.Log.Warn("Could not remove stock applicant " + n + ": " + e.Message); }
            }
            if (names.Count > 0) KsaCore.Log.Info("Removed " + names.Count + " stock applicant(s) from the Astronaut Complex list; hire through KSA Recruiting (agency.cfg stockHiring = false)");
        }

        public static string Summary() { return _withdrawn + " contract(s) withdrawn, " + _purged + " stock applicant(s) removed this session"; }
    }
}
