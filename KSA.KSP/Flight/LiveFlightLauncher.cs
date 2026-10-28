using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core;
using KSA.Core.Missions;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Puts a live project on the pad (M5). Resolves the craft (AI build or template), seats the project's crew,
    /// and hands the vessel to the flight director once the Flight scene is up.
    /// </summary>
    public static class LiveFlightLauncher
    {
        /// <summary>The launch the flight director should adopt when the Flight scene starts.</summary>
        public static LaunchSpec Pending { get; private set; }

        /// <summary>Profiles the built-in flight director can fly (KSA-51/52/53). Eve surface and cloud missions stay simulated.</summary>
        public static string Check(MissionType type)
        {
            if (type == null) return "Unknown mission.";
            bool eve = string.Equals(type.Body, "Eve", StringComparison.OrdinalIgnoreCase);
            bool jool = string.Equals(type.Body, "Jool", StringComparison.OrdinalIgnoreCase);
            if ((eve || jool) && (type.Profile == MissionProfile.Land || type.Profile == MissionProfile.Base)) return "The flight director has no " + type.Body + " atmospheric entry program; " + type.Name + " is simulated.";
            bool baseBody = string.Equals(type.Body, "Mun", StringComparison.OrdinalIgnoreCase) || string.Equals(type.Body, "Minmus", StringComparison.OrdinalIgnoreCase) || string.Equals(type.Body, "Duna", StringComparison.OrdinalIgnoreCase);
            if (type.Profile == MissionProfile.Base && !baseBody) return "Base modules fly live only to Mun, Minmus and Duna; " + type.Name + " is simulated.";
            return null;
        }

        public static void OnLaunchRequested(LaunchSpec spec)
        {
            Agency agency = KsaCore.Agency;
            if (agency == null || spec == null || spec.Project == null) return;
            string path = ResolveCraft(spec);
            string msg;
            if (path == null)
            {
                KsaCore.Log.Warn("No craft file for " + spec.Project.Name + " (AI: '" + spec.CraftPath + "', template: '" + spec.TemplateCraft + "'); simulating instead");
                agency.SimulateInstead(spec.Project.Id, out msg);
                ScreenMessages.PostScreenMessage("[KSA] No craft file for " + spec.Project.Name + "; " + msg, 8f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            try
            {
                ConfigNode craft = ConfigNode.Load(path);
                if (craft == null) throw new InvalidOperationException("craft file unreadable");
                VesselCrewManifest manifest = VesselCrewManifest.FromConfigNode(craft);
                int seated = Seat(manifest, spec.CrewNames);
                if (seated == 0) throw new InvalidOperationException("no seats for the crew (" + spec.CrewNames.Count + " assigned)");
                Pending = spec;
                KsaCore.Log.Info("Launching " + spec.Project.Name + " from " + path + " with " + seated + " crew");
                FlightDriver.StartWithNewLaunch(path, "Squad/Flags/default", "LaunchPad", manifest);
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("Live launch failed for " + spec.Project.Name + ": " + e.Message);
                Pending = null;
                agency.SimulateInstead(spec.Project.Id, out msg);
                ScreenMessages.PostScreenMessage("[KSA] Launch failed (" + e.Message + "); " + msg, 8f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public static void ClearPending() { Pending = null; }

        /// <summary>Absolute path of the craft to fly, or null when nothing usable exists.</summary>
        public static string ResolveCraft(LaunchSpec spec)
        {
            if (!string.IsNullOrEmpty(spec.CraftPath) && File.Exists(spec.CraftPath)) return spec.CraftPath;
            string t = spec.TemplateCraft ?? string.Empty;
            if (t.Length == 0) return null;
            string root = KSPUtil.ApplicationRootPath;
            string path;
            if (t.StartsWith("stock:", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(root, Path.Combine("Ships", Path.Combine("VAB", t.Substring(6).Trim() + ".craft")));
            else
                path = Path.Combine(root, Path.Combine("GameData", t.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? path : null;
        }

        private static int Seat(VesselCrewManifest manifest, List<string> names)
        {
            int seated = 0;
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (manifest == null || roster == null) return 0;
            // Clear whatever the craft file carried, then seat ours in order.
            foreach (PartCrewManifest pcm in manifest.GetCrewableParts())
            {
                ProtoCrewMember[] seats = pcm.GetPartCrew();
                for (int i = 0; i < seats.Length; i++) if (seats[i] != null) pcm.RemoveCrewFromSeat(i);
            }
            int next = 0;
            foreach (PartCrewManifest pcm in manifest.GetCrewableParts())
            {
                ProtoCrewMember[] seats = pcm.GetPartCrew();
                for (int i = 0; i < seats.Length && next < names.Count; i++)
                {
                    ProtoCrewMember kerbal = roster[names[next]];
                    next++;
                    if (kerbal == null) { KsaCore.Log.Warn("Crew member " + names[next - 1] + " not in the roster"); continue; }
                    if (kerbal.rosterStatus != ProtoCrewMember.RosterStatus.Available) { KsaCore.Log.Warn("Crew member " + kerbal.name + " is " + kerbal.rosterStatus + " (still aboard a station?); not seated"); i--; continue; }
                    pcm.AddCrewToSeat(kerbal, i);
                    seated++;
                }
            }
            return seated;
        }
    }
}
