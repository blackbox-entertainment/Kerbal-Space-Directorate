using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Draws simple charts into textures for DialogGUIImage, since KSP's dialog toolkit has no chart widget.
    /// Lines and bars only; axis values and legends are plain labels placed next to the image by the caller.
    /// </summary>
    public static class ChartTexture
    {
        public sealed class Series
        {
            public string Name;
            public Color Color;
            public double[] Values;
            public Series(string name, Color color, double[] values) { Name = name; Color = color; Values = values; }
        }

        public static readonly Color Background = new Color(0.08f, 0.10f, 0.13f, 0.95f);
        public static readonly Color Grid = new Color(0.25f, 0.28f, 0.33f, 1f);
        public static readonly Color Axis = new Color(0.6f, 0.65f, 0.7f, 1f);
        public static readonly Color[] Palette =
        {
            new Color(0.98f, 0.85f, 0.25f), // KSA yellow
            new Color(0.35f, 0.70f, 1.0f),
            new Color(0.95f, 0.45f, 0.35f),
            new Color(0.45f, 0.85f, 0.55f),
            new Color(0.80f, 0.55f, 0.95f)
        };

        /// <summary>Line chart. Each series has one value per x step; NaN leaves a gap. Returns min/max used for the y axis.</summary>
        public static Texture2D Lines(int width, int height, IList<Series> series, out double yMin, out double yMax, bool zeroBased = false)
        {
            var tex = NewTexture(width, height);
            yMin = double.MaxValue; yMax = double.MinValue;
            int steps = 0;
            foreach (Series s in series)
            {
                if (s.Values == null) continue;
                steps = Math.Max(steps, s.Values.Length);
                foreach (double v in s.Values) if (!double.IsNaN(v)) { if (v < yMin) yMin = v; if (v > yMax) yMax = v; }
            }
            if (yMin == double.MaxValue) { yMin = 0; yMax = 1; }
            if (zeroBased && yMin > 0) yMin = 0;
            if (yMax - yMin < 1e-9) { yMax = yMin + Math.Max(1, Math.Abs(yMin) * 0.1); }
            double pad = (yMax - yMin) * 0.08;
            yMin -= pad; yMax += pad;
            if (zeroBased && yMin < 0 && yMax > 0 && yMin > -pad * 1.01) yMin = 0;

            int left = 8, right = width - 8, bottom = 8, top = height - 8;
            DrawGrid(tex, left, right, bottom, top, 4);
            // Zero line when the range crosses zero.
            if (yMin < 0 && yMax > 0) HLine(tex, left, right, Y(0, yMin, yMax, bottom, top), Axis);

            int colorIndex = 0;
            foreach (Series s in series)
            {
                Color c = s.Color.a > 0 ? s.Color : Palette[colorIndex % Palette.Length];
                colorIndex++;
                if (s.Values == null || s.Values.Length == 0) continue;
                int n = s.Values.Length;
                int prevX = -1, prevY = -1;
                for (int i = 0; i < n; i++)
                {
                    double v = s.Values[i];
                    if (double.IsNaN(v)) { prevX = -1; continue; }
                    int x = steps <= 1 ? (left + right) / 2 : left + (int)Math.Round((right - left) * (double)i / (steps - 1));
                    int y = Y(v, yMin, yMax, bottom, top);
                    if (prevX >= 0) Line(tex, prevX, prevY, x, y, c, 2);
                    Dot(tex, x, y, 3, c);
                    prevX = x; prevY = y;
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Horizontal bars, one per row, longest first is the caller's choice. Values must be >= 0 (negatives are clamped).</summary>
        public static Texture2D Bars(int width, int height, IList<double> values, IList<Color> colors, out double max)
        {
            var tex = NewTexture(width, height);
            max = 0;
            foreach (double v in values) if (v > max) max = v;
            if (max <= 0) max = 1;
            int n = values.Count;
            int left = 8, right = width - 8;
            int rowH = n > 0 ? (height - 16) / n : height;
            int bar = Math.Max(4, rowH - 6);
            for (int i = 0; i < n; i++)
            {
                double v = Math.Max(0, values[i]);
                int len = (int)Math.Round((right - left) * v / max);
                int y0 = height - 8 - (i + 1) * rowH + (rowH - bar) / 2;
                Color c = colors != null && i < colors.Count ? colors[i] : Palette[i % Palette.Length];
                for (int y = y0; y < y0 + bar; y++)
                    for (int x = left; x < left + len; x++) Put(tex, x, y, c);
                HLine(tex, left, right, y0 - (rowH - bar) / 2, Grid);
            }
            tex.Apply();
            return tex;
        }

        public static string Hex(Color c)
        {
            return "#" + ((int)(c.r * 255)).ToString("X2") + ((int)(c.g * 255)).ToString("X2") + ((int)(c.b * 255)).ToString("X2");
        }

        // ------------------------------------------------------------------ drawing

        private static Texture2D NewTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            var fill = new Color[width * height];
            for (int i = 0; i < fill.Length; i++) fill[i] = Background;
            tex.SetPixels(fill);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private static void DrawGrid(Texture2D tex, int left, int right, int bottom, int top, int rows)
        {
            for (int r = 0; r <= rows; r++) HLine(tex, left, right, bottom + (top - bottom) * r / rows, Grid);
            VLine(tex, left, bottom, top, Axis);
            HLine(tex, left, right, bottom, Axis);
        }

        private static int Y(double v, double min, double max, int bottom, int top)
        {
            double t = (v - min) / (max - min);
            if (t < 0) t = 0; if (t > 1) t = 1;
            return bottom + (int)Math.Round((top - bottom) * t);
        }

        private static void Put(Texture2D tex, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) return;
            tex.SetPixel(x, y, c);
        }

        private static void HLine(Texture2D tex, int x0, int x1, int y, Color c) { for (int x = x0; x <= x1; x++) Put(tex, x, y, c); }
        private static void VLine(Texture2D tex, int x, int y0, int y1, Color c) { for (int y = y0; y <= y1; y++) Put(tex, x, y, c); }

        private static void Dot(Texture2D tex, int cx, int cy, int r, Color c)
        {
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++) if (x * x + y * y <= r * r) Put(tex, cx + x, cy + y, c);
        }

        private static void Line(Texture2D tex, int x0, int y0, int x1, int y1, Color c, int thickness)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int guard = 0;
            while (guard++ < 100000)
            {
                for (int t = 0; t < thickness; t++) { Put(tex, x0, y0 + t, c); Put(tex, x0 + t, y0, c); }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
