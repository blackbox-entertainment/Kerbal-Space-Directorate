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

1. Click the **Administration** building: the KSA Programs and Analytics windows open with it and close with it. **Astronaut Complex**: Recruiting opens. **Mission Control** building: the agency Mission Control opens. In the **Tracking Station** the KSA toolbar button opens Mission Control. The Director's Office on the Space Center toolbar still reaches everything.
2. **Rendered portraits (KSA-56).** Open the Roster: for about half a second each kerbal shows the generic suit sprite, then it swaps to a rendered face (the kerbal's own IVA model, helmet off, veteran suit for the originals). The Service Record shows it at 128 px, the Hall of Fame too. PNGs appear under `E:KSP_devGameDataKSAPluginDataportraits`. If faces are black, helmeted or empty, send me the `[KSA] Portrait rig:` and `[KSA] Portrait` lines from KSP.log; delete the portraits folder to force a re-render.

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
   **Local instead (your G:i_stack):** Ollama is already running on this PC but has no models pulled. Either run `ollama pull qwen2.5:7b` in PowerShell, or start llama.cpp on one of your GGUFs: `G:i_stacklamacpp_cudalama-server.exe -m G:i_stackwen3\.5\-9B\-Q4_K_M\.gguf\ \-\-port\ 8081\ \-ngl\ 99\`\ \(keep\ that\ window\ open\)\.\ Then\ in\ AI\ Setup\:\ mode\ \*\*Local\*\*\,\ press\ the\ \*\*Ollama\ \:11434\*\*\ or\ \*\*llama\.cpp\ \:8081\*\*\ preset\,\ press\ \*\*List\ models\ on\ server\*\*\,\ click\ the\ model\ it\ reports\,\ \*\*Test\ connection\*\*\,\ \*\*Save\*\*\.\ Hybrid\ mode\ uses\ the\ API\ key\ for\ design\ and\ the\ local\ model\ for\ narrative\ and\ crew\ decisions\. To have KSA start llama-server itself: in AI Setup tick **Start server with the game**, set Server exe to `G:i_stacklamacpp_cudalama-server.exe` and Server arguments to `-m G:i_stackwen3\.5\-9B\-Q4_K_M\.gguf\ \-\-port\ 8081\ \-ngl\ 99\`\,\ Save\,\ restart\ KSP\.\ KSP\.log\ shows\ \"Started\ local\ model\ server\"\;\ it\ is\ stopped\ when\ KSP\ quits\.\ If\ crew\ or\ voxlore\ already\ run\ a\ server\,\ leave\ auto\-start\ off\ and\ point\ the\ endpoint\ at\ their\ port\.
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
