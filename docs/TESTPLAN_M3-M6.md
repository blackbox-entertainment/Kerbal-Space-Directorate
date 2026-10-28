# Test plan: M3 to M6 (written 2026-10-26)

Everything below runs in **KSP_dev** (`E:\KSP_dev`), in a **Career** save. All KSA log lines start with `[KSA]` in `E:\KSP_dev\KSP.log`.

Before you start:

1. In VS Code's terminal: `$env:KSPDIR="E:\KSP_dev"; dotnet build KSA.sln -c Debug --nologo` (should say `Build succeeded`; it copies the DLLs and configs into KSP_dev).
2. Start KSP_dev, load your Career save. The toolbar button opens the Director's Office.

Mark each step pass or fail. If something fails, copy the `[KSA]` lines from the log around that moment.

---

## A. M3: Projects and simulated missions (no AI needed)

1. **Projects window.** Director's Office → Projects. You should see "New project" with mission-type buttons (Suborbital Hop, Kerbin Orbit, ...). Pick Suborbital Hop, pick a template, tick one pilot, tick one or two engineers (or press Auto-staff), leave mode on Simulate, press Start project. Funds drop by the project cost.
2. **Pipeline.** The project shows "Design, N days left". Use time warp at the space centre (or the tracking station) for ~30 days. The status moves Design → Build → Ready → launched (simulated) → Complete or Failed. Screen messages announce each step.
3. **Mission Control.** Director's Office → Mission Control. The report lists the outcome, crew, a few narrative lines, achievements and the odds (design %, crew %, chance). Open the pilot's Service Record: the mission is in their record with the achievements.
4. **Programs.** Director's Office → Programs → New program → "Kerbin Station" (or Mun Base) at a body → Start. A module project appears in Projects. Warp a few months: the next module starts after the previous succeeds. "Resume" shows up if a module fails.
5. **Mode selector.** Create a Kerbin Orbit project and press "Watch Live" before starting it. On Ready it should NOT auto-launch; a notice says "launch it live from Mission Control" (that's M5 below).
6. **Save/load.** Quicksave, quit to the main menu, load. Projects, programs, reports and news are still there.

## A3. Buildings (added 2026-10-27)

1. Click the **Administration** building: a small "KSA:" bar appears at the top of the screen with **Programs** and **Analytics** buttons (nothing pops open by itself); the windows close when you leave the building. **Astronaut Complex**: the bar offers **Recruiting** and **Roster**. **Mission Control** building: **Mission Control**. Stock hiring from the Astronaut Complex list still works at the stock price and the kerbal joins the roster; a screen message says it skipped KSA interviews. In the **Tracking Station** the KSA toolbar button opens Mission Control. The Director's Office on the Space Center toolbar still reaches everything.
2. **Rendered portraits (KSA-56).** Open the Roster: for about half a second each kerbal shows the generic suit sprite, then it swaps to a rendered face (the kerbal's own IVA model, helmet off, veteran suit for the originals). The Service Record shows it at 128 px, the Hall of Fame too. PNGs appear under `E:KSP_devGameDataKSAPluginDataportraits`. If faces are black, helmeted or empty, send me the `[KSA] Portrait rig:` and `[KSA] Portrait` lines from KSP.log; delete the portraits folder to force a re-render.
3. **Departments.** Roster → select a 3-star kerbal for Program Manager (Management track). Director's Office → **Departments** (also on the KSA bar in the Administration building): Appoint them to a post; the line under the post states the bonus. They vanish from project crew pickers. Year end: "Head of Finances trimmed the payroll by ..." and a note per filled post.
4. **Stock systems.** Mission Control building: the offered-contracts list is empty and stays empty (KSP.log: "Withdrew N stock contract(s)"). Astronaut Complex: the stock list still shows applicants and its own "Next hire" price, but hiring one refunds that price and charges the KSA walk-in rate (2,000 to 4,000 funds depending on recruiting.cfg); a screen message says so. Set `stockContracts = true` / `stockHiring = true` in agency.cfg for stock behaviour.
5. **Career progression.** In a fresh career the Projects window greys out every mission except the **Flea test hop** ("(research)" after the name); designs that need unresearched parts show "[needs research: ...]" on their button. Roster → press **Lab** on Bob: the Director's Office Research line shows the Science he will make at year end; warp past year end: "+N Science: Research lab" on screen and the stock Science counter rises. A successful hop adds mission Science. Government goals never ask for orbit while orbit is unreachable. Local AI design requests may now take up to 15 minutes before giving up (7B models are slow on the first call).
6. **Science missions.** Projects: "Kerbin biome survey" is a day-one science mission (a Flea-class hop with the goo canister). Put Bob on it: the Design Review shows the yield, and on success the office logs "+N Science" with the scientist multiplier (25 x 2.6 for a 3-star scientist). Watch Live: the HUD shows "Science aboard: N (M experiments run)" ticking up as the goo fires in each situation; after recovery the stock Science counter also rises by what the experiments collected. A science mission flown with no data aboard resolves as Partial.
7. **Custom mission.** Projects → "Define your own mission": Kerbin, Orbit, Orbits 2, crew 1 → Define mission → it appears selected as "2 orbits of Kerbin (custom)"; start it like any project. Watch Live: the HUD says "Holding for 2 orbits (N min)" after circularisation and warps through them before reentry.
8. **Campaign.** Director's Office → Campaign: a row per body with Suborbit/Flyby, Orbit, Land, Science, Base, Supplied cells and a "next step" hint; government goals never skip a rung (no "Land on the Mun" before Orbit:Mun is on the books).
9. **Resupply.** Finish a station or base program, then Programs → "Send resupply" (or "Resupply (Watch Live)"). The row shows "Supplies: ..." and "Crew aboard"; on success the crews swap and the Service Records of the relief crew gain Resupply:<body>. Leave a program unsupplied past its interval and the year-end notices say so, with a morale hit to the crew aboard.
10. **Exploration medals.** Service Record → Exploration line (farthest km, days on other worlds, worlds visited, longest mission). After a Mun flight expect Far Traveller (Mun class) in the medals.

## A2. Recruiting tweaks (added 2026-10-27)

1. Recruiting → run a drive → the Interview button now reads "Interview (√200)". Press it: funds drop by 200 and the button becomes "Interview (√300)". A fully interviewed applicant shows "Interviewed". With a senior crew member on staff each interview reveals two facts, so it costs half as much to learn everything.
2. Hire an applicant without interviewing: allowed, you just take the risk.
3. Roster → Dismiss someone: the message says how far morale across the agency fell. Check another kerbal's morale column dropped by that much. The hit scales with roster size: with 12 kerbals left a dismissal costs 4 and a death 15; with 3 left it is 8 and 30; with 99 left it is 1 and 5 (`GameDataKSAConfigsmorale.cfg`, KSA_MORALE_SCALING).
4. Service Record → "Traits": after a completed mission the pilot's stupidity drops a little and courage rises; the Astronaut Complex shows the same numbers (the mod writes them back to the kerbal).
5. Director's Office → Analytics: a year-over-year table (grant, spend, net, balance, prestige, missions, deaths, goals, review band) and the Space race league table with three rival agencies. Warp past a year end: rivals fly, headlines appear in the Press Room ("Northern Launch Directorate completes suborbital flight"), and the Firsts list shows who got there first.

## B. M4: AI engineering

Needs an OpenRouter key (or Ollama running locally with a model pulled).

G:\ai_stack\llamacpp_cuda\llama-server.exe -m G:\ai_stack\Qwen3.5-9B-Q4_K_M.gguf --port 8081 -ngl 99


1. **AI Setup.** Director's Office → AI Setup. Pick mode "API", paste the key, choose "GPT-4o mini" for Design, press **Test connection**. Within a few seconds the status line should say the model replied. Press **Save**. The status shows the path `...\GameData\KSA\PluginData\ksa_settings.cfg`. That file holds your key: never commit it (it is git-ignored). After Save, open the file: the key line reads `apiKey = dpapi:...`, encrypted to your Windows account (DPAPI); it cannot be read by another user or on another PC.
   **Local instead (your G:\ai_stack):** Ollama is already running on this PC but has no models pulled. Either run `ollama pull qwen2.5:7b` in PowerShell, or start llama.cpp on one of your GGUFs: `G:\ai_stack\llamacpp_cuda\llama-server.exe -m G:\ai_stack\Qwen3.5-9B-Q4_K_M.gguf --port 8081 -ngl 99` (keep that window open). Then in AI Setup: mode **Local**, press the **Ollama :11434** or **llama.cpp :8081** preset, press **List models on server**, click the model it reports, **Test connection**, **Save**. Hybrid mode uses the API key for design and the local model for narrative and crew decisions.
   **Local, the easy way:** AI Setup now checks for Ollama when it opens. No Ollama: a "Get Ollama" button opens the download page, then "Check again". Ollama with no model: one button, "Set up local AI", downloads a model picked for your graphics memory, tests it and saves. Ollama with models: "Use it" switches local AI on. Everything else (llama.cpp, LM Studio, endpoints, server exe) is under an Advanced toggle. Director's Office also has an Agency name field with a Rename button, and the Analytics league marks your row "(you)". On an 8 GB card the game and a 7B model do not fit together (the first design request on this PC timed out inside Ollama with the GPU at 99%); the setup now recommends Gemma 3 4B below 11 GB. Switch: AI Setup → Advanced → Gemma 3 4B → Download this model → Use it. The simple section offers three sizes side by side, Tiny (1B, 0.8 GB), Small (4B, 3.3 GB) and Standard (7B, 4.7 GB), with "fits this PC" on the right one; pressing a size downloads it if needed, tests it and saves it.
   **Local, by hand (your G:i_stack):** Ollama is already running on this PC but has no models pulled. In AI Setup you can now pick a local model and press **Pull on Ollama**: the mod downloads it through Ollama (2 to 6 GB, status updates when done). Either run `ollama pull qwen2.5:7b` in PowerShell, or start llama.cpp on one of your GGUFs: `G:i_stacklamacpp_cudalama-server.exe -m G:i_stackwen3\.5\-9B\-Q4_K_M\.gguf\ \-\-port\ 8081\ \-ngl\ 99\`\ \(keep\ that\ window\ open\)\.\ Then\ in\ AI\ Setup\:\ mode\ \*\*Local\*\*\,\ press\ the\ \*\*Ollama\ \:11434\*\*\ or\ \*\*llama\.cpp\ \:8081\*\*\ preset\,\ press\ \*\*List\ models\ on\ server\*\*\,\ click\ the\ model\ it\ reports\,\ \*\*Test\ connection\*\*\,\ \*\*Save\*\*\.\ Hybrid\ mode\ uses\ the\ API\ key\ for\ design\ and\ the\ local\ model\ for\ narrative\ and\ crew\ decisions\. To have KSA start llama-server itself: in AI Setup tick **Start server with the game**, set Server exe to `G:i_stacklamacpp_cudalama-server.exe` and Server arguments to `-m G:i_stackwen3\.5\-9B\-Q4_K_M\.gguf\ \-\-port\ 8081\ \-ngl\ 99\`\,\ Save\,\ restart\ KSP\.\ KSP\.log\ shows\ \"Started\ local\ model\ server\"\;\ it\ is\ stopped\ when\ KSP\ quits\.\ If\ crew\ or\ voxlore\ already\ run\ a\ server\,\ leave\ auto\-start\ off\ and\ point\ the\ endpoint\ at\ their\ port\.
2. **Part list.** Press **Dump part list**. Open `E:\KSP_dev\GameData\KSA\PluginData\parts_catalog.txt` and glance at it: only researched parts in Career, kind column filled (Command, Tank, Engine, Booster, Decoupler, Parachute...). If a kind looks wrong, note the part name.
3. **AI design.** Create a Kerbin Orbit project (Simulate mode). The Projects row shows "Design: team at work...". Within 10 to 60 seconds it becomes "Design: <name> (AI, Approved, NNNN m/s)" or "Design: template" with a note why. Press **Design** to open the Design Review: figures, stages, rationale, review log, lessons.
4. **Craft file.** When the project reaches Build, a message says "vehicle assembled (N parts)". Go to the VAB → Load: a craft named "KSA P1 <project name>" exists. Load it. **This is the riskiest part of M4:** check that parts are connected in a sensible stack, boosters are around the first stage, staging order looks right (engines first, parachute last). Screenshot anything odd.
5. **Cost guard.** In AI Setup, set max calls per day to 1, save, start two projects. The second shows "Daily AI call cap reached" in its notes and flies the template.
6. **Request revision.** On a project in Build, Design Review → Request revision. It goes back to Design and a new session runs.
7. **AI off.** Set mode to Off, save. New projects say "Design: template" and everything still works.

## C. M5: Live flight

No MechJeb needed. The built-in flight director flies every mission type live except Eve surface/cloud missions (see C2 below for what to expect beyond Kerbin). `liveFlightScope` in agency.cfg can narrow it to Kerbin or Kerbin+moons while you test.

1. Create a **Suborbital Hop** with mode **Watch Live**. Warp until Ready. Mission Control → "Pad and flights" → **Launch live**. The game switches to the launch pad with your pilot aboard the craft (AI craft if one was built, else the stock Kerbal 1).
2. A small "KSA Flight Director" HUD appears. The rocket should ignite, climb, tilt east, stage on burnout, coast, come back down retrograde and pop chutes. Watch the HUD phase line and the log (`[KSA] Flight ...` lines).
3. After touchdown the existing auto-recovery countdown runs (10 s). Recovery closes the mission: a screen message gives the outcome, Mission Control shows the flight log under the report, and the pilot's record has Flight/Suborbit achievements.
4. Repeat with **Kerbin Orbit**. Expect: cutoff near 85 km apoapsis, circularisation burn, ~10 minutes in orbit, deorbit burn, reentry, chutes. If the rocket runs dry before orbit the flight resolves as partial/abort with the real reason in the log.
5. **Take Control.** In `GameData\KSA\Configs\agency.cfg` set `manualOverride = true`, restart the game, fly a live mission and press **Take control** on the HUD. The report is marked "Director-flown".
6. **Post-mortem.** With API mode on, after a live mission a "Post-mortem filed" message arrives a few seconds later; Mission Control shows it under the report and the Press Room gets a "Post-mortem filed" dispatch.
7. **Abandon.** Start a live flight, quit to the space centre without recovering, open Mission Control → "Resolve as simulated" (two clicks). The project closes with a simulated outcome.

**Check B4 closely for boosters:** the craft-template work found that stock files rotate Z-oriented radial parts (SRBs, solar panels, batteries) the opposite way from X-oriented ones (RCS, radial decouplers); CraftWriter uses one rule for all, so an AI design with boosters may show them clipped inside the core tank in the VAB. If so, send me a screenshot and I will fix `KSA.KSP/Build/CraftWriter.cs` SurfaceAttach.

Known limitations to expect, not bugs: boosters attach without radial decouplers if the catalog has none researched; the ascent is simple and can fail on odd rockets (that is a real mission failure, it goes in the record).

## C2. M5: Beyond Kerbin orbit (added 2026-10-27, KSA-51/52/53)

The flight director runs a list of legs (shown on the HUD as "Leg: name (n/total)"); the full plan is printed to KSP.log as `[KSA] Flight plan for ...`. Each leg logs its burns and decisions to the flight log. New stock templates live in `GameDataKSATemplates` (Mun lander, interplanetary stack, Duna lander, station core, station module); they have never been flown, so check them in the VAB first (load them from the mod folder by copying into ShipsVAB if you want to look).

1. **Mun flyby, Watch Live.** Expect: ascent to 85 km, a transfer burn (a maneuver node appears, the craft warps to it and burns), "encounter predicted" in the log, a small correction burn, warp to the Mun SOI, "Entered the sphere of influence of Mun" and a Flyby achievement, past periapsis, back into Kerbin SOI, a correction to 35 km periapsis, reentry, chutes, recovery. Outcome Success with Flyby:Mun.
2. **Mun orbit.** As above, plus a capture burn at Mun periapsis ("Captured into X x Y km orbit"), ten minutes in orbit, a return burn found by searching the next orbit, reentry.
3. **Mun landing.** Needs the Mun lander template or an AI craft. After capture: deorbit, warp down, "Site survey: flattest cell ..." in the log, braking burn, gear down, touchdown under 3 m/s with the slope logged, one crew member EVAs for 45 s and boards again (EVA:Mun achievement), liftoff, orbit, return, reentry. If the EVA fails to board, the log says so clearly and the flight continues; tell me.
4. **Station core, then a module.** Create a Kerbin Station program; set the core module project to Watch Live before it is Ready. The core launches, circularises, "Station core delivered; the crew stays aboard" closes the mission and the vessel stays in orbit. Then set the next module to Watch Live: it launches 15 km lower, matches planes, plans an intercept, matches velocity under 2.5 km, and docks with RCS ("Hard dock with ..."). The module craft needs RCS and a docking port (the stock station module template has both). If the core was simulated, the module says so and simulates.
5. **Duna flyby (long).** The ejection waits for the next window (the HUD shows the date); warping through a year or more is expected. Interplanetary legs are the least tested; send me the flight log whatever happens.
6. **Halted program.** If any leg fails (no propellant, no trajectory found), the HUD says "Flight program halted", the crew holds, and the HUD offers "Retry program". With manualOverride on you can Take control instead; Mission Control can also resolve the flight as simulated.
8. **Roll out yourself.** When a Watch Live project is Ready you have 20 s before it rolls out on its own (open Mission Control to hold it). To do it by hand: VAB → Load → "KSA P1 ..." under Current Game (or the mod's template under KSA Templates) → Launch. On the pad the flight director recognises the craft by name, swaps in whichever agency kerbals you seated, and flies it with the HUD, exactly like the automatic path.
9. **Launch on your command.** On the pad the HUD says "Holding on the pad" with a big LAUNCH button: press it (or the spacebar) and the crew takes it from there. `launchOnCommand = false` in agency.cfg makes them go the moment the pad is ready.
10. **Save mid-design.** Start a project, quit to the main menu while the Design Review still says "Pending: team at work", reload. The project is still in Design and the note says "Design session resumed after a reload"; the team asks the model again and the verdict arrives as usual. Nothing restarts from scratch.
7. **The kerbals fly.** The flight log opens with the pilot's profile ("Jeb Kerman at the controls (3 stars): precision 68%, bold margins, mistake chance 9% per leg"). A low-star, high-stupidity pilot will sometimes log "slow on the ignition call" and burn late; a brave pilot brakes later on landings. With a local model set up (AI Setup) and `crewDecisions = Local` in agency.cfg, a rough landing site or a halted program makes the pilot answer in character (one radio line in the log, "Source AI" in KSP.log); without a model the trait rule answers the same way. Set `crewDecisions = Rule` to compare.

## D. M6: Narrative

1. **Press Room.** Director's Office → Press Room (the button shows "(N new)"). Every mission, hire, resignation, year-end review and approved design has an item. Filters work. The ticker line on the Director's Office shows the latest headline.
2. **Local model (optional).** Install Ollama, run `ollama pull llama3.1:8b`, in AI Setup set the Local model and mode Local or Hybrid, save. New press releases and memos get rewritten in place within a few seconds (Source changes from Canned to AI). Cloud API budget is never used for this.
3. **Year end.** Warp past year end: a government review item plus, if anyone is below 35 morale, a memo from that kerbal.

---

## What to send me

- Pass/fail per step, screenshots of the VAB craft (B4) and the HUD during a live flight (C2).
- The `[KSA]` log lines for any failure.
- Your call on three open points: (1) keep the courage/stupidity drift plan (KSA-50), (2) Jira project rename to "Kerbal Space Directorate" (KSA-15), (3) lock the supported mod set (KSA-16).

## C3. M5: Hangar flights (added 2026-10-27, KSA-62)

1. Build any rocket in the VAB (not a KSA project craft), seat a KSA kerbal, press Launch.
2. On the pad the Flight Director window asks "What should the crew fly?" with Suborbital hop / Orbit and return / Let me fly, and shows the vacuum delta-v with a suggestion (orbit at 3,300 m/s or more).
3. Pick a mission: a project "Hangar flight: <craft name>" appears in Projects as In flight, costs nothing, and the HUD holds on the pad with LAUNCH.
4. Press LAUNCH (or the staging key): the crew flies. Do not touch the controls; the mission runs to recovery and the project completes with the usual records and post-mortem.
5. Fly another one and tap W/A/S/D, the throttle, or the space bar after the go: the HUD says "Director took the controls", the kerbal stops flying, and "Hand back to crew" re-plans from where you left it. The record says Director-flown (reduced rank credit).
6. "Let me fly" leaves the craft alone and does not ask again for that vessel.
7. If the only kerbal aboard is already assigned to another project, no offer appears (log: no "adopted hangar rocket" line).

## C4. Mission experience (added 2026-10-27, KSA-63)

1. Open a kerbal's Service Record: a new Experience line shows points and how many are needed for the next star (ladder 4 / 12 / 28 / 60 / 110, agency.cfg EXPERIENCE).
2. Fly or simulate a Flea test hop to success with a 0-star pilot. The project notes say "Crew experience: +7 each (includes the First bonus)" and the pilot reaches 1 star (screen message "... reaches 1 star").
3. Repeat the hop: +3 each, no First bonus the second time.
4. A simulated mission now also writes its achievements into the stock flight log, so the Astronaut Complex shows the same stars after a scene change.
5. A Director-flown flight pays half; a failed mission the crew survived pays a quarter; dead crew earn nothing.
