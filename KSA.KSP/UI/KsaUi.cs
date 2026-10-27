using System;
using System.Text.RegularExpressions;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Sizing helpers for DialogGUI. A DialogGUIScrollList only scrolls as far as the heights its children declare,
    /// so a label that wraps to three lines but declares one pushes the last rows out of reach. Every window sizes
    /// wrapped labels with <see cref="LabelHeight"/> and ends its list with <see cref="ScrollTail"/> of padding.
    /// </summary>
    public static class KsaUi
    {
        /// <summary>Average glyph width of the KSP UI font at the default size, pixels. Slightly pessimistic on purpose.</summary>
        public const float CharWidth = 6.6f;
        /// <summary>Padding appended to every scroll list so the final row is always reachable.</summary>
        public const float ScrollTail = 48f;

        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>Opaque backdrop colour for every KSA window (the stock skin's window sprite is translucent).</summary>
        public static readonly UnityEngine.Color Backdrop = new UnityEngine.Color(0.11f, 0.12f, 0.15f, 1f);

        /// <summary>
        /// Puts a solid panel behind a spawned dialog's content so the space centre does not show through.
        /// Safe to call on every spawn; does nothing when the dialog is null.
        /// </summary>
        public static void MakeOpaque(PopupDialog dialog)
        {
            if (dialog == null) return;
            try
            {
                UnityEngine.Transform root = dialog.transform;
                if (root.Find("KSA_Backdrop") != null) return;
                var go = new UnityEngine.GameObject("KSA_Backdrop", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Image));
                var rt = go.GetComponent<UnityEngine.RectTransform>();
                rt.SetParent(root, false);
                rt.anchorMin = UnityEngine.Vector2.zero;
                rt.anchorMax = UnityEngine.Vector2.one;
                rt.offsetMin = new UnityEngine.Vector2(2f, 2f);
                rt.offsetMax = new UnityEngine.Vector2(-2f, -2f);
                rt.SetAsFirstSibling();
                var img = go.GetComponent<UnityEngine.UI.Image>();
                img.color = Backdrop;
                img.raycastTarget = false;
            }
            catch (Exception e) { KsaCore.Log.Warn("Could not make a dialog opaque: " + e.Message); }
        }

        /// <summary>Height a label needs for its text at the given width: one line per wrap, rich-text tags ignored.</summary>
        public static float LabelHeight(string text, float width, float lineHeight)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return lineHeight;
            string plain = Tags.Replace(text, string.Empty);
            int perLine = Math.Max(8, (int)(width / CharWidth));
            int lines = 0;
            foreach (string paragraph in plain.Split('\n'))
                lines += Math.Max(1, (int)Math.Ceiling(paragraph.Length / (double)perLine));
            return lineHeight * lines;
        }
    }
}
