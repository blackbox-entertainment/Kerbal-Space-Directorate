using System;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// What a craft's panels and generators make (0.8.222, T: "build the power check at distance"). Solar panels make 1/d² of
    /// their Kerbin figure at d times Kerbin's distance from the sun: the Mk IIIe's 48 OX-STATs made 5.4 EC/s broadside at
    /// Kerbin's distance and 2.4 at Duna's; at Dres they would make 0.6 and at Jool 0.2, against 0.3 to 1.2 drawn on a coast.
    /// </summary>
    internal static class KspPower
    {
        /// <summary>A ring of fixed panels: a third of them face the sun at best (16.8 EC/s nominal on the Mk IIIe, 5.4 measured).</summary>
        public const double FixedPanelShare = 1.0 / 3;

        /// <summary>EC/s a part's panels make at Kerbin's distance with the best side to the sun, and what its generators make anywhere.</summary>
        public static void Rate(Part part, out double solar, out double generator)
        {
            solar = 0; generator = 0;
            if (part == null || part.Modules == null) return;
            foreach (PartModule m in part.Modules)
            {
                if (m == null) continue;
                var panel = m as ModuleDeployableSolarPanel;
                if (panel != null) { solar += panel.chargeRate * (panel.isTracking ? 1 : FixedPanelShare); continue; }
                var gen = m as ModuleGenerator;
                if (gen != null) { generator += GeneratorRate(gen); continue; }
                // Kerbalism swaps the stock RTG module for its own process (KSA-67): the capacity is its EC/s.
                if (string.Equals(KspLifeSupport.ProcessOf(m), "_RTG", StringComparison.OrdinalIgnoreCase)) generator += KspLifeSupport.CapacityOf(m);
            }
        }

        /// <summary>The whole loaded vessel: panels at Kerbin's distance, generators through the out parameter.</summary>
        public static double Nominal(Vessel v, out double generator)
        {
            double solar = 0;
            generator = 0;
            if (v == null || v.parts == null) return 0;
            foreach (Part p in v.parts)
            {
                double s, g;
                Rate(p, out s, out g);
                solar += s; generator += g;
            }
            return solar;
        }

        /// <summary>An always-on generator with nothing to burn (the stock RTG): its ElectricCharge per second.</summary>
        private static double GeneratorRate(ModuleGenerator g)
        {
            try
            {
                if (g.resHandler == null || (g.resHandler.inputResources != null && g.resHandler.inputResources.Count > 0)) return 0;
                double rate = 0;
                foreach (ModuleResource r in g.resHandler.outputResources) if (string.Equals(r.name, "ElectricCharge", StringComparison.OrdinalIgnoreCase)) rate += r.rate;
                return rate;
            }
            catch { return 0; }
        }
    }
}
