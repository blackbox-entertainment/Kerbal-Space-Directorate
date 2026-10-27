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
