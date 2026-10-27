using System.Collections.Generic;
using KSA.Core.Personnel;

namespace KSA.KSP.Roster
{
    /// <summary>Reads the current save's hired crew from KSP and maps it onto Core records.</summary>
    public sealed class KspRosterSource : IRosterSource
    {
        public IReadOnlyList<CrewRecord> GetRoster()
        {
            var result = new List<CrewRecord>();
            Game game = HighLogic.CurrentGame;
            if (game == null || game.CrewRoster == null) return result;

            // CrewRoster.Crew yields only hired kerbals (type Crew), including dead and missing ones.
            foreach (ProtoCrewMember pcm in game.CrewRoster.Crew)
            {
                if (pcm == null) continue;
                result.Add(new CrewRecord(pcm.name, MapRole(pcm.trait), pcm.experienceLevel, MapStatus(pcm.rosterStatus)));
            }
            return result;
        }

        private static CrewRole MapRole(string trait)
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

        private static CrewStatus MapStatus(ProtoCrewMember.RosterStatus status)
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
