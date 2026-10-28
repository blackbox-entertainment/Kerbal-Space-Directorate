using System.Collections.Generic;
using KSA.Core.Recruiting;

namespace KSA.KSP.Roster
{
    /// <summary>Applicant names in the game's own style, remembering the gender each name was generated for.</summary>
    public sealed class KspNameSource : INameSource
    {
        private static readonly Dictionary<string, ProtoCrewMember.Gender> Genders =
            new Dictionary<string, ProtoCrewMember.Gender>(System.StringComparer.OrdinalIgnoreCase);

        private readonly System.Random _rng = new System.Random();
        private readonly NameCatalog _names;

        public KspNameSource() : this(null) { }

        /// <summary>KSA-66: names come from names.cfg when it has any; else the game's own generator.</summary>
        public KspNameSource(NameCatalog names) { _names = names; }

        public string NextName()
        {
            ProtoCrewMember.Gender gender = _rng.Next(2) == 0 ? ProtoCrewMember.Gender.Male : ProtoCrewMember.Gender.Female;
            string name = null;
            if (_names != null && !_names.IsEmpty)
            {
                name = _names.Pick(_rng, gender == ProtoCrewMember.Gender.Female, n => RosterAccepts(n) && !Genders.ContainsKey(n));
                if (name != null) { Genders[name] = gender; return name; }
            }
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try { name = CrewGenerator.GetRandomName(gender, _rng); }
                catch (System.Exception e) { KsaCore.Log.Warn("Name generator failed: " + e.Message); break; }
                if (RosterAccepts(name)) break;
                name = null;
            }
            if (string.IsNullOrEmpty(name)) name = "Applicant " + _rng.Next(1000, 9999);
            Genders[name] = gender;
            return name;
        }

        /// <summary>
        /// KerbalRoster.AddCrewMember silently refuses a kerbal whose first name occurs inside any name already on the
        /// roster (applicants included). Such a hire would vanish at the next roster sync, so never offer that name.
        /// </summary>
        public static bool RosterAccepts(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null) return true;
            string first = CrewGenerator.RemoveLastName(name);
            if (string.IsNullOrEmpty(first)) return true;
            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember pcm = roster[i];
                if (pcm != null && pcm.name != null && pcm.name.IndexOf(first, System.StringComparison.Ordinal) >= 0) return false;
            }
            return true;
        }

        public static ProtoCrewMember.Gender GenderFor(string name)
        {
            ProtoCrewMember.Gender g;
            return name != null && Genders.TryGetValue(name, out g) ? g : ProtoCrewMember.Gender.Male;
        }
    }
}
