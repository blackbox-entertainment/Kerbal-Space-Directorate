using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Projects office (Jira KSA-35): see active projects, change how they fly, launch or cancel them, and start new ones.
    /// Every action goes through the Agency and the window rebuilds itself to show the new state.
    /// </summary>
    public sealed class ProjectsWindow
    {
        private const string DialogName = "KSA_Projects";
        private const float Width = 720f;
        /// <summary>Tall enough to show mission, design, crew and the engineering team without scrolling on a 1080p screen.</summary>
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 660f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float ButtonHeight = 26f;
        private const int MaxEngineers = 4;
        private const int FinishedShown = 10;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        // New-project selection state; survives Refresh() because the window object does.
        private string _selectedTypeId = string.Empty;
        private string _selectedTemplateId = string.Empty;
        /// <summary>Craft path of the Director's own design picked for the new project, or empty (2026-10-28).</summary>
        private string _selectedPlayerDesign = string.Empty;
        private string _swapFor = string.Empty; // the project whose swap-rocket choices are open
        private readonly List<string> _selectedCrew = new List<string>();
        private readonly List<string> _selectedEngineers = new List<string>();
        private MissionMode _mode = MissionMode.Simulate;

        // Custom mission builder (KSA-59): collapsed behind a toggle; the spec survives Refresh() like the selection above.
        private bool _customOpen;
        private readonly CustomMissionSpec _custom = new CustomMissionSpec();
        private const int MaxNameLength = 60;
        private static readonly MissionProfile[] CustomProfiles = { MissionProfile.Suborbital, MissionProfile.Orbit, MissionProfile.Flyby, MissionProfile.OrbitBody, MissionProfile.Land };

        // One Design Review child window at a time, same pattern as the Director's Office.
        private DesignReviewWindow _designReview;

        public ProjectsWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            _agency.ProjectChanged += OnProjectChanged;
            Spawn();
        }

        public void Close()
        {
            CloseChildren();
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
            KsaCore.Log.Info("Projects window: " + _agency.Projects.Count + " project(s), " + _agency.Catalogs.Missions.Count + " mission type(s)");

            items.Add(Field("Balance", Agency.Money(_agency.Ledger.Balance), textWidth));

            BuildStrandedSection(items, textWidth);
            BuildActiveSection(items, textWidth);
            BuildNewProjectSection(items, textWidth);
            BuildFinishedSection(items, textWidth);
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Pick a mission, a design, and a crew, then start the project.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    HelpWindow.Button("projects"),
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Projects", KsaUi.Skin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        // --- Active projects ---

        private void BuildActiveSection(List<DialogGUIBase> items, float textWidth)
        {
            var active = new List<Project>();
            for (int i = _agency.Projects.Count - 1; i >= 0; i--)
                if (_agency.Projects[i].IsActive) active.Add(_agency.Projects[i]);

            Section(items, "Active projects (" + active.Count + ")", textWidth);
            if (active.Count == 0) Line(items, "No projects under way.", textWidth);
            foreach (Project p in active)
            {
                string id = p.Id;
                MissionType type = _agency.Catalogs.Missions.Get(p.MissionTypeId);
                string typeName = type != null ? type.Name : p.MissionTypeId;
                int duration = type != null ? type.DurationDays : 0;
                Project live = p;
                // Dynamic label: re-evaluated every frame, so the countdown moves under time warp without reopening the window.
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("<b>" + p.Name + "</b>  (" + p.Id + ")", 260f, LineHeight),
                    new DialogGUILabel(() =>
                    {
                        if (_agency.WaitingForAiDesign(live)) return typeName + "  |  " + StatusText(live.Status) + "  |  engineers finishing the design";
                        int d = live.DaysRemaining(_agency.CurrentDay, duration);
                        return typeName + "  |  " + StatusText(live.Status) + "  |  " + d + " day" + (d == 1 ? "" : "s") + " left";
                    }, textWidth - 266f, LineHeight)));
                Line(items, "Mode " + ModeText(p.Mode) + "  |  budget " + Agency.Money(p.Budget) + "  |  design " + _agency.DesignNameOf(p), textWidth);
                Line(items, "Crew: " + Names(p.Crew) + "  |  lead engineer: " + NameOf(p.LeadEngineer), textWidth);
                Line(items, DesignLine(p), textWidth);
                // The fuel depot stop (2026-10-29).
                if (p.DepotId.Length > 0)
                {
                    KSA.Core.Projects.Program depot = _agency.GetProgram(p.DepotId);
                    string wait = _agency.DepotBlocker(p);
                    Line(items, "Tops up at " + (depot != null ? depot.Name : "the fuel depot") + ": " + (p.Design != null ? p.Design.DepotFuel.ToString("0.0") : "?") + " t"
                        + (wait != null ? "  |  <color=#ffd27f>" + wait + "</color>" : "  |  <color=#8be28b>fuel waiting</color>"), textWidth);
                }

                var buttons = new List<DialogGUIBase>();
                buttons.Add(new DialogGUISpace(16f));
                buttons.Add(new DialogGUIButton("Design", () => OpenDesignReview(id), 90f, ButtonHeight, false));
                if (p.Status != ProjectStatus.InFlight)
                {
                    MissionMode other = p.Mode == MissionMode.Simulate ? MissionMode.WatchLive : MissionMode.Simulate;
                    buttons.Add(new DialogGUIButton("Switch to " + ModeText(other), () => OnSetMode(id, other), 170f, ButtonHeight, false));
                }
                if (p.Status == ProjectStatus.Ready)
                {
                    string queue = _agency.LaunchWindowBlocker();
                    if (queue != null) Line(items, "<color=#ffd27f>Launch queue: " + queue + ". Launch now skips the queue.</color>", textWidth);
                    buttons.Add(new DialogGUIButton("Launch now", () => OnLaunchNow(id), 120f, ButtonHeight, false));
                }
                if (p.Status != ProjectStatus.InFlight)
                {
                    buttons.Add(new DialogGUIButton(_swapFor == id ? "Keep this rocket" : "Swap rocket", () => { _swapFor = _swapFor == id ? string.Empty : id; Refresh(); }, 140f, ButtonHeight, false));
                    buttons.Add(new DialogGUIButton("Cancel", () => OnCancel(id), 90f, ButtonHeight, false));
                }
                if (buttons.Count > 1)
                    items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, buttons.ToArray()));
                if (_swapFor == id && p.Status != ProjectStatus.InFlight) BuildSwapChoices(items, textWidth, p, type);
            }
        }

        /// <summary>
        /// Swap the rocket before launch (2026-10-29): one of the Director's saved VAB rockets that fits, or a fresh design by
        /// the engineering team. A project already building or on the pad is rebuilt in half its build time.
        /// </summary>
        private void BuildSwapChoices(List<DialogGUIBase> items, float textWidth, Project p, MissionType type)
        {
            string id = p.Id;
            bool built = p.Status == ProjectStatus.Build || p.Status == ProjectStatus.Ready;
            Line(items, "    Swap the rocket" + (built ? " (it is rebuilt in about " + Math.Max(1, p.BuildDays / 2) + " days)" : string.Empty) + ":", textWidth);
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(32f),
                new DialogGUIButton("New engineers' design", () => OnSwapEngineers(id), 200f, ButtonHeight, false),
                new DialogGUILabel("<color=#aaaaaa>the team draws a fresh rocket from today's parts</color>", textWidth - 250f, ButtonHeight)));
            List<PlayerDesign> fit = _agency.PlayerDesignsFor(type);
            // Say why the others do not fit (2026-10-30, T: "swap rocket is not finding my designs").
            MissionRequirements.Entry need = _agency.Requirements.For(type);
            foreach (PlayerDesign d in _agency.PlayerDesigns())
            {
                if (fit.Contains(d)) continue;
                string why = d.Blocker(type, need);
                Line(items, "    <color=#888888>" + d.Name + ": " + (why ?? "does not fit") + ".</color>", textWidth);
            }
            if (fit.Count == 0)
            {
                Line(items, "    <color=#888888>None of your saved " + (type.IsAircraft ? "planes" : "rockets") + " fits this mission" + (type.IsAircraft ? "." : " (" + Math.Round(need.DeltaV) + " m/s needed). Build one in the " + (type.IsAircraft ? "SPH" : "VAB") + " and save it in this game: it shows here.") + "</color>", textWidth);
                return;
            }
            foreach (PlayerDesign d in fit)
            {
                string name = d.Name;
                bool current = p.Design != null && p.Design.Source == DesignSource.Player && string.Equals(p.Design.DesignName, d.Name, StringComparison.OrdinalIgnoreCase);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(32f),
                    new DialogGUIButton(current ? "Flying it" : "Fly " + d.Name, () => OnSwapMine(id, name), 200f, ButtonHeight, false),
                    new DialogGUILabel(Math.Round(d.DeltaV) + " m/s, lift-off TWR " + d.LiftoffTwr.ToString("0.00") + ", " + d.Seats + " seat" + (d.Seats == 1 ? "" : "s") + (d.IsPlane ? ", spaceplane" : string.Empty), textWidth - 250f, ButtonHeight)));
            }
        }

        private void OnSwapMine(string projectId, string designName)
        {
            string message;
            bool ok = _agency.SwapToPlayerDesign(projectId, designName, out message);
            if (ok) _swapFor = string.Empty;
            Report(ok, message);
        }

        private void OnSwapEngineers(string projectId)
        {
            string message;
            bool ok = _agency.SwapToEngineersDesign(projectId, out message);
            if (ok) _swapFor = string.Empty;
            Report(ok, message);
        }

        // --- New project ---

        /// <summary>KSA-65: crews waiting for a rescue, with the buttons to send one.</summary>
        private void BuildStrandedSection(List<DialogGUIBase> items, float textWidth)
        {
            List<Stranding> waiting = _agency.ActiveStrandings();
            if (waiting.Count == 0) return;
            Section(items, "<color=#ff8080>Stranded crews (" + waiting.Count + ")</color>", textWidth);
            foreach (Stranding s in waiting)
            {
                string id = s.Id;
                var names = new List<string>();
                foreach (string cid in s.Crew) { CrewProfile c = _agency.Crew.Get(cid); names.Add(c != null ? c.Name : cid); }
                Project rescue = s.RescueProjectId.Length > 0 ? _agency.GetProject(s.RescueProjectId) : null;
                int lostAfter = _agency.LostAfterDays();
                string clock = lostAfter > 0 ? "  |  " + Math.Max(0, lostAfter - _agency.DaysStranded(s)) + " days before they are given up for lost" : string.Empty;
                Line(items, "<b>" + string.Join(", ", names.ToArray()) + "</b> stranded " + s.Where + " since day " + s.Day + " (" + _agency.DaysStranded(s) + " days, after " + s.Cause + ")" + clock, textWidth);
                if (rescue != null && rescue.IsActive) { Line(items, "    Rescue under way: " + rescue.Name + " (" + rescue.Status + ")", textWidth); continue; }
                string live = s.OnSurface ? "surface rescues fly simulated" : (s.VesselId.Length == 0 ? "no live vessel to meet; flies simulated" : "can be flown live");
                MissionType need = _agency.RescueMissionFor(s, 1);
                Line(items, "    A rescue craft needs " + need.Seats + " seats (1 pilot + " + s.Crew.Count + " to bring home) and about " + Math.Round(_agency.Requirements.For(need).DeltaV) + " m/s.", textWidth);
                string rescueLocked = _agency.RescueBlocker(s);
                if (rescueLocked != null) Line(items, "    <color=#888888>The engineers: a rescue " + rescueLocked + ".</color>", textWidth);
                else
                    items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                        new DialogGUISpace(16f),
                        new DialogGUIButton("Launch rescue", () => OnRescue(id, MissionMode.Simulate, null), 140f, ButtonHeight, false),
                        new DialogGUIButton("Rescue (Watch Live)", () => OnRescue(id, MissionMode.WatchLive, null), 170f, ButtonHeight, false),
                        new DialogGUILabel("<color=#aaaaaa>" + live + "</color>", textWidth - 360f, ButtonHeight)));
                // The Director's own rockets that fit: build one in the VAB with the seats and delta-v above, save it, and it shows here.
                List<PlayerDesign> mine = _agency.RescueDesignsFor(s);
                if (mine.Count == 0)
                    Line(items, "    <color=#888888>None of your saved rockets fits. Build one in the VAB with those seats and that delta-v and save it in this game: it shows here.</color>", textWidth);
                foreach (PlayerDesign d in mine)
                {
                    string craft = d.CraftPath;
                    items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                        new DialogGUISpace(16f),
                        new DialogGUIButton("Rescue with " + d.Name, () => OnRescue(id, MissionMode.WatchLive, craft), 316f, ButtonHeight, false),
                        new DialogGUILabel("<color=#aaaaaa>your design: " + d.Seats + " seats, " + Math.Round(d.DeltaV) + " m/s; " + live + "</color>", textWidth - 340f, ButtonHeight)));
                }
            }
        }

        private void OnRescue(string strandingId, MissionMode mode, string playerDesign)
        {
            string message;
            Project p = _agency.CreateRescue(strandingId, null, mode, out message, playerDesign);
            Report(p != null, message);
        }

        private void BuildNewProjectSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "New project", textWidth);

            List<CrewProfile> anyCrew = _agency.AvailableFor(CrewRole.Unknown);
            var flyers = new List<CrewProfile>();
            foreach (CrewProfile c in anyCrew) if (c.Role != CrewRole.Tourist) flyers.Add(c);
            if (flyers.Count == 0)
            {
                Line(items, "No crew are available. Hire or free up crew before starting a project.", textWidth);
                return;
            }

            // Mission type row(s): non-module types only.
            var types = new List<MissionType>();
            foreach (MissionType t in _agency.Catalogs.Missions.Types)
                if (string.IsNullOrEmpty(t.ModuleRole)) types.Add(t);
            if (types.Count == 0)
            {
                Line(items, "No mission types configured.", textWidth);
                return;
            }
            MissionType selected = _agency.Catalogs.Missions.Get(_selectedTypeId);
            if (selected != null && !string.IsNullOrEmpty(selected.ModuleRole)) selected = null;

            Line(items, "Mission:", textWidth);
            ButtonRows(items, types.Count, textWidth, 170f, i =>
            {
                MissionType t = types[i];
                string tid = t.Id;
                bool isSel = selected != null && string.Equals(selected.Id, tid, StringComparison.OrdinalIgnoreCase);
                bool locked = _agency.MissionBlocker(t) != null && _agency.PlayerDesignsFor(t).Count == 0; // one of your designs can open it
                int done = _agency.MissionsCompleted(t.Id);
                string name = t.IsCustom ? t.Name + " (custom)" : t.Name;
                string text = locked ? "<color=#888888>" + name + " (research)</color>" : done > 0 ? "<color=#8be28b>" + name + " (done" + (done > 1 ? " x" + done : "") + ")</color>" : name;
                return new DialogGUIButton((isSel ? "> " : "") + text, () => OnSelectType(tid), 170f, ButtonHeight, false);
            });

            BuildCustomMissionSection(items, textWidth);

            if (selected == null)
            {
                Line(items, "Choose a mission type.", textWidth);
                return;
            }

            // Selected type facts.
            if (selected.Description.Length > 0) Line(items, selected.Description, textWidth);
            Line(items, "Crew " + selected.CrewSize + "  |  min stars " + selected.MinStars + "  |  " + selected.DurationDays + " day"
                + (selected.DurationDays == 1 ? "" : "s") + "  |  difficulty " + Percent(selected.Difficulty) + "  |  " + selected.Body
                + (selected.IsFirst ? "  |  a First" : ""), textWidth);
            // The Director's own designs that can fly this mission, and the one picked (2026-10-28).
            List<PlayerDesign> mine = _agency.PlayerDesigns();
            MissionRequirements.Entry need = _agency.Requirements.For(selected);
            PlayerDesign pickedMine = null;
            foreach (PlayerDesign d in mine) if (string.Equals(d.CraftPath, _selectedPlayerDesign, StringComparison.OrdinalIgnoreCase) && d.Blocker(selected, need) == null) pickedMine = d;
            if (pickedMine == null) _selectedPlayerDesign = string.Empty;
            string missionLocked = pickedMine != null ? null : _agency.MissionBlocker(selected);
            if (missionLocked != null && _agency.PlayerDesignsFor(selected).Count > 0) missionLocked = "can only be flown with one of your designs below for now";
            if (missionLocked != null) Line(items, "<color=#ffd27f>Locked: " + selected.Name + " " + missionLocked + ". Put scientists in the lab or fly missions to earn Science, then unlock the parts in R&D.</color>", textWidth);

            // Templates.
            List<Template> templates = _agency.TemplatesFor(selected);
            Template selectedTemplate = null;
            foreach (Template t in templates)
                if (string.Equals(t.Id, _selectedTemplateId, StringComparison.OrdinalIgnoreCase)) { selectedTemplate = t; break; }
            // Default to a fallback design that can be built today. With none, the engineering team designs it from scratch
            // (AI on) and no template is picked (2026-10-28: templates are the AI-off fallback, not a gate).
            Template buildable = templates.Find(t => t.IsAvailable(_agency.InstalledMods) && _agency.TemplateBlocker(t) == null);
            if (selectedTemplate == null && buildable != null) { selectedTemplate = buildable; _selectedTemplateId = buildable.Id; }
            if (buildable == null) { selectedTemplate = null; _selectedTemplateId = string.Empty; }

            Line(items, "Design:", textWidth);
            if (_agency.AiDesignAvailable)
                Line(items, buildable != null
                    ? "The engineering team designs the rocket (AI). The design picked below is the fallback if theirs comes up short."
                    : "<color=#8be28b>The engineering team designs this rocket from scratch (AI).</color> No fallback design can be built with today's parts, so they get " + Agency.MaxAiDesignAttempts + " attempts; if none works, the project is called off and the budget returned.", textWidth);
            else if (buildable == null && templates.Count > 0)
                Line(items, "<color=#ffd27f>No fallback design can be built with today's parts, and AI design is off. Turn it on in AI Setup to let the engineers design one.</color>", textWidth);
            if (templates.Count == 0) Line(items, "No design template can fly " + selected.Name + " with the mods installed.", textWidth);
            MissionType typeForCost = selected;
            ButtonRows(items, templates.Count, textWidth, 220f, i =>
            {
                Template t = templates[i];
                string tid = t.Id;
                bool isSel = selectedTemplate != null && string.Equals(selectedTemplate.Id, tid, StringComparison.OrdinalIgnoreCase);
                string blocked = _agency.TemplateBlocker(t);
                string label = (isSel ? "> " : "") + t.Name + " (" + (t.Seats > 0 ? t.Seats + (t.Seats == 1 ? " seat, " : " seats, ") : "") + Percent(t.DesignScore) + ", " + Agency.Money(_agency.ProjectCost(typeForCost, t)) + ")" + (blocked != null ? "  [" + blocked + "]" : "");
                return new DialogGUIButton(label, () => OnSelectTemplate(tid), 220f, ButtonHeight, false);
            });

            // Your designs: rockets saved in this game's VAB.
            Line(items, "Your designs (saved in the VAB):" + (mine.Count == 0 ? "  none yet. Build a rocket in the VAB and save it; it appears here." : pickedMine != null ? "  the crew flies " + pickedMine.Name + "; Take Control is always allowed on it." : "  pick one to fly it instead."), textWidth);
            var mineButtons = new List<DialogGUIBase>();
            foreach (PlayerDesign d in mine)
            {
                string path = d.CraftPath;
                string unfit = d.Blocker(selected, need);
                bool isSel = pickedMine != null && pickedMine.CraftPath == path;
                string label = (isSel ? "> " : "") + d.Name + "  (" + (d.IsPlane ? "plane" + (d.HasParachute ? ", lands on chutes" : ", you land it") + "; " + Math.Round(d.DeltaV) + " m/s rockets" + (d.HasJets ? " + jets" : string.Empty) : Math.Round(d.DeltaV) + " m/s, TWR " + d.LiftoffTwr.ToString("0.0")) + ", " + d.Seats + (d.Seats == 1 ? " seat" : " seats") + ")";
                if (unfit != null) label = "<color=#888888>" + label + "  [" + unfit + "]</color>";
                var b = new DialogGUIButton(label, () => OnSelectPlayerDesign(path), textWidth - 260f, ButtonHeight, false);
                if (unfit != null) b.OptionInteractableCondition = () => false;
                // Tell the AI Director to fly this design for this mission (2026-10-28).
                string mission = selected.Id, dname = d.Name;
                bool preferred = _agency.PreferredDesigns.ContainsKey(mission) && string.Equals(_agency.PreferredDesigns[mission], dname, StringComparison.OrdinalIgnoreCase);
                var pref = new DialogGUIButton((preferred ? "[x] " : "[  ] ") + "AI Director flies this", () => OnPreferDesign(mission, dname, !preferred), 200f, ButtonHeight, false);
                if ((unfit != null || (d.IsPlane && !d.HasParachute)) && !preferred) pref.OptionInteractableCondition = () => false; // a plane without chutes needs the human Director to land it
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, new DialogGUISpace(16f), b, pref));
            }

            // Crew pick.
            PruneSelection(_selectedCrew, flyers);
            Line(items, "Crew (" + _selectedCrew.Count + " of " + selected.CrewSize + " picked):", textWidth);
            foreach (CrewProfile c in flyers)
            {
                string cid = c.Id;
                bool picked = _selectedCrew.Contains(cid);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    KsaIcons.Badge(c.Role),
                    new DialogGUIButton((picked ? "[x] " : "") + c.Name, () => OnToggleCrew(cid, selected.CrewSize), 190f, ButtonHeight, false),
                    new DialogGUILabel(RosterView.RoleText(c.Role) + "  |  " + RosterView.StarsText(c.Stars) + "  |  " + _agency.Careers.RankName(c.Track, c.RankId)
                        + (c.Stars < selected.MinStars ? "  |  <color=#ffd27f>below min stars</color>" : ""),
                        textWidth - KsaIcons.BadgeSize - 230f, LineHeight)));
            }

            // Engineer pick.
            List<CrewProfile> engineers = _agency.AvailableFor(CrewRole.Engineer);
            PruneSelection(_selectedEngineers, engineers);
            Line(items, "Engineering team (" + _selectedEngineers.Count + " of up to " + MaxEngineers + "):", textWidth);
            if (engineers.Count == 0) Line(items, "No engineers are available; the project will have no engineering team.", textWidth);
            foreach (CrewProfile c in engineers)
            {
                string cid = c.Id;
                bool picked = _selectedEngineers.Contains(cid);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    KsaIcons.Badge(c.Role),
                    new DialogGUIButton((picked ? "[x] " : "") + c.Name, () => OnToggleEngineer(cid), 190f, ButtonHeight, false),
                    new DialogGUILabel(RosterView.StarsText(c.Stars) + "  |  " + _agency.Careers.RankName(c.Track, c.RankId),
                        textWidth - KsaIcons.BadgeSize - 230f, LineHeight)));
            }

            // Mode and start.
            bool simSel = _mode == MissionMode.Simulate;
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton("Auto-staff", OnAutoStaff, 110f, ButtonHeight, false),
                new DialogGUIButton((simSel ? "> " : "") + "Simulate", () => OnSetNewMode(MissionMode.Simulate), 110f, ButtonHeight, false),
                new DialogGUIButton((!simSel ? "> " : "") + "Watch Live", () => OnSetNewMode(MissionMode.WatchLive), 110f, ButtonHeight, false),
                new DialogGUILabel(_agency.LiveFlightAvailable ? "" : "Live flight arrives in M5; will simulate", textWidth - 370f, LineHeight)));

            double cost = _agency.ProjectCost(selected, pickedMine != null ? null : selectedTemplate);
            var start = new DialogGUIButton(missionLocked != null ? "<color=#888888>Start project (research)</color>" : "Start project (" + Agency.Money(cost) + ")", OnStartProject, 240f, 30f, false);
            if (missionLocked != null) start.OptionInteractableCondition = () => false; // KSA-66: greyed until researched
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                start,
                new DialogGUILabel(missionLocked != null ? "<color=#888888>Unlock the parts first.</color>" : _agency.Ledger.Balance < cost ? "<color=#ffd27f>Not enough funds.</color>" : "Charged to the budget on start.", textWidth - 270f, LineHeight)));
        }

        // --- Custom mission builder (KSA-59) ---

        private void BuildCustomMissionSection(List<DialogGUIBase> items, float textWidth)
        {
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton(_customOpen ? "Hide mission builder" : "Define your own mission", OnToggleCustom, 200f, ButtonHeight, false),
                new DialogGUILabel(_customOpen ? "" : "Pick a body and a profile; the agency prices it.", textWidth - 230f, LineHeight)));
            if (!_customOpen) return;

            Line(items, "<b>Custom mission</b>", textWidth);

            // Body.
            Line(items, "Body:", textWidth);
            string[] bodies = Agency.CustomBodies;
            ButtonRows(items, bodies.Length, textWidth, 100f, i =>
            {
                string body = bodies[i];
                bool isSel = string.Equals(body, _custom.Body, StringComparison.OrdinalIgnoreCase);
                return new DialogGUIButton((isSel ? "> " : "") + body, () => OnCustomBody(body), 100f, ButtonHeight, false);
            });

            // Profile: ones the body cannot take are greyed and inert.
            Line(items, "Profile:", textWidth);
            ButtonRows(items, CustomProfiles.Length, textWidth, 130f, i =>
            {
                MissionProfile profile = CustomProfiles[i];
                bool isSel = _custom.Profile == profile;
                bool allowed = ProfileAllowed(profile);
                string label = allowed ? ProfileText(profile) : "<color=#888888>" + ProfileText(profile) + "</color>";
                var b = new DialogGUIButton((isSel ? "> " : "") + label, () => OnCustomProfile(profile), 130f, ButtonHeight, false);
                if (!allowed) b.OptionInteractableCondition = () => false;
                return b;
            });

            // Numbers.
            var numbers = new List<DialogGUIBase>();
            numbers.Add(new DialogGUISpace(16f));
            if (_custom.Profile == MissionProfile.Orbit || _custom.Profile == MissionProfile.OrbitBody)
            {
                numbers.Add(new DialogGUILabel("Orbits: " + _custom.Orbits, 80f, LineHeight));
                numbers.Add(new DialogGUIButton("-", () => OnCustomOrbits(-1), 30f, ButtonHeight, false));
                numbers.Add(new DialogGUIButton("+", () => OnCustomOrbits(1), 30f, ButtonHeight, false));
                numbers.Add(new DialogGUISpace(12f));
            }
            if (_custom.Profile == MissionProfile.Land)
            {
                numbers.Add(new DialogGUILabel("Days on surface: " + _custom.SurfaceDays.ToString("0.#"), 140f, LineHeight));
                numbers.Add(new DialogGUIButton("-", () => OnCustomSurfaceDays(-1), 30f, ButtonHeight, false));
                numbers.Add(new DialogGUIButton("+", () => OnCustomSurfaceDays(1), 30f, ButtonHeight, false));
                numbers.Add(new DialogGUISpace(12f));
            }
            numbers.Add(new DialogGUILabel("Crew: " + _custom.CrewSize, 70f, LineHeight));
            numbers.Add(new DialogGUIButton("-", () => OnCustomCrew(-1), 30f, ButtonHeight, false));
            numbers.Add(new DialogGUIButton("+", () => OnCustomCrew(1), 30f, ButtonHeight, false));
            numbers.Add(new DialogGUISpace(12f));
            numbers.Add(new DialogGUIButton((_custom.Science ? "[x] " : "[ ] ") + "Run experiments", OnCustomScience, 160f, ButtonHeight, false));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft, numbers.ToArray()));

            // Name.
            items.Add(NameInput(textWidth));

            // Live preview.
            string problem = _agency.ValidateCustom(_custom);
            if (problem != null)
            {
                Line(items, "<color=#ffd27f>" + problem + "</color>", textWidth);
            }
            else
            {
                MissionType preview = _agency.PreviewCustom(_custom);
                var earns = new List<string>();
                foreach (string a in preview.Achievements) if (a != "Recover:-" && a != "Flight:Kerbin") earns.Add(a);
                Line(items, "~ " + Agency.Money(preview.BaseCost) + ", " + preview.DurationDays + " day" + (preview.DurationDays == 1 ? "" : "s") + " in flight, difficulty " + Percent(preview.Difficulty)
                    + ", min stars " + preview.MinStars + (preview.IsScience ? ", ~" + preview.ScienceYield.ToString("0") + " science" : "")
                    + (earns.Count > 0 ? ", earns " + string.Join(", ", earns.ToArray()) : ""), textWidth);
                string blocked = _agency.MissionBlocker(preview);
                if (blocked != null) Line(items, "<color=#ffd27f>" + preview.Name + " " + blocked + ".</color>", textWidth);
            }

            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton("Define mission", OnDefineCustom, 160f, 30f, false),
                new DialogGUILabel("Adds it to the mission list and selects it; then pick a design and crew as usual.", textWidth - 190f, LineHeight)));
        }

        private DialogGUIHorizontalLayout NameInput(float textWidth)
        {
            var input = new DialogGUITextInput(_custom.Name ?? string.Empty, false, MaxNameLength, v => { _custom.Name = (v ?? string.Empty).Trim(); return _custom.Name; }, LineHeight + 4f);
            input.size = new Vector2(textWidth - LabelWidth - 180f, LineHeight + 4f);
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUILabel("<b>Name</b>", LabelWidth - 16f, LineHeight),
                input,
                new DialogGUILabel(_custom.Name.Length > 0 ? "" : "blank = \"" + Agency.DefaultName(_custom) + "\"", 170f, LineHeight));
        }

        private bool ProfileAllowed(MissionProfile profile)
        {
            var probe = new CustomMissionSpec { Name = _custom.Name, Body = _custom.Body, Profile = profile, Orbits = _custom.Orbits, SurfaceDays = _custom.SurfaceDays, CrewSize = _custom.CrewSize, Science = _custom.Science };
            return _agency.ValidateCustom(probe) == null;
        }

        private static string ProfileText(MissionProfile p)
        {
            switch (p)
            {
                case MissionProfile.Suborbital: return "Suborbital";
                case MissionProfile.Orbit: return "Orbit";
                case MissionProfile.Flyby: return "Flyby";
                case MissionProfile.OrbitBody: return "Orbit the body";
                case MissionProfile.Land: return "Land";
                default: return p.ToString();
            }
        }

        private void OnToggleCustom()
        {
            _customOpen = !_customOpen;
            Refresh();
        }

        private void OnCustomBody(string body)
        {
            _custom.Body = body;
            // Keep a legal profile for the new body: Kerbin takes Suborbital/Orbit, every other world Flyby/OrbitBody/Land.
            if (!ProfileAllowed(_custom.Profile))
                foreach (MissionProfile p in CustomProfiles) if (ProfileAllowed(p)) { _custom.Profile = p; break; }
            Refresh();
        }

        private void OnCustomProfile(MissionProfile profile)
        {
            _custom.Profile = profile;
            Refresh();
        }

        private void OnCustomOrbits(int delta)
        {
            _custom.Orbits = Math.Max(1, Math.Min(50, _custom.Orbits + delta));
            Refresh();
        }

        private void OnCustomSurfaceDays(int delta)
        {
            _custom.SurfaceDays = Math.Max(1, Math.Min(400, Math.Round(_custom.SurfaceDays) + delta));
            Refresh();
        }

        private void OnCustomCrew(int delta)
        {
            _custom.CrewSize = Math.Max(1, Math.Min(6, _custom.CrewSize + delta));
            Refresh();
        }

        private void OnCustomScience()
        {
            _custom.Science = !_custom.Science;
            Refresh();
        }

        private void OnDefineCustom()
        {
            string message;
            MissionType t = _agency.DefineCustomMission(_custom, out message);
            if (t != null)
            {
                _selectedTypeId = t.Id;
                _selectedTemplateId = string.Empty;
                _selectedCrew.Clear();
                _mode = _agency.DefaultModeFor(t);
                _customOpen = false;
                _custom.Name = string.Empty;
            }
            Report(t != null, message);
        }

        // --- Finished projects ---

        private void BuildFinishedSection(List<DialogGUIBase> items, float textWidth)
        {
            var finished = new List<Project>();
            for (int i = _agency.Projects.Count - 1; i >= 0 && finished.Count < FinishedShown; i--)
                if (!_agency.Projects[i].IsActive) finished.Add(_agency.Projects[i]);

            Section(items, "Finished projects", textWidth);
            if (finished.Count == 0) Line(items, "Nothing finished yet.", textWidth);
            foreach (Project p in finished)
            {
                string id = p.Id;
                string outcome = p.Report != null ? Agency.OutcomeText(p.Report.Outcome) : StatusText(p.Status);
                if (p.Report == null && p.Status == ProjectStatus.Cancelled && p.Notes.Exists(n => n.StartsWith("Called off")))
                    outcome = "<color=#ffd27f>called off: no workable design with the parts researched (budget returned)</color>";
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    new DialogGUIButton("Design", () => OpenDesignReview(id), 90f, ButtonHeight, false),
                    new DialogGUILabel(p.Id + "  " + p.Name + ": " + outcome + (p.EndDay >= 0 ? " (day " + p.EndDay + ")" : ""), textWidth - 118f, LineHeight)));
            }
        }

        // --- Design Review child window (one at a time) ---

        private void OpenDesignReview(string projectId)
        {
            if (_designReview != null && _designReview.IsOpen) _designReview.Close();
            _designReview = new DesignReviewWindow(_agency, projectId, () => { _designReview = null; Refresh(); });
            _designReview.Open();
        }

        private void CloseChildren()
        {
            if (_designReview != null) { DesignReviewWindow w = _designReview; _designReview = null; w.Close(); }
        }

        /// <summary>One line on where the project's design stands: AI record, template, or a session still running.</summary>
        private string DesignLine(Project p)
        {
            if (_agency.DesignInProgress(p.Id)) return "Design: team at work...";
            KSA.Core.Design.DesignRecord d = p.Design;
            if (d == null || d.Source != KSA.Core.Design.DesignSource.Ai) return "Design: template";
            string name = d.DesignName.Length > 0 ? d.DesignName : "(unnamed)";
            return "Design: " + name + " (AI, " + d.Verdict + ", " + d.TotalDeltaV.ToString("0") + " m/s)";
        }

        // --- Director actions: call the Agency, remember its message, rebuild ---

        private void OnSetMode(string projectId, MissionMode mode)
        {
            string message;
            bool ok = _agency.SetProjectMode(projectId, mode, out message);
            Report(ok, message);
        }

        private void OnLaunchNow(string projectId)
        {
            string message;
            bool ok = _agency.LaunchNow(projectId, out message);
            Report(ok, message);
        }

        private void OnCancel(string projectId)
        {
            string message;
            bool ok = _agency.CancelProject(projectId, out message);
            Report(ok, message);
        }

        private void OnSelectType(string typeId)
        {
            if (!string.Equals(_selectedTypeId, typeId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTypeId = typeId;
                _selectedTemplateId = string.Empty;
                _selectedCrew.Clear();
                _mode = _agency.DefaultModeFor(_agency.Catalogs.Missions.Get(typeId));
            }
            Refresh();
        }

        private void OnSelectTemplate(string templateId)
        {
            _selectedTemplateId = templateId;
            _selectedPlayerDesign = string.Empty;
            Refresh();
        }

        private void OnPreferDesign(string missionTypeId, string designName, bool on)
        {
            _agency.SetPreferredDesign(missionTypeId, on ? designName : null);
            MissionType t = _agency.Catalogs.Missions.Get(missionTypeId);
            _status = on ? "The AI Director will fly " + designName + " whenever it starts " + (t != null ? t.Name : missionTypeId) + "." : "The AI Director picks its own design for " + (t != null ? t.Name : missionTypeId) + " again.";
            Refresh();
        }

        private void OnSelectPlayerDesign(string craftPath)
        {
            _selectedPlayerDesign = _selectedPlayerDesign == craftPath ? string.Empty : craftPath; // a second click unpicks it
            Refresh();
        }

        private void OnToggleCrew(string crewId, int crewSize)
        {
            if (_selectedCrew.Contains(crewId)) _selectedCrew.Remove(crewId);
            else if (_selectedCrew.Count < crewSize) _selectedCrew.Add(crewId);
            else _status = "This mission carries " + crewSize + " crew (set per mission type in missions.cfg). Unpick someone first, or choose a bigger mission.";
            Refresh();
        }

        private void OnToggleEngineer(string crewId)
        {
            if (_selectedEngineers.Contains(crewId)) _selectedEngineers.Remove(crewId);
            else if (_selectedEngineers.Count < MaxEngineers) _selectedEngineers.Add(crewId);
            else _status = "Engineering team is full (" + MaxEngineers + "). Unpick someone first.";
            Refresh();
        }

        private void OnAutoStaff()
        {
            MissionType type = _agency.Catalogs.Missions.Get(_selectedTypeId);
            _selectedCrew.Clear();
            _selectedCrew.AddRange(_agency.AutoCrew(type));
            _selectedEngineers.Clear();
            _selectedEngineers.AddRange(_agency.AutoEngineers(2));
            _status = "Auto-staffed: " + _selectedCrew.Count + " crew, " + _selectedEngineers.Count + " engineer" + (_selectedEngineers.Count == 1 ? "" : "s") + ".";
            Refresh();
        }

        private void OnSetNewMode(MissionMode mode)
        {
            _mode = mode;
            Refresh();
        }

        private void OnStartProject()
        {
            MissionType type = _agency.Catalogs.Missions.Get(_selectedTypeId);
            if (type == null) { Report(false, "Choose a mission type first."); return; }
            string message;
            string mine = string.IsNullOrEmpty(_selectedPlayerDesign) ? null : _selectedPlayerDesign;
            Project p = _agency.CreateProject(type.Name, type.Id, mine != null ? null : _selectedTemplateId, new List<string>(_selectedCrew), new List<string>(_selectedEngineers), null, _mode, out message, null, mine);
            if (p != null)
            {
                _selectedTypeId = string.Empty;
                _selectedTemplateId = string.Empty;
                _selectedPlayerDesign = string.Empty;
                _selectedCrew.Clear();
                _selectedEngineers.Clear();
                _mode = MissionMode.Simulate;
            }
            Report(p != null, message);
        }

        private void Report(bool ok, string message)
        {
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Projects: " + _status);
            else KsaCore.Log.Warn("Projects: " + _status);
            Refresh();
        }

        private void OnCloseClicked()
        {
            _agency.ProjectChanged -= OnProjectChanged;
            CloseChildren();
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private static void PruneSelection(List<string> selected, List<CrewProfile> available)
        {
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                bool found = false;
                foreach (CrewProfile c in available) if (string.Equals(c.Id, selected[i], StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) selected.RemoveAt(i);
            }
        }

        private string NameOf(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return "none";
            CrewProfile p = _agency.Crew.Get(crewId);
            return p != null ? p.Name : crewId;
        }

        private string Names(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "none";
            var names = new List<string>();
            foreach (string id in ids) names.Add(NameOf(id));
            return string.Join(", ", names.ToArray());
        }

        private static string ModeText(MissionMode m)
        {
            return m == MissionMode.WatchLive ? "Watch Live" : "Simulate";
        }

        private void OnProjectChanged(Project p) { if (_dialog != null) Refresh(); }

        private static string StatusText(ProjectStatus s)
        {
            switch (s)
            {
                case ProjectStatus.Design: return "in design";
                case ProjectStatus.Build: return "in build";
                case ProjectStatus.Ready: return "ready to launch";
                case ProjectStatus.InFlight: return "in flight";
                case ProjectStatus.Complete: return "complete";
                case ProjectStatus.Failed: return "failed";
                case ProjectStatus.Cancelled: return "cancelled";
                default: return s.ToString();
            }
        }

        private static string Percent(double v)
        {
            return ((int)Math.Round(v * 100.0)) + "%";
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

        /// <summary>Lays out <paramref name="count"/> buttons in rows that fit the text width.</summary>
        private static void ButtonRows(List<DialogGUIBase> items, int count, float textWidth, float buttonWidth, Func<int, DialogGUIBase> make)
        {
            int perRow = Math.Max(1, (int)((textWidth - 16f) / (buttonWidth + 6f)));
            for (int start = 0; start < count; start += perRow)
            {
                var row = new List<DialogGUIBase>();
                row.Add(new DialogGUISpace(16f));
                for (int i = start; i < count && i < start + perRow; i++) row.Add(make(i));
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, row.ToArray()));
            }
        }

        private static DialogGUIHorizontalLayout Field(string label, string value, float textWidth)
        {
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>" + label + "</b>", LabelWidth, LineHeight),
                new DialogGUILabel(value, textWidth - LabelWidth, LineHeight));
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
