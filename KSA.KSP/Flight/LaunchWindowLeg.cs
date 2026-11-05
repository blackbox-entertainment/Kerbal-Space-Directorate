using System;
using KSA.Core.Flight;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Holds on the pad after the Director's go until the fuel station comes round (0.8.224, T: "it should have waited to
    /// lift off and increase the countdown after I hit launch if it needed time to align with the tanker"). Launched into
    /// the station's phase, the meeting is a direct intercept of a few m/s instead of a phasing orbit and the chase that
    /// burnt 246 m/s on 2026-11-05.
    /// </summary>
    public sealed class LaunchWindowLeg : IFlightLeg
    {
        /// <summary>The ascent as flown by the Mk IIIe (2026-11-05): liftoff to a circular orbit in about 380 s, about 40 degrees downrange. The ascent logs both at insertion for tuning.</summary>
        public const double AscentSeconds = 380, DownrangeDeg = 40, LeadDeg = 0;
        private const double GoWithin = 30, MaxWait = 4 * 3600;
        private double _liftoffUt;
        private string _station = string.Empty;

        public string Name { get { return "Launch window"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Vessel v = ctx.Vessel;
            bool onGround = v != null && (v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED);
            if (!onGround || v.mainBody == null || v.mainBody != ctx.Home) { State = LegState.Done; return; }
            Vessel station = TankerStopLeg.FindTanker(v, ctx.Home);
            if (station == null || station.orbit == null || station.orbit.period <= 0) { State = LegState.Done; return; }
            _station = station.vesselName;
            try
            {
                double phase = PhaseAhead(v.mainBody, station.orbit, v.GetWorldPos3D() - v.mainBody.position, ctx.Now);
                double stationRate = 2 * Math.PI / station.orbit.period, padRate = 2 * Math.PI / Math.Abs(v.mainBody.rotationPeriod);
                double wait = LaunchWindow.WaitSeconds(phase, stationRate, padRate, DownrangeDeg * Math.PI / 180, AscentSeconds, LeadDeg * Math.PI / 180);
                if (double.IsNaN(wait) || wait > MaxWait) { ctx.Say("No launch window for " + _station + " within four hours; lifting off now."); State = LegState.Done; return; }
                if (wait < GoWithin) { ctx.Say(_station + " is coming round now: lifting off."); State = LegState.Done; return; }
                _liftoffUt = ctx.Now + wait;
                Status = "Lifting off in " + TransferSupport.Countdown(wait) + " when " + _station + " comes round.";
                ctx.Say("Holding on the pad for " + _station + ", " + Math.Round(phase * 180 / Math.PI) + " deg ahead of the pad now: lifting off in " + TransferSupport.Countdown(wait) + " to meet it in orbit.");
            }
            catch (Exception e)
            {
                KsaCore.Log.Warn("Launch window failed: " + e.Message);
                State = LegState.Done;
            }
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            double left = _liftoffUt - ctx.Now;
            if (left <= 0) { ctx.Control.StopWarp(); State = LegState.Done; return "Lifting off for " + _station + "."; }
            Status = "Lifting off in " + TransferSupport.Countdown(left) + " when " + _station + " comes round.";
            if (left > GoWithin) ctx.Control.WarpTo(_liftoffUt - 15);
            else if (ctx.Control.IsWarping) ctx.Control.StopWarp();
            return null;
        }

        public void End(FlightContext ctx) { ctx.Control.StopWarp(); }

        /// <summary>
        /// The station's angle ahead of a position (from the body's centre, world frame, this instant), positive the way
        /// the station moves. The axis is taken from the station's own motion, so no convention of the game's can turn it round.
        /// </summary>
        public static double PhaseAhead(CelestialBody b, Orbit station, Vector3d fromRel, double ut)
        {
            Vector3d p1 = station.getPositionAtUT(ut) - b.position, p2 = station.getPositionAtUT(ut + 10) - b.position;
            Vector3d axis = Vector3d.Cross(p1, p2);
            if (axis.sqrMagnitude < 1e-12) return double.NaN;
            axis.Normalize();
            Vector3d a = fromRel - axis * Vector3d.Dot(fromRel, axis), c = p1 - axis * Vector3d.Dot(p1, axis);
            return Math.Atan2(Vector3d.Dot(Vector3d.Cross(a, c), axis), Vector3d.Dot(a, c));
        }
    }
}
