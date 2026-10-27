# CLAUDE.md: Kerbal Space Agency (KSA)

Instructions and context for Claude when working on this repo. Read this first, every session.

> **Working title:** Kerbal Space Agency (KSA). The name may change: RocketWerkz's *Kitten Space Agency* also uses "KSA."

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
├── Configs/          ModuleManager data: medals, goals, careers, recruiting sources
├── Templates/        offline fallback .craft designs
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
| Mission modes | Hybrid default: Simulated unless it's a "First" or the player clicks Watch Live |
| AI providers | OpenRouter for design and kOS scripts; local (Ollama/LM Studio) for narrative |
| Hardware targets | Minimum 16 GB RAM (AI via API only), Recommended 32 GB (full mod set, optional small local model). T's 64 GB dev machine is **not** a target. |
| GitHub owner | **Blackbox Entertainment** organization, GitHub slug `blackbox-entertainment` (decided 2026-10-26). Remote: https://github.com/blackbox-entertainment/Kerbal-space-agency |
| Repo location | `G:\KSA` (local), VS Code with C# Dev Kit |

**Candidate supported mod set (not final):** ReStock/ReStock+, Near Future series, SSPXr (inflatables), KPBS (surface bases), Outer Planets Mod, Hooligan Labs Airships (Eve cloud city), Extraplanetary Launchpads. Required: ModuleManager, MechJeb2 (Live mode). Optional: kOS.

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
| **M5: Live Flight** | MechJeb bridge, Watch Live, telemetry, post-mortems |
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

- Code T writes is T's. KSP's code, assets, kerbals, and the "Kerbal" name belong to Take-Two/Squad and can't be reused in a standalone game.
- If outside contributors join, get agreement that T can relicense their code, or keep contributions out of Core.
- Model licenses vary (Apache 2.0, MIT, or custom terms like Llama/Gemma). Recommend models; don't redistribute them.
- Not legal advice. Get a lawyer before any commercial spin-off.

---

## 10. Open questions

1. Can the player manually override an AI design, or is hands-off strict?
2. Can kerbals quit (low morale) or retire (age/service)?
3. Track kerbal age and a hall of fame?
4. Rival agencies / space race mode?
5. How punishing are Crisis Years? Is "Director fired" a game over?
6. Separate science directorate budget?
7. Community-sourced template library?
8. Default OpenRouter models for design vs. post-mortem (benchmark in M4)
9. Final name (avoid the "KSA" collision?)
