using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.KSP.Roster
{
    /// <summary>Reads the current save's hired crew from KSP and maps it onto Core records.</summary>
    public sealed class KspRosterSource : IRosterSource
    {
        public IReadOnlyList<CrewRecord> GetRoster()
        {
            return GetSnapshots();
        }

        /// <summary>Full snapshots: roster row plus traits and the achievement log.</summary>
        public List<CrewSnapshot> GetSnapshots()
        {
            var result = new List<CrewSnapshot>();
            Game game = HighLogic.CurrentGame;
            if (game == null || game.CrewRoster == null) return result;

            // CrewRoster.Crew yields only hired kerbals (type Crew), including dead and missing ones.
            foreach (ProtoCrewMember pcm in game.CrewRoster.Crew)
            {
                if (pcm == null) continue;
                result.Add(Snapshot(pcm));
            }
            return result;
        }

        public static CrewSnapshot Snapshot(ProtoCrewMember pcm)
        {
            var snap = new CrewSnapshot(pcm.name, MapRole(pcm.trait), pcm.experienceLevel, MapStatus(pcm.rosterStatus),
                                        pcm.courage, pcm.stupidity, Achievements(pcm));
            snap.PortraitKey = PortraitKey(pcm);
            return snap;
        }

        /// <summary>"db:&lt;SpritePath&gt;" when the game has a portrait texture for this kerbal, else "asset:&lt;suit icon path&gt;".</summary>
        public static string PortraitKey(ProtoCrewMember pcm)
        {
            if (pcm == null) return string.Empty;
            string sprite = pcm.SpritePath;
            if (!string.IsNullOrEmpty(sprite) && GameDatabase.Instance != null && GameDatabase.Instance.GetTexture(sprite, false) != null) return "db:" + sprite;
            string suit = pcm.GetKerbalIconSuitPath();
            return string.IsNullOrEmpty(suit) ? string.Empty : "asset:" + suit;
        }

        /// <summary>Achievement keys "Type:Body" from both the career log (past flights) and the current flight log.</summary>
        public static List<string> Achievements(ProtoCrewMember pcm)
        {
            var keys = new List<string>();
            if (pcm == null) return keys;
            AppendLog(pcm.careerLog, keys);
            AppendLog(pcm.flightLog, keys);
            return keys;
        }

        private static void AppendLog(FlightLog log, List<string> keys)
        {
            if (log == null || log.Entries == null) return;
            for (int i = 0; i < log.Entries.Count; i++)
            {
                FlightLog.Entry e = log.Entries[i];
                if (e == null || string.IsNullOrEmpty(e.type)) continue;
                string key = AchievementKey(e.type, e.target);
                if (!keys.Contains(key)) keys.Add(key);
            }
        }

        /// <summary>Normalises the game's log entry into Core's vocabulary (ExitVessel becomes EVA).</summary>
        public static string AchievementKey(string type, string body)
        {
            string t = type;
            if (t == "ExitVessel") t = "EVA";
            else if (t == "BoardVessel") t = "Board";
            return t + ":" + (string.IsNullOrEmpty(body) ? "-" : body);
        }

        public static CrewRole MapRole(string trait)
        {
            switch (trait)
            {
                case "Pilot": return CrewRole.Pilot;
                case "Engineer": return CrewRole.Engineer;
                case "Scientist": return CrewRole.Scientist;
                case "Tourist": return CrewRole.Tourist;
                default: return CrewRole.Unknown;
            }
        }

        public static CrewStatus MapStatus(ProtoCrewMember.RosterStatus status)
        {
            switch (status)
            {
                case ProtoCrewMember.RosterStatus.Assigned: return CrewStatus.Assigned;
                case ProtoCrewMember.RosterStatus.Dead: return CrewStatus.Dead;
                case ProtoCrewMember.RosterStatus.Missing: return CrewStatus.Missing;
                default: return CrewStatus.Available;
            }
        }
    }
}
