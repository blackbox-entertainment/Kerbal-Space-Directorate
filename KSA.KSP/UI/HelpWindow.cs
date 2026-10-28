using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// The Help window: topic buttons on the left, the chosen page on the right. Pages come from Configs/help.cfg
    /// (KSA_HELP_TOPIC nodes, one "line" per paragraph, "# " for a heading), so they can be edited without a build.
    /// One window at a time; HelpWindow.Show(topicId) opens it at a page from any window's "?" button.
    /// </summary>
    public sealed class HelpWindow
    {
        public const string NodeName = "KSA_HELP_TOPIC";
        private const string DialogName = "KSA_Help";
        private const float Width = 900f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 200f, 520f, 820f); } }
        private static float ListHeight { get { return WindowHeight - 110f; } }
        private const float TopicWidth = 210f;
        private const float LineHeight = 22f;
        private const string Amber = "#ffd27f";

        private sealed class Topic
        {
            public string Id;
            public string Title;
            public int Order;
            public readonly List<string> Lines = new List<string>();
        }

        private static HelpWindow _open;

        private readonly List<Topic> _topics;
        private string _current;
        private PopupDialog _dialog;

        private HelpWindow(List<Topic> topics, string topicId)
        {
            _topics = topics;
            _current = topicId;
        }

        /// <summary>Opens Help at a page (or brings it to that page if already open).</summary>
        public static void Show(string topicId)
        {
            if (_open != null && _open._dialog != null)
            {
                if (!string.IsNullOrEmpty(topicId)) _open.Select(topicId);
                return;
            }
            List<Topic> topics = Load();
            _open = new HelpWindow(topics, topicId);
            _open.Spawn();
        }

        /// <summary>Closes Help if it is open (scene changes, the Office closing).</summary>
        public static void CloseIfOpen()
        {
            if (_open == null) return;
            HelpWindow w = _open;
            _open = null;
            if (w._dialog != null) { PopupDialog d = w._dialog; w._dialog = null; d.Dismiss(); }
        }

        /// <summary>A small "?" button that opens Help at a page, for any window's button row.</summary>
        public static DialogGUIButton Button(string topicId)
        {
            return new DialogGUIButton("?", () => Show(topicId), 30f, 30f, false);
        }

        private static List<Topic> Load()
        {
            var topics = new List<Topic>();
            try
            {
                foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes(NodeName))
                {
                    var t = new Topic { Id = n.GetValue("id") ?? string.Empty, Title = n.GetValue("title") ?? n.GetValue("id") ?? "?" };
                    int order;
                    t.Order = int.TryParse(n.GetValue("order"), out order) ? order : 999;
                    foreach (string line in n.GetValues("line")) t.Lines.Add(line);
                    topics.Add(t);
                }
            }
            catch (Exception e) { KsaCore.Log.Warn("Help: could not read " + NodeName + " nodes: " + e.Message); }
            topics.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            KsaCore.Log.Info("Help: " + topics.Count + " topic(s)");
            return topics;
        }

        private void Select(string topicId)
        {
            _current = topicId;
            if (_dialog == null) return;
            PopupDialog d = _dialog; _dialog = null; d.Dismiss();
            Spawn();
        }

        private Topic Current()
        {
            foreach (Topic t in _topics) if (string.Equals(t.Id, _current, StringComparison.OrdinalIgnoreCase)) return t;
            return _topics.Count > 0 ? _topics[0] : null;
        }

        private void Spawn()
        {
            Topic page = Current();
            float textWidth = Width - TopicWidth - 70f;

            var topicButtons = new List<DialogGUIBase>();
            foreach (Topic t in _topics)
            {
                string id = t.Id;
                bool sel = page != null && t == page;
                topicButtons.Add(new DialogGUIButton((sel ? "> " : "") + t.Title, () => Select(id), TopicWidth - 16f, 28f, false));
            }
            if (_topics.Count == 0) topicButtons.Add(new DialogGUILabel("No help topics found (Configs/help.cfg).", TopicWidth - 16f, LineHeight * 3));

            var items = new List<DialogGUIBase>();
            if (page != null)
            {
                items.Add(new DialogGUILabel("<size=16><b><color=" + Amber + ">" + page.Title + "</color></b></size>", textWidth, LineHeight + 6f));
                foreach (string line in page.Lines)
                {
                    if (line.StartsWith("# ", StringComparison.Ordinal))
                    {
                        items.Add(new DialogGUISpace(6f));
                        items.Add(new DialogGUILabel("<b>" + line.Substring(2) + "</b>", textWidth, LineHeight));
                    }
                    else
                    {
                        items.Add(new DialogGUILabel(line, textWidth, KsaUi.LabelHeight(line, textWidth, LineHeight)));
                        items.Add(new DialogGUISpace(2f));
                    }
                }
            }

            var topics = new DialogGUIScrollList(new Vector2(TopicWidth, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, KsaUi.Scrollable(topicButtons)));
            var text = new DialogGUIScrollList(new Vector2(Width - TopicWidth - 40f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(8, 8, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel("How Kerbal Space Directorate works. Pick a topic.", Width - 40f, LineHeight),
                new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft, topics, text),
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Help", KsaUi.Skin, new Rect(0.5f, 0.5f, Width, WindowHeight), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_open == this) _open = null;
        }
    }
}
