# Changelog

Versions follow `MAJOR.MINOR.PATCH`. Until the public release (milestone M7) the version stays below 1.0: the minor number tracks the roadmap milestone the build has reached, the patch number counts test builds within it.

## Unreleased

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
