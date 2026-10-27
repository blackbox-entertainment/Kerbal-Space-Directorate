using System;
using System.Diagnostics;
using System.IO;
using KSA.Core.Ai;
using UnityEngine;

namespace KSA.KSP.Ai
{
    /// <summary>
    /// Optional: starts a local model server (llama-server, LM Studio CLI...) with the game when the local endpoint is
    /// not answering, and stops it again when KSP quits. Ollama runs as its own background service and needs none of this.
    /// Configured in ksa_settings.cfg LOCAL: autoStart, serverExe, serverArgs.
    /// </summary>
    public static class LocalServerLauncher
    {
        private static Process _process;
        private static bool _checked;

        public static bool StartedByUs { get { return _process != null && !_process.HasExited; } }

        /// <summary>Call once settings are loaded. Probes /v1/models; launches the server only when nothing answers.</summary>
        public static void EnsureRunning(AiSettings settings)
        {
            if (settings == null || _checked) return;
            _checked = true;
            if (!settings.AutoStartLocalServer || !settings.LocalEnabled) return;
            if (string.IsNullOrEmpty(settings.LocalServerExe)) { KsaCore.Log.Warn("LOCAL autoStart is on but serverExe is empty"); return; }
            if (!File.Exists(settings.LocalServerExe)) { KsaCore.Log.Warn("LOCAL serverExe not found: " + settings.LocalServerExe); return; }
            string url = settings.LocalEndpoint ?? string.Empty;
            int cut = url.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase);
            string modelsUrl = (cut > 0 ? url.Substring(0, cut) : url.TrimEnd('/')) + "/models";
            UnityWebTransport.Instance.GetText(modelsUrl, 3, (ok, body) =>
            {
                if (ok) { KsaCore.Log.Info("Local model server already answering at " + modelsUrl); return; }
                Launch(settings);
            });
        }

        private static void Launch(AiSettings settings)
        {
            try
            {
                var psi = new ProcessStartInfo(settings.LocalServerExe, settings.LocalServerArgs ?? string.Empty)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(settings.LocalServerExe) ?? string.Empty,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                _process = Process.Start(psi);
                KsaCore.Log.Info("Started local model server: " + settings.LocalServerExe + " " + settings.LocalServerArgs + " (pid " + (_process != null ? _process.Id : 0) + ")");
                ScreenMessages.PostScreenMessage("[KSA] Starting local AI server (" + Path.GetFileName(settings.LocalServerExe) + ")...", 6f, ScreenMessageStyle.UPPER_CENTER);
                Application.quitting += Stop;
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("Could not start the local model server: " + e.Message);
                _process = null;
            }
        }

        /// <summary>Stops the server if we started it. Safe to call repeatedly.</summary>
        public static void Stop()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited) { _process.Kill(); KsaCore.Log.Info("Stopped the local model server (pid " + _process.Id + ")"); }
            }
            catch (Exception e) { KsaCore.Log.Warn("Could not stop the local model server: " + e.Message); }
            finally { _process.Dispose(); _process = null; }
        }

        /// <summary>Settings changed in AI Setup: allow another probe/launch.</summary>
        public static void Reset() { _checked = false; }
    }
}
