# CLAUDE.md: Kerbal Space Directorate (KSA)

Instructions and context for Claude when working on this repo. Read this first, every session.

> **Name:** **Kerbal Space Directorate** (decided 2026-10-26; replaces "Kerbal Space Agency", which collided with RocketWerkz's *Kitten Space Agency*). The internal code name stays **KSA**: namespaces, `GameData/KSA`, the `[KSA]` log prefix, and the Jira key. Rename the GameData folder to KSD only at release (M7) if desired.

---

## 1. What this project is

A **Kerbal Space Program 1 (v1.12.x)** mod. The player is the **Director** of a space agency. They hire kerbals, assign them to projects, and manage a government budget. Kerbals design rockets with an AI "engineering team," fly missions on autopilot, earn ranks and medals, and sometimes die.

Full design lives in `docs/KSA_GDD.md`. This file covers how we build it.

**Design pillars**
1. Hands-off, not hands-tied. The player directs; kerbals execute.
2. People matter. Every kerbal has a career, personality, and service record.
3. Consequences over time. Failure is a story, not a reload.
4. Modular and playable at every stage.

---

## 2. Who you're working with

- **T (jaqknife)** is the product director and is **not a coder**.
- **Always deliver complete files**, never partial snippets or "add this near line 40." T copies whole files in.
- Give **exact file paths** for every file.
- Explain build/test steps as numbered instructions T can follow in **VS Code** and **PowerShell**.
- T tests in-game and commits to **GitHub**. When a change is done, suggest a short commit message.
- When something can go more than one way, give a recommendation, not just options.
- Flag risks plainly. Don't hide a hard problem behind a working-looking stub.

---

## 3. Architecture: the two-project rule

The code is split so the agency logic can be reused in a future standalone space game.

```
KSA.sln
├── KSA.Core/        Pure C#. NO references to KSP or Unity. Ever.
└── KSA.KSP/         Thin adapter connecting Core to KSP.
```

**KSA.Core** (portable)
- Personnel: kerbal profiles, career tracks, ranks, promotions, medals, morale
- Recruiting: applicant pool, sources, interviews, recruitment drives
- Agency: fiscal years, government goals, budget reviews, lean/crisis years
- Projects and Programs (multi-mission stations/bases)
- AI: prompt builder, stage plan schema + validation, post-mortems, narrative
- Simulated mission resolution
- Interfaces for anything platform-specific (`ILlmTransport`, `IClock`, `IPersistence`, `IPartCatalog`, `ILogger`)
- `DataNode`: the platform-neutral config/save tree. Core parses careers, medals, and save state from it; KSA.KSP converts ConfigNode to and from it. Tests load the real `GameData/KSA/Configs/*.cfg` through `DataNodeText`.
- `Agency`: the orchestrator. The host feeds it roster snapshots, mission events, a clock, names, and persistence; it owns the ledger, fiscal calendar, goals, reviews, applicant pool, and crew, and raises `Notice`, `CrewDeparted`, `ApplicantHired`, `YearEnded` for the host to act on. `Agency.Tick()` must be called regularly (KSA.KSP does it every 5 s and on scene start).

**KSA.KSP** (KSP-only)
- `ScenarioModule` save/load
- `GameEvents` hooks (recovery, crew killed, EVA, launch)
- VesselBuilder (stage plan → `ShipConstruct`)
- Δv/TWR validation via `VesselDeltaV`
- MechJeb / kOS bridges
- UI (DialogGUI / PopupDialog), AppLauncher button
- Implementations of Core's interfaces (HTTP via `UnityWebRequest`, KSP time, part list)

**Rules**
- If a file in `KSA.Core` needs `using UnityEngine;` or any KSP namespace, it's in the wrong project. Put an interface in Core and implement it in KSA.KSP.
- Core should be testable without launching KSP (`KSA.Core.Tests`, plain unit tests).
- Never use KSP or Squad assets, code, or names inside Core. Use generic terms (`Crew`, `Body`, `Part`) there; KSP names live in the adapter.
- Inside the `KSA.KSP` namespace, the game-provided `KSP.*` namespaces (e.g. `KSP.UI`, `KSP.Localization`) are shadowed. Refer to them as `global::KSP.UI` etc.

---

## 4. Tech stack and build

- **Language:** C#
- **Target framework:** .NET Framework 4.7.2 (matches KSP 1.12 / Unity 2019.4)
- **Editor:** **VS Code** (C# extension). **Do not ask T to install Visual Studio.**
- **Build:** .NET SDK (current LTS) with `dotnet build` / `dotnet test` from PowerShell or the VS Code terminal
- **Project files:** SDK-style `.csproj` with `<TargetFramework>net472</TargetFramework>` and the `Microsoft.NETFramework.ReferenceAssemblies` NuGet package (`PrivateAssets="all"`), so no .NET Framework targeting pack or Visual Studio is needed
- **Tests:** NUnit 4 in `KSA.Core.Tests`, run with `dotnet test`
- **Unity editor:** not needed for v1
- **References (KSA.KSP only):** from `$(KSPDIR)\KSP_x64_Data\Managed\`
  - `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`
  - `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, plus other `UnityEngine.*Module.dll` as needed
  - Set `<Private>false</Private>` on all of these (don't copy them to output).
- **KSP path:** environment variable `KSPDIR` = `E:\KSP_dev` (T's dev copy, managed in CKAN as instance "KSP_dev"). Steam install at `E:\SteamLibrary\steamapps\common\Kerbal Space Program` is play-only: never touch it.
- **Mods:** installed only through **CKAN** into KSP_dev. Steam Workshop is craft files only, not mods.
- **DLC:** KSA must never require Making History or Breaking Ground; templates must have stock-only versions.
- **Output:** DLLs copied to `$(KSPDIR)\GameData\KSA\Plugins\` by a post-build step.
- **Use a separate dev install of KSP**, not the one T plays on.
- **Core dependencies:** avoid third-party packages. If JSON parsing needs a library, check KSP compatibility first and note it here.

**Mod folder layout (in GameData)**
```
GameData/KSA/
├── Plugins/          KSA.Core.dll, KSA.KSP.dll
├── Configs/          ModuleManager data: careers, medals, goals, recruiting, morale, agency settings
├── Templates/        offline fallback .craft designs
├── Textures/         toolbar icon and other UI art (PNG, loaded via GameDatabase)
└── PluginData/       ksa_settings.cfg (user-local), logs/
```

---

## 5. Hard constraints

- **All LLM calls are async**, off the main Unity thread. The game never freezes waiting on AI.
- **The LLM never writes `.craft` files or places parts.** It outputs a JSON stage plan; C# builds the vessel.
- **Part names must come from the allowed part list** sent in the prompt. Unknown parts reject the plan.
- **Filter part lists by job** (e.g., only habs/docking/power for a station module). Don't send thousands of parts.
- **API keys never get committed or shipped.** `ksa_settings.cfg` is in `.gitignore`. Ship only `ksa_settings.example.cfg`.
- **No LLM models are bundled with the mod.** Local AI runs through Ollama / LM Studio; the mod points players to install it and pulls the recommended model on first-run setup.
- **The mod must work with AI off**, using the template library and canned text.
- **Data-driven content** (medals, goals, ranks, recruiting sources) lives in ModuleManager configs, not hardcoded.
- KSA must never require the Making History or Breaking Ground DLC; templates must have stock-only versions.

---

## 6. Decisions made so far

| Area | Decision |
|---|---|
| Planets | Stock **Kerbol system** (Eve, Duna, Dres, Jool moons). No real-solar-system packs. |
| Flight career track | Test Pilot → Astronaut Candidate → Astronaut → Senior Astronaut → Mission Commander → Chief Astronaut |
| Selection | Director picks which Test Pilots become Candidates each year; passed-over pilots lose morale |
| Recruiting | Paid recruitment drives; sources: Kerbin Air Corps, universities, industry poaching, walk-ins; interviews reveal partial stats; prestige affects applicant quality |
| Stations/bases | A **Program** layer above Projects (e.g., "Mun Base Alpha" = several module missions). Assembly defaults to Simulated. |
| Mission modes | Hybrid default: Simulated unless it's a "First" or the player clicks Watch Live. Since 0.6.11 (2026-10-30) "new ground" also flies live: any mission earning an achievement no live flight has earned yet (Agency.BreaksNewGround; simulated successes do not count; modules and rescues excepted) |
| AI providers | OpenRouter for design and kOS scripts; local (Ollama/LM Studio) for narrative |
| Hardware targets | Minimum 16 GB RAM (AI via API only), Recommended 32 GB (full mod set, optional small local model). T's 64 GB dev machine is **not** a target. |
| GitHub owner | **Blackbox Entertainment** organization, GitHub slug `blackbox-entertainment` (decided 2026-10-26). Remote: https://github.com/blackbox-entertainment/Kerbal-Space-Directorate |
| Mod name | **Kerbal Space Directorate** (public name, decided 2026-10-26). Internal code name remains KSA. |
| Repo location | `G:\KSA` (local), VS Code with C# Dev Kit |
| Admin staff | Hireable Management-track kerbals replace the stock Administration faces in function (M2). Decided 2026-10-26. |
| Kerbal stats | Stock stars = competence. One growth stat per track (Leadership / Insight / Design) earned through assignments. Courage = risk tolerance, Stupidity = error chance. Personality tags + morale as modifiers. No more raw stats. |
| Research | A Project type run by Science-track kerbals, producing stock Science points per fiscal year; stock tech tree unchanged (M3). |
| Roster and dossier | Roster rows click through to a read-only Service Record (M1). Portraits use the Astronaut Complex texture (`ProtoCrewMember.SpritePath` via GameDatabase, fallback `AssetBase.GetTexture(GetKerbalIconSuitPath())`), plus class badges. Done in M1 (2026-10-26). |
| Mission mode | Per-mission Simulate / Watch Live selector, pre-filled from an agency default (Hybrid by default). Watch Live needs MechJeb, else falls back to Simulate. |
| Landing sites | Crew picks a flat spot: sample terrain slope around the target, shift to the flattest cell, per-body max slope. Feeds MechJeb in Live (M5), outcome roll in Simulated (M3). Bases reuse one site. |
| Manual flight override | On by default since 0.5.15 (T: the Director should be able to take control); was opt-in. Take Control button during Live flights; logged as Director-flown with reduced rank credit; not available in Simulated. Design override stays strict (M5). |
| Quit and retire | Yes: quit from low morale, retire from age or service; both via the Hall of Fame (M2). |
| Age and hall of fame | Age tracked from hire (M1); Hall of Fame window keeps retired and dead kerbals' records (M2). |
| Crisis Years | Hurt (cuts, cancellations, layoffs) and put the Director on probation; a second bad year replaces the Director, agency continues. Never game over (M2). |
| Community templates | Yes, after release: first-party templates ship, submission path opens post-M7. |
| AI models | Curated per-task list in AI Setup: cheapest-that-works default plus a few flagships, player-selectable; IDs live in a ModuleManager config, benchmarked in M4. |
| Eve and Jool cloud cities | HL Airships Core (M3). Angel-125 Airships stays compatible. Jool cloud city added 2026-10-27 (Late tier, 4-star crew, simulated only). |
| Live flight | Built-in flight director flies Kerbin suborbital/orbital missions live (M5); other profiles simulate until a MechJeb bridge exists. MechJeb is optional, not required. Decided 2026-10-26. |
| Narrative | Local model only (never cloud budget); canned text first, AI rewrite in place. Post-mortems may use the cloud. Decided 2026-10-26. |
| Courage/Stupidity drift | Planned: slow drift with missions (stupidity down, courage up on success, down on deaths/idle). Not built yet. |
| Interviews | Paid: 200 funds first round, +100 each further round (agency.cfg). Senior panel reveals two facts per round. Blind hiring allowed. Decided 2026-10-27. |
| Rival agencies | Three rivals from rivals.cfg simulated each fiscal year (since 2026-10-28 their flights are spread through the year, RivalSimulator.MissionDay; the review flies any left); firsts, league table and year-over-year figures in the Analytics window. On by default (agency.cfg rivals). Decided 2026-10-27. |
| Flight director | KSA's own, built 2026-10-27: ascent, transfers/capture (KSA-51), landing + EVA + ascent (KSA-52), rendezvous/docking (KSA-53). Legs per mission profile (KSA.KSP/Flight), portable math in KSA.Core/Flight/Orbital.cs. Eve surface stays simulated. MechJeb optional, never copied (GPL). |
| Crew competence in flight | The kerbals fly: pilot stars/courage/stupidity/morale set precision, margins, mistakes (agency.cfg FLIGHT_CREW). Judgement calls by trait rule or a local/API model in character (crewDecisions). Decided 2026-10-27. |
| Department heads | Built 2026-10-27: Management-track kerbals hold Finances / Science / PR / Operations (Departments window); rank-scaled bonuses from agency.cfg DEPARTMENTS. Stock strategies untouched. |
| Stock contracts and hiring | Off by default in KSA games (agency.cfg stockContracts, stockHiring): offered contracts withdrawn, stock applicant list emptied. Decided 2026-10-27. |
| Research and tech gating | Built 2026-10-27: templates and mission types gated on researched parts (MissionBlocker/TemplateBlocker); since 2026-10-28 a mission is open only when one of its templates can be built (goals still use the looser part-kind test, PartKindsBlocker), goals only when feasible; lab scientists and successful missions credit stock Science (agency.cfg RESEARCH); Flea test hop for day one. |
| Science missions | Built 2026-10-27 (KSA-58): purpose = Science mission types with scienceYield, Science:<body> achievement, crew multiplier, live experiments via ScienceRunner/ScienceSweepLeg. |
| Custom missions | Built 2026-10-27 (KSA-59): CustomMissionSpec → synthetic MissionType (IsCustom, Orbits, SurfaceDays) saved as CUSTOM_MISSION; templates by shape (Agency.TemplatesFor). |
| Exploration medals | Built 2026-10-27 (KSA-60): FarthestKm / DaysOnOtherWorlds / WorldsVisited / LongestMissionDays on the record; medal triggers FarthestDistance, DaysOnOtherWorlds, WorldsVisited, LongestMission. |
| Resupply and campaign ladder | Built 2026-10-27 (KSA-61): programs.cfg supplyIntervalDays/resupplyMissionType, Agency.CreateResupply, crew rotation, Base:/Resupply: achievements, goals.cfg `requires`, Campaign window. |
| Hangar flights | Built 2026-10-27 (KSA-62): any VAB-built craft with a free KSA kerbal aboard gets a pad offer (Suborbital hop / Orbit and return / Let me fly); since 0.6.13 every mission the craft can fly (seats, delta-v within 10%), up to ten, and SPH planes on the runway get the aircraft missions; Agency.AdoptHangarLaunch creates a zero-cost live project; any stick/throttle/staging input hands control to the Director (always allowed for hangar flights, agency.cfg manualOverride still governs project flights). |
| AI Director | Built 2026-10-28 (KSA-68): optional per-game AI that runs the agency (hire, interview, drives, lab, departments, research via KspResearchHost, projects, programs, resupplies, rescues, cancels). Turns every 30 days or on events; actions go through the same checks as the player; log and plan saved. Default model anthropic/claude-haiku-5.5 on OpenRouter (about a cent per game year). Flights setting: Simulate / Firsts live / Everything live; live flights auto roll out, auto go after a countdown, auto recover and return to the Space Center. agency.cfg AI_DIRECTOR. 2026-10-30 (T): once every goal of the year is met it saves money (spends only what was earned above the opening balance; conserveWhenGoalsMet), and it never repeats a mission that already succeeded unless an open goal needs it (Agency.RepeatBlocker), nor one already under way. Hires only for a short role (Agency.HireBlocker: pilots = biggest open crew + 1, min 2; engineers 2; scientists 2). |
| Names and research locks | Built 2026-10-28 (KSA-66): names.cfg with 160 male, 150 female and 120 family names (all original; 35% keep the traditional family name); NameCatalog in Core, KspNameSource falls back to the stock generator. Missions, programs, resupplies and rescues are greyed and their Start buttons disabled until every part they need is researched; CreateProgram refuses a locked program instead of leaving it stalled. |
| Colonies | Built 2026-10-27 (KSA-64): bases on every world of radius 60 km or more (adds Ike, Moho, Dres, Laythe, Vall, Tylo, Bop, Eeloo, Eve surface); every base and cloud city ends with a "return" module (a rocket that stays). Stages outpost > base > settlement (rocket, 3 resupplies, 4 residents; supplies x2) > colony (drill + ore converter + power from the delivered craft files, 6 resupplies, 8 residents; no supplies). Births need a man and a woman and room. Eve surface base requires the cloud city; its crews return via the city. Base modules fly live to every solid world incl. Eve surface; cloud cities and Jool simulated. agency.cfg COLONY. |
| Rescues | Built 2026-10-27 (KSA-65): strandings from simulated failures (15% Kerbin orbit, 35% away) or the HUD "Crew stranded" button; rescue projects from the Projects window; live orbital rescues rendezvous and transfer crew (EVA fallback), surface rescues simulated; lost after 852 days. agency.cfg RESCUE.; since 0.7.11 an orbital stranding with a real vessel is rescued live only (Agency.MustFlyLive: forced WatchLive, no Simulate, waits on the pad), transfer within 120 m |
| Mission experience | Built 2026-10-27 (KSA-63): every mission pays points to surviving crew (tier 2/4/8, +50% success, +4 on a First, +0.2/extra day, x0.5 Director-flown, x0.25 failed-but-survived); stars = max(stock level, ladder 4/12/28/60/110). Simulated missions write their achievements into the stock flight log. agency.cfg EXPERIENCE. |
| Template research gate | KspTemplateCheck wired 2026-10-27 (was written but unconnected): a project cannot use a template with unresearched or unbought parts; the Flea cluster template was removed 2026-10-28 (no day-one decoupler: it never reached space and fell too fast for its chute, killing Jeb); the suborbital hop needs Basic Rocketry (Kerbal 1).; the Hammer-Flea sounding rocket (plan template, 2026-10-28) is the first suborbital design: Start + Basic Rocketry + Engineering 101 parts only (stock Kerbal 1 needs far more research) |
| AI designs first | Decided 2026-10-28: with AI design on, templates are only the AI-off fallback, never a gate. A mission opens when the researched part kinds cover it (PartKindsBlocker; suborbital now needs decouplers); with no buildable template the project has no TemplateId, the team gets 3 design attempts (MaxAiDesignAttempts, the shortfall fed back as a lesson), then it is called off with a full refund. Designs must include a researched science instrument and, for crewed returns from orbit or beyond, a heat shield or a self-shielded pod (tag heatshielded, ModuleAblator); DesignRequest.MustInclude is checked in the design review. With Kerbalism, crewed designs must also carry an external antenna (data drives are tiny; it sends science home in flight). Also 2026-10-28: crewed designs must carry a parachute (base modules excepted); a design that flew a mission successfully is reused (DesignRecord.PartsResearched) until the researched part count changes; missions the engineers gave up on stay shut until new research (Agency.NoDesignAt, saved). Superseded in part 2026-10-28: KSA.Core/Design/ProceduralDesigner (rule-based, no AI) decides mission availability (Agency.DesignableBlocker, cached per researched-part count), replaces the call-off when the AI falls short (Model = Agency.EngineersModel), and designs with AI off; templates and AI designs are still tried first |
| Kerbalism and colony industry | Built 2026-10-28 (KSA-67): Kerbalism optional, found by assembly or GameData folder, no DLL reference. With it a colony also needs a greenhouse per 8 residents plus scrubber and water recycler capacity; live flights need food and water for durationDays + 2 or fly simulated; stranded crews lost after 20 days; supplied programs' live vessels kept topped up. Every surface base gets an optional "industry" module (Add module) built from stage-plan templates (Templates/Plans/*.json, assembled in game into PluginData/Templates): drill + Convert-O-Tron + panels, plus a greenhouse with Kerbalism. agency.cfg LIFE_SUPPORT. README lists requirements and CKAN identifiers. |
| Crew picking | Decided 2026-10-28: auto-staffed projects rotate pilots (fewest missions first, then longest since last flight, then most stars); an engineer flying a mission is never also its auto-picked design engineer. |
| Ground-work experience | Built 2026-10-28: lead engineer gets the design points (tier x success + First, designLead 1.0; team 0.25; failed 0.25) when the design flies, scientists 3 points per lab year paid daily (agency.cfg EXPERIENCE). Designs flown (Engineering) and full lab years (Science) count toward rank minMissions (PromotionEngine.MissionCredit). |
| Personality effects | Built 2026-10-27: PersonalityRules (agency.cfg PERSONALITY, TAG nodes) feed flight profile, morale scaling, idle immunity, prestige, science, design passes/score/time. |
| Help window | Built 2026-10-28: Help button in the Director's Office plus "?" buttons on Projects, Programs and AI Director; topics live in Configs/help.cfg (KSA_HELP_TOPIC, one "line" per paragraph, "# " headings), editable without a build. |
| Versioning | Decided 2026-10-28: MAJOR.MINOR.PATCH, below 1.0 until the M7 release; minor = roadmap milestone reached, patch = test builds within it. First shared build 0.5.0; 0.6.1 released at T's request on 2026-10-29 (the M5 flight-director fixes); 0.6.8 released at T's request on 2026-10-30 (learning from failures, AI Director fixes); 0.7.0 released at T's request on 2026-10-30 (orbit fixes, aircraft, facility upgrades); 0.7.3 released at T's request on 2026-10-30 (SAS steering fix, upgrades first, plane mods); 0.7.5 released at T's request on 2026-10-30 (probe missions, Un Kerballed Start); 0.7.9 released at T's request on 2026-10-30 (Un Kerballed Start, Airplane Plus and probe missions dropped); 0.8.0 released at T's request on 2026-10-30 (deorbit fix, live orbital rescues, scored recoveries, building limits). Version in Directory.Build.props and GameData/KSA/KSA.version (KSP-AVC format; a test keeps them equal); CHANGELOG.md per version; tools/package.ps1 builds Release, runs tests and writes dist/KerbalSpaceDirectorate-<version>.zip (dist/ is git-ignored); each shared build gets a git tag vX.Y.Z. The version shows in the Office title and KSP.log ("Kerbal Space Directorate v0.5.0 loaded"). |
| Launch spacing | Decided 2026-10-28: one pad, so automatic launches (simulated and live roll-outs) go launchSpacingDays (3) apart, State.LastLaunchDay saved; the Director's Launch now skips the queue. |
| Records | Built 2026-10-28: 0.7.12: live FarthestKm is from the home surface (BodyDistances.HomeRadiusKm, State.FarthestFromSurface corrects older saves once); ScrubStrandedFlights waits 30 s after a scene load (a recovered vessel leaves FlightGlobals before onVesselRecovered). FirstClaim carries Crew, Mission, Day (credited from the mission being resolved); Agency.Records holds HighestAltitude, Fastest, Farthest, LongestMission (flight) and MostDaysInSpace, MostMissions, MostWorlds (career); broken records make news (career ones when they change hands); MissionReport keeps MaxAltitude/MaxSpeed/FarthestKm; Records window off the Office. Lessons learned (newest 12 failed or partial flights) go into the AI Director briefing. |
| Learning from failures | Built 2026-10-30 (0.6.7): the flight director records a FailureCause (Tumbled, OutOfFuel, BurnedUp, HardLanding; BelowSpace from the resolver) with a measured detail; it becomes the report lesson and lessons-learned line. Agency.DesignFixes (per mission, saved) feed DesignRequest.Fixes: Tumbled = second-stage fins, OutOfFuel/BelowSpace = +15% margin each (DeltaVMargin, max 1.55), HardLanding = ChuteFactor 1.5, BurnedUp = heat shield even on hops. A failed mission's design is not reused; Director-flown failures add no fix. 0.6.9: the designer prefers gimballed engines (tag gimbal, Option.Fixed sorts after); the flight director keeps 10% throttle to steer on a gimbal while holding apoapsis or turning for circularisation; Kerbalism experiments pause below 40% charge to keep power for the reaction wheel. 0.6.10: DesignRecord.DesignerRevision; unflown engineers' designs older than ProceduralDesigner.Revision are redrawn on load (raise it whenever design rules change); the ascent's flight-path pitch is signed in the up-east plane. |
| Spaceplanes | Built 2026-10-28: SPH craft are PlayerDesigns (IsPlane, HasJets, HasWheels; jet thrust is not rocket delta-v, a 1200 m/s jet-climb credit instead) for Kerbin suborbital and orbital missions; they launch on the Runway; PlaneAscentLeg (takeoff, jet climb, rockets when jets fade incl. MultiModeEngine toggle, apoapsis, circularise) and PlaneReturnLeg (deorbit, nose-high reentry, glide; chutes or hand the Director the runway landing). The AI Director only uses player designs ticked for that mission, and planes only if they carry parachutes. Not yet: engineer-designed planes, rovers, airships flown live (roadmap). |
| Facility upgrades | Built 2026-10-30 (0.6.17, 0.7.13: FacilityOption PartLimit/CrewLimit/NoEva/NoFlightPlanning/NoPatchedConics/ScienceLimit enforced: PartsBlocker (VAB/SPH), CrewRoomBlocker (Hire, AI drives), EvaBlocker (MissionBlocker), PlanningBlocker (LiveBlocker, other worlds), ResearchLimitBlocker (AI research); UpgradeNeeds covers VAB, Astronaut Complex, R&D. PlayerDesign.HeatShieldRequired only for returns from other worlds (T: a suggestion from Kerbin orbit); engineers still fit one. T: "is the AI Director upgrading the facilities at all? the runway needs to be upgraded"): IFacilityHost (KSA.Core/Agency/FacilityHost.cs) implemented by KSA.KSP/KspFacilityHost.cs (ScenarioUpgradeableFacilities, GameVariables limits; upgrades only in the Space Center scene where the buildings are loaded). Agency.UpgradeFacility pays from the ledger (LedgerCategory.Facilities, refund on refusal, AI held to ConserveRefuses); AI Director action upgrade {facility} and a FACILITIES briefing section. Agency.DesignBlocker = PlayerDesign.Blocker plus SiteBlocker (craft mass over the pad or runway limit). 0.7.2 (T: "it has to do the upgrades first"): nothing launches over its pad or runway limit (DesignableBlocker and MissionBlocker use PadBlocker; ready projects wait, ProjectSiteBlocker, LaunchLive refuses); Agency.Facilities.cs UpgradeNeeds feeds UPGRADE NEEDED briefing lines (AFFORDABLE NOW or SAVE FOR IT, reserve half a year of salaries); needed upgrades bypass the year-end saving rule; UpgradeTick wakes the Director at the Space Center. |
| Aircraft missions | Built 2026-10-30 (0.6.12, T: "maybe we should make some planes and goals for planes"): MissionProfile.Atmospheric (missions.cfg cruiseAltitude, rangeKm, heading): FirstFlightKerbin, AerialSurveyKerbin (Science, west), HighAltitudeFlightKerbin (Science, above 18 km). Always one of the Director's SPH planes (PlayerDesign.Blocker: IsPlane, HasJets, wheels or chute); CreateProject picks the ticked or first fitting plane; engineers never design or swap in planes (DesignableBlocker, EngineersSwapBlocker); AI Director only with a ticked plane with parachutes. PartKind.Jet and Wing (never in rockets) gate goals (PartKindsBlocker). KSA.KSP/Flight/AircraftLegs.cs: takeoff, climb (ceiling detection), terrain lookahead, bank-to-turn with a whole-attitude SAS lock (VesselControl.PointAlong(nose, forwardAxis)), home at half range or half fuel, chute landing at the Space Center or HandToDirector. Achievements Aircraft:, Survey:, HighAir: (planes only); goals FirstAircraft, AerialSurvey, HighAltitudeFlight, RocketPlaneRecord; medal AviatorWings. 0.6.13: RocketPlaneKerbin (rocketDeltaV 600, zoom to 40 km: jets to ceiling, rockets lit (RAPIER to closed cycle), RocketPlane:Kerbin at 95%); RAPIERs rated by rocket mode in KspPlayerDesigns. |
| Leg-by-leg designer | Built 2026-10-29 (0.5.9): missions past a suborbital hop are FlightLeg lists from the stock delta-v map (KSA.Core/Design/FlightBudget.cs; per-world figures in dv_requirements.cfg KSA_DV_BODY): lift-off in thick air (sea-level Isp, SL TWR 1.3 to 2.6), climb, transfer, capture, descent (TWR 1.5 local, 1.3 with atmosphere), take-off, return; base modules and resupplies one way, the base "return" module carries the way home. LegWalk flies every design through the legs (StageAnalyzer, used for AI, engineers', recheck on load and design reuse). ProceduralDesigner sizes 1-6 stages over every leg partition, lightest first, 10% margin; solids only for the Kerbin climb; legs on the touchdown stage. Engineers design first, AI only for what they cannot (agency.cfg engineersDesignFirst). With stock parts: 61 of 64 missions; Moho/Laythe/Tylo crewed land-and-return need clusters/asparagus (not built). |
| Fuel depot | Built 2026-10-29 (0.5.10): ProgramKind.FuelDepot (programs.cfg KerbinDepot): core DepotCoreKerbin (purpose = Depot; payload tank farm under a nose docking port, Payload.TanksEmpty, keeper stays aboard), resupply DepotTankerKerbin (cargoFuel 16: dock, PumpCargoLeg, KeeperSwapLeg, UndockLeg, reentry). Refuel stops: FlightLeg.Refuel after the Kerbin climb, PlanStage.LaunchEmpty stages filled there (LegWalk fills only those), Project.DepotId, DesignRecord.DepotFuel; Agency.EngineersDesign tries the depot when the direct design fails (agency.cfg depotForEverything). Program.DepotFuel is the truth: simulated launches/tankers move it, live legs report measured tonnes (SetDepotFuel), DepotSync sets a live depot's tanks to match. Ready projects wait for fuel (DepotBlocker). Docking craft: nose port, 4 RCS blocks, radial chutes only. Live untested in game at build time. |
| Ascent and rocket shape | Decided 2026-10-29 (0.5.11): the flight director climbs vertical (88 deg) to 100 m/s, kicks at most 5 deg to 150 m/s, then follows 88 x (1 - sqrt(alt/50 km)) (50 km since 0.6.12; since 0.6.14 the SAS lock keeps the roll it was locked with, not the current roll, so roll is damped (an undamped roll stopped every pitch-over); 0.7.1: OnFlyByWire runs AFTER SAS in KSP (Vessel.FeedInputFeed), so VesselControl must never overwrite SAS pitch/yaw while holding a locked attitude (the zeroing that did so had disabled all custom steering since 2026-10-28), T: "level at 50 to 55 km"; 0.5.25, T: early turn fine as long as it keeps control; 0.5.11-0.5.24 stayed vertical to 15 km, which left fast finned rockets unable to lean), lean off the path capped at 10 deg below 20 km (60 km in 0.5.12 and 0.5.13) (AscentLeg.TurnStart/TurnEnd); above 15 km the nose may lean off the flight path by AscentLeg.MaxLean (8 deg at 25 kPa or more, free at 5 kPa or less; 0.5.17); suborbital hops hold 88. PartKind.Fin (Aero lifting surfaces named fin/winglet); four fins on every launch stage (KSA.Core/Design/Fins.cs, biggest fin at most 2% of the rocket); since 0.6.4 the stage above a solids-only launch stage or one under 1,500 m/s at sea level gets fins too when the rocket still passes (Fins.StagesNeedingFins); CraftWriter seats each fin wholly on the stage tanks, lower edge at the stage bottom (0.6.5); a suborbital hop whose design has the delta-v for orbit flies to orbit (Agency.TriesForOrbit); Kerbalism sample experiments (tag sample) stay off suborbital hops, added to AI plans in the review. ProceduralDesigner prefers short, wide stacks (equal mass: fewer tanks; stacks over 10x their width or engines wider than their tanks only as a fallback) and splits a hop into two stages, solid first. |
| Parachute sizing | Decided 2026-10-29 (0.5.22): a main parachute lands about 22x its own mass softly (KSA.Core/Design/Parachutes.cs; drogues 0). Landing mass = pod + payload extras (plans) or every part left after all decouplers fire (player craft, KspPlayerDesigns). ProceduralDesigner and the AI review call Parachutes.Fit (radial chutes in pairs before any docking port, or a bigger stack chute); DesignSession.ChutesShort rejects what is still short; PlayerDesign.Blocker says "parachutes too small". |
| Circularisation and science kit | 2026-10-29 (0.5.23-24): 0.7.10: the orbit hold faces retrograde and every deorbit burn lights only within 15 deg of retrograde (pauses beyond 30); it had lit after a fixed 5 s while prograde and raised a Kerbin orbit to 1,064 km. below TurnEnd AscentLeg holds apoapsis within 10% over target by throttle instead of cutting off; it coasts nose-flat from cutoff, lights the next engine within 120 s of apoapsis, starts the burn at timeToAp <= half the burn (CircularizeDeltaV/BurnTime) and burns along Horizontal(v), throttle open only within 15 deg of it, until Pe > atmosphere + 2 km; "Ascent coast/circularise" attitude lines every 10 s. AutoStage never stages a lit, unburnt engine with no thrust within 3 s of staging or throttle-up or with the throttle closed. DesignRequest.ScienceKit (every researched science part <= 0.1 t, up to 10): ProceduralDesigner and the AI review add one of each (KSA.Core/Design/ScienceKit.cs). |
| Swap rocket | Built 2026-10-29 (0.5.16): Agency.SwapToPlayerDesign / SwapToEngineersDesign for any active project not yet in flight (SwapBlocker); drops an AI session in progress, goes to Build (half the build days when it was building or Ready), writes the craft via OnEnteredBuild. Projects window "Swap rocket" button. Budget unchanged. |
| Part classification fixes | 2026-10-29 (0.5.15): heat shields are classified before decouplers (stock shields carry a ModuleDecouple with staging off; HeatShield0 had been picked as the lightest stage decoupler, so stages never separated); the review rejects a stage decoupler that is not PartKind.Decoupler, so saved designs with one are redesigned on load. Parachutes named *drogue* are tagged "drogue" and never count as the landing chute. 0.5.19: a crewed top stage must name a decoupler (StagePlanValidator); PlayerDesign.Blocker demands a main chute and, for returns from orbit when a >=1.25 m shield is researched (HeatShieldResearched), HasHeatShield (shield at least the pod's stack size, or a self-ablating pod). |
| Suborbital success and descent | Decided 2026-10-29 (0.5.13): a live suborbital flight earns Suborbit (its primary) only above the home world's atmosphere (LiveFlightResult.SpaceAltitude, 70 km on Kerbin); below it is an abort. The survival net drops everything under the capsule when falling through air with nothing burning, over the home world only (VesselControl.ShedForReentry fires the unfired ModuleDecouple nearest below a crewed part directly since 0.5.20, else stages decoupler-only stages; never heat shields). |
| Player designs | Built 2026-10-28: every craft saved in the save's Ships/VAB (not "KSA ..." craft) is a PlayerDesign (KspPlayerDesigns reads delta-v per stage, liftoff TWR, seats, parachute, research from the craft file). Projects pick one under "Your designs" when it fits (PlayerDesign.Blocker: research, seats, parachute, dv_requirements delta-v and TWR); DesignSource.Player, approved, flies the craft as saved, scored like an AI design for simulation; Take Control always allowed on it. The AI Director sees fitting designs per mission (start_project design) and prefers them. |

**Candidate supported mod set (not final):** ReStock/ReStock+, Near Future series, SSPXr (inflatables), KPBS (surface bases), Outer Planets Mod, Hooligan Labs Airships (Eve cloud city), Extraplanetary Launchpads. Required: ModuleManager. Optional and supported: Kerbalism (life support, KSA-67); Airplane Plus /L and Un Kerballed Start were tried and dropped 2026-10-30 (0.7.8, T): the probe missions and the start-node patch were removed; stock SPH planes keep the aircraft missions. Clash with KSA, keep off: Bureaucracy, Strategia, contract packs, Kerbal Construction Time, Custom Barn Kit, EVARepairs. Optional: MechJeb2 (future bridge for missions beyond Kerbin orbit), kOS. Template requiredMods match GameData folder names, DLL names or CKAN identifiers.

---

## 7. Roadmap

Each milestone is playable on its own. Don't start a milestone until the previous one runs in-game.

| Milestone | Scope |
|---|---|
| **M0: Hello KSP** | Solution with Core + KSP projects builds; toolbar button opens a window listing kerbals |
| **M1: Service Record** | Ranks from stars, new Flight track, 5–6 medals, Service Record window, save/load |
| **M2: The Agency** | Fiscal years, goals, budget review, lean years, recruiting, salaries |
| **M3: Projects + Simulated Missions** | Projects, Programs, team assignment, template designs, simulated outcomes |
| **M4: AI Engineering** | LLM client, stage plan schema, VesselBuilder (vertical stacks first; start by tweaking templates), validator loop, Design Review UI |
| **M5: Live Flight** | Built-in flight director (Kerbin missions), Watch Live, flight HUD, Take Control, post-mortems; MechJeb bridge later |
| **M6: Narrative** | Local LLM press releases, dialogue, memos, news ticker |
| **M7: Polish** | Balance, art, docs, compatibility, SpaceDock/CKAN release |

**Biggest known risk:** VesselBuilder (attach nodes, symmetry, staging). Keep it simple and template-based first.

---

## 8. Workflow per change

1. Confirm which milestone/feature the change belongs to.
2. Say which project (Core or KSP) each file goes in, and why.
3. Deliver complete files with full paths.
4. Give numbered build steps (`dotnet build` in the VS Code terminal) and test steps (in KSP, what to click, what to expect).
5. Tell T what to check in `KSP.log` (`<KSPDIR>\KSP.log`) if it fails. All KSA log lines start with `[KSA]`.
6. Suggest a commit message.
7. If a decision was made, update section 6 of this file.

---

## 9. Legal and licensing notes

- **License:** MIT, copyright Blackbox Entertainment (LICENSE at repo root, added 2026-10-26). Blackbox holds copyright on all first-party code, so KSA.Core can be relicensed for a standalone game.
- Code T writes is T's. KSP's code, assets, kerbals, and the "Kerbal" name belong to Take-Two/Squad and can't be reused in a standalone game.
- If outside contributors join, get agreement that T can relicense their code, or keep contributions out of Core. Decided 2026-10-28: no outside code contributions (CONTRIBUTING.md says PRs are closed; bug reports via the issue form are welcome). If that changes, require DCO sign-off and a relicense grant first. Textures checked: all six PNGs are original (drawn by code, commits 508c03a, eda493f).
- Model licenses vary (Apache 2.0, MIT, or custom terms like Llama/Gemma). Recommend models; don't redistribute them.
- Not legal advice. Get a lawyer before any commercial spin-off.

---

## 10. Open questions

1. Can the player manually override an AI design, or is hands-off strict?
2. Can kerbals quit (low morale) or retire (age/service)?
3. Track kerbal age and a hall of fame?
4. ~~Rival agencies / space race mode?~~ Decided 2026-10-27: light version shipped (decision 19); poaching post-release.
5. How punishing are Crisis Years? Is "Director fired" a game over?
6. Separate science directorate budget?
7. Community-sourced template library?
8. Default OpenRouter models for design vs. post-mortem (benchmark in M4)
9. Final name (avoid the "KSA" collision?)
