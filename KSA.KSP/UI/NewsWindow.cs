using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Narrative;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// The Press Room (M6): every press release, memo, review, obituary and dispatch the agency has published,
    /// newest first, with a filter by kind. Items unread when the window opened are flagged NEW for this view;
    /// opening the window marks everything read. Rebuilds itself when the agency publishes or rewrites an item.
    /// </summary>
    public sealed class NewsWindow
    {
        private const string DialogName = "KSA_PressRoom";
        private const float Width = 720f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 680f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 140f; } }
        private const float LineHeight = 22f;
        private const float ButtonHeight = 26f;
        private const float ItemGap = 8f;
        private const int ItemsShown = 60;
        private const int WrapWidth = 100;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private readonly HashSet<int> _newIds = new HashSet<int>();
        private PopupDialog _dialog;
        private string _status = string.Empty;
        private NewsKind? _filter;
        private bool _subscribed;

        public NewsWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
            _agency.NewsPublished += OnNewsPublished;
            _subscribed = true;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            // Remember what was unread for this view's NEW flags, then clear the agency's unread count.
            _newIds.Clear();
            foreach (NewsItem n in _agency.News.Items) if (!n.Read) _newIds.Add(n.Id);
            _agency.MarkAllRead();
            Spawn();
        }

        public void Close()
        {
            Unsubscribe();
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Refresh()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
            Spawn();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            List<NewsItem> news = _agency.News.Latest(ItemsShown, _filter);
            KsaCore.Log.Info("Press Room window: " + news.Count + " item(s)" + (_filter.HasValue ? " of kind " + _filter.Value : string.Empty));

            if (news.Count == 0)
            {
                items.Add(new DialogGUILabel(_filter.HasValue
                    ? "Nothing of this kind has been published yet."
                    : "Nothing has been published yet. Start a project or finish a mission.", textWidth, LineHeight));
            }
            foreach (NewsItem n in news)
            {
                string prefix = _newIds.Contains(n.Id) ? "<color=#ffe066>NEW</color> " : string.Empty;
                items.Add(new DialogGUILabel(prefix + "<b>" + n.Headline + "</b>", textWidth, KsaUi.LabelHeight(n.Headline, textWidth, LineHeight)));
                items.Add(new DialogGUILabel("<color=#aaaaaa>Day " + n.Day + ", year " + n.Year + " · " + KindText(n.Kind)
                    + (string.IsNullOrEmpty(n.Byline) ? string.Empty : " · " + n.Byline) + " · " + n.Source + "</color>", textWidth, LineHeight));
                foreach (string line in Wrap(n.Body, WrapWidth))
                    items.Add(new DialogGUILabel(line, textWidth, KsaUi.LabelHeight(line, textWidth, LineHeight)));
                items.Add(new DialogGUISpace(ItemGap));
            }
            if (_agency.News.Items.Count > ItemsShown && !_filter.HasValue)
                items.Add(new DialogGUILabel("(" + (_agency.News.Items.Count - ItemsShown) + " older item(s) not shown)", textWidth, LineHeight));

            items.Add(new DialogGUISpace(KsaUi.ScrollTail));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var filters = new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                FilterButton("All", null, 70f),
                FilterButton("Press releases", NewsKind.PressRelease, 125f),
                FilterButton("Memos", NewsKind.Memo, 80f),
                FilterButton("Reviews", NewsKind.Review, 90f),
                FilterButton("Obituaries", NewsKind.Obituary, 100f),
                FilterButton("Dispatches", NewsKind.Dispatch, 105f));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "What " + _agency.AgencyName + " has told the world, newest first.", Width - 40f, LineHeight),
                filters,
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Press Room", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private DialogGUIButton FilterButton(string label, NewsKind? kind, float width)
        {
            bool selected = _filter.HasValue == kind.HasValue && (!kind.HasValue || _filter.Value == kind.Value);
            return new DialogGUIButton((selected ? "> " : string.Empty) + label, () => OnFilter(kind), width, ButtonHeight, false);
        }

        private void OnFilter(NewsKind? kind)
        {
            _filter = kind;
            _status = string.Empty;
            Refresh();
        }

        private void OnNewsPublished(NewsItem item)
        {
            if (_dialog == null) return;
            if (item != null && !item.Read) { _newIds.Add(item.Id); item.Read = true; }
            Refresh();
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _agency.NewsPublished -= OnNewsPublished;
            _subscribed = false;
        }

        private void OnCloseClicked()
        {
            Unsubscribe();
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private static string KindText(NewsKind kind)
        {
            switch (kind)
            {
                case NewsKind.PressRelease: return "Press release";
                case NewsKind.Memo: return "Memo";
                case NewsKind.Obituary: return "Obituary";
                case NewsKind.Review: return "Review";
                case NewsKind.Dispatch: return "Dispatch";
                default: return "Ticker";
            }
        }

        /// <summary>Word-wraps a paragraph into lines of at most <paramref name="width"/> characters (newlines break too).</summary>
        private static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (string para in text.Replace("\r", string.Empty).Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (current.Length == 0) current = word;
                    else if (current.Length + 1 + word.Length <= width) current += " " + word;
                    else { lines.Add(current); current = word; }
                }
                if (current.Length > 0) lines.Add(current);
            }
            return lines;
        }
    }
}
