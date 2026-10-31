using System;
using System.Collections.Generic;
using KSA.Core.Missions;

namespace KSA.KSP.Flight
{
    public enum LegState { Running = 0, Done = 1, Failed = 2 }

    /// <summary>
    /// One stretch of a live mission (ascent, transfer, landing, docking...). The flight director runs legs in order;
    /// a leg owns the vessel through <see cref="FlightContext.Control"/> while it runs and must release what it set in End.
    /// </summary>
    public interface IFlightLeg
    {
        /// <summary>Short HUD name, e.g. "Transfer to Mun".</summary>
        string Name { get; }
        /// <summary>One-line HUD status, updated as the leg goes.</summary>
        string Status { get; }
        LegState State { get; }
        void Begin(FlightContext ctx);
        /// <summary>One physics step. Returns a timeline line for the flight log when something notable happened, else null.</summary>
        string Step(FlightContext ctx);
        /// <summary>Called once when the leg stops running (done, failed, or the Director took over).</summary>
        void End(FlightContext ctx);
    }

    /// <summary>Everything a leg needs to know about the mission; owned by the flight director for the whole flight.</summary>
    public sealed class FlightContext
    {
        public Vessel Vessel;
        public VesselControl Control;
        /// <summary>(text, achievementKey or null): appends to the project's flight log.</summary>
        public Action<string, string> Log;

        public CelestialBody Home;
        /// <summary>The mission's body (Mun, Duna, Kerbin for home missions).</summary>
        public CelestialBody Target;
        public MissionProfile Profile;
        /// <summary>Parking orbit altitude above Kerbin, metres (agency.cfg liveOrbitAltitude).</summary>
        public double ParkingAltitude = 85000;
        /// <summary>Orbit altitude above the target body for capture and for the ascent back, metres.</summary>
        public double TargetOrbitAltitude = 50000;
        /// <summary>Kerbin periapsis to aim for on the way home, metres.</summary>
        public double ReentryPeriapsis = 35000;
        /// <summary>Base missions: accepted terrain slope (tan) at the landing site.</summary>
        public double MaxSiteSlope = 0.3;

        /// <summary>Station modules: the vessel to rendezvous with and dock to (null when none).</summary>
        public Vessel DockTarget;
        public uint DockTargetId;
        /// <summary>KSA-65: names of the stranded crew a rescue flight picks up (empty for every other flight).</summary>
        public System.Collections.Generic.List<string> RescueNames = new System.Collections.Generic.List<string>();

        // --- Fuel depot (2026-10-29) ---
        /// <summary>The depot vessel to dock with (0 = none).</summary>
        public uint DepotTargetId;
        /// <summary>Stops at the depot on the way out and fills the tanks that launched empty.</summary>
        public bool Refuel;
        /// <summary>A tanker: tonnes of cargo to pump into the depot.</summary>
        public double CargoFuel;
        /// <summary>A tanker: the depot keepers who fly home in it (its own crew stays at the depot).</summary>
        public System.Collections.Generic.List<string> KeeperNames = new System.Collections.Generic.List<string>();
        /// <summary>The launch id our own parts carry, to tell them from the depot's once docked.</summary>
        public uint OwnLaunchId;
        /// <summary>The flight's dock target before the depot stop (a base module's own target), restored after it.</summary>
        public uint SavedDockTargetId;
        /// <summary>Tells the agency what the depot holds now, tonnes (after a pump either way).</summary>
        public Action<double> ReportDepot;
        /// <summary>After undocking our ship is a vessel of its own again: the director follows it; the depot keeps the rest.</summary>
        public Action<Vessel, Vessel> Undocked;

        /// <summary>The flying crew's numbers (decision 21): precision, margins, mistake chance. Never null.</summary>
        public KSA.Core.Flight.CrewFlightProfile Crew = KSA.Core.Flight.CrewFlightProfile.Neutral();
        /// <summary>Seeded per flight; used for pilot mistakes and the trait rule.</summary>
        public Random Rng = new Random();
        /// <summary>A spaceplane: the plane legs fly it (2026-10-28).</summary>
        public bool IsPlane;
        /// <summary>A suborbital hop with the delta-v for orbit goes for it (2026-10-30).</summary>
        public bool TryOrbit;
        /// <summary>Records why the flight is going wrong; the first cause wins (2026-10-30).</summary>
        public Action<KSA.Core.Flight.FailureCause, string> NoteCause;
        public void Cause(KSA.Core.Flight.FailureCause cause, string detail) { if (NoteCause != null) NoteCause(cause, detail); }
        /// <summary>Hands the controls to the Director (a plane's runway landing). Null when not possible.</summary>
        public Action HandToDirector;
        /// <summary>The crew's judgement call (trait rule or a model in character). The callback may fire at once or later. Null when unavailable.</summary>
        public Action<KSA.Core.Flight.DecisionRequest, Action<KSA.Core.Flight.CrewDecision>> AskCrew;

        /// <summary>Set by legs that leave the vessel in space on purpose (station core in orbit, module docked).</summary>
        public bool Delivered;
        /// <summary>Set by the landing leg: where the crew touched down.</summary>
        public string LandingNote = string.Empty;

        public double Now { get { return Planetarium.GetUniversalTime(); } }
        public bool IsHomeMission { get { return Target == null || Home == null || Target == Home; } }

        public void Say(string text) { if (Log != null) Log(text, null); }
        public void Say(string text, string achievement) { if (Log != null) Log(text, achievement); }
    }

    /// <summary>Runs a list of legs in order. Failed legs stop the program; the director decides what that means.</summary>
    public sealed class MissionProgram
    {
        private readonly List<IFlightLeg> _legs = new List<IFlightLeg>();
        private readonly FlightContext _ctx;
        private int _index = -1;
        private bool _engaged;
        private bool _pendingAdvance = true;

        public MissionProgram(FlightContext ctx) { _ctx = ctx; }

        public FlightContext Context { get { return _ctx; } }
        public IReadOnlyList<IFlightLeg> Legs { get { return _legs; } }
        public IFlightLeg Current { get { return _index >= 0 && _index < _legs.Count ? _legs[_index] : null; } }
        public bool Finished { get { return _index >= _legs.Count; } }
        public bool Failed { get; private set; }
        /// <summary>Which leg stopped the program and what it said (2026-10-30: halts only ever said "Program halted.").</summary>
        private string _failReason = string.Empty;
        public string Status { get { IFlightLeg l = Current; return l != null ? l.Status : (Failed ? (_failReason.Length > 0 ? "Program halted: " + _failReason : "Program halted.") : "Program complete."); } }
        private void NoteFailure(IFlightLeg l, string why) { _failReason = l.Name + ": " + (string.IsNullOrEmpty(why) ? "failed" : why); }
        public string Phase { get { IFlightLeg l = Current; return l != null ? l.Name : (Failed ? "Halted" : "Complete"); } }
        public int Index { get { return _index; } }

        public MissionProgram Add(IFlightLeg leg) { if (leg != null) _legs.Add(leg); return this; }

        public void Engage()
        {
            if (_engaged) return;
            _engaged = true;
            _ctx.Control.Engage();
            if (_pendingAdvance) Advance();
            else { IFlightLeg l = Current; if (l != null) { _ctx.Control.Throttle = 0; l.Begin(_ctx); } }
        }

        /// <summary>The leg that will run next (after Engage or the current one finishing), for logging.</summary>
        public int NextLegNumber { get { return _pendingAdvance ? _index + 2 : _index + 1; } }

        /// <summary>The Director took the stick: stop flying but remember where we were.</summary>
        public void Disengage()
        {
            if (!_engaged) return;
            _engaged = false;
            // A hand-over keeps the throttle where it was: some legs zero it as they end (2026-10-28).
            float throttle = FlightInputHandler.state != null ? FlightInputHandler.state.mainThrottle : 0f;
            IFlightLeg l = Current;
            if (l != null) l.End(_ctx);
            _ctx.Control.Disengage();
            if (FlightInputHandler.state != null) FlightInputHandler.state.mainThrottle = throttle;
        }

        /// <summary>Skip to the first leg of the given type (used when resuming a saved flight in a known state).</summary>
        public void SkipTo(Func<IFlightLeg, bool> match)
        {
            for (int i = 0; i < _legs.Count; i++) if (match(_legs[i])) { _index = i - 1; _pendingAdvance = true; return; }
        }

        private void Advance()
        {
            _pendingAdvance = false;
            // Legs whose Begin already reports Done (resumed past them) are skipped without a Step, so an ascent leg
            // never stages or throttles up in orbit.
            for (int guard = 0; guard < _legs.Count + 1; guard++)
            {
                _index++;
                IFlightLeg l = Current;
                if (l == null) return;
                _ctx.Control.Throttle = 0;
                try { l.Begin(_ctx); }
                catch (Exception e) { KsaCore.Log.Error("Leg " + l.Name + " failed to start: " + e); NoteFailure(l, "could not start (" + e.Message + ")"); Failed = true; _index = _legs.Count; return; }
                if (l.State == LegState.Running) return;
                l.End(_ctx);
                if (l.State == LegState.Failed) { NoteFailure(l, l.Status); Failed = true; _ctx.Control.Throttle = 0; _index = _legs.Count; return; }
            }
        }

        public string Step()
        {
            if (!_engaged || Failed || Finished) return null;
            IFlightLeg l = Current;
            if (l == null) return null;
            string line = null;
            if (l.State == LegState.Running) try { line = l.Step(_ctx); }
            catch (Exception e)
            {
                KsaCore.Log.Error("Leg " + l.Name + " threw: " + e);
                l.End(_ctx);
                NoteFailure(l, "fault (" + e.Message + ")");
                Failed = true;
                _ctx.Control.Throttle = 0;
                return "Flight program fault in " + l.Name + "; crew holding.";
            }
            if (l.State == LegState.Done)
            {
                l.End(_ctx);
                // Short of orbit there is no transfer to fly (2026-10-30: a broken rocket planned a Mun burn at 8 km): come home.
                AscentLeg ascent = l as AscentLeg;
                if (ascent != null && ascent.FellShort)
                {
                    SkipTo(x => x is ReentryLeg);
                    if (!_pendingAdvance) { NoteFailure(l, "no orbit reached"); Failed = true; _ctx.Control.Throttle = 0; _index = _legs.Count; return line; }
                    line = "No orbit: the flight plan is off, coming home.";
                }
                Advance();
                if (Finished) _ctx.Control.Throttle = 0;
            }
            else if (l.State == LegState.Failed)
            {
                l.End(_ctx);
                NoteFailure(l, l.Status);
                Failed = true;
                _ctx.Control.Throttle = 0;
                _index = _legs.Count;
            }
            return line;
        }
    }
}
