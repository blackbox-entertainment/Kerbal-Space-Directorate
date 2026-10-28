using KSA.Core.Personnel;
using KSA.Core.Recruiting;

namespace KSA.KSP.Roster
{
    /// <summary>Creates and removes real roster kerbals on the agency's behalf (Jira KSA-29, KSA-30).</summary>
    public static class KspCrewFactory
    {
        /// <summary>Adds a hired applicant to the stock roster so every other system in the game sees them.</summary>
        public static ProtoCrewMember Create(Applicant applicant, CrewProfile profile)
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null || applicant == null) return null;

            if (roster.Exists(applicant.Name))
            {
                ProtoCrewMember existing = roster[applicant.Name];
                existing.type = ProtoCrewMember.KerbalType.Crew;
                existing.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                KsaCore.Log.Info("Hire: " + applicant.Name + " already in roster; reactivated");
                return existing;
            }

            ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
            pcm.ChangeName(applicant.Name);
            pcm.gender = KspNameSource.GenderFor(applicant.Name);
            KerbalRoster.SetExperienceTrait(pcm, TraitName(applicant.Role));
            pcm.courage = applicant.Courage;
            pcm.stupidity = applicant.Stupidity;
            pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            pcm.type = ProtoCrewMember.KerbalType.Crew;

            // Prior experience: write the source's achievements into the career log so stock stars follow.
            foreach (string ach in applicant.Achievements)
            {
                string type = ach, body = string.Empty;
                int colon = ach.IndexOf(':');
                if (colon >= 0) { type = ach.Substring(0, colon); body = ach.Substring(colon + 1); }
                if (type == "EVA") type = "ExitVessel";
                if (body == "-" || body == "*") body = string.Empty;
                pcm.careerLog.AddEntry(type, body);
            }
            pcm.UpdateExperience();

            if (!roster.AddCrewMember(pcm))
            {
                // The game refuses a first name it already holds inside another name; the profile would vanish at the next sync.
                KsaCore.Log.Error("Hire: the game refused to add " + pcm.name + " to the roster (name clash with an existing kerbal). " + applicant.Name + " could not join.");
                ScreenMessages.PostScreenMessage("[KSA] The roster refused " + applicant.Name + " (name clash); please recruit someone else.", 8f, ScreenMessageStyle.UPPER_CENTER);
                return null;
            }
            KsaCore.Log.Info("Hire: added " + pcm.name + " (" + pcm.trait + ", level " + pcm.experienceLevel + ") to the roster");
            return pcm;
        }

        /// <summary>Removes a crew member who quit, retired, or was fired. The agency keeps their record.</summary>
        public static bool Remove(string name)
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null || string.IsNullOrEmpty(name) || !roster.Exists(name)) return false;
            ProtoCrewMember pcm = roster[name];
            if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
            {
                KsaCore.Log.Warn("Cannot remove " + name + " from the roster while assigned to a vessel");
                return false;
            }
            bool ok = roster.Remove(pcm);
            KsaCore.Log.Info("Roster: removed " + name + (ok ? string.Empty : " (failed)"));
            return ok;
        }

        public static string TraitName(CrewRole role)
        {
            switch (role)
            {
                case CrewRole.Engineer: return "Engineer";
                case CrewRole.Scientist: return "Scientist";
                case CrewRole.Tourist: return "Tourist";
                default: return "Pilot";
            }
        }
    }
}
