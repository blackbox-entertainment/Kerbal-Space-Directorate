using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Narrative;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Recruiting;
using KSA.Core.Review;

namespace KSA.Core
{
    /// <summary>
    /// M6: the press room. Agency events become news items with canned text at once; when a narrative model is
    /// configured (local by default), each item is rewritten in the background and updated in place.
    /// </summary>
    public sealed partial class Agency
    {
        public NewsFeed News { get; } = new NewsFeed();

        /// <summary>A new item was published, or an AI rewrite replaced its text.</summary>
        public event Action<NewsItem> NewsPublished;

        public string AgencyName { get { return State.AgencyName.Length > 0 ? State.AgencyName : Settings.AgencyName; } }

        /// <summary>Renames the agency for this save (press releases, league table, HUD). Empty restores the config default.</summary>
        public bool Rename(string name, out string message)
        {
            string n = (name ?? string.Empty).Trim();
            if (n.Length > 48) { message = "Keep the name under 48 characters."; return false; }
            State.AgencyName = n;
            message = n.Length > 0 ? "The agency is now " + n + "." : "Agency name reset to " + Settings.AgencyName + ".";
            Push(null, message);
            return true;
        }

        private void InitNarrative()
        {
            MissionResolved += r => PublishMission(r);
            ReportUpdated += r => { if (!string.IsNullOrEmpty(r.PostMortem)) Publish(new NewsItem { Kind = NewsKind.Dispatch, Related = r.ProjectId, Byline = "Flight Safety Office", Headline = "Post-mortem filed: " + r.ProjectName, Body = r.PostMortem }, false); };
            YearEnded += OnYearEndedNews;
            ApplicantHired += (a, p) => Publish(Narrator.Hired(p, Catalogs.Recruiting.NameOf(a.SourceId), AgencyName));
            CrewDeparted += (p, reason) => { NewsItem n = Narrator.Departed(p, reason, AgencyName, Rng); if (n != null) Publish(n); };
            DesignFinished += (p, d) => { if (d.Verdict == DesignVerdict.Approved) Publish(Narrator.DesignApproved(p, d, NameOf(p.LeadEngineer))); };
        }

        private void PublishMission(MissionReport r)
        {
            Project p = GetProject(r.ProjectId);
            MissionType type = Catalogs.Missions.Get(r.MissionTypeId);
            var names = new List<string>();
            foreach (string id in r.Crew) names.Add(NameOf(id));
            Publish(Narrator.MissionResolved(r, type, names, AgencyName, Rng));
        }

        private void OnYearEndedNews(ReviewResult r)
        {
            Publish(Narrator.YearEnded(r, AgencyName));
            // One grumble per year from the unhappiest active kerbal, if anyone is unhappy.
            CrewProfile worst = null;
            foreach (CrewProfile p in Crew.Active) if (p.Morale < 35 && (worst == null || p.Morale < worst.Morale)) worst = p;
            if (worst != null) Publish(Narrator.LowMoraleMemo(worst, Rng));
        }

        private string NameOf(string crewId)
        {
            CrewProfile c = Crew.Get(crewId);
            return c != null ? c.Name : (crewId ?? string.Empty);
        }

        /// <summary>Announces a project the Director just approved (called by CreateProject).</summary>
        private void PublishProjectStart(Project p, MissionType type)
        {
            Publish(Narrator.ProjectStarted(p, type, NameOf(p.LeadEngineer), AgencyName), false);
        }

        /// <summary>Stores the item, raises NewsPublished, and queues an AI rewrite when a narrative model is available.</summary>
        public NewsItem Publish(NewsItem item, bool allowAi = true)
        {
            if (item == null) return null;
            item.Day = CurrentDay;
            item.Year = CurrentYear;
            News.Add(item);
            Log.Info("News: " + item.Headline);
            Action<NewsItem> handler = NewsPublished;
            if (handler != null) handler(item);
            if (allowAi && item.Kind != NewsKind.Ticker) TryRewrite(item);
            return item;
        }

        private void TryRewrite(NewsItem item)
        {
            if (Ai == null || Transport == null) return;
            LlmEndpoint endpoint = Ai.EndpointFor(AiTask.Narrative);
            if (endpoint == null || !endpoint.IsLocal) return; // narrative is local-only by design (GDD 6.1): never spends API budget on flavour text
            int id = item.Id;
            new LlmClient(Transport, Log).Chat(endpoint, Narrator.SystemPrompt(item, AgencyName), Narrator.UserPrompt(item), 0.8, false, result =>
            {
                if (!result.Ok || string.IsNullOrEmpty(result.Text)) return;
                NewsItem live = News.Get(id);
                if (live == null) return;
                string text = result.Text.Trim();
                if (text.Length < 20 || text.Length > 2000) return;
                live.Body = text;
                live.Source = "AI";
                Action<NewsItem> handler = NewsPublished;
                if (handler != null) handler(live);
            });
        }

        public void MarkAllRead() { foreach (NewsItem n in News.Items) n.Read = true; }
    }
}
