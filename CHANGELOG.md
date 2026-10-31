# Changelog

Versions follow `MAJOR.MINOR.PATCH`. Until the public release (milestone M7) the version stays below 1.0: the minor number tracks the roadmap milestone the build has reached, the patch number counts test builds within it.

## 0.8.29 (2026-10-31), test build

- **Mun orbit takes one kerbal.** It asked for two for no good reason; Minmus orbit already took one, so a smaller rocket can fly it.

## 0.8.28 (2026-10-31), test build

- **Side boosters stay on while they burn.** The crew's early booster drop fired on solids still burning; they cannot be shut off, so they flew up the side of the rocket and destroyed it. Burning solids now stay on until burnout, and liquid side boosters are shut down before they drop.
- **Fixed solar panels left alone.** Folding panels for the air also told OX-STATs to retract, which threw an error every frame. Only panels and antennas that move are touched.
- **No orbit, no Mun.** An ascent that ends short of orbit now goes to reentry instead of on to the transfer burn.

## 0.8.27 (2026-10-30), test build

- **Flight tests: pick the rocket.** The window lists the save's craft with delta-v and seats. Pick one, then Fly any mission; a rocket that cannot fly it says why.

## 0.8.26 (2026-10-30), test build

- **Full warp after the air.** Physics warp left over from flying through the atmosphere held the crew's warp at 4x. Outside the air the crew now switches to regular time warp.

## 0.8.25 (2026-10-30), test build

- **Reentry from the Mun.** A return with its periapsis at 35.7 km counted as needing a deorbit burn and warped toward an apoapsis six hours away, in the middle of reentry, without dropping the service stage. Any path that meets the air now goes straight to reentry, and the safety net that drops stages covers it too.

## 0.8.24 (2026-10-30), test build

- **Antennas and panels.** Under the crew, deployable antennas and solar panels now open once above the air and fold away before coming back into it.

## 0.8.23 (2026-10-30), test build

- **Flight tests: pick a mission.** A Fly button on every mission flies that one alone and keeps the other results.

## 0.8.22 (2026-10-30), test build

- **Kerbin orbit passes on the bench,** Orbiter mk I from pad to landing, deorbit included.
- **Flight tests:** a Next rung button. The "cannot turn retrograde" message no longer appears while the crew is only waiting for apoapsis.

## 0.8.21 (2026-10-30), test build

- **Handing back on the way down.** A flight that has been to orbit and is coming down now resumes at reentry, where time warp works. It had restarted the ascent, which tried to circularise a capsule with no fuel and held warp at 1x. Circularising also gives up when there is no fuel left.

## 0.8.20 (2026-10-30), test build

- **The stock hold can turn the ship.** The crew re-sent the SAS mode every frame, which restarted the hold, so pilots with SAS skill never lined up for the deorbit or reentry. It is now set only when it changes. If the deorbit still has not lined up after 45 s, the crew steers it by hand.

## 0.8.19 (2026-10-30), test build

- **Moon flybys fly a free return.** The course correction aimed only for a height above the Mun and took the cheapest burn, which could pass behind the Mun and fling the craft away. It now looks at the path after the Mun, keeps a correction only if it stays in Kerbin's space, prefers the one that falls closest to Kerbin, and may pass up to 1,200 km above the Mun to get it.

## 0.8.18 (2026-10-30), test build

- **Deorbit: warp first, then turn.** The crew lined up and then warped to apoapsis, but attitude freezes in warp, so the nose came out up to 175 degrees off with no time to turn and the burn window closed. Warp now ends 150 s before apoapsis, and the burn may start up to 4 minutes after it.
- **Flight tests are stricter and simpler.** A flight the Director took over no longer counts as a pass, and the bench skips the food and water planning check so it tests the flying.

## 0.8.17 (2026-10-30), test build

- **Earliest parts first, on every rocket.** The engineers now take the earliest parts in the tech tree that can do the job, not the lightest: they try only the oldest engines, tanks and boosters, then allow one tech level more at a time until a rocket works. Pods, heat shields, decouplers and legs follow the same rule. Your own craft are never changed.

## 0.8.16 (2026-10-30), test build

- **Flight tests fly your rockets.** The bench now uses the smallest saved craft in the save that fits each mission, so it tests the flying and not the engineers' designs. With none that fits it says so and stops.
- **Staging gap back to 1.5 s.** The 0.3 s gap from 0.8.12 let the crew fire three stages in 0.6 s and throw away a fuelled stage.
- **No booms on rockets.** The Magnetometer Boom and robot arm scanner are no longer added to the engineers' designs.

## 0.8.15 (2026-10-30), test build

- **Real capsules only.** The engineers put a crew in a Cupola with a 2.5 m heat shield under it. Flybys, orbits and landings now use a capsule that comes home through the air: no cockpits, Cupolas or lander cans. Kerbal Planetary Base Systems parts are for bases only. More than four side boosters is a last resort.

## 0.8.14 (2026-10-30), test build

- **Only parts that stack.** The engineers chose a Kerbal Planetary Base Systems container tank, which has one attach node, and the rocket could not be assembled. Rocket tanks and stack decouplers now need nodes at both ends and engines a top node.
- **Flight tests say why** a project was called off.

## 0.8.13 (2026-10-30), test build

- **Parts by job.** Designs now draw from five categories: flybys and orbits, landers and returns, station building, base building (cloud cities keep their own list), and planes. Parts can belong to several. A plane's cockpit belongs only to planes; jets and wings stay out of rockets; landing legs go to landers and bases.

## 0.8.12 (2026-10-30), test build

- **Upper stages stay straight.** The first bench flight tracked its turn perfectly, then its finless second stage tumbled 1.5 s after separating at 17.5 km. An upper stage now gets fins unless the launch stage has 2,800 m/s at sea level (was 1,500), and in the air the next engine lights 0.3 s after separation instead of 1.5 s. Unflown engineers' rockets are redrawn (design rules revision 2).
- **Real capsules and parts that fit.** The engineers took the lightest part that passed: a plane's cockpit for the crew pod (a plane cockpit is now only a last resort), a 0.625 m separator between 1.875 m stages (each decoupler now matches its stage, unless a design at its limit needs the lighter one), and the 0.625 m heat shield on a 1.25 m pod (part sizes now come from the part's own profile, not its attach nodes).
- **Office window.** The Flight tests button no longer stretches the bottom row past the window.

## 0.8.11 (2026-10-30), test build

- **Flight test bench.** In a Sandbox save the Director's Office has a Flight tests button. It flies a ladder of missions live with no one at the controls (Kerbin orbit, Mun flyby, Mun orbit, Mun landing, Minmus orbit, Minmus landing, Duna flyby, Duna orbit, Duna landing): the engineers design each rocket, the crew flies it with no pilot mistakes, and it stops at the first failure. Results go to GameData/KSA/PluginData/FlightTests/flight-tests.txt.

## 0.8.10 (2026-10-30), test build

- **No more westward launches.** During the vertical climb the crew left the stock SAS hold in charge, which keeps whatever attitude the rocket drifts to. Orbiter mk III leaned west 3 degrees by 500 m, the gravity turn then followed its path, and gravity bent it on over the top: it flew west, against Kerbin's spin, burning about 350 m/s more than it should. The crew now steers to 88 degrees east from just off the pad.
- **No false "out of fuel".** Just after the ship came out of time warp the game's delta-v read 0 for a moment; the ascent took that for empty tanks, ended before circularising, and the Mun transfer could not start from a suborbital path, so the program halted (and halted again on Retry). Out of fuel now has to read so for 3 seconds out of warp.
- **The survival net leaves the orbit stage alone.** While the ascent is coasting to, or turning for, its circularisation burn above 42 km, the net no longer drops the stage under the capsule (a retry lost its full 2,051 m/s stage that way and fell back in flames).
- **Halts say why.** "Flight program halted" now names the leg and its reason; a flyby leg reads "Flyby" until it knows which world.

## 0.8.9 (2026-10-30), test build

- **The AI Director keeps money in hand.** With every goal of the year met it spent 225,000 of 238,000 funds upgrading the VAB for next year's missions. Now an upgrade must leave at least a quarter of the yearly grant (or half a year of salaries, if more), and once the year's goals are met it may only spend what was earned above the year's opening balance; otherwise the briefing says the upgrade waits for next year.
- **No spacewalk without the Astronaut Complex.** A spacewalk mission could be started and flown simulated at Astronaut Complex level 1 when the rocket was your own design (the mission check was skipped for player designs). The building rule now applies whoever built the rocket: in the Projects window, in the pad offer for your own craft, and when a project is started.

## 0.8.8 (2026-10-30), test build

- **What can this fly?** In the VAB and SPH the KSA toolbar button measures the craft on the floor, saved or not, against every mission: the ones it can fly (hardest first), the near misses with the delta-v each lacks, and why the rest are out of reach.
- **A real deorbit burn.** With power the crew waits for apoapsis (time warp gets there) and burns at about 1 g down to the target periapsis, instead of a split second at full throttle wherever it happened to be. A crew without power still takes the first moment the nose lines up.
- **Warp down to the air.** After the deorbit burn the flight director warps to just above the atmosphere, and you can warp there yourself: the 0.8.1 rule that stopped fast warp 15 km above the air is gone (nothing separates until a second after warp ends, so it is no longer needed).

## 0.8.7 (2026-10-30), release

Everything since 0.8.5, from the test build 0.8.6 below. In short:

- **Bases you can see.** A surface base stands on the ground at its site as one connected vessel and grows: each delivered module adds its section (core, habitat, lab, power, industry), each of the first six resupplies adds a habitat or a greenhouse, and a colony keeps adding habitats on its own. With Kerbal Planetary Base Systems it is built from that mod's modules, round a corridor junction once Composites is researched and on its own feet once Landing is; otherwise from stock parts. Power is the mod's reactor or your best solar panels. It is rebuilt at the Space Center or Tracking Station when it changes, at a flat dry spot or beside a module landed live. It carries no crew; the agency keeps its residents.
- **Broken mod templates removed.** Three templates named craft files that never existed; a template whose craft file is missing is now never used.
- **Kerbal Planetary Base Systems is supported** (CKAN `KerbalPlanetaryBaseSystems`). Untick the mods CKAN recommends with it (Probes Before Crew rearranges the tech tree).

## 0.8.6 (2026-10-30), test build

- **Bases you can see.** A surface base now stands on the ground at its site as one connected vessel and grows: each delivered module adds its section (core, habitat, lab, power, industry), each of the first six resupplies adds a habitat or a greenhouse, and a colony keeps adding habitats on its own. With Kerbal Planetary Base Systems the base is built from that mod's modules, round a corridor junction once Composites is researched, on its own feet once Landing is; otherwise from stock parts. It is rebuilt at the Space Center or Tracking Station when it changes, at a flat dry spot (or beside a module landed live). It carries no crew; the agency keeps its residents.
- **Broken mod templates removed.** The station, base and airship templates for Stockalike Station Parts Expansion Redux, Kerbal Planetary Base Systems and HL Airships named craft files that never existed; with the mods installed KSA could have picked them. A template whose craft file is missing is now never used.

## 0.8.5 (2026-10-30), release

Everything since 0.8.0, from the test builds 0.8.1 to 0.8.4 below. In short:

- **Safe reentry out of time warp.** Nothing is staged or decoupled while the ship is on rails or warping, nor in the first second after it unpacks (a capsule decoupled in that frame exploded), and the reentry warp ends 15 km above the air.
- **Time warp works while watching.** Warp is stopped only when something is about to separate; physics warp is allowed in the air.
- **Deorbit burns happen out of warp.** The engine lights only off rails, warp ends before the nose lines up, and a crew with a flat battery waits for the orbit to swing the nose round to retrograde. The flight director window says what it is waiting for and shows the periapsis coming down.
- **Records, world by world.** Firsts grouped by world with record-book names (First kerbal in space, First kerbal on the Mun), the agency records, and a new Records by world section (missions, longest mission and surface stay, leading kerbals).

## 0.8.4 (2026-10-30), test build

- **No deorbit burn in time warp.** With a flat battery Valentina waited for the orbit to swing her nose round to retrograde (as she should), but the burn was lit while the game was still in time warp: on rails the engine gives no thrust, 76 seconds passed, and when the warp ended she burned for about one second. Now the warp ends while the nose is still 45 degrees from retrograde, the engine lights only off rails, and any warp during the burn is stopped. The same guard covers spaceplane deorbits.
- **Clearer flight director window.** While waiting without power it says so ("no power to turn; waiting for the orbit to swing the nose round"); during the burn it shows the periapsis coming down to the target; at the end it says "Deorbit burn done: periapsis 35 km, inside the air; coming home."

## 0.8.3 (2026-10-30), test build

- **Time warp works while watching again.** The 0.8.1 safety check (nothing separates while warping) stopped the warp every time it was asked, and the reentry leg asked every frame, so warp was cancelled the moment you started it. Now the warp stops only when something is actually about to be staged or decoupled. Physics warp is allowed in the air during reentry and under parachutes; only rails warp ends 15 km above the atmosphere.

## 0.8.2 (2026-10-30), test build

- **Records, world by world.** The Records window now lists the firsts grouped by world, Kerbin first, with record-book names: First flight, First kerbal in space, First kerbal in orbit, First spacewalk, First kerbal on the Mun, First orbit of Duna and so on. Worlds nobody has reached share one line. Then the agency records (altitude, speed, distance, days, missions, worlds), then a new Records by world section: for each world flown to, crewed missions and successes, the longest mission, the longest surface stay, and the kerbals with the most missions and the most surface days there.

## 0.8.1 (2026-10-30), test build

- **No staging out of time warp.** Valentina's orbit went perfectly, deorbit included, but the flight director warped toward periapsis, KSP dropped out of warp at the top of the air, and in that same frame the capsule was decoupled while the ship was still packed on rails: the parts overlapped as it unpacked and the pod exploded at 70 km. Now nothing is staged or decoupled while the ship is packed or warping, nor in the first second after it unpacks, and the reentry warp ends 15 km above the air.

## 0.8.0 (2026-10-30), release

Everything since 0.7.9, from the test builds 0.7.10 to 0.7.13 below. In short:

- **Deorbit burns point the right way.** The crew holds retrograde through the orbit and the deorbit engine lights only within 15 degrees of retrograde (it had lit after a fixed 5 seconds while facing forward and thrown a capsule into a 1,064 km orbit). The same guard covers Mun and Minmus landings and spaceplanes.
- **Orbital rescues fly live.** A crew stranded in orbit aboard a real ship is rescued only by a live flight that comes within about 120 m and moves them across; such a rescue never falls back to a simulated dice roll.
- **Recovered live flights are always scored.** Recovering a ship from flight no longer loses the flight, its firsts and its records.
- **Records:** farthest from Kerbin is measured from the surface (older saves corrected once).
- **Every building's limits count.** VAB part limits, Astronaut Complex staff limit and spacewalks, the R&D Science limit, and flight planning for live flights to other worlds (Mission Control and Tracking Station level 2). The AI Director plans its upgrades around them.
- **Heat shields** are required on your own designs only coming home from another world; from Kerbin orbit they are a suggestion.

## 0.7.13 (2026-10-30), test build

- **Every building's limits count.** The VAB's part limit (30 at level 1) now holds back missions, projects and your designs like the pad's mass limit. A full Astronaut Complex (5 kerbals at level 1) refuses hires, and the AI Director runs no recruitment drives. Missions that need a spacewalk stay locked until the Astronaut Complex allows them in flight. Research above the R&D building's Science limit (100 at level 1) is refused. Live flights to other worlds need Mission Control and Tracking Station at level 2 (manoeuvre nodes, predicted encounters) and are simulated until then. The AI Director's briefing marks each one, and UPGRADE NEEDED now names the VAB, the Astronaut Complex and R&D when they hold something back.
- **Heat shields: a suggestion from Kerbin orbit.** Your own designs need a heat shield only coming home from another world; a return from Kerbin orbit no longer refuses them (Orbiter mk I can fly Kerbin orbit). The engineers still fit one when it is researched.

## 0.7.12 (2026-10-30), test build

- **A recovered live flight is always scored.** Recovering a ship from the flight scene sent the game to the Space Center, where KSA looked for the ship before KSP had reported the recovery, found none and put the project back on the pad: the flight, its firsts and records were lost (Valentina's orbit). KSA now waits 30 seconds after a scene loads before calling any live flight lost.
- **Farthest from Kerbin measured from the surface.** Live flights measured from Kerbin's centre, so an 87.6 km hop read 687.6 km. Older saves are corrected once on load: the live flights, every crew member's farthest distance and the record.

## 0.7.11 (2026-10-30), test build

- **Orbital rescues fly live.** A crew stranded in orbit aboard a real ship is rescued only by a live flight that comes alongside (within about 120 m, speeds matched) and moves the crew across; no spacewalk is needed. Such a rescue is set to Watch Live whatever mode was asked for, cannot be switched to Simulate, and waits on the pad rather than falling back to a simulation. Surface rescues and crews stranded by simulated flights are still simulated.

## 0.7.10 (2026-10-30), test build

- **Deorbit burns point the right way.** The deorbit burn lit after a fixed 5 seconds wherever the nose pointed. Valentina was still facing prograde after her orbit, so the burn raised her apoapsis from 96 km to 1,064 km and emptied the tank. Now the crew holds retrograde through the whole orbit, the engine lights only within 15 degrees of retrograde, and the burn pauses if the nose drifts more than 30 degrees off. The same check now guards the deorbit before a Mun or Minmus landing and a spaceplane's deorbit.
- **An honest report.** A deorbit burn that runs out of propellant with the periapsis still above the air says so (and the next design gets more fuel) instead of "Deorbit complete".

## 0.7.9 (2026-10-30), release

Everything since 0.7.5, from the test builds 0.7.6 to 0.7.8 below. In short:

- **Un Kerballed Start and Airplane Plus are no longer supported.** The probe missions built for Un Kerballed Start (Sounding rocket, Probe to space, First satellite), their goals and its start-node patch are removed; every mission carries a crew again. Stock SPH planes still fly the aircraft missions. A probe project in an old save is called off on load with a full refund.
- **Parachutes in pairs.** A design never carries a single radial parachute; an odd one gets a partner on the other side.

## 0.7.8 (2026-10-30), test build

- **Un Kerballed Start and Airplane Plus dropped.** KSA no longer supports either mod. The start-node patch for Un Kerballed Start is gone, and so are the three probe missions built for it (Sounding rocket, Probe to space, First satellite) with their goals (Launch a probe, A probe in space, First satellite). Every mission carries a crew again. Stock planes from the SPH still fly the aircraft missions.
- **Kept from 0.7.7:** a design never carries a single radial parachute; an odd one gets a partner.
- **Old saves:** a probe project in progress is dropped on load (its mission no longer exists).

## 0.7.7 (2026-10-30), test build

- **Radial chutes for probes.** The Stayputnik is a sphere with no nose for a stack parachute, so a probe now carries a pair of radial parachutes (Mk2-R) on its sides, never a stack chute, whenever radial chutes are researched. With Un Kerballed Start, KSA moves the Mk2-R to the start node along with the Mk16, the Flea and the TD-06.
- **Chutes in pairs.** A design never carries a single radial parachute: an odd one gets a partner on the other side, so the drag pulls straight.
- **Older designs redrawn.** The engineers' design rules moved to revision 2, so a probe rocket drawn before this build (no parachute) is redrawn when the game loads, if it has not flown yet.

## 0.7.6 (2026-10-30), test build

- **Watch your probes.** Sounding rockets and Probe to space now fly live under the same rules as any first (they had been switched to simulated once built, so a project marked Watch Live never flew). The launcher puts a craft with no crew on the pad, nothing waits for food or water, and a probe that lands is recovered automatically like a crewed capsule. First satellite stays simulated for now: a Stayputnik cannot steer to orbit.
- **How a live probe flight is scored.** Its liftoff, its climb above the air and a full orbit count as probe achievements (Probe:, ProbeSpace:, Satellite:), never as the crewed ones. Its data counts whether the probe comes home or not, so a spent sounding rocket that did its job is a success. Probe to space succeeds only above the air (its main objective is now reaching space), and one that falls short says so.
- **A parachute for the probe.** Probe sounding rockets carry a parachute when one is researched, so the probe can be recovered.

## 0.7.5 (2026-10-30), release

Everything since 0.7.3, from the test build 0.7.4 below. In short:

- **Probe missions.** Missions can fly with no crew: Sounding rocket, Probe to space and First satellite, each paying Science and repeatable at a shrinking payout, with goals of their own (Launch a probe, A probe in space, First satellite). The engineers fit a probe core and an antenna, no parachute; probes fly simulated for now.
- **Un Kerballed Start is supported.** A probes-first career now starts flying on day one, and KSA moves the Mk16 parachute, the RT-5 Flea and the TD-06 decoupler to the start node when that mod is installed.
- **A crew pod means seats.** Crewed missions need a pod with seats researched; a probe core no longer counts.

## 0.7.4 (2026-10-30), test build

- **Probe missions.** Missions can now have no crew. Three uncrewed missions: Sounding rocket (a probe on a small booster), Probe to space (above the air) and First satellite (a probe in orbit), each paying Science. The engineers fit the lightest probe core and an antenna (the data goes home by radio) and no parachute. Probes fly simulated for now. A probe science mission may be flown again for more data, each success paying 60% of the last (never under a quarter), so a probe career can earn its way to the first crew pod. Goals: Launch a probe, A probe in space, First satellite. Probe achievements count for the agency even with no crew to carry them.
- **Un Kerballed Start works.** Its career opens with only the Stayputnik: KSA now flies probe missions from day one, and a ModuleManager patch (applied only with Un Kerballed Start) moves the Mk16 parachute, the RT-5 Flea and the TD-06 decoupler to the start node. README and help list it as supported again.
- **A crew pod means seats.** Crewed missions now need a pod with seats researched; a probe core no longer counts as one.

## 0.7.3 (2026-10-30), release

Everything since 0.7.0, from the test builds 0.7.1 and 0.7.2 below. In short:

- **The crew can steer.** KSP runs a mod's fly-by-wire hook after SAS, and ours had been zeroing SAS's pitch and yaw since 2026-10-28, so no attitude the crew chose (the gravity turn, facing the horizon, planes) was ever steered to. Fixed. The circularisation burn also no longer starts under time warp and gives up as "out of propellant".
- **Upgrades first.** Nothing launches that its pad or runway cannot take: such missions stay locked, too-heavy saved designs are not offered, and built projects wait on the pad. The AI Director sees an UPGRADE NEEDED line for each building in the way, upgrades when it can afford it and saves for it when it cannot.
- **Plane mods.** README and help list Airplane Plus /L (propeller planes fly the aircraft missions like jets) as optional and supported, and name the mods that clash with KSA (Un Kerballed Start, Bureaucracy, Strategia, contract packs, Kerbal Construction Time, Custom Barn Kit, EVARepairs). Un Kerballed Start was listed as supported in the first 0.7.3 notes; it is not: a career under it starts with only a probe core.

## 0.7.2 (2026-10-30), test build

- **Upgrades first.** Nothing launches that its pad or runway cannot take. A mission whose lightest engineers' rocket (or ready-made design) is heavier than the launch pad takes stays locked, with the reason ("its lightest rocket weighs 24 t and the Launch Pad takes 18 t: upgrade the Launch Pad"); a saved design too heavy is not offered; and a project already built that is too heavy waits on the pad ("Waiting for a building") and cannot be launched, live or simulated, until the building is upgraded.
- **The AI Director plans the upgrade.** Its briefing has an UPGRADE NEEDED line for each building in the way, with what the next level frees (missions, waiting projects, your saved craft), the price, and either "AFFORDABLE NOW: upgrade it this turn" or "SAVE FOR IT", in which case it starts nothing optional until it can pay (keeping half a year of salaries in reserve). A needed upgrade is allowed even while it is saving money at the year's end. When an upgrade is due and affordable at the Space Center, the Director takes a turn for it.

## 0.7.1 (2026-10-30), test build

- **The crew can steer at last.** KSP runs a mod's fly-by-wire hook after SAS has written its corrections (checked in the game's own code: pre-autopilot, autopilot, post-autopilot, fly-by-wire). Since 2026-10-28 our hook set pitch and yaw to zero "so SAS would not let go", which wiped out every SAS correction: whenever the crew held an attitude of their own (the pitch program, facing the horizon, planes), nothing steered. The 0.7.0 log shows it plainly: the program asked for 85 degrees leaning east, the nose sat at 82-86 degrees leaning west and turned 0.1 deg/s, all the way up and through the coast. The hook now leaves SAS's commands alone. Stock SAS holds (prograde, retrograde) were never affected.
- **No burn under time warp.** The circularisation burn started while the game was in on-rails warp, where engines cannot fire, and was given up as "out of propellant" with 2,770 m/s aboard; the crew went home without trying for orbit. Warp is now stopped before the burn, and no thrust under warp never counts as empty tanks.

## 0.7.0 (2026-10-30), release

Everything since 0.6.8, from the test builds 0.6.9 to 0.6.17 below. In short:

- **Getting to orbit.** The flight director's biggest fixes yet: the crew now holds the rocket's roll (an undamped roll had stopped every pitch-over), never burns while the craft is turning, stops a burn that throws the apoapsis away, levels the gravity turn by 50 km, prefers engines that swivel and steers on a little throttle. Every 10 seconds of the climb, turn, coast and circularisation is in KSP.log.
- **No paper firsts.** A mission that would do something the agency has never done in a real flight (orbit, a flyby, a landing, a spacewalk) flies live; a simulated success is not know-how.
- **Aircraft.** Four missions flown in your own SPH planes, no rocket needed: First aircraft flight, Aerial biome survey (Science), High-altitude research flight (Science, above 18 km) and Rocket plane to 40 km (jets to their ceiling, then a rocket zoom climb). Goals for each, an Aviator's Wings medal, and RAPIERs counted as the rockets they also are.
- **Launch, then choose.** A craft launched straight from the VAB or SPH with a KSA kerbal aboard offers every mission it can fly.
- **Buildings.** The AI Director upgrades space center buildings when one is in the way, and a design too heavy for its pad or runway says which building to upgrade. Swap rocket lists every saved design with the reason it does not fit.
- **Reports.** The flight safety office files an after-action report; it is a post-mortem only when crew died.
- **Older designs are redrawn** by today's rules, and the engineers fix the cause of a failed flight in the next design (a roll is no longer mistaken for a tumble).

## 0.6.17 (2026-10-30), test build

- **The AI Director upgrades buildings.** A new action, upgrade {facility}, raises a space center building one level, paid from the agency's funds (held to the saving rule once the year's goals are met; refunded if the game refuses). Its briefing lists every building with its level, what it allows now, what the next level allows and the price: launch pad and runway weight and size, VAB and SPH part counts, spacewalks (Astronaut Complex), flight planning (Mission Control), orbit patches (Tracking Station), research cost (R&D). Upgrades happen while the Space Center is on screen; prices are shown there.
- **Too heavy for the runway or pad.** A saved design heavier than its launch site takes is no longer offered, with the reason ("14 t, too heavy for the Runway at level 1 (10 t): upgrade the Runway"), and the AI Director's briefing names every design held back by a building.

## 0.6.16 (2026-10-30), test build

- **Post-mortem only for a death.** The flight safety office's write-up is a post-mortem when crew died on the flight and an after-action report otherwise: in Mission Control, the Press Room, the screen message and what the AI is asked to write.

## 0.6.15 (2026-10-30), test build

- **After-action reports.** The flight safety office's write-up of a flight is now called an after-action report everywhere you see it (Mission Control, the Press Room, screen messages, AI Setup, help). Save files and settings keep their old keys, so nothing needs redoing.

## 0.6.14 (2026-10-30), test build

- **The roll is held.** The crew pointed the rocket by locking SAS to the wanted direction "keeping the current roll", taken afresh each time. SAS was therefore always told the roll was right, so nothing damped a roll: at maximum dynamic pressure the rockets spun up to 60-156 deg/s, and a spinning rocket cannot pitch over (both Kerbin orbit attempts climbed nearly straight up, the second drifting west, periapsis -600 km at 57 km up). The crew now holds the roll SAS was locked to and turns the nose from there, so SAS fights any roll.
- **The gravity turn is in the log.** Every 10 s during the turn KSP.log has an "Ascent turn" line: the program's pitch, the nose's actual pitch and heading, turn and roll rates, throttle, speed and apoapsis.

## 0.6.13 (2026-10-30), test build

- **Rocket plane to 40 km.** A new aircraft mission (Mid tier) and goal: climb on the jets to their ceiling (about 18 to 22 km), light the rockets and zoom to 40 km, then glide home on the jets. Needs a plane with 600 m/s of rocket power (rocket engines or RAPIERs with oxidizer; mission field rocketDeltaV). The crew keeps the rockets off from takeoff until the zoom, switches RAPIERs to closed cycle and back, and earns RocketPlane:Kerbin at 95% of the height. Jets starving of air up high no longer count as an engine failure.
- **RAPIERs counted as rockets too.** A dual-mode engine was read only as a jet, so its rocket delta-v was left out; now it is rated by its rocket mode and the plane still counts as having jets.
- **Launch anything, then pick the mission.** A craft launched straight from the VAB or SPH with a free KSA kerbal aboard now offers every mission it can fly (seats, and the mission's delta-v within 10%), hardest first, up to ten, instead of only a hop or an orbit. A plane on the runway gets the aircraft missions plus Kerbin hops and orbits, and the crew now knows it is flying a plane.
- **Swap rocket says why.** Every saved design that does not fit the mission is listed with the reason (for example "3202 of 3500 m/s"), and the delta-v the mission needs is shown.

## 0.6.12 (2026-10-30), test build

- **Aircraft missions.** Three new missions fly in the air with no rocket: First aircraft flight, Aerial biome survey (Science, 400 km west over grasslands, highlands and mountains) and High-altitude research flight (Science, above 18 km). They fly one of the Director's own SPH planes (cockpit, jets, and wheels or parachutes); the engineers do not design planes. A new flight leg takes off, climbs, keeps clear of the ground ahead, banks to turn, heads home at half the range or half the fuel, and lands under parachutes by the Space Center or hands the Director the runway landing. New achievements Aircraft:, Survey: and HighAir:, three goals (Fly an aircraft, Aerial survey, High-altitude flight; issued once jets, wings and gear are researched) and the Aviator's Wings medal. Jets and wings are now part kinds of their own (Jet, Wing), never used in rockets. The AI Director flies them only in a plane ticked for that mission that carries parachutes.
- **No burning while spinning.** Handed back from the Director past apoapsis, Valentina's circularisation burn opened the throttle each time the spinning craft swept past the horizon and threw the apoapsis to 348 km. The engine now stays off until the nose is within 15 degrees of the horizon and turning under 5 deg/s, steering on the engine only under 10 deg/s, and the burn stops if the apoapsis runs away (over twice the target) with the periapsis still in the air.
- **SAS gets time to work.** The crew re-locked the SAS target every physics frame; now only when the target moves half a degree or a second has passed.
- **Roll is not a tumble.** A rocket rolling about its nose with the nose on the flight path was logged as tumbling (and the fix added fins). Only the nose swinging counts as a tumble now; a fast roll is noted in KSP.log. The attitude log lines show turn rate, roll rate and throttle, and are written during the climb too.
- **Level by 50 km.** The gravity turn now finishes flat at 50 km (was 55 km): 88 x (1 - sqrt(alt / 50 km)).

## 0.6.11 (2026-10-30), test build

- **No paper firsts.** A mission that would earn something the agency has never done in a flight it actually flew (reaching orbit, a flyby, a landing, a spacewalk) now flies live under the Hybrid default and the AI Director's "Firsts live" setting, not only missions marked as a First. Orbital sounding had been simulated and the dice credited an orbit no rocket had flown, while every live orbit attempt was still failing. Simulated achievements no longer count as know-how for this; program modules and rescues keep their usual modes. The AI Director's briefing marks such missions "new ground: flies live".

## 0.6.10 (2026-10-30), test build

- **Old designs are redrawn.** Every engineers' design now carries the revision of the rules it was drawn by (ProceduralDesigner.Revision). On load, a project that is building or on the pad with a design by older rules gets a fresh one (like Swap rocket: half the build time). Raise the revision whenever the design rules change. P7 had flown on 8 fins and fixed Reliants from 0.6.5 rules.
- **The gravity turn corrects a drift west.** The ascent compared the nose with the flight path's angle above the horizon without its direction, so a path leaning west read as leaning east; a rocket on fixed engines that drifted west kept going west and climbed past vertical. The path is now measured in the up-east plane, so the turn pulls it back east.

## 0.6.9 (2026-10-30), test build

- **Engines that steer.** The engineers prefer engines that swivel (gimbal) whenever one is researched: a Kerbin orbit rocket on two LV-T30 Reliants (fixed nozzles) could not turn, climbed nearly straight up and needed 2,182 m/s to circularise. A Reliant is used only when nothing that swivels does the job.
- **Steering on the engine.** A swivelling engine steers only while it burns, so the crew keeps 10% throttle while holding the apoapsis during the turn, and while swinging round to the horizon for the circularisation burn (it had sat at 45 degrees with the throttle closed).
- **Power kept for steering (Kerbalism).** Below 40% electric charge the crew pauses Kerbalism experiments and restarts them above 60%: on a pod with no battery researched, the experiments drained the charge, the reaction wheel went dead and the capsule came in nose first. The attitude log lines now show the charge.

## 0.6.8 (2026-10-30), release

Everything since 0.6.1, from the test builds 0.6.2 to 0.6.7 below. In short:

- **Failures teach.** A live flight that goes wrong records why (tumbled, ran out of fuel, burned up, landed too hard, never reached space) and the next design for that mission fixes it: fins on the second stage, more delta-v margin, bigger parachutes, a heat shield. The cause is the lesson the AI Director and the AI designer read.
- **A smarter AI Director.** It saves money once the year's goals are met, hires only for a role that is short, never repeats a mission that already succeeded unless a goal needs it, and runs one of each mission at a time. Everything it does is in KSP.log too.
- **Rockets that hold together.** Four fins per stage, seated on the tanks; the second stage gets its own set when the first burns out in thick air. A suborbital hop with the delta-v for orbit goes for orbit.
- **Kerbalism.** No 10-minute goo on 4-minute hops; KSP.log says why an experiment will not start.
- **Fixes.** The OpenRouter key no longer gets lost on save (a "//" in the stored key was read as a comment); roster syncs no longer flood the log.

## 0.6.7 (2026-10-30), test build

- **Failures teach.** A live flight that goes wrong now records why, as the flight director saw it: the rocket (or the upper stage) tumbled, with the altitude, speed and spin; it ran out of propellant, and where; a part overheated coming down; the capsule hit the ground too fast, under parachutes that were too small or with none open; or a hop never reached space. The cause is the lesson (in the report, the lessons learned and the AI Director's briefing), saved with the game, and kept over the AI post-mortem's last sentence.
- **The next design fixes it.** The engineers' next rocket for that mission: fins on the second stage after a tumble; 15% more delta-v margin after running dry or falling short of space (up to 55% in all); half as much parachute again after a hard landing; a heat shield after a burn-up, even on a hop (research one if none is available). The AI designer is told the same fixes. The old design of a mission whose last flight failed is not reused. Flights the Director flew by hand teach nothing about the rocket.

## 0.6.6 (2026-10-30), test build

- **Four fins a stage again.** 0.6.4 put eight small fins on light rockets; with fins on the second stage as well, four per stage is enough.

## 0.6.5 (2026-10-30), test build

- **Fins on the tanks, not round the engine.** On a stack of short tanks the fins were hung on the bottom FL-T100 and stuck out below the stage, round the engine bell. Each fin now sits wholly on the stage's tanks, its lower edge level with the bottom of the stage, on whichever tank is there.
- **Fins on the second stage when the first is weak.** The stage above the launch stage gets fins whenever the launch stage has under 1,500 m/s at sea level (it burns out in thick air), not only above solids, as on T's Suborbital hop mk I. The engineers keep them only when the rocket still makes the mission with them.
- **One of each mission at a time.** The AI Director no longer starts a mission that is already under way (it had two Flea test hops running side by side).

## 0.6.4 (2026-10-30), test build

- **Fins where the air is.** The stage above a solid booster gets fins too: solids burn out at 10-15 km and the liquid stage climbs on through thick air (a finless upper stage tumbled at 16 km, spinning at almost 1,000 degrees a second). Eight fins instead of four when four of the biggest fin researched weigh under 2% of the stack they steer.
- **A suborbital hop goes for orbit when it can.** If the rocket's delta-v covers orbit of the same world, the crew flies the orbital ascent, one orbit and reentry instead; the hop still counts once they reach space, and the orbit is earned too. A hop rocket with less (the 1,667 m/s Suborbital hop mk I needs about 3,400 for orbit) flies the hop as before.
- **No goo on short hops with Kerbalism.** Kerbalism's sample experiments (Mystery Goo; tagged "sample") run about 10 minutes and need sample storage, so they stay off suborbital hops; orbital flights still carry them. When a Kerbalism experiment will not start, KSP.log now says why.
- **The AI Director hires only when a role is short.** Pilots for the biggest crew of any open mission plus one to rotate (at least 2), two engineers, two scientists; hires, interviews and recruitment drives for a role that is already staffed are refused, and its briefing shows the staffing.
- **Quieter, lighter roster syncs.** Kerbal added/removed events (the game fired them every frame after a hop reached space) sync the roster at most once a second, and the log line only appears when someone joined or left.

## 0.6.3 (2026-10-30), test build

- **The AI Director saves money once the year is won.** When every goal of the year is met (or already lost), it spends only what the agency earned above the year's opening balance until the budget review: new projects, programs, recruitment drives and hires wait for next year unless they fit. Rescues, resupplies, research and lab work go on. The briefing says how much may be spent; agency.cfg AI_DIRECTOR conserveWhenGoalsMet turns it off.
- **No more pointless repeat flights.** The AI Director no longer flies a mission again once it has succeeded, unless an open goal needs it (an achievement goal it earns, or a goal that counts missions flown). Such missions are listed as "already done" in its briefing, and an idle agency with nothing new to fly is told to research the next parts instead (it had flown three Flea test hops in a row, one fatal).

## 0.6.2 (2026-10-30), test build

- **The OpenRouter key no longer gets lost on save.** The encrypted key was written in a form that could contain "//", which KSP's config reader takes for the start of a comment; about one save in ten cut the key short, it could not be read back ("Invalid length for a Base-64 char array"), and the AI Director quietly fell back to the local model. Keys are now stored without "/" characters; keys saved by older versions still load.
- **A quiet AI Director can be diagnosed.** Everything in the AI Director's log (thoughts, actions, failures, "the model did not answer") is also written to KSP.log as "[KSA] AI Director ...", and a reply that is not valid JSON is logged with its first 200 characters.

## 0.6.1 (2026-10-29), release

Everything since 0.5.10, from the test builds 0.5.11 to 0.5.25 below. In short:

- **Rockets that can fly.** Fins on every launch stage; short, wide stacks (fewer, bigger tanks; no engine wider than its tanks); two-stage suborbital hops, solid then liquid; real decouplers between stages and under the capsule (a heat shield is no longer mistaken for one).
- **Flights that reach orbit.** A proper gravity turn with the nose kept close to the flight path; the apoapsis held through the turn instead of cutting off steep; circularisation along the horizon, centred on apoapsis, only once the nose is there; engines get time to build thrust, so no stage is thrown away full.
- **Crews that come home.** Everything below the capsule is dropped on the way down; parachutes sized for the capsule (no drogue-only landings); a pod-sized heat shield for returns from orbit once one is researched.
- **Fair results.** A suborbital hop counts only if it reached space; a crash that kills the crew is reported as one.
- **The Director in charge.** Take Control on every live flight; swap the rocket on any project before it launches; your own rockets are checked like the engineers' (parachutes, heat shield).
- **Science.** Every crewed rocket carries one of each small instrument researched.

## 0.5.25 (2026-10-29), twenty-sixth test build

- **A real gravity turn.** The crew no longer climbs straight up to 15 km: by then a finned rocket is going 600 m/s and the air holds its nose on the path, so it could not lean and never built sideways speed. Now: straight up to 100 m/s, a small kick east (at most 5 degrees) to 150 m/s, then a curve from the ground, about 77 degrees at 1 km, 50 at 10 km, 33 at 20 km, 23 at 30 km, flat at 55 km. To keep control the nose never leans more than 10 degrees off the flight path below 20 km (8 in the thickest air), opening up as the air thins. Suborbital hops still go straight up.

## 0.5.24 (2026-10-29), twenty-fifth test build

- **No cutoff while still steep.** Below 55 km, an apoapsis at target no longer ends the climb: the crew throttles back to hold it within 10% over target and keeps following the turn, so what the engine adds goes sideways. The engine cuts off once the rocket is up at the end of the turn. (A strong rocket had reached its 75 km apoapsis at about 28 km up and coasted on pointing straight up.)
- **The circularisation burn waits for the nose.** The crew starts turning to the horizon at cutoff and opens the throttle only when the nose is within 15 degrees of it: a rocket with no engine gimbal turns only on its pod's reaction wheel, and burning while still pointing up only raised the apoapsis, to 2,000 km on one flight. The log now records every 10 s how far the nose is from the horizon and how fast it is turning.

## 0.5.23 (2026-10-29), twenty-fourth test build

- **A sharper turn.** Still straight up to 15 km, then a turn that drops quickly and flattens out (44 degrees at 25 km, 26 at 35, 12 at 45, flat at 55) instead of an even one. A strong rocket reached its 75 km apoapsis at about 30 km up while still steep, and the upper stage could not make up the sideways speed.
- **Circularisation along the horizon.** Out of the air the crew turns the nose to the horizon, lights the next engine ahead of time, works out how long the burn takes and starts it so it is centred on apoapsis, then burns along the horizon until the periapsis clears the atmosphere. Before, the burn followed the direction of travel, which still pointed mostly up: it raised the apoapsis to 125 km and stopped there with the periapsis far underground.
- **No stage thrown away full.** Auto-staging no longer reads an engine that was just lit, or a throttle just opened, as dead: the crew had lit the second stage for the circularisation burn and dropped it, full, 1.5 seconds later.
- **The whole science kit.** Every crewed rocket now carries one of each small science instrument researched (thermometer, barometer, goo, radiation counter and the rest, up to ten; labs and the heavy materials bay stay home), not just the lightest one. The AI is told to carry them all and the review adds any it leaves out.

## 0.5.22 (2026-10-29), twenty-third test build

- **Parachutes sized for the capsule.** The engineers no longer snap on one parachute whatever the pod weighs. A main parachute lands about 22 times its own mass softly (a 0.1 t Mk16 or Mk2-R about 2.2 t, a 0.3 t Mk16-XL about 6.6 t); drogues count for nothing. The engineers weigh what comes down (pod, heat shield, instruments, the chutes themselves) and add radial chutes in pairs, or a bigger stack chute, until it lands softly. The design review checks AI designs the same way (adding chutes where it can), the AI is told the rule, and your own rockets show "parachutes too small" when the capsule they bring home is too heavy for them.

## 0.5.21 (2026-10-29), twenty-second test build

- **A crash that kills the crew is reported as one.** When a live craft was destroyed, the flight was graded before the game reported the deaths: "vehicle lost, crew safe", then the pilot listed as killed on an unknown vessel. Everyone still aboard a craft being destroyed is now counted as lost before the report is written.

## 0.5.20 (2026-10-29), twenty-first test build

- **The capsule lets go, however the rocket is staged.** Coming down over Kerbin with nothing burning, the crew now fires the decoupler nearest under the capsule directly, so everything below goes at once. Before, they only staged, and a rocket with that decoupler in the parachute stage (which the crew never fires; they arm chutes directly) came down with its top stage still on. Over other worlds only spent stages are dropped, so a lander coasting down keeps its descent engine.

## 0.5.19 (2026-10-29), twentieth test build

- **A decoupler under every capsule.** The design review now turns back any crewed design whose top stage has no decoupler under the pod and its heat shield: the capsule must be able to drop its last stage before it comes home. (Stack order: pod, heat shield, decoupler, upper stage, decoupler, first stage.)
- **Your own rockets get the same checks.** A saved VAB rocket needs a main parachute (a drogue alone no longer counts) and, for flights home from orbit or beyond once a 1.25 m heat shield is researched, a heat shield at least as wide as the pod (or a pod with its own ablator). Rockets that fail say why under Your designs and in Swap rocket.

## 0.5.18 (2026-10-29), nineteenth test build

- **The circularisation burn is flown.** The crew opened the throttle and, in the same instant, read "no thrust" as empty tanks: the burn was given up with 1,600 m/s still aboard and the flight came home from 84 km. Any burn now gets three seconds for the engine to build thrust, and the next engine is lit first if the coast left none burning.

## 0.5.17 (2026-10-29), eighteenth test build

- **The turn actually turns.** Above 15 km the crew kept the nose within 8 degrees of the flight path, and a fast rocket going straight up hardly bends that way: a Kerbin orbit flight coasted to 84 km at only 386 m/s sideways, far short of orbit. The limit now opens as the air thins: 8 degrees at 25 kPa of dynamic pressure or more, none at 5 kPa or less.

## 0.5.16 (2026-10-29), seventeenth test build

- **Swap the rocket before launch.** Every active project that has not flown has a Swap rocket button in Projects. It lists your saved VAB rockets that fit the mission (delta-v, lift-off thrust, seats) and a fresh design by the engineering team. A project still in design goes straight on to build; one in build or on the pad is rebuilt in half its build time. Any AI design under way is dropped.

## 0.5.15 (2026-10-29), sixteenth test build

- **Stages separate again.** Stock heat shields can decouple themselves (with staging switched off), and the part list counted the small 0.625 m Heat Shield as a decoupler. Being the lightest one, the engineers put it between every pair of stages, so pressing stage dropped nothing. Heat shields are now heat shields, and the design review turns back any stage "decoupler" that is not a decoupler. Approved designs with one are redesigned when the save loads.
- **Real parachutes.** A drogue (Mk25) no longer counts as the landing parachute: it slows a fast capsule but cannot land it. Crewed designs carry a main parachute.
- **Take Control is on by default.** Every live flight shows the Take Control button (`agency.cfg manualOverride = false` hides it again on project flights). A flight you finish is still logged as Director-flown.

## 0.5.14 (2026-10-29), fifteenth test build

- **The turn is flat at 55 km again** (60 km in 0.5.12 and 0.5.13): vertical to 15 km, then an even turn to level at 55 km, which wastes less fuel fighting gravity on the way to orbit.

## 0.5.13 (2026-10-29), fourteenth test build

- **A suborbital hop must reach space.** A live hop that never climbed above the top of Kerbin's air (70 km) earns no Suborbit credit and is an abort, crew safe, with "Space not reached" and its peak altitude in the report. The Suborbit log entry is also only written above the air.
- **Nothing rides the capsule down.** When a craft falls back through the air with nothing burning, the crew now stages away everything below the capsule one stage at a time, fuelled or not, before the parachutes (heat shields stay on). Before, only empty stages were dropped: a hop came down with its fuelled upper stage still attached, fell nose first at 837 m/s and the parachutes could not save the crew.

## 0.5.12 (2026-10-29), thirteenth test build

- **A gentler turn.** The ascent now turns evenly from vertical at 15 km to flat at 60 km (was 55 km), so the rocket stays steeper through the upper air.

## 0.5.11 (2026-10-29), twelfth test build

- **Straight up to 15 km.** The crew now holds the rocket vertical (88 degrees, a touch east) until 15 km, then turns evenly until it is flat at 55 km. A suborbital hop climbs at 88 degrees all the way. Below 15 km the rocket is pushed back upright if it drifts, instead of following its flight path down. A finless Suborbital hop had leaned over right off the pad and was flying flat, heading the wrong way, at 3 km.
- **Fins.** Every rocket the engineers draw up has four fins at the base of its launch stage: the biggest researched fin whose four weigh at most 2% of the rocket (the Basic Fin from day one). AI designs without fins get them added in the design review, and the AI is told to fit them.
- **Short, wide stacks.** At equal weight the engineers now pick fewer, bigger tanks: three FL-T200s, not six FL-T100s. A tank stack more than ten times as tall as it is wide, or an engine wider than its tanks (a Mainsail under 1.25 m tanks), is used only when nothing short and wide does the job. A Mun lander now launches on a Jumbo-64, not seven FL-T800s.
- **Two-stage hops, solid then liquid.** A suborbital hop can now be split between two stages, a solid booster first with a liquid stage above (with stock start parts, a Hammer with fins under an FL-T200 and a Terrier). One stage is used only when two will not do it.
- **Design review fix:** a stage too weak for the next leg is now staged away even when it has not fired yet (the stage below had flown the depot rendezvous on its leftovers), instead of failing the design.

## 0.5.10 (2026-10-29), eleventh test build

- **Orbital fuel depot.** A new program in Kerbin orbit: a tank farm with a docking port on its nose and a keeper aboard. It goes up empty and holds about 64 t (`agency.cfg depotCapacity`).
- **Tankers** are its resupply flights ("Send tanker" in Programs). Each docks, pumps its 16 t of cargo across, swaps its pilot for the keeper and flies the old keeper home. Simulated and live.
- **Refuelling stops.** A flight to another world that the engineers cannot design in one piece now stops at the depot on the way out (`depotForEverything = true` sends every flight beyond Kerbin there).
  - Its upper stages go up with empty tanks, so the launch from Kerbin lifts only empty tanks; the stages below take it to the depot.
  - The crew docks, fills the empty stages, undocks and goes on.
  - The flight waits on the pad until the depot holds the fuel it was designed to take. Projects shows how much and whether it is there.
- **Live and simulated share one fuel figure.** Simulated tankers and departures move the agency's figure, and a live depot's tanks are set to match it. Live pumping is measured and reported back. A live flight refuels live only when the depot itself was flown live.
- **Docking craft are designed for it:** a docking port on the very top, four RCS blocks round the pod, and side parachutes only (a nose parachute would block the port). The craft builder writes the depot's tanks and the stages that fill there as empty.
- **The design review counts the stop:** stages marked to launch empty are filled at the depot, a tanker must still hold its cargo on arrival, and the leg-by-leg check, not the old vacuum total, decides whether the fuel lasts.
- **What it opens:** with every part researched, the crewed Moho landing (about 250 t taken on in orbit, so raise `depotCapacity` and send tankers). Laythe and Tylo landings with the trip home still need more lift than one core engine with side boosters gives.
- Stock templates for the depot core and the tanker, and the AI Director sees the depot's fuel and the flights waiting for it.

## 0.5.9 (2026-10-29), tenth test build

- **A designer that works the mission out leg by leg.** Every mission past a suborbital hop is now a list of burns from the stock delta-v map: lift-off through the thick air, the climb to orbit, transfer, capture, descent, take-off from the other world, the trip home. Each leg has its own thrust need, measured in the gravity of the world it is flown at: a lander has to hover over Tylo, not just carry enough fuel. The figures per world live in `dv_requirements.cfg` (`KSA_DV_BODY`) and can be edited without a build.
- **The engineers draw the rocket first.** Their own designer (no AI) now goes first. It tries every way of sharing the legs among up to six stages, sizes each stage for its legs with 10% to spare, and keeps the lightest rocket. Landing legs go on the stage that touches down, and solid boosters only help climb out of Kerbin's air. With stock parts it designs 61 of the 64 missions: every station, base module and resupply, and crewed landings with the trip home on every world but Moho, Laythe and Tylo. Those three need more lift than one core engine with side boosters gives. The AI designs only what the engineers cannot (`agency.cfg engineersDesignFirst = false` puts the AI first again).
- **The design review stops approving rockets that cannot reach orbit.** Every design, AI or engineers', is flown through the legs on paper, stage by stage:
  - the first stage counts at sea-level performance;
  - lift-off must be 1.3 to 2.6 times the rocket's weight at sea level (the 0.5.x AI rockets left the pad at 4 to 6 and wasted their fuel on drag);
  - a stage too weak for the next leg is dropped, as a pilot would;
  - running short anywhere sends the design back with which leg it failed and by how much.

  Designs approved before, and proven designs about to be reused, are checked again the same way.
- **Base modules fly one way;** a base's "return" module carries the fuel for the trip home. Cloud-city modules float down on their envelopes.
- **Crewed landings can have legs:** landing legs were never offered for crewed landings, so no lander could be drawn up.
- **The AI is told the legs:** the design prompt lists every leg with its delta-v and thrust need, so the AI aims at what it will be checked against.

## 0.5.8 (2026-10-28), ninth test build

- **Rescue craft seat everyone:** a rescue now needs a seat for the pilot and for every stranded kerbal. The engineers' design is checked for it (a bigger pod, or crew cabins added when no researched pod is big enough), and templates with too few seats are not used.
- **Rescue with your own rocket:** the stranded crew's entry in Projects shows the seats and delta-v a rescue needs, and lists every rocket you saved in this game that fits, each with a "Rescue with" button. Your rocket can fly the rescue even when the engineers cannot design one.
- **Save repair:** a crew stranded by a flight that was simulated without a rocket (before 0.5.6) is brought home on load, with no mission, medal or prestige from it, and the project is scrubbed with its budget returned.

## 0.5.7 (2026-10-28), eighth test build

- **Fixed: designs were credited with fuel they could not burn.** The design review added up every tank in a stage, so liquid fuel tanks under a solid booster (with no liquid engine to burn them) counted as propellant, and rockets were approved on delta-v they never had. Tank propellant now counts only when the same stage has a liquid engine; otherwise the tanks are dead weight and the design goes back to the engineers.
- **Better rocket designers:** Claude Haiku 5.5 is the new default design model, and Claude Sonnet 5.5 is listed for the best designs. GPT-4o mini stays in the list as the cheapest option, but its rockets were weak. Your current choice in AI Setup is not changed.

## 0.5.6 (2026-10-28), seventh test build

- **No rocket, no flight.** A design that cannot be assembled in the VAB is never simulated. The engineers first build one by hand from the researched parts; if that cannot be assembled either, the project goes back to design with the failure as a lesson, and when the design attempts run out it is called off with its budget returned. A launch with no craft file is scrubbed and sent back to the engineers the same way.
- **Fixed: a stranded crew broke the Projects window and the AI Director.** Checking whether a rescue rocket can be built crashed (rescue missions are not in the mission catalog), so the Projects window failed to draw and its buttons did nothing, and the AI Director's turn crashed every 5 seconds.
- **Watch Live flights no longer get simulated for want of a rocket:** the design review now rejects a radial (side) decoupler placed between two stages, which the VAB builder cannot assemble (two AI designs with one were approved, and their Mun flyby and Orbital spacewalk Firsts were simulated instead of flown). If a design still cannot be assembled, the engineering team draws one up by hand from the researched parts and that one flies live.
- **The crew finishes the climb to orbit after coasting:** coasting to apoapsis with the engines idle no longer reads as "out of fuel" (it checks the delta-v left in the tanks), so the crew burns to circularise instead of turning for home. When the Director hands back on the way up, or already past apoapsis, the crew circularises from there.
- **A gentler gravity turn:** the crew climbs straight up to 1 km, then turns evenly to horizontal at 45 km (about 72 degrees at 10 km, 49 at 20 km, 29 at 30 km), burns flat until the apoapsis is high enough, coasts to it and circularises there. The old curve was at 53 degrees by 10 km, far too early for a modest rocket.
- **Side boosters that drag the rocket off course are dropped:** if the rocket stays more than 25 degrees off its heading for 3 seconds and the next stage is only radial decouplers, the crew drops the side boosters early, as a pilot would.
- **Orbit means a complete orbit.** A live flight earns orbit only after flying one full orbit in a stable orbit (periapsis above the atmosphere, or 10 km over an airless world); breaking the orbit starts the count again. Captures at the Mun, Minmus and beyond count the same way. Simulated flights claim orbit only on a full success.
- **Fixed: orbit missions held for 600 orbits** (about four days) instead of 600 seconds; they now hold exactly one full orbit (or the custom mission's count).
- **Fixed: taking control cut the engines and fired the chute.** Handing over now leaves the throttle where it was, and the parachute safety net only acts while the crew is flying and no engine is burning.
- **Fixed: missing the main objective was scored a partial success.** A live flight that missed its main objective (a Kerbin orbit flight that peaked at 82 km) counted as a partial success and showed as "done". It is now an abort with the crew safe, and "done" in Projects needs the agency to hold the mission's main achievement.
- **Simulated reports say what was reached and missed** ("Reached: suborbital flight, orbit. Missed: a science return from Kerbin."), since simulated flights count as flown and their achievements meet goals.
- **Fixed: a live flight could stay "in flight" for ever** after its craft was gone (left falling, then removed by the game): the clean-up skipped it because the flight director still held it from the flight scene. Out of flight, a held flight with no vessel is now dropped and the project closed (crew lost, or back on the pad with the crew freed).

## 0.5.5 (2026-10-28), sixth test build

- **The AI Director flies your design only where you ticked it.** It no longer offers or falls back on your saved designs for other missions (it flew one on a Kerbin biome survey it was never picked for).
- **Spaceplane autopilot:** the crew now flies your SPH planes on Kerbin suborbital and orbital missions: runway takeoff, climb on the jets, switch to rockets when the jets fade (multi-mode engines are toggled, separate rockets staged), raise the apoapsis and circularise; coming home, a nose-high reentry and a glide. With parachutes it lands under them by itself; without, the crew hands you the controls on final approach for the runway landing. The AI Director may use planes that carry parachutes.
- **Your planes fly missions:** craft saved in the spaceplane hangar (SPH) appear under "Your designs" for Kerbin suborbital and orbital missions and roll out to the runway; the crew, records and recovery count as usual. A plane fits a mission when its rockets plus a jet-climb allowance reach the mission's delta-v and it has landing gear or a parachute.

## 0.5.4 (2026-10-28), fifth test build

- **Records:** a Records window in the Director's Office. Firsts are credited to the kerbals who flew them (crew, mission, date) or show the rival that got there first; the agency record book keeps highest altitude, fastest flight, farthest from Kerbin, longest mission, and the career leaders for days in space, missions flown and worlds visited. A broken record makes the news (career records when they change hands). Older saves fill in the career records from their service records.
- **Lessons learned for the AI Director:** every flight that is not a clean success leaves one line (design and its delta-v, pilot and stars, outcome, and the key moments from the flight log such as "Propellant exhausted during ascent at 20 km"). The newest 12 stay for the whole game and are in the AI Director's briefing every turn, so it does not repeat a mistake once it drops out of recent results. Older saves are backfilled from their reports.
- **No side tanks, no undroppable side boosters:** the design check rejects fuel tanks placed on the side (nothing can feed from them without fuel lines, yet their fuel was counted) and side boosters when no radial decoupler is researched (they ended up bolted to the core). Saved designs breaking these rules are redesigned on load.
- **Fixed: Minmus orbit was approved with far too little delta-v.** dv_requirements.cfg listed it twice; a second, "from Kerbin orbit" figure (2400 m/s) overrode the 4900 m/s round trip, so a rocket that could not even reach Kerbin orbit was approved. Duplicates removed (Duna orbit, Dres landing, Jool orbit kept at the safer figure), and a test now keeps every other world at or above Kerbin orbit. Approved designs in a save are re-checked against the requirements on load and redesigned if short.

## 0.5.3 (2026-10-28), fourth test build

- **Help brought up to date:** who designs the rockets (AI first, then the engineering team by hand), the design rules, how the crew flies (pre-launch SAS and RCS, pitch program, staging), the parachute safety net, the KSA mission report, and telling the AI Director to fly your design.
- README status and feature list updated.

## 0.5.2 (2026-10-28), third test build

- **Tell the AI Director to fly your design:** in Projects, pick a mission, then tick "AI Director flies this" next to one of your saved designs. Whenever the AI Director starts that mission it flies your rocket (as long as it still fits); its briefing says so. Saved with the game.
- **Built-in designs audited:** the Duna lander had two parachute-only stages between its engine and the pod's separation decoupler, which the crew never stages, so the capsule could not be freed from the lander; its drogues and side chutes now share the main chute's stage (the flight code opens them directly). The other five built-in designs and the Hammer-Flea plan checked clean: every engine staged, side boosters on radial decouplers, a parachute on every crewed return, heat shields on orbital returns, no stacked engines.
- **Staging keeps burning engines:** while any engine still burns, the crew stages only when the next stage drops nothing that is burning; burnt-out side boosters go on their own radial decouplers, and the core keeps firing. Before, the crew dropped the whole first stage, main engine still running, as soon as the side boosters burnt out. The engineering team only puts boosters on the sides when a radial decoupler is researched.
- **Survival net: chutes always arm when falling.** Every physics frame, whatever the flight plan is doing and whoever flies, a craft falling back through an atmosphere on a path that meets the ground drops spent stages and arms its parachutes (forced open below 2.5 km). Before, an ascent that ran dry at 20 km moved on to "On orbit", nothing armed the chutes, and Valentina hit the sea at 198 m/s. The orbit hold is also skipped when the craft is not in orbit.
- **Honest launch-stage sums:** the engineering team counts the launch stage at the mean of sea-level and vacuum efficiency and aims 10% over the requirement, so its rockets carry the fuel they need through the thick lower air.
- **Steering by stock SAS:** when the crew points the craft (pitch program, retrograde for a rookie, docking approaches), KSA now locks stock Stability Assist to the wanted direction instead of working the stick itself; KSP's own controller knows each craft's axes and torque. The old hand steering could guess an axis wrong and drift (a rocket ended up heading northwest and nose-down).
- **One engine per stack:** the design check now rejects plans with several engines or boosters stacked in one column (the upper ones fire into the parts below and KSP blocks their thrust; an AI design stacked four Hammers and crawled to 8 km). Extra boosters go radially. Approved designs in a save are re-checked on load and redesigned if they break the rule.
- **Pre-launch checks:** on the pad the crew switches SAS on (Stability Assist) and RCS on when the craft has thrusters, before the go.
- **Pitch program on ascent:** the crew now holds a set nose angle for the altitude, pointing east (about 78 degrees at 1 km, 53 at 10 km, 26 at 30 km), staying within 8 degrees of the flight path once faster than 150 m/s; suborbital hops hold 85 degrees. Before, a yaw kick and "follow prograde" let heavy, slow rockets topple over (and on some pods kicked them west).
- **Stock parts first:** designers prefer stock parts; Making History parts (like the KV-1 Onion pod) are used only when no stock part does the job. The AI brief marks them [DLC].
- **The engineering team can design by hand.** A rule-based designer stacks the researched engines, tanks, boosters and decouplers into 1 to 4 stages, with the payload the brief demands (pod, parachute, heat shield, instruments, antenna, battery, landing legs), and checks it like an AI design. It decides which missions are open (a mission opens when a rocket can actually be built for it, not because the right kinds of parts exist), takes over when the AI design falls short (no more "called off" for a mission that is possible), and designs rockets when AI is off. Before, a small local model giving up shut Kerbin orbit while Mun and Duna missions showed as open.
- **Your projects are safe from the AI Director:** projects you start (including custom missions) are marked as yours; the AI Director sees them as "started by the human Director" and cannot cancel them. It still runs everything else around them.
- **KSA mission report after recovery:** a small window beside KSP's recovery screen lists what the crew and engineers earned on KSA's ladder (points, total, stars) and the Science paid. KSP's own screen only shows stock experience, which a kerbal repeating a flight does not get.
- **Spent stages are jettisoned when empty:** once every lit engine has burnt out and the next stage only separates (no engine, no parachute), the crew drops it at once, in the coast and reentry legs too. Before, a booster that burnt past the target apoapsis rode along until the craft fell back below 70 km, which also kept the capsule from turning to retrograde.
- **The AI design is part of the design phase:** no separate "waiting for the AI design" status and no time-warp stop; the project shows "engineers finishing the design" until they answer. A project with a fallback design builds that if the AI has not answered 2 real minutes after its design days (agency.cfg `designWaitSeconds`); the late answer is ignored.
- **The AI Director sees the campaign ladder:** its briefing now lists the next open step on each world (Kerbin: suborbital hop; Mun: flyby...), marking firsts a rival already took, so it climbs the ladder when the year's goals allow.
- **Your own rockets fly projects.** Every rocket you save in this game's VAB is listed under "Your designs" in Projects, with its delta-v, liftoff TWR and seats worked out from the craft file. Pick one that fits the mission (enough seats, a parachute, the mission's delta-v and thrust, all parts researched) and the crew flies it; simulated flights score it like an AI design. Take Control is always allowed on your designs. The AI Director sees them too and prefers them when one fits.
- **Every crewed design must carry a parachute.** An AI design with no chute was approved and its pilot died on landing; the design review now rejects a plan without one (base modules that stay on another world excepted).
- **Proven designs are reused.** A new project for a mission that a past AI design flew successfully reuses that design (design phase a quarter as long, no AI call), until new parts are researched or Design Review asks for a redesign.
- **Called-off missions stay shut** until something new is researched, so neither you nor the AI Director keeps paying for designs that cannot work; the Finished list says "called off: no workable design (budget returned)" instead of "cancelled".
- **Flight toolbar button:** the KSD button on the flight toolbar shows or hides the Flight Director window; the crew keeps flying either way, and a pad offer still appears.
- **Fixed: a rocket's main booster could get no stage.** The craft builder decided what was an engine from the researched-parts list; when a part was missing from it (the Hammer on the Hammer-Flea), it was stacked unlit, the first stage fired the decoupler above it, and the flight topped out at 4 km. Engines are now recognised from the part itself, and fins no longer claim an empty stage.

## 0.5.1 (2026-10-28), second test build

- **One pad:** projects that are ready together launch 3 days apart (agency.cfg `launchSpacingDays`); simulated launches and live roll-outs wait their turn, time warp stops only when a live flight's window opens, and Launch now skips the queue.
- README: install steps for the test zip; Kerbalism science notes brought up to date.
- **Rivals fly through the year:** each rival's flights (2 to 6 a year, more for the aggressive ones) are spread across the fiscal year, with firsts, notices and wire stories as they happen; the year-end review only flies any left over. Before, they did everything at the review. Analytics shows when each rival flies next.
- **Service Record stays on top** of the Roster when the Roster refreshes; the Roster is taller on big screens and says when to scroll.
- **Instruments sit on the pod properly:** radial extras (goo, thermometer, antenna) were all placed on one spot and floated off the Mk1 pod's cone at an angle; they now go a quarter turn apart, flush on the slope at mid-height.
- **Batteries with Kerbalism:** crewed AI designs must carry a battery or power part once one is researched (the Z-100 comes with Basic Science); the pod's own charge runs down in minutes and Kerbalism experiments stop with "no Electricity".
- **Automatic RCS:** in space, the crew switches RCS on while the craft is turning or tumbling (spin above about 3 deg/s, or more than 5 degrees off the SAS target) and off again after two steady seconds, to save monopropellant. Docking still takes RCS over itself.
- **No more tumbling capsules:** when a rookie pilot steers by hand (stock SAS gives retrograde hold only from one star), Stability Assist now stays on and stops any spin before the capsule is turned toward retrograde. Before, SAS was switched off and a craft spun past 90 degrees was never brought back.

## 0.5.0 (2026-10-28), first test build

The first build shared outside the dev machine. Everything through the live flight director (M5) is in, plus parts of M6.

- **The agency:** fiscal years, government goals and reviews, Crisis Years, salaries, recruiting with paid interviews, rival agencies, departments.
- **People:** careers and ranks, stars from experience, medals, morale, quitting and retiring, the Hall of Fame. Engineers earn experience when their designs fly and scientists for years in the lab.
- **Projects and programs:** missions from a Flea hop to the outer planets, custom missions, stations, bases and colonies with resupply, rescues of stranded crews.
- **AI engineering (optional):** the engineering team designs every rocket from researched parts through OpenRouter; designs must carry the science instruments researched, a heat shield for returns from orbit, and an antenna with Kerbalism. Ready-made designs are the AI-off fallback.
- **AI Director (optional):** an AI that runs the agency for you.
- **Live flight:** KSA's own flight director flies missions live, runs the experiments aboard and recovers the crew; Take Control hands the flight to you.
- **Science:** lab Science and mission Science go into the stock R&D pool; research gates missions.
- **Kerbalism support (optional):** life support for colonies, live flights and stranded crews; Kerbalism experiments switched on in flight.
- **Help window** in the Director's Office.

Known: most of the newest features have not been flown much in game yet. Report bugs as GitHub issues with KSP.log attached.
