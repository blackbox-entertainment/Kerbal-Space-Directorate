# Kerbal Space Directorate

A career mod for **Kerbal Space Program 1.12**. You are the Director of a space agency. You hire kerbals, give them projects and programs, and manage the government's budget. Your engineers design the rockets, your crews fly them, and every kerbal keeps a service record of ranks, medals, missions and, sometimes, an obituary.

> Status: in development, test builds only (current: 0.5.5, see [CHANGELOG.md](CHANGELOG.md)). Test builds are on the [Releases](https://github.com/blackbox-entertainment/Kerbal-Space-Directorate/releases) page as a zip; a CKAN listing is pending. To build one yourself, see [Building from source](#building-from-source).

## What it does

- **People.** Kerbals have careers, ranks, medals, morale, personalities, ages and a Hall of Fame. You recruit them through paid drives and interviews.
- **The agency.** Fiscal years, government goals, budget reviews, lean and crisis years, department heads and rival agencies.
- **Projects and programs.** Missions go from design to build to launch. Programs build stations, bases and Eve and Jool cloud cities module by module. Bases grow into settlements and then self-sufficient colonies.
- **Flights.** Missions fly simulated, or live with KSA's own flight director: launch on a pitch program, staging, transfers, landings, rendezvous and docking, reentry and recovery. The crew runs the science aboard, and the chutes always arm when a craft falls back through the air. Take the controls at any time (always on your own designs). Nothing flies, live or simulated, until its rocket has actually been built.
- **Rockets.** The engineering team designs every rocket from the parts you have researched. They work each mission out leg by leg from the stock delta-v map (climb, transfer, capture, landing, take-off, the trip home), with enough thrust for each world's gravity, fins on the launch stage and short, wide stacks, and an AI helps with what they cannot draw up. Every design must carry a parachute, the instruments researched and, for returns from orbit, a heat shield. Or fly your own: rockets you save in the VAB and planes you save in the SPH appear under "Your designs" in Projects (and can be swapped onto a project any time before launch), and you can tell the AI Director to fly them. The crew flies spaceplanes too: runway takeoff, jets to rockets, orbit and a glide home. Jet planes also fly aircraft missions of their own (a first flight, an aerial biome survey, a high-altitude flight) with goals, Science and a medal.
- **Fuel depot.** A tank farm in Kerbin orbit, filled by tanker flights. Ships bound for other worlds launch their upper stages empty and fill up there, simulated or live.
- **Rescues.** Crews stranded in odd orbits or on other worlds wait for a rescue mission: the engineers design a craft with a seat for everyone, or fly your own.
- **AI, optional.** An AI engineering team designs rockets, and an optional AI Director can run the whole agency while you watch. The mod plays fully with AI off: the engineers design by hand and the text is written in.
- **Records.** Every first is credited to the kerbals who flew it (or the rival that beat you to it), and an agency record book keeps the highest, fastest, farthest and longest flights and the career leaders. Broken records make the news.
- **Help.** A Help window in the Director's Office explains every part of the game.

## Requirements

| What | Version | Needed for |
|---|---|---|
| Kerbal Space Program | 1.12.x (developed on 1.12.5) | Everything |
| ModuleManager | 4.2.x | Everything. KSA's careers, medals, goals and settings are ModuleManager configs |

KSA never requires the Making History or Breaking Ground DLC. The engineers design every rocket from the parts you have researched, AI on or off; stock-parts template designs fill in where they fit.

**Computer.** 16 GB of RAM is the minimum, with AI through an online API. 32 GB is recommended if you also want to run a small AI model on your own machine.

## Optional mods

Install these through CKAN. Use the CKAN identifier shown to find each one.

| Mod | CKAN identifier | What KSA does with it |
|---|---|---|
| Kerbalism | `Kerbalism` | Real life support for colonies, live flights and stranded crews. See [Kerbalism](#kerbalism) below |
| Stockalike Station Parts Expansion Redux | `StationPartsExpansionRedux` | Station module designs (planned) |
| Kerbal Planetary Base Systems | `KerbalPlanetaryBaseSystems` | Surface base module designs (planned) |
| HL Airships Core | `HooliganLabsAirships` | Eve cloud city designs (planned; cloud cities fly simulated today) |
| Airplane Plus /L | `AirplanePlusL` | Propeller engines, small cockpits and early plane parts. Its propellers burn fuel with intake air like jets, so prop planes fly KSA's aircraft missions. Pulls in Firespitter. Pick this one, not the old "Airplane Plus" (KSP 1.8 only) |
| Un Kerballed Start | `UnKerballedStart` | A probes-first, planes-first tech tree. A career opens with the Stayputnik probe core: KSA flies uncrewed probe missions (sounding rocket, probe to space, first satellite) to earn the Science for the first crew pod, and moves the Mk2-R radial and Mk16 parachutes, the Flea booster and the TD-06 decoupler to the start node. Pulls in Community Tech Tree. Start a new career with it. When CKAN offers recommended mods, click Uncheck All (see below) |

KSA does not need MechJeb or kOS. It has its own flight director.

**Mods that clash with KSA.** Leave these off (CKAN offers several as recommendations with Un Kerballed Start):
- **Bureaucracy and Strategia** replace the budget and strategy systems. KSA runs its own fiscal years, budgets and departments.
- **Contract packs** (Career Evolution, Exploration Plus, Research Advancement Division, the GAP packs) add contracts. KSA switches stock contracts off and issues government goals instead.
- **Kerbal Construction Time** adds its own build and roll-out times. KSA has its own design and build days and launch queue.
- **Custom Barn Kit** changes building levels and upgrade costs, which KSA reads for the AI Director's upgrades.
- **EVARepairs** breaks parts that only a kerbal on a spacewalk can mend. KSA's crews do not do repairs.

## Installing with CKAN

1. Open CKAN and select your KSP 1.12 install.
2. Search for **ModuleManager** and tick it.
3. Optional: search for **Kerbalism** and tick it. CKAN adds what it needs by itself: the default Kerbalism config, Harmony 2, KSP Community Fixes and Community Resource Pack. For propeller planes, also tick **Airplane Plus /L**.
4. Click **Apply changes**.
5. Unzip the test build (`KerbalSpaceDirectorate-<version>.zip`) and copy its `GameData\KSA` folder into your KSP `GameData` folder, so you have `GameData/KSA/Plugins/KSA.KSP.dll`. CKAN cannot install KSA until it is released, so this step is manual for now. To update, delete `GameData\KSA` and copy the new one in.
6. Start KSP and begin a new Career game. The KSA button is on the Space Center toolbar.

Use a separate copy of KSP for testing mods, not the one you play on.

## AI setup (optional)

KSA works with no AI at all. To turn it on, open **AI Setup** in the Director's Office, or copy `GameData/KSA/PluginData/ksa_settings.example.cfg` to `ksa_settings.cfg` in the same folder and edit it.

- **Online:** an [OpenRouter](https://openrouter.ai) API key. Rocket design, after-action reports and the AI Director use it. A monthly spend cap in AI Setup keeps costs down. The AI Director's default model costs about a cent per game year.
- **On your own machine:** [Ollama](https://ollama.com) or LM Studio. Press releases, crew dialogue and news use it, free. KSA does not ship any AI model. AI Setup pulls the recommended one for you.

Your `ksa_settings.cfg` holds your API key. Never share or upload it.

## Kerbalism

Kerbalism is optional. KSA finds it on its own and changes three things.

- **Colonies must feed themselves.** A settlement becomes a self-sufficient colony only when it has a drill, an ore converter and power, plus a greenhouse for every 8 residents and scrubber and water recycler capacity for everyone. Each base can add an **industry module** with Add module in the Programs window. With Kerbalism, that module carries a greenhouse as well. A self-sufficient colony keeps growing: it fits out the room it builds and adds a greenhouse every two years. A colony that outgrows its food needs resupplies again.
- **Live flights must carry food and water.** A stock crew pod holds about five days of supplies. A mission longer than its craft's supplies, plus two days spare, flies simulated instead of live. AI-designed rockets are told to add supply containers.
- **The stranded run out.** A stranded crew is lost after 20 days, not two years.

While a station or base is supplied on KSA's schedule, KSA keeps the food, water and air of its live vessels topped up, since the resupply flights stand in for the cargo. Once a resupply is overdue, Kerbalism's own clock takes over and crews can die.

**Known limits with Kerbalism:**
- Kerbalism replaces the stock science experiments. On live flights the crew switches Kerbalism's experiments on, and AI designs carry an antenna so the data goes home before the pod's small drive fills ("no storage space"). Samples such as goo still wait for recovery. The Science a mission pays through KSA arrives either way.
- Kerbalism's radiation, stress and part failures apply to live flights. They don't apply to simulated ones.

Every number is in `GameData/KSA/Configs/agency.cfg`, in the `LIFE_SUPPORT` block. Set `enabled = false` there to keep Kerbalism installed but leave KSA on its stock rules.

Use one life support mod only. Kerbalism conflicts with TAC Life Support, Snacks and USI Life Support.

## Building from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) and a copy of KSP 1.12. Visual Studio is not needed. VS Code with the C# extension is enough.

1. Set `KSPDIR` to your test copy of KSP, for example in PowerShell:
   ```powershell
   $env:KSPDIR = "E:\KSP_dev"
   ```
2. Build. The build copies the mod into `$KSPDIR\GameData\KSA`:
   ```powershell
   dotnet build KSA.sln -c Debug
   ```
3. Run the tests:
   ```powershell
   dotnet test KSA.Core.Tests
   ```

4. Package a zip to share (builds Release, runs the tests, writes `dist\KerbalSpaceDirectorate-<version>.zip`):
   ```powershell
   .\tools\package.ps1
   ```

**Versions.** The version lives in `Directory.Build.props` and `GameData/KSA/KSA.version` (a test checks they match). Changes are listed in [CHANGELOG.md](CHANGELOG.md). Below 1.0 the minor number tracks the roadmap milestone reached.

The code is split in two. `KSA.Core` is the agency logic in plain C# with no KSP references. `KSA.KSP` connects it to the game. Design notes are in [docs/KSA_GDD.md](docs/KSA_GDD.md), and the in-game test plan is in [docs/TESTPLAN_M3-M6.md](docs/TESTPLAN_M3-M6.md).

If something goes wrong, look in `KSP.log` in your KSP folder. Every KSA line starts with `[KSA]`.

## License

MIT, copyright Blackbox Entertainment. See [LICENSE](LICENSE).

Bug reports are welcome; code contributions are not accepted. See [CONTRIBUTING.md](CONTRIBUTING.md).

Kerbal Space Program, its assets and the Kerbal name belong to Take-Two Interactive and Squad. This mod is not affiliated with them.
