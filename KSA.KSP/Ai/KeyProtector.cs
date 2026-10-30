using System;
using System.Security.Cryptography;
using System.Text;

namespace KSA.KSP.Ai
{
    /// <summary>
    /// Keeps the API key out of plain text on disk. On Windows it uses DPAPI (the same user-scoped protection the
    /// Windows Credential Manager builds on): only the same Windows account on the same machine can decrypt it.
    /// On other platforms DPAPI is unavailable and the key is stored as typed, with a log line saying so.
    /// </summary>
    public static class KeyProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KSA.ksa_settings.v1");

        public const string Prefix = "dpapi:";

        /// <summary>Returns "dpapi:&lt;base64&gt;", or null when protection is not available here.</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            try
            {
                byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                // URL-safe Base64 (2026-10-30): KSP's config reader takes "//" for a comment, so a key whose Base64 held
                // "//" was cut short on load and lost ("Invalid length for a Base-64 char array").
                return Prefix + Convert.ToBase64String(cipher).Replace('+', '-').Replace('/', '_');
            }
            catch (Exception e)
            {
                KsaCore.Log.Warn("API key protection unavailable on this platform (" + e.GetType().Name + "); storing the key as typed.");
                return null;
            }
        }

        /// <summary>Accepts either a protected value or a plain key (older files). Returns the plain key, or empty when it cannot be read.</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length).Trim().Replace('-', '+').Replace('_', '/')), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("Could not decrypt the stored API key (different Windows user or machine?): " + e.Message + ". Re-enter it in AI Setup.");
                // Say it on screen too: without the key the AI quietly falls back to the local model (2026-10-28).
                try { ScreenMessages.PostScreenMessage("[KSA] Your OpenRouter key could not be read. Enter it again in AI Setup (Director's Office) and save.", 10f, ScreenMessageStyle.UPPER_CENTER); } catch { }
                return string.Empty;
            }
        }

        public static bool IsProtected(string stored) { return !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal); }
    }
}
