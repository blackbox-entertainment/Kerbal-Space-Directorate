# Changelog

Versions follow `MAJOR.MINOR.PATCH`. Until the public release (milestone M7) the version stays below 1.0: the minor number tracks the roadmap milestone the build has reached, the patch number counts test builds within it.

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
