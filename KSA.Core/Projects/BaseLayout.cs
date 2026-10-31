using System;
using System.Collections.Generic;

namespace KSA.Core.Projects
{
    /// <summary>A section of a surface base as it is built on the ground.</summary>
    public enum BaseSection { Core = 0, Habitat = 1, Lab = 2, Greenhouse = 3, Power = 4, Industry = 5 }

    /// <summary>
    /// What a surface base looks like on the ground (2026-10-30, T: "I want it to look like an actual base on a planet";
    /// "bases grow with subsequent flights to them, at least so many before they are self sufficient, then they grow on
    /// their own"). Every delivered module adds its section, every resupply adds a habitat or greenhouse, and a colony
    /// adds habitats as it builds its own room. Pure: the host turns the sections into parts and puts them on the ground.
    /// </summary>
    public static class BaseLayout
    {
        /// <summary>Most sections drawn: beyond this the base is big enough to show the agency's work.</summary>
        public const int MaxSections = 20;
        /// <summary>Resupplies that add a section each; after these, only a colony grows.</summary>
        public const int ResupplySections = 6;
        /// <summary>Room (residents) a colony builds per extra habitat.</summary>
        public const int RoomPerHabitat = 6;

        /// <summary>
        /// The sections, in building order: the core first, then the delivered module roles, then one per resupply
        /// (habitat, greenhouse, habitat, ...), then a habitat per <see cref="RoomPerHabitat"/> of a colony's own room.
        /// Empty until a module is delivered.
        /// </summary>
        public static List<BaseSection> Sections(IList<string> deliveredRoles, int resupplies, int colonyRoom)
        {
            var list = new List<BaseSection>();
            if (deliveredRoles == null || deliveredRoles.Count == 0) return list;
            list.Add(BaseSection.Core);
            foreach (string role in deliveredRoles)
            {
                switch ((role ?? string.Empty).ToLowerInvariant())
                {
                    case "core": case "return": break; // the core is drawn first; the return rocket stays a rocket
                    case "habitation": list.Add(BaseSection.Habitat); break;
                    case "lab": list.Add(BaseSection.Lab); break;
                    case "power": list.Add(BaseSection.Power); break;
                    case "industry": list.Add(BaseSection.Industry); break;
                    default: list.Add(BaseSection.Habitat); break;
                }
            }
            for (int i = 0; i < Math.Min(resupplies, ResupplySections); i++) list.Add(i % 2 == 0 ? BaseSection.Habitat : BaseSection.Greenhouse);
            for (int i = 0; i < Math.Max(0, colonyRoom) / RoomPerHabitat; i++) list.Add(BaseSection.Habitat);
            if (list.Count > MaxSections) list.RemoveRange(MaxSections, list.Count - MaxSections);
            return list;
        }

        /// <summary>A short key for a layout: the host rebuilds the base when it changes.</summary>
        public static string Key(IList<BaseSection> sections)
        {
            var chars = new char[sections.Count];
            for (int i = 0; i < sections.Count; i++) chars[i] = "CHLGPI"[(int)sections[i]];
            return new string(chars);
        }
    }
}
