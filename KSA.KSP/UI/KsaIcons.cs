using System.Collections.Generic;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>Small UI art loaded once from GameData/KSA/Textures. Missing files degrade to "no icon", never a crash.</summary>
    public static class KsaIcons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public const float BadgeSize = 20f;

        public static Texture2D ForRole(CrewRole role)
        {
            switch (role)
            {
                case CrewRole.Pilot: return Get("KSA/Textures/role_pilot");
                case CrewRole.Engineer: return Get("KSA/Textures/role_engineer");
                case CrewRole.Scientist: return Get("KSA/Textures/role_scientist");
                case CrewRole.Tourist: return Get("KSA/Textures/role_tourist");
                default: return Get("KSA/Textures/role_unknown");
            }
        }

        /// <summary>A badge image for a dialog row, or a blank spacer of the same width when the texture is missing.</summary>
        public static DialogGUIBase Badge(CrewRole role)
        {
            Texture2D tex = ForRole(role);
            if (tex == null) return new DialogGUISpace(BadgeSize);
            return new DialogGUIImage(new Vector2(BadgeSize, BadgeSize), Vector2.zero, Color.white, tex);
        }

        private static Texture2D Get(string url)
        {
            Texture2D tex;
            if (Cache.TryGetValue(url, out tex)) return tex;
            tex = GameDatabase.Instance != null ? GameDatabase.Instance.GetTexture(url, false) : null;
            if (tex == null) KsaCore.Log.Warn("Icon '" + url + "' not found in GameDatabase");
            Cache[url] = tex;
            return tex;
        }
    }
}
