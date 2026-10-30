using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Science missions (KSA-58): runs every experiment aboard once per distinct experiment, situation and biome
    /// the vessel passes through, without the stock results dialog, and keeps the data in the pod so recovery pays.
    /// Not a leg: the flight director calls <see cref="Step"/> every physics step while a science mission flies, in
    /// parallel with whatever leg is running. Never throws into the director.
    /// </summary>
    public sealed class ScienceRunner
    {
        private sealed class Pending
        {
            public ModuleScienceExperiment Exp;
            public string Key;
            public string Title;
            public string Where;
            public double Since;
            public bool OnEva;
        }

        private const double ScanInterval = 1.0;        // seconds between availability scans
        private const double DeploySpacing = 0.6;       // seconds between two deploys (one dialog at a time)
        private const double PendingTimeout = 15.0;     // give up waiting for an experiment's data after this
        private const double DialogWatch = 20.0;        // dismiss result dialogs this long after our last deploy

        private readonly FlightContext _ctx;
        private readonly HashSet<string> _done = new HashSet<string>();
        private readonly List<Pending> _pending = new List<Pending>();
        private double _nextScan;
        private double _lastDeploy = -1e9;
        private double _points;
        private int _runs;

        public ScienceRunner(FlightContext ctx) { _ctx = ctx; }

        /// <summary>Recovery-value science points of everything collected so far (0 in sandbox).</summary>
        public double PointsCollected { get { return _points; } }
        /// <summary>Experiments run to completion by this runner.</summary>
        public int ExperimentsRun { get { return _runs; } }
        /// <summary>True while an experiment is still gathering data or one was deployed in the last few seconds; legs hold warp then.</summary>
        public bool Busy { get { return _pending.Count > 0 || Now - _lastDeploy < 3.0; } }

        private double Now { get { return Planetarium.GetUniversalTime(); } }

        /// <summary>One physics step. Returns a flight-log line when an experiment finished, else null.</summary>
        public string Step()
        {
            string line = null;
            try
            {
                double now = Now;
                CloseDialogs(now);
                line = FinishPending(now);
                if (now >= _nextScan && now - _lastDeploy >= DeploySpacing && _pending.Count == 0)
                {
                    _nextScan = now + ScanInterval;
                    Scan(now);
                }
            }
            catch (Exception e) { KsaCore.Log.Error("ScienceRunner.Step: " + e); }
            return line;
        }

        // ------------------------------------------------------------------ scanning and deploying

        private void Scan(double now)
        {
            Vessel v = _ctx != null ? _ctx.Vessel : null;
            Vessel active = FlightGlobals.ActiveVessel;
            // Crew science: while a kerbal is outside (SurfaceOpsLeg), their EVA report and surface sample count too.
            bool onEva = active != null && active != v && active.isEVA && active.state != Vessel.State.DEAD;
            Vessel subject = onEva ? active : v;
            if (subject == null || subject.state == Vessel.State.DEAD || subject.mainBody == null) return;
            if (subject.packed && !subject.loaded) return;

            ExperimentSituations sit = ScienceUtil.GetExperimentSituation(subject);
            CelestialBody body = subject.mainBody;
            string biome = BiomeOf(subject);

            if (Parts.KspLifeSupport.KerbalismInstalled)
            {
                // Power for steering comes first (2026-10-30): Kerbalism experiments drained a battery-less Mk1 pod and the
                // reaction wheel went dead, so the capsule came in nose first. Below 40% charge they pause; above 60% they run.
                double charge = VesselControl.ChargeFraction(v);
                if (charge >= 0 && charge < LowCharge) StopKerbalismExperiments(subject, charge);
                else if (charge < 0 || charge > LowCharge + 0.2) { _pausedForPower = false; StartKerbalismExperiments(subject); }
            }
            List<ModuleScienceExperiment> exps = subject.FindPartModulesImplementing<ModuleScienceExperiment>();
            if (exps == null) return;
            foreach (ModuleScienceExperiment exp in exps)
            {
                try
                {
                    if (!TryDeploy(exp, subject, sit, body, biome, onEva, now)) continue;
                    return; // one at a time; the next scan picks up the rest
                }
                catch (Exception e) { KsaCore.Log.Warn("ScienceRunner: experiment skipped: " + e.Message); }
            }
        }

        /// <summary>
        /// Kerbalism replaces the stock experiments with its own "Experiment" module, which records data over time and
        /// moves to the next subject by itself as the situation changes; recovery (or transmission) pays it. All the
        /// crew has to do is switch each one on, through its "Start" action. Read by name: no Kerbalism reference.
        /// </summary>
        private void StartKerbalismExperiments(Vessel subject)
        {
            foreach (Part p in subject.parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    if (m == null || m.moduleName != "Experiment") continue;
                    try
                    {
                        System.Reflection.PropertyInfo running = m.GetType().GetProperty("Running");
                        if (running != null && (bool)running.GetValue(m, null)) continue;
                        BaseAction start = m.Actions != null ? m.Actions["StartAction"] : null;
                        if (start == null) continue;
                        string id = (m.GetType().GetField("experiment_id") != null ? m.GetType().GetField("experiment_id").GetValue(m) as string : null) ?? p.partInfo.title;
                        string key = "kerbalism|" + p.flightID + "|" + id;
                        if (_kerbalismTries.ContainsKey(key) && _kerbalismTries[key] >= 3) continue; // it will not run here (needs something we lack)
                        start.Invoke(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate));
                        int n; _kerbalismTries.TryGetValue(key, out n); _kerbalismTries[key] = n + 1;
                        bool nowRunning = running == null || (bool)running.GetValue(m, null);
                        if (nowRunning && n == 0) { _runs++; KsaCore.Log.Info("ScienceRunner: started Kerbalism experiment " + id + " on " + p.partInfo.title); }
                        // Say why it would not run, once (2026-10-30: the goo silently refused with "no storage space").
                        if (!nowRunning && n == 2) KsaCore.Log.Info("ScienceRunner: Kerbalism experiment " + id + " on " + p.partInfo.title + " will not start" + KerbalismIssue(m) + ".");
                    }
                    catch (Exception e) { KsaCore.Log.Warn("ScienceRunner: Kerbalism experiment skipped: " + e.Message); }
                }
            }
        }

        private readonly Dictionary<string, int> _kerbalismTries = new Dictionary<string, int>();

        private const double LowCharge = 0.4;
        private bool _pausedForPower;

        /// <summary>Switches every running Kerbalism experiment off to save electric charge for steering (2026-10-30).</summary>
        private void StopKerbalismExperiments(Vessel subject, double charge)
        {
            int stopped = 0;
            foreach (Part p in subject.parts)
                foreach (PartModule m in p.Modules)
                {
                    if (m == null || m.moduleName != "Experiment") continue;
                    try
                    {
                        System.Reflection.PropertyInfo running = m.GetType().GetProperty("Running");
                        if (running != null && !(bool)running.GetValue(m, null)) continue;
                        BaseAction stop = m.Actions != null ? m.Actions["StopAction"] : null;
                        if (stop == null) continue;
                        stop.Invoke(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate));
                        stopped++;
                    }
                    catch (Exception e) { KsaCore.Log.Warn("ScienceRunner: could not pause a Kerbalism experiment: " + e.Message); }
                }
            if (stopped > 0 && !_pausedForPower)
            {
                _pausedForPower = true;
                KsaCore.Log.Info("ScienceRunner: charge at " + Math.Round(charge * 100) + "%: " + stopped + " Kerbalism experiment(s) paused to keep power for steering.");
            }
        }

        /// <summary>Kerbalism's own reason an experiment cannot run ("no storage space", "requires ..."), if it shows one.</summary>
        private static string KerbalismIssue(PartModule m)
        {
            try
            {
                foreach (string name in new[] { "issue", "Issue" })
                {
                    System.Reflection.FieldInfo f = m.GetType().GetField(name);
                    object v = f != null ? f.GetValue(m) : null;
                    if (v == null) { System.Reflection.PropertyInfo pr = m.GetType().GetProperty(name); v = pr != null ? pr.GetValue(m, null) : null; }
                    string s = v as string;
                    if (!string.IsNullOrEmpty(s)) return ": " + s;
                }
            }
            catch { }
            return string.Empty;
        }

        private bool TryDeploy(ModuleScienceExperiment exp, Vessel subject, ExperimentSituations sit, CelestialBody body, string biome, bool onEva, double now)
        {
            if (exp == null || exp.part == null || exp.experiment == null) return false;
            if (exp.Inoperable || exp.Deployed) return false;
            if (exp.useCooldown && exp.cooldownToGo > 0) return false;
            ScienceData[] held = exp.GetData();
            if (held != null && held.Length > 0) return false;

            ScienceExperiment se = exp.experiment;
            if (!se.IsAvailableWhile(sit, body)) return false;
            if (!se.IsUnlocked()) return false;
            if (exp.requiresEVASituation && !onEva) return false;

            bool biomeMatters = se.BiomeIsRelevantWhile(sit);
            string key = se.id + "|" + body.name + "|" + sit + "|" + (biomeMatters ? biome : string.Empty);
            if (_done.Contains(key)) return false;

            // Nothing left to learn there: skip rather than fill the pod with worthless repeats.
            if (ResearchAndDevelopment.Instance != null)
            {
                string subjectId = se.id + "@" + body.name + sit + (biomeMatters ? biome.Replace(" ", string.Empty) : string.Empty);
                ScienceSubject known = ResearchAndDevelopment.GetSubjectByID(subjectId);
                if (known != null && known.scientificValue < 0.02f) { _done.Add(key); return false; }
            }

            string msg = string.Empty;
            if (!ScienceUtil.RequiredUsageInternalAvailable(subject, exp.part, (ExperimentUsageReqs)exp.usageReqMaskInternal, se, ref msg)) return false;

            string title = !string.IsNullOrEmpty(se.experimentTitle) ? se.experimentTitle : se.id;
            string where = Describe(sit) + " over " + (biomeMatters && !string.IsNullOrEmpty(biome) ? DisplayBiome(body, biome) : body.bodyName);

            exp.DeployExperiment();
            _lastDeploy = now;
            _done.Add(key);
            _pending.Add(new Pending { Exp = exp, Key = key, Title = title, Where = where, Since = now, OnEva = onEva });
            return true;
        }

        // ------------------------------------------------------------------ collecting results

        private string FinishPending(double now)
        {
            string line = null;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                try
                {
                    ModuleScienceExperiment exp = p.Exp;
                    if (exp == null || exp.part == null || exp.vessel == null)
                    {
                        _pending.RemoveAt(i);
                        continue;
                    }
                    ScienceData[] data = exp.GetData();
                    bool hasData = data != null && data.Length > 0;
                    if (!hasData)
                    {
                        if (now - p.Since > PendingTimeout)
                        {
                            _pending.RemoveAt(i);
                            _done.Remove(p.Key); // never produced data; let a later pass try again
                            KsaCore.Log.Warn("ScienceRunner: " + p.Title + " produced no data within " + PendingTimeout + " s.");
                        }
                        continue;
                    }
                    double value = Value(data);
                    _points += value;
                    _runs++;
                    _pending.RemoveAt(i);
                    if (!p.OnEva) Stow(exp, data);
                    string text = "Ran " + p.Title + " (" + p.Where + "): +" + Math.Round(value, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " science aboard.";
                    if (_ctx != null) _ctx.Say(text, null);
                    if (line == null) line = text;
                }
                catch (Exception e)
                {
                    KsaCore.Log.Warn("ScienceRunner: result handling failed for " + p.Title + ": " + e.Message);
                    _pending.RemoveAt(i);
                }
            }
            return line;
        }

        /// <summary>Recovery value of the data, summed; zero when there is no R&amp;D (sandbox) or the subject is unknown.</summary>
        private static double Value(ScienceData[] data)
        {
            double sum = 0;
            try
            {
                if (ResearchAndDevelopment.Instance == null) return 0;
                foreach (ScienceData d in data)
                {
                    if (d == null || string.IsNullOrEmpty(d.subjectID)) continue;
                    ScienceSubject subject = ResearchAndDevelopment.GetSubjectByID(d.subjectID);
                    if (subject == null) continue;
                    float v = ResearchAndDevelopment.GetScienceValue(d.dataAmount, subject, 1f);
                    if (!float.IsNaN(v) && !float.IsInfinity(v)) sum += v;
                }
            }
            catch (Exception e) { KsaCore.Log.Warn("ScienceRunner: value lookup failed: " + e.Message); }
            return sum;
        }

        /// <summary>Moves the data into the vessel's science container (command pod) and re-arms a rerunnable experiment.</summary>
        private void Stow(ModuleScienceExperiment exp, ScienceData[] data)
        {
            try
            {
                Vessel v = exp.vessel;
                ModuleScienceContainer container = null;
                foreach (ModuleScienceContainer c in v.FindPartModulesImplementing<ModuleScienceContainer>())
                {
                    if (c == null || c.part == null) continue;
                    if (c.part == exp.part) continue;          // a pod's own experiment would move data into itself
                    if (c.evaOnlyStorage) continue;
                    container = c; break;
                }
                if (container == null) return; // no pod: the data stays in the experiment part, which still recovers fine

                var sources = new List<IScienceDataContainer> { exp };
                container.StoreData(sources, false, false, false);

                // Only a drained experiment can safely be re-armed; ResetExperiment would dump data still inside.
                ScienceData[] left = exp.GetData();
                bool drained = left == null || left.Length == 0;
                if (drained && exp.Deployed && !exp.Inoperable && exp.rerunnable) exp.ResetExperiment();
            }
            catch (Exception e) { KsaCore.Log.Warn("ScienceRunner: could not stow data from " + exp.part.partInfo.title + ": " + e.Message); }
        }

        /// <summary>DeployExperiment pops the stock results window after a short delay; close it and keep the data.</summary>
        private void CloseDialogs(double now)
        {
            if (now - _lastDeploy > DialogWatch) return;
            try
            {
                global::KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog dialog = global::KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog.Instance;
                if (dialog != null) dialog.Dismiss();
            }
            catch (Exception e) { KsaCore.Log.Warn("ScienceRunner: could not dismiss results dialog: " + e.Message); }
        }

        // ------------------------------------------------------------------ naming

        private static string BiomeOf(Vessel v)
        {
            try
            {
                if (v == null || v.mainBody == null) return string.Empty;
                string b = ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude);
                return b ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string DisplayBiome(CelestialBody body, string biome)
        {
            try
            {
                string d = ScienceUtil.GetBiomedisplayName(body, biome);
                return string.IsNullOrEmpty(d) ? biome : d;
            }
            catch { return biome; }
        }

        private static string Describe(ExperimentSituations sit)
        {
            switch (sit)
            {
                case ExperimentSituations.SrfLanded: return "landed";
                case ExperimentSituations.SrfSplashed: return "splashed down";
                case ExperimentSituations.FlyingLow: return "flying low";
                case ExperimentSituations.FlyingHigh: return "flying high";
                case ExperimentSituations.InSpaceLow: return "in low space";
                case ExperimentSituations.InSpaceHigh: return "in high space";
                default: return sit.ToString();
            }
        }
    }

    /// <summary>
    /// Science missions: hold where we are (throttle zero, SAS stability) for a while so the <see cref="ScienceRunner"/>
    /// can collect, warping through the wait like <see cref="HoldOrbitLeg"/> once the runner is idle. Works in orbit and
    /// on the surface.
    /// </summary>
    public sealed class ScienceSweepLeg : IFlightLeg
    {
        private const double SettleSeconds = 15;   // no warp at first: let the runner deploy what is available here

        private readonly double _seconds;
        private readonly ScienceRunner _runner;
        private double _until;
        private double _start;

        public ScienceSweepLeg(double seconds) : this(seconds, null) { }

        /// <param name="runner">When given, warp waits while the runner is busy with an experiment.</param>
        public ScienceSweepLeg(double seconds, ScienceRunner runner)
        {
            _seconds = seconds > 0 ? seconds : 60;
            _runner = runner;
        }

        public string Name { get { return "Science sweep"; } }
        public string Status { get; private set; }
        public LegState State { get; private set; }

        public void Begin(FlightContext ctx)
        {
            State = LegState.Running;
            Status = "Collecting science.";
            try
            {
                _start = ctx.Now;
                _until = _start + _seconds;
                ctx.Control.Throttle = 0;
                ctx.Control.ClearInputs();
                Vessel v = ctx.Vessel;
                bool down = v != null && LandingHelpers.IsDown(v);
                ctx.Control.Sas(VesselAutopilot.AutopilotMode.StabilityAssist, down || (v != null && v.mainBody != null && v.altitude < v.mainBody.atmosphereDepth));
            }
            catch (Exception e) { KsaCore.Log.Error("ScienceSweepLeg.Begin: " + e); }
        }

        public string Step(FlightContext ctx)
        {
            if (State != LegState.Running) return null;
            try
            {
                Vessel v = ctx.Vessel;
                if (LandingHelpers.IsDead(v)) { State = LegState.Failed; Status = "Vessel lost."; ctx.Say("Vessel lost during the science sweep."); return null; }
                double now = ctx.Now;
                if (now >= _until) { State = LegState.Done; Status = "Science sweep complete."; return null; }
                ctx.Control.Throttle = 0;
                double left = _until - now;
                Status = "Collecting science, " + Math.Round(left) + " s to go.";
                bool busy = _runner != null && _runner.Busy;
                bool settled = now - _start >= SettleSeconds;
                if (left > 60 && settled && !busy) ctx.Control.WarpTo(_until - 20);
                else if (busy && ctx.Control.IsWarping) ctx.Control.StopWarp();
            }
            catch (Exception e) { KsaCore.Log.Error("ScienceSweepLeg.Step: " + e); }
            return null;
        }

        public void End(FlightContext ctx)
        {
            try { ctx.Control.Throttle = 0; ctx.Control.StopWarp(); }
            catch (Exception e) { KsaCore.Log.Error("ScienceSweepLeg.End: " + e); }
        }
    }
}
