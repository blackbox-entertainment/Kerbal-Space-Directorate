using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>Small UI art: class badges from GameData/KSA/Textures and kerbal portraits from the game itself.</summary>
    public static class KsaIcons
    {
        private static readonly Dictionary<string, Texture> Cache = new Dictionary<string, Texture>();

        public const float BadgeSize = 20f;

        // ------------------------------------------------------------ class badges

        public static Texture ForRole(CrewRole role)
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
            Texture tex = ForRole(role);
            if (tex == null) return new DialogGUISpace(BadgeSize);
            return new DialogGUIImage(new Vector2(BadgeSize, BadgeSize), Vector2.zero, Color.white, tex);
        }

        // ------------------------------------------------------------ portraits

        /// <summary>
        /// The same picture the Astronaut Complex shows: the kerbal's SpritePath texture from GameDatabase,
        /// falling back to the stock suit icon from the game's asset bundles. Null if neither can be found.
        /// </summary>
        public static Texture Portrait(ProtoCrewMember pcm)
        {
            if (pcm == null) return null;

            string spritePath = pcm.SpritePath;
            if (!string.IsNullOrEmpty(spritePath))
            {
                Texture fromDb = Get(spritePath);
                if (fromDb != null) return fromDb;
            }

            string suitPath = pcm.GetKerbalIconSuitPath();
            if (string.IsNullOrEmpty(suitPath)) return null;
            Texture cached;
            if (Cache.TryGetValue("asset:" + suitPath, out cached)) return cached;
            Texture fromAssets = AssetBase.GetTexture(suitPath);
            if (fromAssets == null) KsaCore.Log.Warn("No portrait for " + pcm.name + " (sprite '" + spritePath + "', suit icon '" + suitPath + "')");
            Cache["asset:" + suitPath] = fromAssets;
            return fromAssets;
        }

        /// <summary>
        /// Portrait by crew name: from the game roster while they are on it, otherwise from the portrait key the agency
        /// stored on their profile (so dismissed, retired and dead crew keep their picture). Null if neither exists.
        /// </summary>
        public static Texture PortraitFor(string crewName)
        {
            if (string.IsNullOrEmpty(crewName)) return null;
            if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.CrewRoster != null && HighLogic.CurrentGame.CrewRoster.Exists(crewName))
                return Portrait(HighLogic.CurrentGame.CrewRoster[crewName]);
            Agency agency = KsaCore.Agency;
            CrewProfile p = agency != null ? agency.Crew.Get(crewName) : null;
            return p != null ? FromKey(p.PortraitKey) : null;
        }

        /// <summary>Resolves a stored portrait key ("db:path" or "asset:path").</summary>
        public static Texture FromKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key.StartsWith("db:")) return Get(key.Substring(3));
            if (key.StartsWith("asset:"))
            {
                Texture cached;
                if (Cache.TryGetValue(key, out cached)) return cached;
                Texture tex = AssetBase.GetTexture(key.Substring(6));
                Cache[key] = tex;
                return tex;
            }
            return null;
        }

        /// <summary>A portrait image for a dialog, or a blank spacer of the same size.</summary>
        public static DialogGUIBase PortraitImage(string crewName, float size)
        {
            Texture tex = PortraitFor(crewName);
            if (tex == null) return new DialogGUISpace(size);
            return new DialogGUIImage(new Vector2(size, size), Vector2.zero, Color.white, tex);
        }

        // ------------------------------------------------------------ internals

        private static Texture Get(string url)
        {
            Texture tex;
            if (Cache.TryGetValue(url, out tex)) return tex;
            tex = GameDatabase.Instance != null ? GameDatabase.Instance.GetTexture(url, false) : null;
            if (tex == null && url.StartsWith("KSA/")) KsaCore.Log.Warn("Icon '" + url + "' not found in GameDatabase");
            Cache[url] = tex;
            return tex;
        }
    }
}
