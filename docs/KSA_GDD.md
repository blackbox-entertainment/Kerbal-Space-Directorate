# Kerbal Space Directorate (KSA): Game Design Document

**Name:** Kerbal Space Directorate (decided 2026-10-26; internal code name KSA; see 14.1)
**Type:** Kerbal Space Program 1 (v1.12.5) mod
**Genre:** Space agency management sim / AI-driven autonomous sandbox
**Author:** T (jaqknife), Blackbox Entertainment
**Status:** Pre-production
**Version:** 0.2
**Tracking:** Jira project KSA

### Changes since v0.1
- New Flight career track: Test Pilot → Astronaut (4.2)
- New Recruiting system (4.3)
- New Programs layer and off-world construction for stations and bases (4.7, 5.7)
- Planets fixed to the stock Kerbol system (2.1)
- Supported mod set, CKAN-only installs, and no-DLC rule (10)
- Hardware targets based on current player hardware (11)
- No bundled LLM models; first-run AI setup instead (6.4)
- Two-project architecture (KSA.Core / KSA.KSP) for reuse in a future standalone game (9.1)
- Toolchain: VS Code + .NET SDK, no Visual Studio (9.2)
- New milestone M0: Hello KSP (12)

---

## 1. High Concept

You are the **Director of the Kerbal Space Agency**. You don't build rockets or fly them. You **recruit kerbals, assign them to programs, and manage the budget** the government gives you. Your kerbals design rockets with an AI "engineering team," fly missions on autopilot, earn experience, rise from Test Pilot to Chief Astronaut, win medals, and sometimes die.

Hit the government's program goals and your budget grows. Miss them and you face **lean years**: cuts, hiring freezes, and canceled programs.

> *"Football Manager meets the space race, inside KSP."*

---

## 2. Design Pillars

1. **Hands-off, not hands-tied.** The player directs and the kerbals execute. Watching is the gameplay.
2. **People matter.** Every kerbal has a career, a personality, and a service record. Who you recruit shapes the rockets you get.
3. **Consequences over time.** Budgets, reputation, and kerbal experience carry forward. Failure is a story, not a reload.
4. **Modular and playable at every stage.** Each system works on its own and ships incrementally.

### 2.1 Setting

KSA uses the **stock Kerbol system**. Destinations are the stock bodies and their real-world analogs: Mun and Minmus, Eve (Venus), Duna and Ike (Mars), Dres (Ceres), Jool and its moons (outer planets). Outer Planets Mod is supported for players who want more distant targets. Real Solar System is out of scope.

---

## 3. Core Gameplay Loop

```
┌──────────────────────────────────────────────────────────┐
│  FISCAL YEAR START                                       │
│  Government issues budget + 2–4 Program Goals            │
└───────────────┬──────────────────────────────────────────┘
                ▼
┌──────────────────────────────────────────────────────────┐
│  DIRECTOR PHASE (player)                                 │
│  • Recruit / fire / train kerbals                        │
│  • Select Test Pilots for astronaut training             │
│  • Create Programs and Projects tied to goals            │
│  • Assign Engineering Teams + Flight Crews               │
│  • Allocate budget                                       │
└───────────────┬──────────────────────────────────────────┘
                ▼
┌──────────────────────────────────────────────────────────┐
│  EXECUTION PHASE (kerbals, autonomous)                   │
│  • Engineers design rocket (AI loop)                     │
│  • Rocket validated (Δv / TWR) → revise if needed         │
│  • Build time elapses (in-game days)                     │
│  • Mission flown: Simulated or Live (autopilot)          │
└───────────────┬──────────────────────────────────────────┘
                ▼
┌──────────────────────────────────────────────────────────┐
│  RESULTS                                                 │
│  • XP, promotions, medals, injuries, deaths              │
│  • Post-mortems feed back into future designs            │
│  • Press releases / public reaction                      │
└───────────────┬──────────────────────────────────────────┘
                ▼
┌──────────────────────────────────────────────────────────┐
│  FISCAL YEAR END: Government Review                      │
│  Goals met → budget ↑, prestige ↑                        │
│  Goals missed → budget ↓, lean year events               │
└──────────────────────────────────────────────────────────┘
```

---

## 4. Systems

### 4.1 Kerbal Personnel

| Attribute | Source | Effect |
|---|---|---|
| Role | Stock (Pilot / Engineer / Scientist) | Which jobs they can do |
| Stars (0–5) | Stock experience system | Base competence |
| Courage / Stupidity | Stock traits | Risk tolerance in design and flight |
| Career Track | KSA | Flight, Engineering, Science, Management |
| Rank | KSA | Unlocks roles, perks, salary |
| Personality tags | KSA (generated) | Flavors AI prompts and dialogue (*Cautious*, *Reckless*, *Perfectionist*, *Showboat*) |
| Morale | KSA | Affects performance; drops after deaths, lean years, or being passed over |
| Salary | KSA | Ongoing budget cost |
| Recruitment source | KSA | Where they came from; shapes starting stats |
| Service Record | KSA | Missions, medals, incidents, quotes |

### 4.2 Career Tracks and Ranks

**Flight track (Pilots)**

| Rank | How it's earned | What they can do |
|---|---|---|
| **Test Pilot** | Hired | Aircraft and suborbital test flights only |
| **Astronaut Candidate** | **Selected by the Director** after test flights | Training, simulator time, backup crew |
| **Astronaut** | Pass training, first spaceflight | Crew on orbital missions |
| **Senior Astronaut** | 3★, several missions | Crew on Mun/Minmus and interplanetary missions |
| **Mission Commander** | 4★, a command-worthy record | Command crews, make abort calls |
| **Chief Astronaut** | 5★, one slot per agency | Picks crews, agency-wide morale bonus |

**Astronaut selection:** each fiscal year the Director picks which Test Pilots become Candidates. Passed-over pilots lose morale and may eventually quit. Test Pilots fly the agency's experimental aircraft and hardware, so the Flight track's riskiest work (and many Survivor medals) happens before anyone reaches orbit.

**Other tracks**

| Track | Ranks (low → high) | Key duties |
|---|---|---|
| **Engineering** | Junior Engineer → Engineer → Lead Engineer → Chief Designer | Design rockets (AI loop), EVA repairs |
| **Science** | Research Assistant → Scientist → Principal Investigator → Science Director | Experiments, science goals |
| **Management** (any role, high rank) | Program Manager → Deputy Director | Run projects and programs, boost team efficiency |

**Promotion criteria:** stars threshold, missions completed, specific achievements, and an open slot (rank caps scale with agency size). The Test Pilot → Candidate step is always a Director decision.

**Rank perks (examples):**
- Chief Designer: +1 design revision pass, access to experimental parts
- Mission Commander: crew morale bonus, can abort missions intelligently
- Program Manager: −10% build time on their project

All tracks, ranks, requirements, and perks are **ModuleManager configs**, not hardcoded.

### 4.3 Recruiting

Stock KSP's flat-fee hiring is replaced by a recruiting system.

**Recruitment drives:** the Director spends budget on a campaign to fill the applicant pool. Bigger drives produce more and better applicants. The pool also refreshes slowly each quarter on its own.

**Recruitment sources**

| Source | Produces | Notes |
|---|---|---|
| **Kerbin Air Corps** | Pilots | Start as Test Pilots with flight experience |
| **University programs** | Engineers, Scientists | Cheaper, green, high potential |
| **Industry poaching** | Experienced Engineers | Expensive; sometimes bring perks |
| **Walk-ins** | Anyone | Cheap, random; occasional hidden gem or high-Stupidity disaster |

**Interviews:** applicants show partial stats and personality tags. Senior staff on the hiring panel reveal more, which rewards keeping experienced kerbals around.

**Prestige:** after a famous success, the best applicants seek you out. After a crisis year, few want the job.

**Returning applicants:** rejected applicants may reapply a year later with more experience (or, in a future rival-agency mode, join a competitor).

Sources and their stat ranges are data-driven configs.

### 4.4 Medals and Awards

Awarded automatically from flight log events and shown in the Service Record.

| Medal | Criteria |
|---|---|
| Orbital Pioneer | First kerbal to reach orbit |
| Mun Walker | Surface EVA on the Mun |
| Lifesaver | Participated in a successful rescue |
| Iron Kerbal | 100+ cumulative days in space |
| Survivor | Survived a mission failure / crash |
| Memorial Star | Posthumous, killed in the line of duty |
| Chief's Commendation | Director-awarded (limited per year; arrives in M2) |

Medals are **data-driven** (ModuleManager configs), so players and modders can add their own (e.g., Station Builder, Cloud Walker for Eve, Base Commander).

### 4.5 Government Funding and Program Goals

**Fiscal year:** configurable (default: 1 Kerbin year = 426 days).

**Each year the government issues** a base budget (scaled by prestige and last year's performance) and **2–4 Program Goals** from a tiered pool:

| Tier | Example goals |
|---|---|
| Early | "Reach space," "Achieve orbit," "Return a kerbal safely" |
| Mid | "Mun flyby," "Crewed Mun landing," "Launch a station core" |
| Late | "Duna landing," "Permanent Mun base," "Crewed Jool flyby" |
| Programs | "Complete a 3-module station," "Crewed base on Duna," "Eve cloud city," "Outpost on a Jool moon" |
| Conditional | "No crew losses this year," "Stay under budget," "3 launches this quarter" |

Program goals may span multiple fiscal years.

**Year-end review**

| Outcome | Effect |
|---|---|
| All goals met | Budget +20–30%, prestige ↑, bonus applicants |
| Most goals met | Budget +5–10% |
| Half met | Budget flat |
| Most missed | **Lean Year:** budget −15–25%, hiring freeze |
| All missed / disaster | **Crisis Year:** budget −40%, project cancellations, possible Director "review" |

**Lean year events:** hiring freeze, forced layoffs (player picks who goes), program cancellation (the government kills the most expensive project), agency-wide morale drop, public hearing (generated narrative).

### 4.6 Projects

A **Project** is a single rocket or mission effort.

```
Project
├── Name / Goal link (e.g., "Project Lantern → Mun Landing")
├── Budget allocation
├── Engineering Team (1–4 engineers, 1 lead)
├── Flight Crew (1–N kerbals)
├── Program Manager (optional)
├── Status: Design → Build → Ready → In Flight → Complete / Failed
└── History: designs, flights, post-mortems
```

### 4.7 Programs (Stations and Bases)

A **Program** is a multi-mission, often multi-year effort made of several Projects.

```
Program: "Mun Base Alpha"
├── Goal link (e.g., "Permanent Mun base")
├── Program Manager
├── Budget (spread across fiscal years)
├── Modules (each is a Project)
│   ├── Core module
│   ├── Hab module
│   ├── Power module
│   └── Lab module
├── Assembly method: docking (orbit) / surface placement / on-site construction
└── Status per module + overall completion
```

**Program types**
- **Orbital stations**, including inflatable habs and centrifuges (SSPXr)
- **Surface bases** on the Mun, Minmus, Duna, Dres, and Jool's moons (KPBS, optional MKS)
- **Eve cloud city** and **Jool cloud city**, using buoyancy parts (Airships or Hooligan Labs Airships); Jool is the far frontier: Late tier, four-star crew, 400-day missions
- **Outposts with on-site construction**, where kerbals at a base build new modules locally (Extraplanetary Launchpads)

Losing funding mid-program creates natural drama: a half-built station, a stranded crew, a hearing.

---

## 5. AI Design Pipeline (Core Feature)

### 5.1 Principle

> **The LLM makes decisions. The mod's code does the geometry.**

LLMs are unreliable at precise 3D part placement, so they never write `.craft` files directly. They output a **structured stage plan**, and deterministic C# assembles a valid vessel.

### 5.2 Pipeline

```
 [Mission Spec] ──► [LLM: Design] ──► [JSON Stage Plan]
                         ▲                    │
                         │                    ▼
               [Feedback: Δv/TWR]   [C# Builder: assemble vessel]
                         ▲                    │
                         │                    ▼
                         └──── [Validator: stock Δv / TWR calc]
                                              │ pass
                                              ▼
                                   [Saved .craft + design record]
```

1. **Mission Spec** comes from the Project: destination, crew count, payload, budget cap, available parts.
2. **LLM Design call** is prompted *as the engineering team*, with each engineer's personality and rank.
3. **JSON Stage Plan** is validated against a schema (5.4).
4. **C# Builder** stacks parts on real attach nodes, applies symmetry, adds decouplers and fairings.
5. **Validator** uses KSP's built-in Δv and TWR calculator (`VesselDeltaV`).
6. **Feedback loop:** failures return to the LLM as text ("Stage 2 short by 800 m/s; TWR at liftoff 1.05"). Revision passes are set by the Lead Engineer's rank and traits.
7. **Output:** a valid `.craft` file plus a design record saved to Project history.

**Build order for the builder (risk control):** first, tweak templates from the library; then simple vertical stacks; radial parts and symmetry last.

### 5.3 Engineer Personality → Prompt Behavior

| Trait | Prompt modifier | Gameplay effect |
|---|---|---|
| Cautious | "Prioritize 20%+ Δv margins and redundancy" | Heavier, safer, pricier rockets |
| Reckless | "Minimize mass and cost; margins optional" | Cheap rockets, more failures |
| Perfectionist | +2 revision passes | Slower, better designs |
| Junior (low rank) | 1 revision pass, limited part list | More errors |
| Chief Designer | Extra passes, experimental parts | Best designs |

### 5.4 Stage Plan Schema (draft)

```json
{
  "design_name": "Lantern I",
  "rationale": "Two-stage lander with Mun-capable descent stage.",
  "stages": [
    {
      "stage": 0,
      "role": "launch",
      "core": [
        { "part": "liquidEngine2", "count": 1 },
        { "part": "fuelTank.long", "count": 2 }
      ],
      "radial": [
        { "part": "solidBooster1-1", "count": 4, "symmetry": 4 }
      ],
      "decoupler": "Decoupler.2"
    }
  ],
  "payload": {
    "command": "mk1pod.v2",
    "extras": ["parachuteSingle", "HeatShield1"]
  }
}
```

Part names must come from the **allowed part list** sent in the prompt. Unknown parts reject the plan automatically.

### 5.5 Part List Filtering

With many part mods installed there can be over a thousand parts, far too many for a prompt. KSA builds a **filtered part list per job**:
- Launch vehicle: engines, tanks, decouplers, fairings, boosters
- Station module: habs, docking ports, power, RCS, life support
- Surface base module: base parts, landing legs, power, wheels
- Cloud city module: buoyancy envelopes, habs, power

The list is built dynamically from what's installed, so DLC and mod parts appear only for players who have them.

### 5.6 Flight Scripts

- **MechJeb (default):** ascent, maneuvers, landing, and docking autopilots via its API. Reliable.
- **kOS (optional):** the LLM writes kerboscript for custom mission logic; stored and versioned with the design.

### 5.7 Off-World Construction and Assembly

Station docking and precision landings next to existing modules are the hardest flying in KSP and fail often under autopilot. Therefore:
- **Assembly defaults to Simulated.** Outcome is rolled from design quality, crew skill, and the Program Manager.
- **Watch Live** is offered for assembly milestones (first docking, first module landed).
- **On-site construction** (Extraplanetary Launchpads) lets a base build modules locally, avoiding repeated launches from Kerbin.

### 5.8 Post-Mortems

After every flight, telemetry (altitude/velocity log, staging events, failure point) is summarized and sent to the LLM:
- It writes an **in-character post-mortem** from the Lead Engineer.
- **Lessons learned** are stored per agency and injected into future design prompts.
- Result: the agency visibly improves over time.

---

## 6. AI Provider Architecture

### 6.1 Overview

| Task | Default provider | Why |
|---|---|---|
| Rocket design (JSON stage plans) | **OpenRouter** (cloud) | Needs strong reasoning |
| kOS flight scripts | **OpenRouter** (cloud) | Code generation quality |
| Post-mortems | OpenRouter or local | Medium difficulty |
| Narrative: dialogue, press releases, memos, news | **Local** (Ollama / LM Studio) | High volume, low difficulty, free |

### 6.2 OpenRouter

- **Endpoint:** `https://openrouter.ai/api/v1/chat/completions` (OpenAI-compatible)
- **Auth:** `Authorization: Bearer <OPENROUTER_API_KEY>`
- **Model:** user-selectable (strong model for design, cheap fast model for post-mortems)
- **Structured output:** request JSON mode or a JSON schema where supported; always validate locally.
- **Optional headers:** `HTTP-Referer` and `X-Title` for app attribution.

### 6.3 Local (Ollama / LM Studio)

- OpenAI-compatible, e.g., `http://localhost:11434/v1/chat/completions` (Ollama).
- **Model sizing:** a 3–4B parameter model at 4-bit quantization (about 2–3 GB) fits alongside KSP on an 8 GB GPU. Larger models are optional with CPU offload.

### 6.4 Distribution: No Bundled Models

KSA **never ships an LLM model**:
- Models are multi-gigabyte, too large for mod sites and GitHub release limits.
- KSP mods can't run inference themselves; a separate runtime (Ollama/LM Studio) is required anyway.
- Model licenses vary (Apache 2.0, MIT, or custom terms like Llama and Gemma). KSA recommends models; it doesn't redistribute them.

**First-run AI setup** (in-game window):
1. Explains the three options: Cloud (OpenRouter key), Local (Ollama), or No AI.
2. For Local: links to the Ollama installer, then has Ollama pull the recommended model with one click.
3. For Cloud: a field for the OpenRouter key, saved only to the user-local config.
4. No AI: the template library and canned text keep the game fully playable.

### 6.5 Config File

`GameData/KSA/PluginData/ksa_settings.cfg` (user-local, never committed or distributed). The repo ships only `ksa_settings.example.cfg`.

```
KSA_SETTINGS
{
    designMode = Hybrid        // Local | API | Hybrid | Off

    API
    {
        provider = OpenRouter
        endpoint = https://openrouter.ai/api/v1/chat/completions
        apiKey = YOUR_KEY_HERE
        designModel = <openrouter-model-id>
        postMortemModel = <openrouter-model-id>
        maxCallsPerDay = 50     // cost guard
        monthlySpendCapUSD = 5  // soft cap, tracked locally
    }

    LOCAL
    {
        endpoint = http://localhost:11434/v1/chat/completions
        model = <recommended-local-model>
        enabled = true
    }

    FALLBACK
    {
        onApiFailure = UseTemplateLibrary
        onLocalFailure = UseCannedText
    }
}
```

### 6.6 Technical Requirements

- **All LLM calls are asynchronous**, off the main Unity thread. In fiction, "design takes X in-game days" covers latency.
- **Timeouts and retries** with exponential backoff.
- **Cost guard:** call counter and spend estimate shown in the Director UI.
- **Offline fallback:** a curated **template library** of designs per mission tier, with **stock-only versions** of every template.
- **Prompt/response logging** (toggle) to `PluginData/logs/`.

---

## 7. Mission Resolution Modes

| Mode | Description | Use |
|---|---|---|
| **Simulated** | Outcome rolled from design quality (Δv margin, TWR), crew skill, courage/stupidity, morale, and luck. No physics flight. | Routine missions, assembly, time warp, low-end PCs |
| **Live** | Real KSP flight via MechJeb / kOS. Player watches. | Milestones, spectacle |
| **Hybrid (default)** | Simulated unless the mission is a "First" or the player clicks **Watch Live**. | Best balance |

**Simulated outcomes:** Success / Partial Success / Abort (crew safe) / Loss of Vehicle / Loss of Crew, weighted by design score and crew score.

---

## 8. User Interface

Built with KSP's DialogGUI / PopupDialog (no Unity editor needed for v1).

| Window | Contents |
|---|---|
| **Director's Office** (hub) | Budget, fiscal year timer, active goals, alerts |
| **Personnel** | Roster, ranks, morale, fire, astronaut selection |
| **Recruiting** | Applicant pool, recruitment drives, interviews |
| **Service Record** | Per-kerbal history, medals, quotes |
| **Programs** | Stations and bases, module status, assembly progress |
| **Projects** | Project list, team assignment, status pipeline |
| **Design Review** | AI stage plan, rationale, Δv/TWR stats, approve / send back |
| **Mission Control** | Mission feed, telemetry, Watch Live button |
| **News Ticker** | Press releases, public reaction |
| **Government Review** | Year-end report card, budget change, lean-year events |
| **AI Setup** | First-run provider choice and model setup |

**Toolbar:** KSA button via stock AppLauncher.

---

## 9. Technical Architecture

### 9.1 Two-Project Structure

The agency logic is kept free of KSP so it can be reused in a future standalone space game.

```
KSA.sln
├── KSA.Core/         Pure C#. No KSP or Unity references.
├── KSA.KSP/          Thin adapter connecting Core to KSP.
└── KSA.Core.Tests/   Unit tests for Core (no KSP needed).
```

| KSA.Core (portable) | KSA.KSP (KSP-only) |
|---|---|
| Personnel, careers, ranks, medals, morale | ScenarioModule save/load |
| Recruiting | GameEvents hooks |
| Fiscal years, goals, reviews, lean years | VesselBuilder (stage plan → ShipConstruct) |
| Projects and Programs | Δv/TWR validation via VesselDeltaV |
| LLM client logic, prompt builder, schema | MechJeb / kOS bridges |
| Post-mortems, narrative | DialogGUI windows, AppLauncher |
| Simulated mission resolution | Implementations of Core interfaces |

**Core interfaces** implemented by the adapter: `ILlmTransport`, `IClock`, `IPersistence`, `IPartCatalog`, `ILogger`.

**What carries over to a standalone game:** everything in Core. **What must be rebuilt:** physics, orbits, vessel building, flight, and UI. KSP's code, assets, kerbals, and the "Kerbal" name cannot be reused.

### 9.2 Toolchain

- **Language:** C#, targeting **.NET Framework 4.7.2** (matches KSP 1.12 / Unity 2019.4)
- **Editor:** VS Code with the C# extension
- **Build:** .NET SDK (`dotnet build`, `dotnet test`), SDK-style `.csproj` files with the `Microsoft.NETFramework.ReferenceAssemblies` NuGet package. **No Visual Studio required.**
- **KSP references** (KSA.KSP only): from `$(KSPDIR)\KSP_x64_Data\Managed\`, not copied to output
- **Dev install:** a separate KSP copy at `E:\KSP_dev`, managed in CKAN; environment variable `KSPDIR` points to it. The Steam install stays clean.
- **Logging:** all lines prefixed `[KSA]` in KSP.log
- **Source control:** GitHub (owner to be decided: Blackbox Entertainment org or jaqknife)

### 9.3 Key KSP Hooks

| Hook | Use |
|---|---|
| `GameEvents.onVesselRecovered` | Award XP, medals, mission completion |
| `GameEvents.onCrewKilled` | Memorial Star, morale hit |
| `GameEvents.onCrewOnEva` | EVA tracking |
| Launch / rollout events | Mission start |
| `ScenarioModule` | Persist KSA data in the save |
| `ProtoCrewMember.flightLog` | Read achievements |
| `VesselDeltaV` | Validate AI designs |
| `ShipConstruct` / `ShipConstruction` | Build and save vessels |

### 9.4 Mod Folder Layout

```
GameData/KSA/
├── Plugins/          KSA.Core.dll, KSA.KSP.dll
├── Configs/          medals, goals, careers, recruiting sources
├── Templates/        offline fallback .craft designs (stock-only + mod variants)
└── PluginData/       ksa_settings.cfg (user-local), logs/
```

---

## 10. Mod Support

### 10.1 Rules
- **Install mods only through CKAN.** Steam Workshop for KSP1 hosts craft files and missions, not mods.
- **No DLC required.** KSA must work without Making History or Breaking Ground. DLC parts appear only for owners, and every template has a stock-only version. Test with DLC both on and off.
- **Dynamic part list:** KSA works with whatever parts are installed; it is designed and balanced against the supported set below.

### 10.2 Dependencies

| Mod | Status |
|---|---|
| ModuleManager | Required |
| MechJeb 2 | Required for Live mode |
| kOS | Optional (custom flight scripts) |
| ClickThroughBlocker / ToolbarController | Optional (UI quality of life) |
| Contract Configurator | Optional (advanced goal generation) |
| Kerbal Construction Time | Optional (compatibility) |

### 10.3 Supported Mod Set (candidate, to lock in Jira KSA-16)

| Purpose | Mods |
|---|---|
| Rocket parts | ReStock + ReStock+, Near Future series (selected modules), Kerbal Atomics, Tundra Exploration |
| Stations | SSPXr (inflatables, centrifuges), HabTech2 |
| Surface bases | Kerbal Planetary Base Systems (KPBS); USI Kolonization (MKS) optional |
| Eve cloud city | Airships (Angel-125) or Hooligan Labs Airships, to be compared |
| On-site construction | Extraplanetary Launchpads |
| Extra destinations | Outer Planets Mod |

---

## 11. Hardware Targets

Most Steam players have 16–32 GB of system RAM (32 GB recently became the most common), and 8 GB remains a meaningful minority. Heavy part and planet mods mainly consume system RAM.

| Tier | RAM | Experience |
|---|---|---|
| **Minimum** | 16 GB | KSA + light mod set, AI via OpenRouter only, no local model |
| **Recommended** | 32 GB | Full supported mod set, optional small local model |
| Dev machine | 64 GB | Not a target; test on 16 GB settings before release |

GPU: an 8 GB card (e.g., RTX 4060) can run KSP with stock visuals plus a 3–4B local model.

---

## 12. Development Roadmap

Each milestone is playable on its own. Jira epics KSA-1 to KSA-8.

| Milestone | Scope | Deliverable |
|---|---|---|
| **M0: Hello KSP** | Dev environment, solution with Core/KSP/Tests, toolbar button, roster window | Proven build pipeline |
| **M1: Service Record** | Crew profiles, career tracks incl. Test Pilot path, promotions, 6 medals, Service Record window, save/load | Playable career/medal mod |
| **M2: The Agency** | Fiscal years, goals, reviews, lean years, recruiting, astronaut selection, salaries, Chief's Commendation | Management layer over stock Career |
| **M3: Projects, Programs, Simulated Missions** | Projects, Programs (stations/bases), template library, simulated resolution, Hybrid mode | Fully hands-off play, no AI |
| **M4: AI Engineering** | LLM client, prompt builder, schema, part filtering, VesselBuilder, validator loop, Design Review, AI Setup | AI-designed rockets |
| **M5: Live Flight** | MechJeb bridge (incl. docking), optional kOS, Watch Live, telemetry, post-mortems | Watch kerbals fly AI rockets |
| **M6: Narrative** | Local-LLM press releases, dialogue, memos, News Ticker | Living agency |
| **M7: Polish and Release** | Balancing, art, docs, compatibility, 16/32 GB testing, CKAN metadata | Public release (SpaceDock / CKAN) |

---

## 13. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| LLM produces invalid designs | High | Schema validation, allowed-part list, revision loop, template fallback |
| VesselBuilder complexity | High | Template tweaking first, then vertical stacks, radial/symmetry last |
| Autopilot docking/landing failures | High | Assembly Simulated by default; Watch Live for milestones only; on-site construction |
| Prompt overload from large mod sets | Medium | Part list filtered by job |
| Live flight physics quirks | Medium | Hybrid default; abort logic |
| API cost creep | Medium | Call caps, spend tracker, cheap models for non-design tasks |
| Player RAM limits | Medium | Clear 16/32 GB tiers; API-only mode; test at minimum spec |
| VRAM contention (8 GB) | Medium | Small local model, API for heavy tasks |
| Main-thread freezes | High | Strictly async calls; queue system |
| Mod compatibility | Medium | Supported mod set; dynamic part list; ModuleManager patches |
| DLC dependency creeping in | Medium | Stock-only templates; test with DLC off |
| API key leakage | High | User-local config, gitignored, never bundled |
| Name collision (Kitten Space Agency) | Low–Medium | Decide final name before public release |
| Core logic tied to KSP | Medium | Two-project rule enforced in code review |

---

## 14. Open Questions

1. ~~Final name~~ Decided 2026-10-26: **Kerbal Space Directorate**. Internal code name stays KSA.
2. ~~Manual override~~ Decided 2026-10-26: see 14.1 item 7. Design override remains strict (hands-off); flight override is an opt-in.
3. ~~Quit or retire~~ Decided 2026-10-26: yes to both (14.1 item 8).
4. ~~Age and hall of fame~~ Decided 2026-10-26: yes to both (14.1 item 9).
5. ~~Rival agencies~~ Decided 2026-10-26: yes, as an optional mode after M7 (14.1 item 10).
6. ~~Crisis Years~~ Decided 2026-10-26: hurt, but never game over; probation then a new Director (14.1 item 11).
7. ~~Separate science directorate budget?~~ Decided 2026-10-26: yes. Research is a Project type funded by the Director (see 14.1).
8. ~~Community templates~~ Decided 2026-10-26: yes, after release (14.1 item 12).
9. ~~Default models~~ Decided 2026-10-26: curated list, cheapest-that-works default plus a few flagships, player-selectable (14.1 item 13).
10. ~~Airship mod~~ Decided 2026-10-26: HL Airships Core (14.1 item 14).
11. ~~GitHub owner~~ Decided: Blackbox Entertainment organization (2026-10-26)

### 14.1 Decisions recorded 2026-10-26

Stock kerbals carry only stars (0–5), a trait, and Courage/Stupidity (cosmetic in stock). Stock admin staff (Mortimer, Linus, Walt, Gus) are fixed faces with no stats. KSA fills that gap:

1. **Admin staff become hireable KSA kerbals** on the Management track (Program Manager → Deputy Director), replacing the stock Administration faces in function. Arrives in M2.
2. **Stats:** stock stars remain the competence number. Each track gets one growth stat that rises through assignments rather than flights: **Leadership** (Management: team efficiency, morale buffer), **Insight** (Science: research output), **Design** (Engineering: design quality). Courage feeds risk tolerance in design prompts and abort calls; Stupidity feeds error chance. Personality tags and morale act as modifiers on top. No further raw stats; avoid a spreadsheet.
3. **Research is a Project type** run on the ground by Science-track kerbals, producing stock Science points over a fiscal year scaled by stars and Insight. It plugs into the stock tech tree unchanged. Arrives in M3.
4. **Roster opens the Service Record.** Clicking a kerbal row in the roster opens that kerbal's Service Record (missions, medals, rank history, incidents, quotes), read-only. Portraits ship in M1 after all: the Astronaut Complex picture is a plain texture (SpritePath / suit icon), reusable in dialogs. Class badges alongside.
5. **Per-mission flight mode.** Every mission has a Simulate / Watch Live selector at launch, pre-filled from an agency-wide default setting (Simulate, Watch Live, or Hybrid; Hybrid ships as default). Watch Live requires MechJeb; without it the mission falls back to Simulate with a notice. Supersedes the fixed "Hybrid only" wording in section 7.
6. **Landing site selection.** When landing on the Mun or any body with a surface, the crew picks a flat spot. Before descent, KSA samples terrain slope in a grid around the intended target (stock PQS terrain queries) and shifts the target to the flattest nearby cell, with a per-body maximum slope. Live mode feeds that target to MechJeb's landing guidance; Simulated mode uses site slope as a factor in the outcome roll. Programs (bases) choose a site once and reuse it so modules land next to each other. Pilot stars widen the search and lower the accepted slope.
7. **Manual flight override ("Director may fly").** A mod setting, off by default. When on, Live flights show a Take Control button that pauses the autopilot and hands the player the controls; control can be handed back at any time. The mission is logged as Director-flown in the Service Record, the pilot earns reduced KSA rank credit for it, and the post-mortem narrative reflects it. Not available for Simulated missions (choose Watch Live first). Design override stays strict: the player never edits an AI stage plan by hand.
8. **Kerbals can quit and retire.** Quitting is driven by low morale (passed over for selection, deaths, lean years, long idle stretches); retirement by age or years of service. Both go through the hall of fame. M2.
9. **Age and hall of fame.** Every kerbal has an age (assigned at hire, advances with the fiscal calendar). Retired and dead kerbals keep their Service Record in a Hall of Fame window. Age from M1, hall from M2.
10. **Rival agencies: optional mode, post-M7.** A space-race mode with competing agencies that claim firsts and poach applicants. Off by default, enabled per save. Not scheduled before release.
11. **Crisis Years hurt but never end the game.** A Crisis Year brings budget cuts, cancellations, and forced layoffs, and puts the Director on probation. A second bad year ends the Director's tenure: the agency continues under a new Director with the same staff, and the record shows it. No game over. M2.
12. **Community template library: yes, after release.** Ship with first-party templates; open a submission path (shared .craft plus a KSA metadata file) once the mod has traction. Post-M7.
13. **AI models: curated, player-selectable.** The AI Setup window offers a short list per task: a cheapest-that-does-the-job default plus two or three flagship options, for design, post-mortem, and narrative separately. Exact model IDs are benchmarked in M4 and kept in a ModuleManager config so they can be updated without a code release.
14. **Eve cloud city uses HL Airships Core.** Angel-125's Airships stays on the compatibility list. M3.
15. **Live flight ships with a built-in flight director, MechJeb optional (M5, 2026-10-26).** Kerbin suborbital and orbital missions fly live on KSA's own ascent/reentry program (vertical climb, eastward kick, prograde hold with auto-staging, circularise, deorbit, parachutes). Missions beyond Kerbin orbit fall back to Simulate until a MechJeb bridge lands; MechJeb moves from Required to Optional.
16. **Narrative is local-only (M6, 2026-10-26).** Press releases, memos, reviews and obituaries are written canned first, then rewritten by the local model when one is configured. Cloud API budget is never spent on flavour text; post-mortems may use the cloud (PostMortem task).
17. **Courage and Stupidity drift with experience (planned, not yet built).** Stupidity falls a little after every completed mission (more after a survived failure, floor 0.15); Courage rises after successes and survived close calls, falls after a crewmate's death or a long idle stretch (0.1 to 0.95). Small steps (0.02 to 0.05) so veterans show it over a career. Ships as an M3 follow-up story.
18. **Interviews cost money (2026-10-27).** An applicant's first interview costs 200 funds and each further round 100 more (200, 300, 400, 500), set in agency.cfg. A senior panel reveals two facts per interview and so halves the bill. Hiring without interviews is allowed: the Director takes the risk of a bad hire. Dismissals (-4) and deaths (-15) hit everyone else, scaled by roster size: those are the numbers when 12 kerbals remain, and the hit grows as the square root of how much smaller the roster is (double at 3, a third at 99, capped at triple). The message states the actual number.
19. **Rival agencies arrive early, lightly (2026-10-27).** Three rivals (rivals.cfg: Northern Launch Directorate, Kerbalski Rocket Works, Pan-Kerbin Space Cooperative) are simulated at every Government Review: they climb the same milestone ladder as the player, claim firsts, lose crew and make headlines in the Press Room. The player claims a first when they hold a milestone before anyone else. Firsts and prestige feed a league table in the new Analytics window beside year-over-year budget, mission and goal figures. Poaching applicants and rival-claimed goals remain post-release (decision 10). On by default; agency.cfg rivals = false turns it off.
20. **KSA grows its own flight director instead of bridging to MechJeb (2026-10-27).** The M5 ascent/reentry program is the base; transfers and capture (KSA-51), powered landing with the flat-spot search (KSA-52) and docking (KSA-53) follow. MechJeb is GPL: approaches may be studied, code is never copied. MechJeb stays optional.
21. **Own flight director, built (2026-10-27).** Live missions are a list of legs built per mission profile: Kerbin ascent, transfer (Lambert-based planner in Core, executed through stock maneuver nodes), mid-course corrections by bisecting burns against the patched-conic prediction, capture, powered landing with a 5x5 terrain slope survey (burn timing biased toward the flattest cell; no lateral steering yet), surface EVA, airless ascent, return burn search, reentry, plus rendezvous and RCS docking for station modules. Programs remember the station vessel so later modules dock to it; a module whose core was simulated is simulated too. Eve surface and cloud missions stay simulated. `liveFlightScope` (Kerbin / Moons / All) in agency.cfg limits what flies live.
22. **The kerbals fly (2026-10-27).** Live flights are flown by the crew, not by a perfect autopilot: the best pilot aboard sets precision (stars, morale), safety margins (courage: brave pilots cut landing margins finer), docking approach speed, burn cutoff tolerance and a per-leg mistake chance (stupidity, low morale) that shows up as late ignitions, all tuned in agency.cfg FLIGHT_CREW. At judgement points (a rough landing site, a halted program) the pilot decides: a trait rule always answers, and with `crewDecisions = Local` (or `Any` for the API key) the local model is asked in character first with a 20 s timeout and the rule as fallback; the pilot's radio line goes in the flight log. Engineers still make the designs (M4) and Programs hold the station, base and cloud-city plans (M3).
23. **Department heads (2026-10-27).** Management-track kerbals (Program Manager, Deputy Director) hold Finances, Science, Public Relations and Operations through the Departments window (Director's Office, or the KSA bar in the Administration building). A head keeps their salary, cannot fly or join a project, and gives a rank- and star-scaled bonus: Finances trims salaries and raises the grant, Science raises simulated success chance, Public Relations amplifies prestige gains and adds year-end morale, Operations cuts design and build time (agency.cfg DEPARTMENTS). This is the M2 decision that hireable staff replace the stock Administration faces in function; the stock strategies are left alone.
24. **Stock contracts and stock hiring are switched off in KSA games (2026-10-27).** Offered Mission Control contracts are withdrawn as they appear and stock Astronaut Complex hires are refunded and charged at the KSA walk-in rate, so KSA goals are the tasking and KSA Recruiting is the hiring path at KSA prices. agency.cfg `stockContracts` / `stockHiring` turn either back on. Accepted contracts and already-hired kerbals are never touched.
25. **Career progression follows the tech tree, and the agency earns the Science (2026-10-27).** Templates whose parts are not researched cannot be built; a mission type is offered only when a buildable template exists or the researched part kinds can cover it (pod, booster and chute for a hop; engine, tanks and decouplers for orbit; more engine choice and legs beyond Kerbin); government goals are only issued when some feasible mission earns them. Science comes from the agency: scientists put in the research lab (Roster → Lab) produce labBase + labPerStar x stars per fiscal year, successful missions return Science by tier, and the Head of Science amplifies both (agency.cfg RESEARCH). A day-one "Flea test hop" mission and starting-parts template exist so a fresh career has something to fly.
26. **Science missions (2026-10-27, KSA-58).** A mission family flown for the data: Kerbin biome survey and orbital sounding (Early), Mun surface samples and Minmus geology (Mid), Duna atmosphere study (Late). Each carries `purpose = Science` and a `scienceYield`; the primary achievement is `Science:<body>`, which sits on the milestone ladder, counts as a first against the rivals, and backs three new goals. Payoff = yield x crew multiplier (a scientist aboard doubles it, +20% per star, +5% per other crew) x Head of Science. Simulated flights roll it from the outcome; live flights run the stock experiments through the flight (a ScienceRunner fires every usable experiment per situation and biome, a sweep leg holds orbit for it, surface ops are longer) and the primary is earned only when data is actually aboard at recovery, so the stock Science comes home too.
27. **Every planet and moon has a mission (2026-10-27).** Added Minmus orbit, Duna orbit, Eve flyby, Moho flyby and landing, Gilly, Ike, Dres, Jool orbit, Laythe, Vall, Tylo, Bop and Pol landings, Eeloo flyby and landing: 40 mission types in all, each with its own delta-v requirement in dv_requirements.cfg. Eve and Jool surfaces are cloud-city programs instead of landings. All are Firsts on the milestone ladder for the space race.
28. **Custom missions (2026-10-27, KSA-59).** The Director can define a mission: body, profile (suborbital, orbit, flyby, orbit the body, land), number of orbits or days on the surface, crew size, experiments on or off. It is priced from the profile, the body's distance class, crew and orbits; a synthetic mission type is added to the catalogue and saved with the game; templates are borrowed from catalogue missions of the same shape; the flight director holds for exactly N orbits (by the real period) and stays the asked days on the surface. Achievements follow from the shape, so a custom flight can still be a First.
29. **Exploration medals (2026-10-27, KSA-60).** The service record tracks farthest distance from Kerbin (live flights measure it, simulated ones use a body table), days on other worlds, worlds visited and the longest single mission. Six data-driven medals: Far Traveller Mun/Duna/Jool class, Offworld Veteran (30 days), Grand Tourist (5 worlds), Long Haul (200-day mission).
30. **Resupply, crew rotation and the campaign ladder (2026-10-27, KSA-61).** Finished programs need resupply: each program type has a supply interval and a resupply mission type (station resupply, base resupply per body, cloud resupply). A resupply is a cheap project aimed at the program; on success the program is supplied again and the crews rotate (relief crew stays aboard, old crew comes home). Overdue programs cost the crew aboard morale at year end and cost prestige. Finishing a program grants Base:<body> to its crews; a resupply grants Resupply:<body>. Goals carry a `requires` field so the government asks in order: orbit, land, science, base, resupply, body by body (the Campaign window shows the ladder).

---

## 15. Glossary

| Term | Meaning |
|---|---|
| Δv (delta-v) | Total change in velocity a rocket can achieve; the core measure of mission capability |
| TWR | Thrust-to-weight ratio; must be > 1 at liftoff |
| Stage Plan | The LLM's JSON description of a rocket |
| VesselBuilder | Mod code that turns a stage plan into a real KSP vessel |
| Project | A single rocket or mission effort |
| Program | A multi-mission effort, such as a station or base, made of Projects |
| Lean Year | A fiscal year with reduced budget after missed goals |
| Template Library | Pre-built designs used when AI is unavailable or off |
| KSA.Core | The portable, KSP-free agency logic |
| KSA.KSP | The adapter connecting Core to KSP |
| CKAN | The KSP mod manager used for all mod installs |
| KSPDIR | Environment variable pointing to the dev KSP install (`E:\KSP_dev`) |
| OpenRouter | API aggregator serving many LLMs through one OpenAI-compatible endpoint |
| Ollama | Local runtime for running LLMs on the player's own machine |
