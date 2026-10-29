using System;
using System.Collections.Generic;
using KSA.Core.Parts;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// KSA-68: the stock tech tree for the AI Director. Options are the nodes whose prerequisites are researched and that
    /// are not yet bought; Research spends the Science, marks the node researched and buys its parts (free when the game's
    /// difficulty skips part purchases, else with funds while they last). Career only: sandbox and science mode have
    /// nothing to buy or no funds, so the Director is simply told there is nothing to research.
    /// </summary>
    public sealed class KspResearchHost : IResearchHost
    {
        public double Science { get { return ResearchAndDevelopment.Instance != null ? ResearchAndDevelopment.Instance.Science : 0; } }

        private static bool Career { get { return HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER && ResearchAndDevelopment.Instance != null; } }

        public List<TechOption> Options()
        {
            var list = new List<TechOption>();
            if (!Career || AssetBase.RnDTechTree == null) return list;
            ProtoRDNode[] nodes;
            try { nodes = AssetBase.RnDTechTree.GetTreeNodes(); } catch (Exception e) { KsaCore.Log.Warn("Tech tree unreadable: " + e.Message); return list; }
            if (nodes == null) return list;
            foreach (ProtoRDNode n in nodes)
            {
                if (n == null || n.tech == null || string.IsNullOrEmpty(n.tech.techID)) continue;
                if (Researched(n.tech.techID) || !ParentsDone(n)) continue;
                list.Add(new TechOption { Id = n.tech.techID, Title = Title(n.tech.techID), ScienceCost = n.tech.scienceCost, Unlocks = Unlocks(n.tech.techID), PartCount = PartsOf(n.tech.techID).Count });
            }
            return list;
        }

        public bool Research(string techId, out string message)
        {
            message = string.Empty;
            if (!Career) { message = "there is no research to buy in this game mode"; return false; }
            ProtoRDNode node = Find(techId);
            if (node == null) { message = "no technology called '" + techId + "'"; return false; }
            string title = Title(node.tech.techID);
            if (Researched(node.tech.techID)) { message = title + " is already researched"; return false; }
            if (!ParentsDone(node)) { message = title + " needs its prerequisites researched first"; return false; }
            float cost = node.tech.scienceCost;
            if (ResearchAndDevelopment.Instance.Science < cost) { message = title + " costs " + cost.ToString("0") + " Science; the agency has " + ResearchAndDevelopment.Instance.Science.ToString("0"); return false; }
            try
            {
                ProtoTechNode state = ResearchAndDevelopment.Instance.GetTechState(node.tech.techID) ?? new ProtoTechNode { techID = node.tech.techID, scienceCost = node.tech.scienceCost, partsPurchased = new List<AvailablePart>() };
                if (state.partsPurchased == null) state.partsPurchased = new List<AvailablePart>();
                state.state = RDTech.State.Available;
                ResearchAndDevelopment.Instance.SetTechState(node.tech.techID, state);
                ResearchAndDevelopment.Instance.AddScience(-cost, TransactionReasons.RnDTechResearch);
                int bought = 0, unpaid = 0;
                bool free = FreeParts();
                foreach (AvailablePart ap in PartsOf(node.tech.techID))
                {
                    if (state.partsPurchased.Contains(ap)) continue;
                    if (!free)
                    {
                        if (Funding.Instance == null || Funding.Instance.Funds < ap.entryCost) { unpaid++; continue; }
                        if (ap.entryCost > 0) Funding.Instance.AddFunds(-ap.entryCost, TransactionReasons.RnDPartPurchase);
                    }
                    state.partsPurchased.Add(ap);
                    bought++;
                }
                KspPartCatalog.Invalidate();
                message = title + " researched for " + cost.ToString("0") + " Science; " + bought + " part" + (bought == 1 ? "" : "s") + " ready" + (unpaid > 0 ? ", " + unpaid + " not bought (not enough funds)" : string.Empty);
                KsaCore.Log.Info("AI Director research: " + message);
                return true;
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("Research of " + techId + " failed: " + e);
                message = "the game refused: " + e.Message;
                return false;
            }
        }

        private static bool FreeParts()
        {
            try { return HighLogic.CurrentGame.Parameters.Difficulty.BypassEntryPurchaseAfterResearch; }
            catch { return false; }
        }

        private static ProtoRDNode Find(string idOrTitle)
        {
            if (string.IsNullOrEmpty(idOrTitle) || AssetBase.RnDTechTree == null) return null;
            foreach (ProtoRDNode n in AssetBase.RnDTechTree.GetTreeNodes())
            {
                if (n == null || n.tech == null) continue;
                if (string.Equals(n.tech.techID, idOrTitle, StringComparison.OrdinalIgnoreCase) || string.Equals(Title(n.tech.techID), idOrTitle, StringComparison.OrdinalIgnoreCase)) return n;
            }
            return null;
        }

        private static bool Researched(string techId) { return ResearchAndDevelopment.GetTechnologyState(techId) == RDTech.State.Available; }

        private static bool ParentsDone(ProtoRDNode n)
        {
            if (n.parents == null || n.parents.Count == 0) return true;
            int done = 0;
            foreach (ProtoRDNode p in n.parents) if (p != null && p.tech != null && Researched(p.tech.techID)) done++;
            return n.AnyParentToUnlock ? done > 0 : done == n.parents.Count;
        }

        private static string Title(string techId)
        {
            try
            {
                string t = ResearchAndDevelopment.GetTechnologyTitle(techId);
                if (!string.IsNullOrEmpty(t)) return global::KSP.Localization.Localizer.Format(t);
            }
            catch { }
            return techId;
        }

        private static List<AvailablePart> PartsOf(string techId)
        {
            var list = new List<AvailablePart>();
            if (PartLoader.LoadedPartsList == null) return list;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList) if (ap != null && string.Equals(ap.TechRequired, techId, StringComparison.OrdinalIgnoreCase)) list.Add(ap);
            return list;
        }

        /// <summary>What a node unlocks, in the words the Director's mission list uses.</summary>
        private static string Unlocks(string techId)
        {
            var kinds = new SortedDictionary<string, int>();
            foreach (AvailablePart ap in PartsOf(techId))
            {
                if (ap.partPrefab == null) continue;
                PartKind k;
                try { k = KspPartCatalog.Classify(ap, ap.partPrefab); } catch { continue; }
                string word = Word(k);
                if (word == null) continue;
                int n; kinds.TryGetValue(word, out n); kinds[word] = n + 1;
            }
            if (kinds.Count == 0) return "no flight parts";
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in kinds) parts.Add(kv.Value > 1 ? kv.Key + " x" + kv.Value : kv.Key);
            return string.Join(", ", parts.ToArray());
        }

        private static string Word(PartKind k)
        {
            switch (k)
            {
                case PartKind.Command: return "crew pod";
                case PartKind.Tank: return "fuel tank";
                case PartKind.Engine: return "liquid engine";
                case PartKind.Booster: return "solid booster";
                case PartKind.Decoupler: return "decoupler";
                case PartKind.Parachute: return "parachute";
                case PartKind.HeatShield: return "heat shield";
                case PartKind.Docking: return "docking port";
                case PartKind.Habitat: return "habitat";
                case PartKind.Power: return "power";
                case PartKind.Rcs: return "RCS";
                case PartKind.Science: return "science";
                case PartKind.LandingLeg: return "landing legs";
                case PartKind.Wheel: return "wheels";
                case PartKind.Envelope: return "airship envelope";
                case PartKind.Antenna: return "antenna";
                case PartKind.Industry: return "drill or ore converter";
                case PartKind.Fin: return "fins";
                default: return null;
            }
        }
    }
}
