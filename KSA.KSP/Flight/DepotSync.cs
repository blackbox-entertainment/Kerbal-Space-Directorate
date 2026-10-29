using System;
using KSA.Core.Projects;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Keeps a live fuel depot's tanks matching the agency's figure (2026-10-29). Simulated tankers and departures change
    /// the figure only; this puts the same share of propellant in every tank of the depot vessel, loaded or not. A depot
    /// with a live KSA flight docked to it is left alone (that flight measures and reports what it moved).
    /// </summary>
    public static class DepotSync
    {
        public static void Apply(Program g)
        {
            try { ApplyInner(g); }
            catch (Exception e) { KsaCore.Log.Warn("Depot sync failed: " + e.Message); }
        }

        private static void ApplyInner(Program g)
        {
            if (g == null || g.VesselId.Length == 0 || g.DepotCapacity <= 0 || FlightGlobals.Vessels == null) return;
            uint id;
            if (!uint.TryParse(g.VesselId, out id)) return;
            if (KsaFlightDirector.Live != null && KsaFlightDirector.Live.VesselId == id) return;
            Vessel v = null;
            foreach (Vessel x in FlightGlobals.Vessels) if (x != null && x.persistentId == id) { v = x; break; }
            if (v == null) return;
            double capacity = 0;
            if (v.loaded)
            {
                foreach (Part p in v.parts) foreach (PartResource r in p.Resources) if (DepotOps.IsPropellant(r)) capacity += r.maxAmount * r.info.density;
                if (capacity <= 0) return;
                double f = Math.Max(0, Math.Min(1, g.DepotFuel / capacity));
                foreach (Part p in v.parts) foreach (PartResource r in p.Resources) if (DepotOps.IsPropellant(r)) r.amount = r.maxAmount * f;
            }
            else if (v.protoVessel != null)
            {
                foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                    foreach (ProtoPartResourceSnapshot r in pps.resources)
                        if (r.definition != null && Array.IndexOf(DepotOps.Propellants, r.resourceName) >= 0) capacity += r.maxAmount * r.definition.density;
                if (capacity <= 0) return;
                double f = Math.Max(0, Math.Min(1, g.DepotFuel / capacity));
                foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                    foreach (ProtoPartResourceSnapshot r in pps.resources)
                        if (Array.IndexOf(DepotOps.Propellants, r.resourceName) >= 0) r.amount = r.maxAmount * f;
            }
            KsaCore.Log.Info("Depot " + g.Name + ": tanks set to " + g.DepotFuel.ToString("0.0") + " t.");
        }
    }
}
