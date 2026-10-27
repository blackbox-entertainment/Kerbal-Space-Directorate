# Kerbal Space Agency (KSA): Game Design Document

**Working title:** Kerbal Space Agency (KSA) *(name under review; see 14.1)*
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
- **Eve cloud city**, using buoyancy parts (Airships or Hooligan Labs Airships)
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

1. **Final name.** "KSA" collides with RocketWerkz's *Kitten Space Agency*. Keep or rename before release?
2. Can the player **override** a design manually, or is hands-off strict?
3. Can kerbals **quit** (low morale) or **retire** (age/service time)?
4. Track **kerbal age** and a hall of fame?
5. Rival **space agencies** for a space-race mode?
6. How punishing are Crisis Years? Is **"Director fired"** a game over?
7. Separate **science directorate** budget?
8. **Community-sourced** template library?
9. Default OpenRouter models for design vs. post-mortem (benchmark in M4)
10. Cloud city buoyancy: **Airships vs. Hooligan Labs**?
11. GitHub owner: **Blackbox Entertainment vs. jaqknife**?

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
