using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Shared UI plumbing for every KSA window:
    ///  - <see cref="Skin"/>: KSP's dialog skin with opaque window, box and scroll backgrounds, so nothing behind a
    ///    KSA window shows through (the stock skin's sprites are translucent by design).
    ///  - <see cref="Scrollable"/>: wraps a scroll list's items with a content sizer, which KSP's DialogGUIScrollList
    ///    needs to measure its content; without it the list stops scrolling before the last rows.
    ///  - <see cref="LabelHeight"/>: the height a wrapped label really needs.
    /// </summary>
    public static class KsaUi
    {
        public const float CharWidth = 6.6f;
        public const float ScrollTail = 48f;
        public static readonly Color Backdrop = new Color(0.11f, 0.12f, 0.15f, 1f);
        public static readonly Color PanelColor = new Color(0.14f, 0.15f, 0.18f, 1f);

        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static UISkinDef _skin;

        /// <summary>The KSA dialog skin: HighLogic.UISkin with opaque backgrounds. Built once; falls back to the stock skin on any error.</summary>
        public static UISkinDef Skin
        {
            get
            {
                if (_skin != null) return _skin;
                try { _skin = BuildSkin(HighLogic.UISkin); }
                catch (Exception e) { KsaCore.Log.Warn("Opaque skin unavailable, using the stock skin: " + e.Message); _skin = HighLogic.UISkin; }
                return _skin;
            }
        }

        private static UISkinDef BuildSkin(UISkinDef source)
        {
            var skin = new UISkinDef();
            foreach (FieldInfo f in typeof(UISkinDef).GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(skin, f.GetValue(source));
            skin.name = "KSA_Opaque";
            Sprite window = SolidSprite(Backdrop);
            Sprite panel = SolidSprite(PanelColor);
            skin.window = CloneStyle(source.window, window);
            skin.box = CloneStyle(source.box, panel);
            skin.scrollView = CloneStyle(source.scrollView, panel);
            KsaCore.Log.Info("Opaque dialog skin built");
            return skin;
        }

        private static UIStyle CloneStyle(UIStyle source, Sprite background)
        {
            var style = new UIStyle();
            if (source != null)
                foreach (FieldInfo f in typeof(UIStyle).GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(style, f.GetValue(source));
            style.normal = CloneState(source != null ? source.normal : null, background);
            style.highlight = CloneState(source != null ? source.highlight : null, background);
            style.active = CloneState(source != null ? source.active : null, background);
            style.disabled = CloneState(source != null ? source.disabled : null, background);
            return style;
        }

        private static UIStyleState CloneState(UIStyleState source, Sprite background)
        {
            var state = new UIStyleState();
            if (source != null)
                foreach (FieldInfo f in typeof(UIStyleState).GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(state, f.GetValue(source));
            state.background = background;
            return state;
        }

        private static Sprite SolidSprite(Color color)
        {
            var tex = new Texture2D(8, 8, TextureFormat.ARGB32, false);
            var px = new Color[64];
            for (int i = 0; i < px.Length; i++) px[i] = color;
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
        }

        /// <summary>Items for a scroll list's inner layout: a content sizer first (so the list measures its content), then the items, then padding.</summary>
        public static DialogGUIBase[] Scrollable(List<DialogGUIBase> items)
        {
            var list = new List<DialogGUIBase>(items.Count + 2);
            list.Add(new DialogGUIContentSizer(ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize, true));
            list.AddRange(items);
            list.Add(new DialogGUISpace(ScrollTail));
            return list.ToArray();
        }

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
