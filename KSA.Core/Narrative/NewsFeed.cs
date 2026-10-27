using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Narrative
{
    public enum NewsKind { Ticker = 0, PressRelease = 1, Memo = 2, Obituary = 3, Review = 4, Dispatch = 5 }

    /// <summary>One item in the agency's press room (M6).</summary>
    public sealed class NewsItem
    {
        public int Id { get; set; }
        public int Day { get; set; }
        public int Year { get; set; }
        public NewsKind Kind { get; set; }
        public string Headline { get; set; }
        public string Body { get; set; }
        /// <summary>"Canned" or "AI".</summary>
        public string Source { get; set; }
        /// <summary>Who signed it: the agency, the government, or a kerbal's name.</summary>
        public string Byline { get; set; }
        /// <summary>Project, crew or report the item is about (for the UI to link), or empty.</summary>
        public string Related { get; set; }
        public bool Read { get; set; }

        public NewsItem() { Headline = string.Empty; Body = string.Empty; Source = "Canned"; Byline = string.Empty; Related = string.Empty; }

        public DataNode ToDataNode()
        {
            var n = new DataNode("NEWS");
            n.AddValue("id", Id).AddValue("day", Day).AddValue("year", Year).AddValue("kind", Kind.ToString())
             .AddValue("headline", Headline).AddValue("body", Body).AddValue("source", Source).AddValue("byline", Byline)
             .AddValue("related", Related).AddValue("read", Read);
            return n;
        }

        public static NewsItem FromDataNode(DataNode n)
        {
            NewsKind kind;
            if (!Enum.TryParse(n.GetValue("kind", "Ticker"), true, out kind)) kind = NewsKind.Ticker;
            return new NewsItem
            {
                Id = n.GetInt("id", 0), Day = n.GetInt("day", 0), Year = n.GetInt("year", 0), Kind = kind,
                Headline = n.GetValue("headline", string.Empty), Body = n.GetValue("body", string.Empty),
                Source = n.GetValue("source", "Canned"), Byline = n.GetValue("byline", string.Empty),
                Related = n.GetValue("related", string.Empty), Read = n.GetBool("read", false)
            };
        }
    }

    /// <summary>The press room archive: newest last, capped.</summary>
    public sealed class NewsFeed
    {
        public const int MaxItems = 200;

        public List<NewsItem> Items { get; } = new List<NewsItem>();
        public int NextId { get; set; } = 1;

        public NewsItem Add(NewsItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            item.Id = NextId++;
            Items.Add(item);
            if (Items.Count > MaxItems) Items.RemoveRange(0, Items.Count - MaxItems);
            return item;
        }

        public NewsItem Get(int id) { return Items.Find(i => i.Id == id); }

        public int Unread { get { return Items.FindAll(i => !i.Read).Count; } }

        /// <summary>Newest first.</summary>
        public List<NewsItem> Latest(int count, NewsKind? kind = null)
        {
            var list = new List<NewsItem>();
            for (int i = Items.Count - 1; i >= 0 && list.Count < count; i--)
                if (!kind.HasValue || Items[i].Kind == kind.Value) list.Add(Items[i]);
            return list;
        }

        /// <summary>Headline lines for a ticker, newest first.</summary>
        public List<string> TickerLines(int count)
        {
            var lines = new List<string>();
            for (int i = Items.Count - 1; i >= 0 && lines.Count < count; i--) lines.Add(Items[i].Headline);
            return lines;
        }

        public DataNode ToDataNode()
        {
            var n = new DataNode("NEWSFEED");
            n.AddValue("nextId", NextId);
            foreach (NewsItem i in Items) n.AddChild(i.ToDataNode());
            return n;
        }

        public void Load(DataNode n)
        {
            Items.Clear();
            if (n == null) return;
            NextId = Math.Max(1, n.GetInt("nextId", 1));
            foreach (DataNode c in n.GetChildren("NEWS")) Items.Add(NewsItem.FromDataNode(c));
        }
    }
}
