using KSA.Core.Personnel;
using KSA.Core.Recruiting;

namespace KSA.KSP.Roster
{
    /// <summary>Creates and removes real roster kerbals on the agency's behalf (Jira KSA-29, KSA-30).</summary>
    public static class KspCrewFactory
    {
        /// <summary>Adds a hired applicant to the stock roster so every other system in the game sees them.</summary>
        /// <summary>True while a hire is being added: roster syncs wait, so the half-added kerbal is not counted as gone.</summary>
        public static bool Creating { get; private set; }

        public static ProtoCrewMember Create(Applicant applicant, CrewProfile profile)
        {
            Creating = true;
            try { return CreateInner(applicant, profile); }
            finally { Creating = false; }
        }

        private static ProtoCrewMember CreateInner(Applicant applicant, CrewProfile profile)
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
            // The game renames only kerbals already in its roster; a new prototype is not, so ChangeName fails silently
            // and the random name sticks (the hire then vanished as "dismissed"). Name it before it joins the roster.
            SetName(pcm, applicant.Name);
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
            if (pcm.name != applicant.Name && !pcm.ChangeName(applicant.Name))
                KsaCore.Log.Error("Hire: " + applicant.Name + " joined the roster as " + pcm.name + " and could not be renamed.");
            KsaCore.Log.Info("Hire: added " + pcm.name + " (" + pcm.trait + ", level " + pcm.experienceLevel + ") to the roster");
            return pcm;
        }

        private static readonly System.Reflection.FieldInfo NameField =
            typeof(ProtoCrewMember).GetField("_name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>Sets the name of a kerbal that is not in the roster yet (ProtoCrewMember._name).</summary>
        private static void SetName(ProtoCrewMember pcm, string name)
        {
            if (NameField != null) { NameField.SetValue(pcm, name); return; }
            pcm.ChangeName(name); // fallback; the caller renames again once the kerbal is in the roster
        }

        /// <summary>
        /// Save repair: gives hires that reached the roster under a random name (before 2026-10-28) their own names back,
        /// so the next sync revives their records. Run before the first roster sync of a scene.
        /// </summary>
        public static void RepairMisnamedHires(KSA.Core.Agency agency)
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (agency == null || roster == null) return;
            foreach (System.Collections.Generic.KeyValuePair<string, string> pair in agency.MisnamedHires(new KspRosterSource().GetSnapshots()))
            {
                if (roster.Exists(pair.Value) || !roster.Exists(pair.Key)) continue;
                if (!roster.ChangeName(pair.Key, pair.Value)) { KsaCore.Log.Warn("Roster repair: could not rename " + pair.Key + " to " + pair.Value); continue; }
                agency.ForgetMisnamedHire(pair.Key, pair.Value);
                ScreenMessages.PostScreenMessage("[KSA] Roster repaired: " + pair.Key + " is " + pair.Value + " again.", 8f, ScreenMessageStyle.UPPER_CENTER);
            }
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
