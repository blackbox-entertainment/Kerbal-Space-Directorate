using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KSA.Core;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.KSP.Flight;
using UnityEngine;

namespace KSA.KSP.Testing
{
    /// <summary>
    /// The flight test bench (2026-10-30, T: "the kerbals have to be able to fly to any planet and eventually land on them
    /// and return. that is mvp mod"). In a sandbox save it flies a ladder of missions live, one after another, with no one
    /// at the controls: the engineers design each rocket from every part, the pad gets it at once, the crew flies it with no
    /// pilot mistakes and no judgement calls, and the result goes to GameData/KSA/PluginData/FlightTests/flight-tests.txt.
    /// The ladder stops at the first failure, so the report always names the first thing to fix. Only in sandbox games, and
    /// never with the AI Director on.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public sealed class FlightTestBench : MonoBehaviour
    {
        /// <summary>The ladder: each rung must pass before the next is tried.</summary>
        public static readonly string[] Ladder =
        {
            "KerbinOrbit", "MunFlyby", "MunOrbit", "MunLanding", "MinmusOrbit", "MinmusLanding", "DunaFlyby", "DunaOrbit", "DunaLanding"
        };

        /// <summary>Real minutes a single test may take before it is called stuck.</summary>
        private const double TimeoutMinutes = 60;

        public static bool Active { get; private set; }
        public static int Index { get; private set; }
        public static readonly List<string> Results = new List<string>();
        private static string _projectId = string.Empty;
        private static double _startedReal;
        private static double _haltSince = -1;
        private static double _lastPoke;

        public static string ReportPath
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", Path.Combine("KSA", Path.Combine("PluginData", Path.Combine("FlightTests", "flight-tests.txt"))))); }
        }

        /// <summary>Why the bench cannot run here, or null.</summary>
        public static string Blocker()
        {
            if (HighLogic.CurrentGame == null || KsaCore.Agency == null) return "no game loaded";
            if (HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX) return "flight tests run only in a sandbox save (make a new Sandbox game, for example \"KSA test\")";
            if (KsaCore.Agency.DirectorOn) return "switch the AI Director off first";
            return null;
        }

        /// <summary>Starts the ladder at <paramref name="from"/> (0 = the first rung).</summary>
        public static bool Start(int from, out string message)
        {
            message = Blocker();
            if (message != null) return false;
            Active = true;
            Index = Math.Max(0, Math.Min(Ladder.Length - 1, from));
            _projectId = string.Empty;
            Results.Clear();
            Write("=== Flight tests started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ", KSA v" + Core.CoreInfo.Version + ", from " + Ladder[Index] + " ===");
            message = "Flight tests started at " + Ladder[Index] + ". Leave the game running; results go to " + ReportPath;
            KsaCore.Log.Info("Flight test bench: " + message);
            return true;
        }

        public static void Stop(string why)
        {
            if (!Active) return;
            Active = false;
            Write("=== Stopped: " + why + " ===");
            KsaCore.Log.Info("Flight test bench stopped: " + why);
            ScreenMessages.PostScreenMessage("[KSA] Flight tests stopped: " + why, 10f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static void Write(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.AppendAllText(ReportPath, line + Environment.NewLine);
            }
            catch (Exception e) { KsaCore.Log.Warn("Flight test report not written: " + e.Message); }
        }

        private static void Record(bool pass, string mission, string detail)
        {
            double minutes = (Time.realtimeSinceStartup - _startedReal) / 60.0;
            string line = (pass ? "PASS " : "FAIL ") + mission + ": " + detail + " (" + minutes.ToString("0.0", CultureInfo.InvariantCulture) + " real min)";
            Results.Add(line);
            Write(line);
            KsaCore.Log.Info("Flight test: " + line);
            ScreenMessages.PostScreenMessage("[KSA] Flight test " + line, 10f, ScreenMessageStyle.UPPER_CENTER);
        }

        // ------------------------------------------------------------------ driving

        private void Update()
        {
            if (!Active) return;
            Agency agency = KsaCore.Agency;
            if (agency == null) return;
            if (agency.DirectorOn) { Stop("the AI Director was switched on"); return; }
            double now = Time.realtimeSinceStartup;
            if (now - _lastPoke < 1.0) return;
            _lastPoke = now;
            try
            {
                if (HighLogic.LoadedScene == GameScenes.SPACECENTER) SpaceCenterStep(agency, now);
                else if (HighLogic.LoadedScene == GameScenes.FLIGHT) FlightStep(agency, now);
            }
            catch (Exception e) { KsaCore.Log.Error("Flight test bench: " + e); Stop("the bench itself failed (" + e.Message + ")"); }
        }

        private static void SpaceCenterStep(Agency agency, double now)
        {
            Project p = _projectId.Length > 0 ? agency.GetProject(_projectId) : null;
            if (p == null)
            {
                if (Index >= Ladder.Length) { Stop("every rung passed"); return; }
                StartTest(agency, now);
                return;
            }
            string mission = Ladder[Index];
            if (now - _startedReal > TimeoutMinutes * 60) { Record(false, mission, "timed out in " + p.Status); Stop(mission + " timed out"); return; }
            switch (p.Status)
            {
                case ProjectStatus.Design:
                case ProjectStatus.Build:
                    // No waiting for design and build days: move the phase start back so the next tick finishes it.
                    p.PhaseStartDay = agency.CurrentDay - 10000;
                    KsaCore.TickAgency("flight test");
                    break;
                case ProjectStatus.Ready:
                    if (p.Mode != MissionMode.WatchLive) { Record(false, mission, "would fly simulated: " + string.Join("; ", p.Notes.ToArray())); Stop(mission + " cannot fly live"); return; }
                    {
                        string live = agency.LiveBlocker(p);
                        if (live != null) { Record(false, mission, "cannot fly live: " + live); Stop(mission + " cannot fly live"); return; }
                    }
                    agency.State.LastLaunchDay = -1; // one pad, no queue for a test
                    break;
                case ProjectStatus.Complete:
                case ProjectStatus.Failed:
                case ProjectStatus.Cancelled:
                    Finish(agency, p, mission);
                    break;
            }
        }

        private static void StartTest(Agency agency, double now)
        {
            string mission = Ladder[Index];
            MissionType type = agency.Catalogs.Missions.Get(mission);
            if (type == null) { Record(false, mission, "no such mission in missions.cfg"); Stop("unknown mission " + mission); return; }
            agency.Ledger.SetBalance(100000000); // a test never stops for money
            EnsureCrew(Math.Max(1, type.CrewSize) + 1);
            string msg;
            Project p = agency.CreateProject("Test " + type.Name, type.Id, null, null, null, null, MissionMode.WatchLive, out msg);
            _startedReal = now;
            _haltSince = -1;
            if (p == null) { Record(false, mission, "could not start the project: " + msg); Stop(mission + " could not start"); return; }
            _projectId = p.Id;
            Write("--- " + mission + " (" + type.Name + "), project " + p.Id + " ---");
            ScreenMessages.PostScreenMessage("[KSA] Flight test " + (Index + 1) + " of " + Ladder.Length + ": " + type.Name, 8f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static void Finish(Agency agency, Project p, string mission)
        {
            MissionReport r = p.Report;
            bool pass = p.Status == ProjectStatus.Complete && r != null && (r.IsSuccess || r.Outcome == MissionOutcome.PartialSuccess) && r.Mode == MissionMode.WatchLive && r.Deaths.Count == 0;
            string detail = r == null ? p.Status + (p.Notes.Count > 0 ? ": " + p.Notes[p.Notes.Count - 1] : string.Empty) : Agency.OutcomeText(r.Outcome) + (r.Mode != MissionMode.WatchLive ? ", flown simulated" : string.Empty)
                + (r.Deaths.Count > 0 ? ", " + r.Deaths.Count + " died" : string.Empty)
                + (r.CauseDetail.Length > 0 ? ", cause: " + r.CauseDetail : string.Empty)
                + (r.Achievements.Count > 0 ? ", earned " + string.Join(" ", r.Achievements.ToArray()) : string.Empty);
            Record(pass, mission, detail);
            _projectId = string.Empty;
            if (!pass) { Stop(mission + " failed"); return; }
            Index++;
        }

        private static void FlightStep(Agency agency, double now)
        {
            if (_projectId.Length == 0) return;
            string mission = Ladder[Index];
            if (now - _startedReal > TimeoutMinutes * 60)
            {
                KsaFlightDirector fd = KsaFlightDirector.Instance;
                Record(false, mission, "timed out in flight, leg " + (fd != null ? fd.LegName : "?"));
                Stop(mission + " timed out");
                return;
            }
            KsaFlightDirector d = KsaFlightDirector.Instance;
            string halt = d != null ? d.HaltReason : null;
            if (halt == null) { _haltSince = -1; return; }
            // A halt gets the crew's one retry; one still standing after 20 s is the result.
            if (_haltSince < 0) { _haltSince = now; return; }
            if (now - _haltSince < 20) return;
            Record(false, mission, "flight program halted: " + halt);
            Stop(mission + " halted in flight (the ship is left where it is)");
        }

        /// <summary>Adds pilots to the game's roster until there are enough free kerbals for the test.</summary>
        private static void EnsureCrew(int needed)
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null) return;
            int free = 0;
            foreach (ProtoCrewMember c in roster.Crew) if (c.rosterStatus == ProtoCrewMember.RosterStatus.Available) free++;
            bool added = false;
            for (; free < needed; free++)
            {
                ProtoCrewMember k = roster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                if (k == null) break;
                KerbalRoster.SetExperienceTrait(k, "Pilot");
                k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                added = true;
            }
            if (added) KsaCore.SyncRoster("flight test crew");
        }
    }
}
