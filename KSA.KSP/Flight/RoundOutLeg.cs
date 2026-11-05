using System;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// A prograde burn at apoapsis that lifts the periapsis up to meet it: a round, high orbit (0.8.222, T: Eve orbit and
    /// return with the Mk IIIe). A capture into a loose orbit costs a tenth of a capture into a low one, and from a round
    /// orbit the way home can be planned at any point of it. The Mk IIIe holds about 4,200 m/s after the tanker stop: it
    /// cannot pay 1,400 m/s down into a low Eve orbit and 1,400 back out, but 150 in, 500 to round the orbit at 14,000 km
    /// and 600 out is well within it.
    /// </summary>
    public sealed class RoundOutLeg : IFlightLeg
    {
        private const double RoundEnough = 0.05, TurnSeconds = 120, MinBurn = 0.5;
        private readonly CelestialBody _target;
        private NodeExecutor _exec;

        public RoundOutLeg(CelestialBody target) { _target = target; }
        public string Name { get { return "Rounding the orbit"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            _exec = null;
            Vessel v = ctx.Vessel;
            if (v == null || v.orbit == null || v.mainBody == null || !TransferSupport.IsClosed(v.orbit)) { State = LegState.Done; Status = "No closed orbit to round."; return; }
            if (v.orbit.eccentricity < RoundEnough) { State = LegState.Done; Status = "The orbit is round already."; return; }
            double mu = v.mainBody.gravParameter, rAp = v.orbit.ApR;
            double tAp = v.orbit.timeToAp;
            if (tAp < TurnSeconds) tAp += v.orbit.period;
            double vAp = Math.Sqrt(Math.Max(0, mu * (2 / rAp - 1 / v.orbit.semiMajorAxis)));
            double dv = Math.Sqrt(mu / rAp) - vAp;
            if (dv < MinBurn) { State = LegState.Done; Status = "The orbit is round enough."; return; }
            NodePlanner.ClearNodes(v);
            if (NodePlanner.Add(v, ctx.Now + tAp, new Vector3d(0, 0, dv)) == null) { State = LegState.Failed; Status = "Could not place the rounding burn."; return; }
            _exec = new NodeExecutor();
            _exec.Begin(ctx);
            if (_exec.Failed) { State = LegState.Failed; Status = _exec.FailReason; return; }
            Status = "Rounding the orbit at apoapsis: " + Math.Round(dv) + " m/s.";
            ctx.Say("Rounding the orbit: " + Math.Round(dv) + " m/s prograde at apoapsis, to " + TransferSupport.Km(v.orbit.ApA) + " all round.");
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            if (_exec == null) { State = LegState.Done; return null; }
            string line = _exec.Step(ctx);
            Status = _exec.Status;
            if (_exec.Failed) { State = LegState.Failed; Status = _exec.FailReason; return "Rounding burn failed: " + _exec.FailReason; }
            if (_exec.Done)
            {
                State = LegState.Done;
                Vessel v = ctx.Vessel;
                NodePlanner.ClearNodes(v);
                string orbit = v != null && v.orbit != null ? TransferSupport.Km(v.orbit.ApA) + " x " + TransferSupport.Km(v.orbit.PeA) : "a round orbit";
                return "Orbit rounded: " + orbit + " around " + TransferSupport.BodyName(_target) + ".";
            }
            return line;
        }

        public void End(FlightContext ctx)
        {
            if (_exec != null) _exec.End(ctx);
            ctx.Control.StopWarp();
        }
    }
}
