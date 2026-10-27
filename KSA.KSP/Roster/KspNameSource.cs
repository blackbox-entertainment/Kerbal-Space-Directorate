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

        public string NextName()
        {
            ProtoCrewMember.Gender gender = _rng.Next(2) == 0 ? ProtoCrewMember.Gender.Male : ProtoCrewMember.Gender.Female;
            string name = null;
            try { name = CrewGenerator.GetRandomName(gender, _rng); }
            catch (System.Exception e) { KsaCore.Log.Warn("Name generator failed: " + e.Message); }
            if (string.IsNullOrEmpty(name)) name = "Applicant " + _rng.Next(1000, 9999);
            Genders[name] = gender;
            return name;
        }

        public static ProtoCrewMember.Gender GenderFor(string name)
        {
            ProtoCrewMember.Gender g;
            return name != null && Genders.TryGetValue(name, out g) ? g : ProtoCrewMember.Gender.Male;
        }
    }
}
