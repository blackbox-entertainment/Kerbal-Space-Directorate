using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Analytics;
using KSA.Core.Rivals;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Analytics: the agency's year-over-year figures (money, prestige, crew, missions, goals, review band) and the
    /// space race league table against the rival agencies, with the list of who claimed each milestone first.
    /// Read-only; everything comes from Agency.AllYears(), Agency.League() and Agency.Firsts.
    /// </summary>
    public sealed class AnalyticsWindow
    {
        private const string DialogName = "KSA_Analytics";
        private const float Width = 900f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 700f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float CellGap = 2f;

        // Year table column widths (sum 670, plus 10 gaps of 2 = 690 < 700).
        private const float ColYear = 50f;
        private const float ColGrant = 78f;
        private const float ColSpend = 78f;
        private const float ColNet = 78f;
        private const float ColClosing = 82f;
        private const float ColPrestige = 48f;
        private const float ColCrew = 38f;
        private const float ColMissions = 52f;
        private const float ColDeaths = 44f;
        private const float ColGoals = 44f;
        private const float ColBand = 78f;

        // League table column widths (sum 667, plus 8 gaps of 2 = 683 < 700).
        private const float ColRank = 28f;
        private const float ColName = 140f;
        private const float ColStyle = 95f;
        private const float ColLPrestige = 50f;
        private const float ColMilestones = 55f;
        private const float ColFirsts = 42f;
        private const float ColLMissions = 55f;
        private const float ColLDeaths = 42f;
        private const float ColLatest = 160f;

        private const string Green = "#8be28b";
        private const string Red = "#ff8080";
        private const string Grey = "#aaaaaa";

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        public AnalyticsWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            Spawn();
        }

        public void Close()
        {
            if (_dialog == null) return;
            ReleaseTextures();
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Spawn()
        {
            ReleaseTextures();
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();

            List<YearSummary> years = _agency.AllYears();
            KsaCore.Log.Info("Analytics window: " + years.Count + " year(s), rivals " + (_agency.Settings.RivalsEnabled ? "on" : "off"));

            // --- Year over year ---
            Section(items, "Year over year", textWidth);
            items.Add(Row(
                Cell("<b>Year</b>", ColYear), Cell("<b>Grant</b>", ColGrant), Cell("<b>Spend</b>", ColSpend), Cell("<b>Net</b>", ColNet),
                Cell("<b>Closing</b>", ColClosing), Cell("<b>Prest.</b>", ColPrestige), Cell("<b>Crew</b>", ColCrew), Cell("<b>Miss.</b>", ColMissions),
                Cell("<b>Deaths</b>", ColDeaths), Cell("<b>Goals</b>", ColGoals), Cell("<b>Review</b>", ColBand)));
            for (int i = years.Count - 1; i >= 0; i--)
            {
                YearSummary y = years[i];
                string yearText = y.Closed ? y.Year.ToString() : y.Year + " (now)";
                string netText = y.Net >= 0 ? Color(Cash(y.Net), Green) : Color(Cash(y.Net), Red);
                string deathsText = y.Deaths > 0 ? Color(y.Deaths.ToString(), Red) : y.Deaths.ToString();
                items.Add(Row(
                    Cell(yearText, ColYear), Cell(Cash(y.Grant), ColGrant), Cell(Cash(y.Spend), ColSpend), Cell(netText, ColNet),
                    Cell(Cash(y.ClosingBalance), ColClosing), Cell(((int)Math.Round(y.Prestige)).ToString(), ColPrestige), Cell(y.Crew.ToString(), ColCrew),
                    Cell(y.Successes + "/" + y.Missions, ColMissions), Cell(deathsText, ColDeaths), Cell(y.GoalsMet + "/" + y.GoalsIssued, ColGoals),
                    Cell(y.Band ?? string.Empty, ColBand)));
                Line(items, Color("salaries " + Cash(y.Salaries) + " · recruiting " + Cash(y.Recruiting) + " · projects " + Cash(y.Projects)
                    + (y.Milestones.Count > 0 ? "  |  firsts: " + MilestoneNames(y.Milestones) : string.Empty), Grey), textWidth);
            }
            Line(items, Color("Money in funds. Closing is the balance at year end; the live year shows today's balance.", Grey), textWidth);
            items.Add(new DialogGUISpace(4f));
            Line(items, Trend(years), textWidth);

            // --- Charts ---
            AddCharts(items, years, textWidth);

            // --- Space race ---
            Section(items, "Space race", textWidth);
            if (!_agency.Settings.RivalsEnabled)
            {
                Line(items, "Rival agencies are off (agency.cfg: rivals = false).", textWidth);
            }
            else
            {
                List<LeagueRow> league = _agency.League();
                items.Add(Row(
                    Cell("<b>#</b>", ColRank), Cell("<b>Agency</b>", ColName), Cell("<b>Style</b>", ColStyle), Cell("<b>Prest.</b>", ColLPrestige),
                    Cell("<b>Milest.</b>", ColMilestones), Cell("<b>Firsts</b>", ColFirsts), Cell("<b>Miss.</b>", ColLMissions), Cell("<b>Deaths</b>", ColLDeaths),
                    Cell("<b>Latest milestone</b>", ColLatest)));
                for (int i = 0; i < league.Count; i++)
                {
                    LeagueRow r = league[i];
                    string name = r.IsPlayer ? "<b>" + Color(r.Name, Green) + "</b>" : r.Name;
                    items.Add(Row(
                        Cell((i + 1).ToString(), ColRank), Cell(name, ColName), Cell(r.Style ?? string.Empty, ColStyle),
                        Cell(((int)Math.Round(r.Prestige)).ToString(), ColLPrestige), Cell(r.Milestones.ToString(), ColMilestones), Cell(r.Firsts.ToString(), ColFirsts),
                        Cell(r.Successes + "/" + r.Missions, ColLMissions), Cell(r.Deaths > 0 ? Color(r.Deaths.ToString(), Red) : r.Deaths.ToString(), ColLDeaths),
                        Cell(string.IsNullOrEmpty(r.LatestMilestone) ? "none yet" : r.LatestMilestone, ColLatest)));
                }
                AddLeagueChart(items, league, textWidth);
                items.Add(new DialogGUISpace(4f));
                foreach (RivalAgency rival in _agency.Rivals)
                {
                    RivalDefinition def = _agency.RivalDefinition(rival.Id);
                    string rname = def != null ? def.Name : rival.Id;
                    string blurb = def != null && !string.IsNullOrEmpty(def.Blurb) ? def.Blurb : "No public profile.";
                    Line(items, "<b>" + rname + "</b>  " + Color(blurb, Grey), textWidth);
                }
                if (_agency.Rivals.Count == 0) Line(items, Color("No rival agencies defined (rivals.cfg).", Grey), textWidth);

                Section(items, "Firsts", textWidth);
                var firsts = new List<FirstClaim>(_agency.Firsts);
                firsts.Sort((a, b) =>
                {
                    int c = a.Year.CompareTo(b.Year);
                    return c != 0 ? c : string.Compare(a.Achievement, b.Achievement, StringComparison.OrdinalIgnoreCase);
                });
                if (firsts.Count == 0) Line(items, "No milestone has been claimed by anyone yet.", textWidth);
                foreach (FirstClaim f in firsts)
                    Line(items, "Year " + f.Year + ": " + MilestoneLadder.Describe(f.Achievement) + " — " + ClaimantName(f.ClaimedBy), textWidth);
            }
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "How the agency has done year by year, and where it stands against the other agencies.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Analytics", KsaUi.Skin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        private void OnCloseClicked()
        {
            ReleaseTextures();
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        /// <summary>Compares the last closed year to the one before it.</summary>
        private static string Trend(List<YearSummary> years)
        {
            var closed = new List<YearSummary>();
            foreach (YearSummary y in years) if (y.Closed) closed.Add(y);
            if (closed.Count < 2) return "Not enough closed years for a trend yet.";
            YearSummary last = closed[closed.Count - 1];
            YearSummary prev = closed[closed.Count - 2];

            string net;
            if (Math.Abs(prev.Net) < 0.5) net = "Net " + Cash(last.Net) + " (was " + Cash(prev.Net) + ")";
            else
            {
                int pct = (int)Math.Round((last.Net - prev.Net) / Math.Abs(prev.Net) * 100.0);
                net = "Net " + Signed(pct) + "%";
            }
            int prestigeDelta = (int)Math.Round(last.Prestige) - (int)Math.Round(prev.Prestige);
            string trend = "Year " + last.Year + " vs " + prev.Year + ": " + net
                + ", prestige " + Signed(prestigeDelta)
                + ", crew " + Signed(last.Crew - prev.Crew)
                + ", missions " + last.Missions + " (was " + prev.Missions + ")"
                + ", deaths " + last.Deaths + " (was " + prev.Deaths + ")";
            return trend;
        }

        private static string Signed(int v)
        {
            return (v >= 0 ? "+" : string.Empty) + v;
        }

        /// <summary>Agency.Money without the " funds" suffix, so the figure fits a table cell.</summary>
        private static string Cash(double amount)
        {
            string s = Agency.Money(amount);
            const string suffix = " funds";
            return s.EndsWith(suffix, StringComparison.Ordinal) ? s.Substring(0, s.Length - suffix.Length) : s;
        }

        private static string MilestoneNames(List<string> keys)
        {
            var names = new List<string>();
            foreach (string k in keys) names.Add(MilestoneLadder.Describe(k));
            return string.Join(", ", names.ToArray());
        }

        private string ClaimantName(string claimedBy)
        {
            if (string.Equals(claimedBy, Agency.PlayerId, StringComparison.OrdinalIgnoreCase)) return Color(_agency.AgencyName, Green);
            RivalDefinition def = _agency.RivalDefinition(claimedBy);
            return def != null ? def.Name : claimedBy;
        }

        // ------------------------------------------------------------------ charts

        private const int ChartWidth = 740;
        private const int ChartHeight = 170;
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        private void AddCharts(List<DialogGUIBase> items, List<YearSummary> years, float textWidth)
        {
            if (years.Count == 0) return;
            Section(items, "Charts", textWidth);
            var closing = new double[years.Count];
            var net = new double[years.Count];
            var prestige = new double[years.Count];
            for (int i = 0; i < years.Count; i++) { closing[i] = years[i].ClosingBalance; net[i] = years[i].Net; prestige[i] = years[i].Prestige; }
            string axis = years.Count == 1 ? "Year " + years[0].Year : "Years " + years[0].Year + " to " + years[years.Count - 1].Year + ", left to right";

            double lo, hi;
            var money = new List<ChartTexture.Series>
            {
                new ChartTexture.Series("Closing balance", ChartTexture.Palette[0], closing),
                new ChartTexture.Series("Net for the year", ChartTexture.Palette[1], net)
            };
            AddChart(items, "Money", ChartTexture.Lines(ChartWidth, ChartHeight, money, out lo, out hi, true), money, Cash(lo), Cash(hi), axis, textWidth);

            var race = new List<ChartTexture.Series> { new ChartTexture.Series(_agency.AgencyName, ChartTexture.Palette[0], prestige) };
            if (_agency.Settings.RivalsEnabled)
            {
                int ci = 1;
                foreach (RivalAgency rival in _agency.Rivals)
                {
                    RivalDefinition def = _agency.RivalDefinition(rival.Id);
                    var vals = new double[years.Count];
                    for (int i = 0; i < years.Count; i++)
                    {
                        vals[i] = double.NaN;
                        foreach (RivalYear ry in rival.Years) if (ry.Year == years[i].Year) vals[i] = ry.Prestige;
                        if (double.IsNaN(vals[i]) && !years[i].Closed) vals[i] = rival.Prestige;
                    }
                    race.Add(new ChartTexture.Series(def != null ? def.Name : rival.Id, ChartTexture.Palette[ci++ % ChartTexture.Palette.Length], vals));
                }
            }
            AddChart(items, "Prestige race", ChartTexture.Lines(ChartWidth, ChartHeight, race, out lo, out hi, true), race, ((int)Math.Round(lo)).ToString(), ((int)Math.Round(hi)).ToString(), axis, textWidth);
            if (years.Count == 1) Line(items, Color("One point per year: the lines grow as fiscal years close.", Grey), textWidth);
        }

        private void AddLeagueChart(List<DialogGUIBase> items, List<LeagueRow> league, float textWidth)
        {
            if (league.Count == 0) return;
            var values = new List<double>();
            var colors = new List<Color>();
            var legend = new List<ChartTexture.Series>();
            for (int i = 0; i < league.Count; i++)
            {
                values.Add(Math.Max(0, league[i].Score));
                Color c = league[i].IsPlayer ? ChartTexture.Palette[0] : ChartTexture.Palette[1 + (i % (ChartTexture.Palette.Length - 1))];
                colors.Add(c);
                legend.Add(new ChartTexture.Series(league[i].Name + " (" + Math.Round(league[i].Score) + ")", c, null));
            }
            double max;
            Texture2D tex = ChartTexture.Bars(ChartWidth, 30 + 26 * league.Count, values, colors, out max);
            AddChart(items, "League score (milestones x10, firsts x6, prestige x0.2, successes, deaths x-4)", tex, legend, "0", Math.Round(max).ToString(), "Top of the table first", textWidth);
        }

        private void AddChart(List<DialogGUIBase> items, string title, Texture2D tex, List<ChartTexture.Series> legend, string lo, string hi, string axis, float textWidth)
        {
            _textures.Add(tex);
            Line(items, "<b>" + title + "</b>", textWidth);
            var parts = new List<string>();
            foreach (ChartTexture.Series s in legend) parts.Add(Color("<b>" + s.Name + "</b>", ChartTexture.Hex(s.Color)));
            Line(items, string.Join("   ", parts.ToArray()), textWidth);
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft,
                new DialogGUISpace(16f),
                new DialogGUIVerticalLayout(false, false, 2f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperRight,
                    new DialogGUILabel(Color(hi, Grey), 70f, LineHeight),
                    new DialogGUISpace(tex.height - 2 * LineHeight - 4f),
                    new DialogGUILabel(Color(lo, Grey), 70f, LineHeight)),
                new DialogGUIImage(new Vector2(tex.width, tex.height), Vector2.zero, UnityEngine.Color.white, tex)));
            Line(items, Color(axis, Grey), textWidth);
            items.Add(new DialogGUISpace(6f));
        }

        private void ReleaseTextures()
        {
            foreach (Texture2D t in _textures) if (t != null) UnityEngine.Object.Destroy(t);
            _textures.Clear();
        }

        private static string Color(string text, string hex)
        {
            return "<color=" + hex + ">" + text + "</color>";
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

        private static DialogGUILabel Cell(string text, float width)
        {
            return new DialogGUILabel(text, width, LineHeight);
        }

        private static DialogGUIHorizontalLayout Row(params DialogGUIBase[] cells)
        {
            return new DialogGUIHorizontalLayout(false, false, CellGap, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, cells);
        }

        private static void Section(List<DialogGUIBase> items, string title, float textWidth)
        {
            items.Add(new DialogGUISpace(SectionGap));
            items.Add(new DialogGUILabel("<b>" + title + "</b>", textWidth, LineHeight + 2f));
        }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUIHorizontalLayout(false, false, 0f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUILabel(text, textWidth - 16f, KsaUi.LabelHeight(text, textWidth - 16f, LineHeight))));
        }
    }
}
