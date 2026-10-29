# Changelog

Versions follow `MAJOR.MINOR.PATCH`. Until the public release (milestone M7) the version stays below 1.0: the minor number tracks the roadmap milestone the build has reached, the patch number counts test builds within it.

## Unreleased

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
