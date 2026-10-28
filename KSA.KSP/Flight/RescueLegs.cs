using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// KSA-65: the last stretch of an orbital rescue. After the rendezvous (which ends within 2.5 km) the pilot closes to
    /// about 100 m with short main-engine burns, matches speed, then moves the stranded crew across into free seats.
    /// If the game refuses the move (no free seat, a part that will not take crew), the leg waits for the Director to
    /// bring them over on EVA, and finishes when every stranded kerbal is aboard.
    /// </summary>
    public sealed class RescueTransferLeg : IFlightLeg
    {
        private enum Phases { Closing, Transfer, WaitEva }

        private const double TransferRange = 120;
        private const double MatchSpeed = 1.5;
        private const double TimeoutSeconds = 30 * 60;
        private const double EvaWaitSeconds = 45 * 60;

        private Phases _phase;
        private Vessel _target;
        private double _started;
        private double _evaSince;

        public string Name { get { return "Rescue pickup"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        /// <summary>Names aboard the rescue vessel that are on the stranded list (the flight director reads this at the end).</summary>
        public static readonly HashSet<string> Aboard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _started = ctx.Now;
            _target = StationOps.ResolveTarget(ctx);
            if (_target == null) { Fail(ctx, "the stranded vessel cannot be found"); return; }
            if (ctx.RescueNames.Count == 0) { Fail(ctx, "nobody to pick up"); return; }
            _phase = Phases.Closing;
            ctx.Control.Throttle = 0;
            Status = "Closing on " + _target.vesselName + ".";
            ctx.Say("Rescue: closing on " + _target.vesselName + " (" + Math.Round(StationOps.Distance(ctx.Vessel, _target)) + " m).");
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            Vessel v = ctx.Vessel;
            if (v == null || _target == null || _target.state == Vessel.State.DEAD) { Fail(ctx, "lost track of the stranded vessel"); return null; }
            if (ctx.Now - _started > TimeoutSeconds && _phase == Phases.Closing) { Fail(ctx, "could not close on the stranded vessel in time"); return null; }
            if (LandingHelpers.Warping) ctx.Control.StopWarp();

            switch (_phase)
            {
                case Phases.Closing: return Close(ctx, v);
                case Phases.Transfer: return Transfer(ctx, v);
                default: return WaitEva(ctx, v);
            }
        }

        private string Close(FlightContext ctx, Vessel v)
        {
            Vector3d toTarget = _target.CoMD - v.CoMD;
            double d = toTarget.magnitude;
            Vector3d rel = v.obt_velocity - _target.obt_velocity;   // our velocity relative to them
            if (d < TransferRange && rel.magnitude < MatchSpeed)
            {
                ctx.Control.Throttle = 0;
                ctx.Control.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, false);
                _phase = Phases.Transfer;
                Status = "Alongside at " + Math.Round(d) + " m.";
                return "Alongside " + _target.vesselName + " at " + Math.Round(d) + " m; transferring the crew.";
            }
            // Wanted closing speed: brisk far out, a walk near in; nothing toward them inside the transfer range.
            double wanted = d > TransferRange ? Math.Min(8.0, Math.Max(1.0, (d - 60) / 40.0)) : 0;
            Vector3d desired = toTarget.normalized * wanted;
            Vector3d error = desired - rel;
            double accel = ctx.Control.MaxAcceleration();
            if (error.magnitude < 0.6 || accel <= 0)
            {
                ctx.Control.Throttle = 0;
                Status = "Coasting in, " + Math.Round(d) + " m, closing at " + Math.Round(Vector3d.Dot(rel, toTarget.normalized), 1) + " m/s.";
                return null;
            }
            Vector3d dir = error.normalized;
            ctx.Control.PointAlong(() => error.normalized);
            double attitude = ctx.Control.AttitudeError(dir);
            // Short, gentle burns: never more than the error needs in about two seconds.
            ctx.Control.Throttle = attitude < 8 ? (float)Math.Max(0.05, Math.Min(1.0, error.magnitude / Math.Max(0.1, accel * 2.0))) : 0f;
            Status = "Closing, " + Math.Round(d) + " m (" + Math.Round(error.magnitude, 1) + " m/s to correct).";
            return null;
        }

        private string Transfer(FlightContext ctx, Vessel v)
        {
            int moved = 0, missing = 0;
            string why = null;
            foreach (string name in ctx.RescueNames)
            {
                if (IsAboard(v, name)) { Aboard.Add(name); continue; }
                ProtoCrewMember pcm = Find(_target, name);
                if (pcm == null) { missing++; continue; }
                Part seat = FreeSeat(v);
                if (seat == null) { why = "no free seat aboard"; break; }
                Part from = PartHolding(_target, pcm);
                try
                {
                    if (from != null) from.RemoveCrewmember(pcm);
                    pcm.seat = null;
                    seat.AddCrewmember(pcm);
                    Aboard.Add(name);
                    moved++;
                }
                catch (Exception e) { why = e.Message; KsaCore.Log.Warn("Rescue transfer of " + name + " failed: " + e.Message); break; }
            }
            if (moved > 0)
            {
                try
                {
                    v.SpawnCrew(); _target.SpawnCrew();
                    v.CrewListSetDirty(); _target.CrewListSetDirty();
                    GameEvents.onVesselCrewWasModified.Fire(v); GameEvents.onVesselCrewWasModified.Fire(_target);
                }
                catch (Exception e) { KsaCore.Log.Warn("Rescue crew respawn: " + e.Message); }
                ctx.Say(moved + " crew transferred across from " + _target.vesselName + ".", null);
            }
            if (AllAboard(v, ctx)) { Done(ctx); return null; }
            _phase = Phases.WaitEva;
            _evaSince = ctx.Now;
            Status = "Bring the crew across on EVA" + (why != null ? " (" + why + ")" : string.Empty) + ".";
            return "Automatic transfer incomplete" + (why != null ? ": " + why : string.Empty) + (missing > 0 ? " (" + missing + " not aboard the stranded vessel)" : string.Empty) + ". Waiting for an EVA transfer.";
        }

        private string WaitEva(FlightContext ctx, Vessel v)
        {
            foreach (string name in ctx.RescueNames) if (IsAboard(v, name)) Aboard.Add(name);
            int left = 0;
            foreach (string name in ctx.RescueNames) if (!IsAboard(v, name)) left++;
            if (left == 0) { Done(ctx); return null; }
            if (ctx.Now - _evaSince > EvaWaitSeconds)
            {
                if (Aboard.Count > 0) { ctx.Say("Leaving with " + Aboard.Count + " aboard; " + left + " still on the stranded vessel.", null); State = LegState.Done; return null; }
                Fail(ctx, "nobody came across"); return null;
            }
            Status = "Waiting for an EVA transfer: " + left + " still to come aboard.";
            return null;
        }

        private void Done(FlightContext ctx)
        {
            ctx.Control.Throttle = 0;
            State = LegState.Done;
            Status = "Everyone aboard.";
            ctx.Say("All stranded crew aboard (" + Aboard.Count + "); heading home.", null);
        }

        private void Fail(FlightContext ctx, string why)
        {
            if (ctx != null && ctx.Control != null) ctx.Control.Throttle = 0;
            State = LegState.Failed;
            Status = "Rescue pickup failed: " + why + ".";
            if (ctx != null) ctx.Say("Rescue pickup failed: " + why + ".");
        }

        public void End(FlightContext ctx) { if (ctx != null && ctx.Control != null) ctx.Control.Throttle = 0; }

        private static bool AllAboard(Vessel v, FlightContext ctx)
        {
            foreach (string name in ctx.RescueNames) if (!IsAboard(v, name)) return false;
            return true;
        }

        private static bool IsAboard(Vessel v, string name)
        {
            if (v == null) return false;
            foreach (ProtoCrewMember c in v.GetVesselCrew()) if (c != null && string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static ProtoCrewMember Find(Vessel v, string name)
        {
            if (v == null) return null;
            foreach (ProtoCrewMember c in v.GetVesselCrew()) if (c != null && string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        private static Part FreeSeat(Vessel v)
        {
            foreach (Part p in v.parts) if (p != null && p.CrewCapacity > p.protoModuleCrew.Count) return p;
            return null;
        }

        private static Part PartHolding(Vessel v, ProtoCrewMember pcm)
        {
            foreach (Part p in v.parts) if (p != null && p.protoModuleCrew.Contains(pcm)) return p;
            return null;
        }
    }
}
