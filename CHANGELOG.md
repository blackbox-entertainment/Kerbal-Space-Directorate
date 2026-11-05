# Changelog

Versions follow `MAJOR.MINOR.PATCH`. Until the public release (milestone M7) the version stays below 1.0: the minor number tracks the roadmap milestone the build has reached, the patch number counts test builds within it.

## 0.8.225 (2026-11-05), test build

- **A save taken on the way out of Kerbin resumes on the way out.** A quicksave made just after the Eve ejection burn, reloaded, resumed at the ascent: the resume read the escape path's negative apoapsis as "not yet in orbit", and the ascent leg opened the throttle along its pitch program until the Director paused it, 160 m/s later. Only a closed orbit can be still climbing, and the ascent leg now stands down on any escape path.

## 0.8.224 (2026-11-05), test build

The Eve flight's rendezvous with the new station came apart: the first intercept ended 7 km short, the ship raised its periapsis away from the station's orbit, then three "straight pushes" from 9, 10 and 26 km took it to 237 km, burning 32, 82 and 132 m/s, and the crew planned a 462 m/s intercept with a 331 m/s match to follow.

- **The closing push follows the orbit, not a straight line.** It had aimed straight at the station at a speed that gets there in about five minutes (0.8.165, built for the last few kilometres); over those minutes the orbit's curvature bends that line by kilometres. The push now comes from Hill's equations of relative motion about the station's orbit (KSA.Core/Flight/Hill.cs, checked against a two-body integration in the tests), with a transit of two minutes close in and up to a third of an orbit from far out.
- **The leg's periapsis rule follows the station.** It demanded 5 km above the air of every arc, match and orbit, and the new station sits at 86 x 75 km, on that line itself: the first miss had the ship raise its periapsis to 85 km, away from the station, and every match near the station would have been refused as "in the air". The floor is now a kilometre under the station's own low point when that is lower.
- **The crew hold on the pad for the station's pass** (T: "it should have waited to lift off and increase the countdown after I hit launch if it needed time to align with the tanker"). After the Director's go, with a station in orbit, the countdown runs to the moment the station comes round, so the ascent ends beside it and the meeting is a direct intercept of a few m/s instead of a phasing orbit and a chase. The ascent logs how far downrange it ended and where the station was, to tune the two figures the hold uses (380 s to orbit, 40 degrees downrange). Warp on the pad is rails warp.
- **The fuel station's lights are on** (T): switched on when it reaches its station, and again by the crew as they come in to dock, for one parked by an older build.

## 0.8.223 (2026-11-05), test build

- **The crew meet the fullest station.** With two stations above Kerbin, one holding 6 t after the Duna ship took 32.7, the stop went to whichever the game listed first. The log line now says how much the chosen station holds.

## 0.8.222 (2026-11-05), test build

T's goal is the Mk IIIe to every world as far as Duna, with Eve orbit and return. By its own sums the Mk IIIe holds about 4,170 m/s in orbit after the tanker stop. A low Eve orbit costs 1,400 m/s to enter and 1,400 to leave, which with the ejection and the corrections is more than it holds.

- **The power check at distance** (T: "build the power check at distance"). Solar panels make 1/d² of their Kerbin figure at d times Kerbin's distance from the sun: the Mk IIIe's 48 OX-STATs made 5.4 EC/s broadside at Kerbin's distance and 2.4 at Duna's, against 0.3 to 1.2 drawn on the coast; at Dres they would make 0.6, at Jool 0.2. Every design is now read for its panels (tracking panels at their rate, a ring of fixed ones at a third) and its RTGs; the delta-v map knows each world's distance from the sun (dv_requirements.cfg sunDistance); the coast's draw is 0.5 EC/s plus 0.2 a head (agency.cfg cruisePowerBase, cruisePowerPerKerbal). What can this fly?, the Projects window and the pad offer warn in red when the panels make less than the draw at the destination and in yellow when they make less than 1.5 times it. The engineers still fit eight panels whatever the world; RTGs and panels sized by distance are not built.
- **Orbit missions at another planet capture into a loose orbit and round it out high.** At Eve that is about 200 m/s in, 500 to round the orbit at 13,000 km and 600 out, in place of 1,400 and 1,400; the full orbit that counts takes five days there. A moon of Kerbin keeps its low orbit.
- **The long wait is lowered only where Kerbalism gives the world belts.** Kerbin, Jool, Moho and Bop have them; Eve, Duna and Dres do not, and lowering a high orbit there would spend the fuel the loose capture saved.

## 0.8.221 (2026-11-05)

**The first complete, hands-off Duna round trip.** Verdin, Franuki and Billy-Bobmore Kerman flew the Mk IIIe on 0.8.219 from the pad to a splashdown with nobody touching the controls: a stop at the Kerbin fuel station (32.7 t taken on), the 1,111 m/s ejection, a 130 m/s deep-space correction and two trims on the thrusters, the cruise with the panels turned back to the sun every 10 degrees, the 431 m/s capture at 36 km, three aerobraking passes and a round-out to 71 x 69 km, a 5% site in full sun, a 2.8 m/s touchdown, Verdin's walk on the surface, the climb into the plane of the way home, a 463-day wait in Duna orbit for the window, the 559 m/s return burn, two trims, a 27 km entry and a 4 m/s splashdown. 1,141 days, 120 Science, five records, Long Haul medals, two promotions. Nine warp cuts by the game during the wait were each read and warped through.

- **The surface hose ignores flags, debris and rocks.** It had "connected" to the flag Verdin planted 13 m from the lander.
- **The slope reported at touchdown is the ground sampled round the craft**, as the survey reads it. The game's terrain normal had said "-339%" on the 5% site.

## 0.8.220 (2026-11-05), test build

- **The HUD countdown stays live through the turn to the node.** It had kept "Burn in 50 min; panels to the sun" while the navball said 18 minutes.
- **Dates read "Year 12, Day 75 at 3h 11m"** instead of the game's "Day 75 - 3h, 11m".
- **The portrait gallery is found by its full name** after a crew move.

## 0.8.219 (2026-11-05), test build

Verdin, Franuki and Billy-Bobmore Kerman died of CO2 poisoning a day short of Duna's space, the battery flat. The mid-course check nine days out trimmed the path four times; the two trims on the thrusters went straight to their warp at the attitude the last engine burn had left, the sun on the stack's axis and all 48 panels at nothing, and nothing measured it. Kerbalism stopped the warp four times with its warnings and the crew warped on each time.

- **Every wait on the thrusters is spent panels to the sun**, like a wait for an engine burn. The thrusters push from that hold; no turn is needed for a nudge.
- **Every sun hold is measured before the warp.** The panels must make at least 70% of the best a settled hold has made this flight, scaled to the sun's distance (before any best is known, at least what Kerbalism says the ship uses). Out in the sun's own space a poor reading keeps the turn going for up to ten minutes, then the crew hold at 1x and say the panels cannot be brought round. Round a world the night side and the battery cover it, as before. The log says what the panels make, what the ship uses and what was expected.
- **The crew watch the battery.** In deep space, with no burn within twenty minutes, a battery under 30% and falling stops the warp and keeps it stopped; the coast or node wait turns the panels to the sun again and measures; the warp is allowed again once the battery charges or is back over half.
- **A warp cut short by the game or another mod is logged**, and the next warp waits ten seconds, so Kerbalism's warnings can be read.

## 0.8.218 (2026-11-04), test build

- **No more 0.0 m/s burns.** 204 days from Kerbin the trim the path needed was 0.02 m/s. The thrusters cannot push that: each nudge ended in its first frame with nothing done, the leg trimmed three rounds, then planned the same nothing again every minute, and the Director took the stick. Now a trim under the finest nudge (0.05 m/s on the thrusters, 0.3 on the engine) is left for the next correction nearer Kerbin, where the same miss is a real burn.
- **The cruise reads the path the way the correction does.** The coast's own reader saw no Kerbin patch while a zero node did, so it ended at once and the correction placed its trims a minute ahead. Both legs now take the entry time from either reader.
- **A node gone under the warp is planned again**, up to three times, with the line saying when it was due.
- **Taking control moves everyone into the pod.** The Director took the stick with two of the crew in the Hitchhiker for the coast, and the cabin burned on the manual entry with both aboard. Franuki and Billy-Bobmore Kerman.
- **Portraits come back after a crew move.** The kerbals are respawned a frame after the despawn and the portrait gallery is asked to refresh.

## 0.8.217 (2026-11-04), test build

- **The pad offer's rows wrap.** The window chip and the supplies chip were cut off at the edge of the flight director window, and the supplies chip was the one that mattered. Each mission row now wraps onto as many lines as it needs.

## 0.8.216 (2026-11-04), test build

- **Supplies: short is red, tight is yellow.** The pad chip and the VAB line counted supplies at 1.5 times the nominal rate and called anything under that short. Kerbalism's rates are exact (the mk IIIe used 111.6 oxygen a day for three, to the unit, over 572 days); the crews who died of supplies died of counting errors, not of variance. Now a ship whose food, water and oxygen last the trip at the real rate but not at 1.5 x shows yellow with both figures; red means it does not last the trip at all.

## 0.8.215 (2026-11-04), test build

- **A hot arrival skims the air instead of diving.** The mk IIIe took a 2,326 m/s excess path to beat its oxygen (my faster-path search allowed it and asked nothing about the capsule), dived to a 27 km periapsis at 4,187 m/s and burned up at 45 km: Verdin, Franuki and Billy-Bobmore Kerman. Now, above 3,600 m/s at the top of the air (a direct dive's limit; Mun and Minmus returns are 3,100 to 3,300, a plain Duna return about 3,400): the last corrections aim the periapsis at the height the off-game simulator says one skimming pass captures the capsule (36 to 56 km on Kerbin, the highest that does), every drop of propellant goes into a retro burn just above the air, the service section is shed once, and the capsule skims pass by pass with the chutes held until the whole orbit is inside the air; the log says what each pass did, and plainly when a pass fails to catch. The faster-path line now says what its arrival means at the top of the air. Tested in the simulator for the mk IIIe's own arrival; unflown.
- **No path home faster than the capsule can take.** Both faster-path searches (at departure and in deep space) now cap the arrival excess at what a direct entry can survive, 1,555 m/s at Kerbin, instead of the 2,000 and 2,500 I had guessed. When the air aboard cannot be met within that, the crew say so, and the Director decides, rather than the crew choosing between suffocating and burning. The skim is the backstop for an arrival that drifts a little over the limit, not a licence for 4 km/s.

## 0.8.214 (2026-11-04), test build

- The build of 0.8.213: the tag v0.8.213 points at a commit that did not compile (a missing namespace on one line). No change beyond that fix.

## 0.8.213 (2026-11-04), test build

- **Tiny corrections go on the thrusters, no turn.** A 0.10 m/s trim 185 days from Kerbin was left to "the next correction" with the path through Kerbin's centre, because the engine cannot burn under 0.3 m/s cleanly and pointing SAS at a node of no length spins the craft. With thrusters and monopropellant aboard the crew now push along the node from whatever attitude they hold, measuring what they burned, and the correction leg no longer skips such trims. The log says "Nudge started" and "Nudge complete".

## 0.8.212 (2026-11-04), test build

- **A hit is never called "no encounter".** After the deep-space burn toward Kerbin the vessel's own prediction chain showed no Kerbin encounter while a node of 0 m/s at the same spot showed one at -600 km, and the crew halted 173 days out with 293 m/s aboard ("This path misses Kerbin"). The path reader now asks a zero-length node whenever the vessel chain shows nothing, the coast leg's miss check uses the same reader, the first disagreement of a flight is logged with the chain, and the reading after a burn waits a second for the solver instead of running in the frame the burn ended.
- **The deep-space correction honours the air.** When the supplies aboard run out before the plain path lands, the crew look for the cheapest path landing five days before that, within everything aboard including the thrusters less 40 m/s, up to 2,500 m/s arrival excess, and say what they took; when nothing fits they say the crew will not make it on this.
- **The time stamp counts days, always.** The stock compact print dropped the years: a flight two years and a day old read "T+1:00:42", and the log was misread for an afternoon.

## 0.8.211 (2026-11-04), test build

- **The climb off Duna: the kick sets the plane, then the nose follows the air.** 0.8.210 steered the aimed heading on through Duna's thin air, and a tall lander leaned off the airflow at 90 m/s tumbled at 7.8 km and killed Verdin, Franuki and Billy-Bobmore Kerman. That steering is gone. The kick now does the aiming at low speed, where the air cannot argue: the nose leans along the heading and against any sideways drift the lift-off gave, until the horizontal speed is 30 m/s along the heading and within a tenth of that across (25 s at most with air, 10 without), and then the nose follows the air as it always did. The plane is set by then and prograde keeps it. The log says what the kick achieved.

## 0.8.210 (2026-11-04), test build

- **The climb off Duna goes where it is aimed.** The aimed heading of 0.8.209 was never followed: on a world with air the kick lasted 2.5 s on a lander that cannot turn in 2.5 s, then the crew "followed prograde", which was whichever way the lift-off happened to lean. The orbit came out tilted 99.8 degrees, nearly polar, and the ejection home cost 286 m/s more than the tanks held, twice today. Now with air the kick lasts until there is real speed along the heading (20 m/s, or 15 s), thin air (Duna's 6.7 kPa) is steered through like no air at all, and only thick air (25 kPa and up, Laythe and Eve) makes the nose follow the airflow, leaning off it by 8 degrees at most, as the Kerbin climb does. The orbit line now gives the inclination, and the log says how far the way home lies out of the plane when it does.

## 0.8.209 (2026-11-04), test build

- **The climb off another world goes into the plane of the way home.** A climb east from a 40 degree landing site left the mk IIIe in an orbit tilted 40 degrees from the way to Kerbin, and the ejection from it cost 820 m/s against 803 aboard (T: "if it is not ground vs orbit it should not matter if I start from the ground"; it did not, until now). Before lifting off for home, the crew find the next window to Kerbin, work out the heading that puts the orbit in the plane of that departure, and climb on it, so the ejection is the plain prograde burn again. A world's spin is nothing off Duna or the Mun, so any heading costs the same climb. The log says the heading and why. A return from a moon of Kerbin, or a flight that is not going home from here, climbs east as before.

## 0.8.208 (2026-11-04), test build

- **The ejection counts the thrusters and tries a cheaper window before holding.** The mk IIIe held in Duna orbit with 803 m/s in the tanks against an 820 m/s ejection, 168 monopropellant unspent. The affordability check now adds what the thrusters could give (monopropellant at the thrusters' Isp against the craft's mass), the reserve for corrections is 60 m/s (a planned ejection's corrections ran 54 + 4 on the way out), and when the gentlest window still does not fit the crew try the window with the cheapest departure, since a reentry at home does not pay for a faster arrival. The log says which window they took and what rides on the thrusters.

## 0.8.207 (2026-11-04), test build

- **The ejection burn is planned on the orbit as it is, tilted or not.** An ascent from a landing site leaves a parking orbit tilted by the site's latitude. The ejection sums aimed in that plane and threw the out-of-plane part away: the mk IIIe's Duna ejection missed Kerbin by 447,000 km, the 334 m/s deep-space correction ran the tanks dry and then the thrusters, and the crew were 123 m/s short of home. The planner now finds the point on the lap where one burn at periapsis leaves along the wanted direction, plane change and all, and says how much of the burn is the plane change. Tested from level, 25 and 45 degree orbits.
- **No ejection the tanks cannot pay for.** If the ejection plus 100 m/s for the corrections is more than the craft has, the crew hold in orbit and say what is short and what would fix it, instead of leaving on a path they cannot finish.

## 0.8.206 (2026-11-04), test build

- **The crew move between the pod and the habitat as the flight goes** (T). At the start of every coast (cruises, corrections, waits for a window, orbit holds, captures) everyone but the pilot moves into the roomiest habitat aboard, by Kerbalism's living space when it says, else by seats; the pilot stays in the pod so the ship is never without a hand on the controls, and moves too when a probe core is aboard. Before anything that sheds parts, enters air, lands, docks or steps outside (aerobraking, reentry, landing, ascent, docking, surface work) everyone is back in the pod, and the survival net puts them there before it drops a thing. A Kerbalism habitat switched off is never used. The log names who moved where.

## 0.8.205 (2026-11-04), test build

- **The thrusters finish a burn the engine ran dry on.** The mk IIIe's deep-space correction toward Kerbin ran out of propellant 51 m/s short of an encounter with 168 monopropellant aboard, and the crew gave up. Now when the engine runs dry mid-burn and the craft has RCS with over 5 monopropellant, the crew keep the nose on the node and push on the thrusters until the burn is done or the monopropellant is gone, and the log says so. Restart to get it: the resumed correction leg plans the remaining 52 m/s and the executor burns it this way.
- **A self-sufficient base makes its own water and oxygen** (T: "a self sufficient base needs to make fuel oxygen food and water and power"). With Kerbalism a settlement becomes a colony, and a colony stays self-sufficient, only with a drill set to water excavation and a water electrolysis unit (oxygen for the residents, hydrogen for the fuel plant) besides the drill, ore converter, power and greenhouses it already needed. The hardware check reads which drill setup a craft chose and counts electrolysis units; the Programs window names what is missing.
- **Water in the ground counts.** Each base site's water abundance is read from the game's resource map and shown on its site line; a site under the drill's 1% (agency.cfg LIFE_SUPPORT waterMinAbundance) cannot be self-sufficient and the hint says water must be shipped. A base's first landing now looks for ground with water first, at the usual slope limits, and only then settles for dry ground, so Mun bases head for the polar ice and Duna bases are fine anywhere. The site line in the log says what it found.

## 0.8.204 (2026-11-04), test build

- **Nobody is left behind.** A restart on Duna with Verdin on EVA resumed the flight at the ascent and the lander lifted off without him (T: "it really should wait for me to board before returning"). Three locks: on a restart a kerbal on foot within a kilometre sends the flight back to the surface leg, not the ascent; the surface leg takes a kerbal already outside as its own and boards them first; and the ascent never lights while one of ours is on foot within a kilometre, saying so every minute and pointing the Director at the Board button.
- **The Board button reaches 15 m** (T), from 5: a kerbal standing by the lander on the ground boards at a click.

## 0.8.203 (2026-11-04), test build

- Test build of 0.8.202: the warnings test now expects the oxygen line for the mk IIIe's own figures. No code change.

## 0.8.202 (2026-11-04), test build

- **Oxygen counts in every supplies check.** The pad offer, the VAB "What can this fly?" line and the live-flight supply check counted food and water only. The mk IIIe carries 120,561 units of oxygen: 1,080 days for three at Kerbalism's rate, against a 1,150-day Duna round trip. The crew before (T+1,081 days, "thirst" by the report) ran out of air. All three checks now take the smallest of food, water and oxygen and name which runs out first (agency.cfg LIFE_SUPPORT oxygenPerKerbalDay 37.3).
- **The return home arrives before the supplies run out.** The flight director keeps a running figure for when the food, water and oxygen aboard run out at the nominal rate. When the cheapest window home would land after that, the crew plan the cheapest path that lands ten days before it instead, within the fuel aboard less 150 m/s and never arriving faster than 2,000 m/s excess, and the log says what it costs. When no such path fits the fuel they fly the window and tell the Director plainly that the crew will not make it without a resupply.

## 0.8.201 (2026-11-04), test build

- **The meeting with the tanker counts the matching burn.** The rule that sends the crew round a phasing orbit weighed the intercept burn alone against its 120 m/s limit: 99 m/s passed, then the 100 m/s velocity match on arrival burnt out the second stage and ate into the lander, twice today, for a station on nearly the same orbit (T: \"it used to be able to dock with the tanker and still have fuel in the 2nd stage\"). The limit now covers intercept plus match, so a station at a bad phase angle gets a cheap phasing orbit first; the log says what a direct meeting would have cost.

## 0.8.200 (2026-11-04), test build

- **The approach tilt is the fallback moon dodge** (T). When no timing shift under 100 m/s clears a moon across the approach, the crew tilt the approach plane about the vertical instead (same speed and height, so the same periapsis), and only ride through when neither works.

## 0.8.199 (2026-11-04), test build

- **A moon across the approach no longer kills the crew.** The mk IIIe's Duna capture was planned two days out with a 46 km periapsis; on the way in the path ran through Ike's sphere, the pass bent it into the ground, the burn kept its planned time, and the crew warped to it and hit the surface at 632 m/s (Verdin, Franuki and Billy-Bobmore Kerman). Three fixes:
  - **The capture leg looks for a moon across the approach** before planning the burn and shifts the arrival past it: a burn along the velocity changes the timing so the moon has moved on at the crossing, with a horizontal part that keeps the periapsis (under 100 m/s; the aerobraking passes' plane-change dodge does almost nothing on a path falling straight in, as the new test shows). A moon too close or too dear to dodge is ridden through under warp, and the insertion is planned again on the far side.
  - **A path that moved under the planned burn is trimmed back.** After a moon pass, or whenever the periapsis is 20 km or 30% off the planned one, the crew stops the warp and burns radially (out or in, up to 900 m/s) to put it back, then plans the insertion again. A path into the ground on arrival gets the same radial burn instead of a halt.
  - **The burn executor never warps into the ground.** While waiting for any burn, a periapsis under the surface whose ground comes before the burn would end stops the warp and hands the leg the problem.
- **A periapsis counts only where it is really flown.** The patched-conic readers (the course correction's "predicted periapsis", every node score) took the first patch around the target; a patch that ends in a moon's sphere while still descending never reaches its low point, so the correction at the edge of Duna's space trimmed to a 46 km periapsis that did not exist. Such a patch is skipped and the one after the moon is read.

## 0.8.198 (2026-11-04), test build

- **The last kilometre to the tanker is a two-minute push, not five.** Four pushes of 2 to 5 m/s each ended "Closest approach is 1 km, not a meeting" (T: "it never used this much fuel before"): at 85 km the orbit turns 0.2 degrees a second, so a 2 m/s push over five minutes bent a kilometre off its straight line, and a 0.6 s main-engine burn cannot hold 2 m/s to the tenth. Inside 3 km the crew now pushes at 2 to 10 m/s to arrive in about two minutes, and leaves a minute to turn round for the match. The ascent itself was the same as the last mk IIIe flight (cutoff at 85 km); the meeting cost 204 m/s instead of 42 because of where the station happened to be.

## 0.8.197 (2026-11-04), test build

- **What can this fly? counts the crew seated in the editor.** It had counted the mission's crew (2 for a Duna orbit or atmosphere study) and passed a rocket with 3 aboard that the pad then flagged red for supplies. The supplies line now counts the larger of the mission's crew and the kerbals seated in the VAB, and says how many it counted.

## 0.8.196 (2026-11-04), test build

- **A rocket on the pad gets its mission offer while another flight is on record.** The Director visited the fuel station in orbit, whose hangar flight the director resumed, then launched a new rocket from the VAB and saw no offer: the director was still holding the station's flight (T: "tried to launch from the vab and see no director to select a mission"). A craft with no project standing on the home world now gets its offer; the other flight resumes from its project when next visited, as after a restart.
- **Save repair: stale duplicate hangar flights.** A crewless hangar launch (probe, fuel station) left one project per click before 0.8.160 and the extras stayed in flight for ever (ten on Fuel Station mk I). On load, crewless in-flight hangar projects on a vessel whose mission already finished are removed, and on any other vessel only the earliest stays. Logged as "Save repair:" lines.

## 0.8.195 (2026-11-04), test build

- **The surface hose (KSA-90).** A crewed craft landed within 100 m of other landed craft on the same world draws propellant, monopropellant, charge and (with Kerbalism) food, water and oxygen from them at 15 units a second, as if a hose ran across. Donors keep 5% of each tank and 10% of their batteries (agency.cfg SURFACE_LINK). "Hose:" lines in the flight log say what connected and what moved. Only in the flight scene: a lander waiting out a night keeps its flight on screen. T: "I figured 100m would be about the limit of a hose connection."
- **Precision landing beside a base (KSA-85 to 89).** A flight to a world with a base aims 90 m from its centre (agency.cfg landingOffset), the flattest of eight bearings on that ring; the braking burn leans up to 12 degrees toward the aim so the miss closes for almost no fuel; at 2 km the crew looks for the flattest 50 m patch within 150 m of the aim, never inside the base's footprint; a slide that would eat the fuel reserve is given up for the ground underneath when it will do. The Director's picked site gets the same lean and fine search. Unflown.
- **The surface stay is 15 minutes** (agency.cfg surfaceStaySeconds; T, 2026-11-03: no time to plant the flag or run the EVA science), and the HUD has a **Lift off now** button that ends it, or a science sweep, early. The flight test bench keeps its short stay.
- **Cloud cities dock like stations (KSA-91, foundation, unflown).** With HL Airships Core installed, Eve and Jool cloud modules may fly live: entry to the cloud deck, envelopes inflate and climb to the city altitude (programs.cfg cloudAltitude: Eve 18 km, Jool 100 km as a guess), the core anchors there as the city, later modules fly to it on a gentle rocket push and dock. The envelopes are driven by reflection; without the mod the city stays simulated as before.

## 0.8.194 (2026-11-03), test build

- **The supplies check credits a water recycler.** Kerbalism's chosen life-support setups ride in the craft file as pseudo-resources; the check read the part prefab, which is always Scrubber and Pressure control, so a Hitchhiker set to Water Recycler counted for nothing and the new red SUPPLIES line would have cried wolf on the right ship. Each recycler covers one crew member's water at 85% recovery (agency.cfg waterRecyclerRecovery); "What can this fly?" says "(water recycled)" and names the 1.5 x rate it counts at.

## 0.8.193 (2026-11-03), test build

- **Three dead of thirst 73 days short of Kerbin** (2026-11-03, mk IIIe): the first crew to land on Duna and stand, climb out, wait 452 days for the window and burn for home. The supplies check had counted the wait at Duna as a quarter of a synodic period (228 days; the real wait is about 450) and consumption at Kerbalism's nominal rate (the ship drank at twice that on the way home), so a 1,150-day trip passed with 880 days of water.
- **The trip length counts the real wait for the window home**, from the return phasing (out 324, wait about 434, back 324, plus 12 for the arrival at Duna), and **supplies are counted at 1.5 x the nominal rate** (agency.cfg supplySafetyFactor). The pad offer shows "SUPPLIES x of y days" in red when a craft is short; the flight still goes if the Director says so.

## 0.8.192 (2026-11-03), test build

- **Rival agencies keep a believable pace** (T: "how are we getting beaten to Laythe or Jool?"). They flew two to six missions a year straight up the milestone ladder on a dice roll, untouched by windows, trip length or research, and reached Laythe while the Directorate fought real flights. Now a rival climbs no more than one tier past the Directorate's own best first, and a trip to another planet costs it a window: nothing else beyond the moons for two years, succeed or fail. Existing saves keep the firsts already claimed.

## 0.8.191 (2026-11-03), test build

- **The panel re-aim runs only on the cruise through the sun's space.** In orbit of a world the game nudges the attitude every lap, so the 10-degree rule stopped the warp every orbit or two; a 452-day wait at Duna for the window home would have been thousands of stops. In orbit the crew aim once after a burn and leave it; night every lap and the battery cover it, as they always did at Kerbin.

## 0.8.190 (2026-11-03), test build

- **The landing-site picker knows where an air landing comes down.** On a world with air the deorbit aims a periapsis inside it and the ship coasts half an orbit round before braking; the picker assumed a strong lander brakes right after the burn, so it checked the sun and the slope 150 degrees short of the real touchdown: "sun 66 deg up" for a landing in the dark on Duna. The predicted point is now the periapsis of the deorbit ellipse.

## 0.8.189 (2026-11-03), test build

- **The panel cruise worked** (2026-11-03, mk IIIe): re-aims every two weeks, panels making 4.0 EC/s near Kerbin down to 2.4 near Duna, battery full at T+337 d.
- **No burn under 0.3 m/s.** The mid-course trims came out at 0.1-0.2 m/s, below what the Poodle can burn cleanly; pointing SAS at a node of no length set the ship spinning on its direction noise. A node that small is removed unburned, a correction that small counts as on course, and the next correction nearer the target takes what is left.
- **The panel attitude is fixed in space once chosen.** It was recomputed from the ship's attitude of the moment every frame, so with the ship spinning the target spun too and SAS chased it for hours, re-aiming every two minutes. It is now refreshed from the target itself as the sun moves, never from the ship.

## 0.8.188 (2026-11-03), test build

- **The panel re-aim waits for the last turn to settle.** Right after a turn is ordered the panels are still 60-70 degrees off the sun, and 0.8.187 read that as "the sun has moved" every frame: it re-ordered the turn, reset the settle clock and held the warp at 1x on the coast out of Kerbin. The drift check now runs only once the panels have settled on the sun.

## 0.8.187 (2026-11-03), test build

- **The panel re-aim no longer stalls a wait in low orbit.** Below about 100 km the game turns the world with the planet and a craft on rails turns with it, a degree a minute, so the 10-degree rule fired every ten game-minutes and a three-day wait for the ejection crawled at 1x. The re-aim now measures the panels' real pointing error and is off while the game is in that rotating frame; night every half hour and the battery cover low orbit, as on every earlier wait.

## 0.8.186 (2026-11-03), test build

- **Three dead of CO2 poisoning 24 days short of Duna, the third crew lost to power** (2026-11-03, mk IIIe). The panels were turned to the sun and measured at 4.02 EC/s before the cruise; 226 days later the ledger showed 0.02. On rails the attitude is fixed in space while the ship goes round the sun, and a 300-day cruise sweeps about 130 degrees: panels aimed at the start are edge-on by the middle and facing away by the end.
- **The crew turn the panels back to the sun every 10 degrees.** Coasts and long waits for a burn now stop the warp whenever the sun has moved 10 degrees round the sky since the last aim, turn, settle, log the panel output, and warp on; about a dozen stops over a Duna cruise.

## 0.8.185 (2026-11-03), test build

- **First crew landed on Duna after a tanker stop** (2026-11-03, Mk IIe: 2.7 m/s, 0.2 m/s sideways, upright, 4% ground 447 m from the Director's pick), then over on its side: the leg contact spun it 19 deg/s, it leaned to 18 degrees and went. A tall lander on four LT-1 struts, with two empty X200-32 side tanks still bolted on.
- **Empty drop tanks go whatever stage they sit in.** The rule only looked at the next staging group, and that was the chute stage, so the side tanks rode down to the surface. Each radial decoupler holding an empty branch is now fired itself.
- **Tanks that carry landing legs are the undercarriage** (T: four radial tanks as a wide base). They stay on through the descent, the landing and the surface stay, and are dropped once the ship has lifted off and is climbing away.

## 0.8.184 (2026-11-03), test build

- **Aerobraking ends in a round orbit** (T). Once the low point is lifted clear of the air, a retrograde burn at periapsis brings the high point down to the same height; the leg had left the orbit as the last pass made it (239 x 70 km).

## 0.8.183 (2026-11-03), test build

- **The lift out of the air burns at apoapsis.** After six passes at Duna (14,746 down to 161 x 28 km) the lift clear of the air was planned "now, just past the top", because "within an hour of apoapsis" is true everywhere on a 51-minute orbit; it burned near the periapsis, raised the apoapsis instead, and would have repeated every lap. The shortcut now also requires being within a quarter of the orbit past the top.

## 0.8.182 (2026-11-03), test build

- **Aerobraking passes run at 4x physics warp** (T: "slow and boring"). Once the nose is steady on the velocity the crew set 4x through the air, as the landing already does on a steady descent, and drop it if the craft starts to swing. Never rails warp in the air: the game does not simulate drag on rails and can delete a low vessel outright, which is also why the flight must stay on screen through the passes.

## 0.8.181 (2026-11-03), test build

- **A loose capture stays under the innermost moon** (T: a round orbit would have needed no Ike dodge). The capture burn at a world with moons now aims an apoapsis below the moon's periapsis less its sphere of influence: 1,735 km at Duna instead of 14,376, about 64 m/s more than before and out of Ike's reach for good. The air still rounds it; the dodge stays as a backstop.

## 0.8.180 (2026-11-03), test build

- **A burn is never called complete in its first second.** The 9 m/s Ike dodge at Duna "completed" at 0 of 9 one second after it lit (the vector-flip test ran on the orbit as it came off rails) and had to be planned again. The flip test now waits a second after ignition, as it already did after a stage drop.

## 0.8.179 (2026-11-03), test build

- **A crew reached Duna orbit alive** (2026-11-03, Mk IIe from the "duan insert" save: ejection by the count, trims, panels to the sun, mid-course check, 343 m/s capture into 14,746 x 139 km). At the edge of Duna's space seven 1 m/s trims fired before the ship had finished turning to the node, each the wrong way, and walked the periapsis from 36 km to 139 km, out of the air; the aerobraking leg lowers it again at apoapsis.
- **No burn until the ship points at the node.** The executor had lit at the planned second whatever the attitude. It now holds until within 5 degrees, for up to two minutes or the burn's own length, then goes as it is and says so. The wait before a burn spent broadside to the sun ends twenty minutes out instead of ten, time enough for a heavy stack to come round.

## 0.8.178 (2026-11-02), test build

- **Three dead again, 26 days short of Duna, power** (2026-11-02, Mk IIe from the "duan insert" save; the ejection by the count, the trims and the mid-course check all worked, 38 km periapsis). The coast had ordered the broadside turn and started the warp in the same second; the attitude froze half way round and Kerbalism took it for the whole 222-day cruise: 0.01 EC/s from 48 panels.
- **The panels' side faces the sun, roll included, and the warp waits for the turn.** The crew read every panel's normal (Kerbalism's own reading when it is there), work out the one direction the sun should come from in the craft's frame, turn to it on the reaction wheel, wait until the turn has settled (up to two minutes), log "Panels making x EC/s", and only then warp. Same before any long wait for a burn. Broadside with the roll left as it was is the fallback for a craft with no panels.

## 0.8.177 (2026-11-02), test build

- **An ejection burn finishes by the count once the path crosses to escape.** The node's burn vector is read off the orbit the node was placed on; when the ejection crosses to escape that vector drifts and flips, and the executor called the burn complete 223 m/s short (661 of 884; the earlier flight 1,044 of 1,063). From the patch change on, the direction is frozen and the burn runs on the delta-v counted.
- **A spent node is never "resumed".** The ejection's node was left in the plan, the first course correction took it for its own interrupted burn and flew 883 m/s into nothing; the crew then had no Duna encounter and turned for home on 1,708 m/s. A correction now resumes only a node of its own size and time; anything else is cleared and logged. The ejection leg clears its node when it finishes.

## 0.8.176 (2026-11-02), test build

- **Three dead of CO2 poisoning 23 days short of Duna** (2026-11-02, Mk IIe, Kerbalism): the trims worked (0.2, 0.1 and 2.1 m/s to a 37 km periapsis), but the ship then cruised 300 days nose-on to the sun, as the last trim had left it. Its 48 side-mounted panels: 29 at 0% exposure, the rest at 0.1 to 3%. The 1,340 EC battery covered the scrubbers for 320 days and then did not.
- **Long coasts and long waits are flown broadside to the sun.** The coast legs and any wait of more than ten minutes before a burn hold the nose square to the sun so panels on the sides of the stack see it; the turn to the burn comes in the last ten minutes.
- **Low battery sheds the antenna too, and says what is drawing the power.** Below 50% charge the crew now switch off Kerbalism science transmission as well as the experiments, and log Kerbalism's own power ledger ("Power: net -0.12 EC/s: scrubber -0.08, ..."); the same line opens every coast.

## 0.8.175 (2026-11-02), test build

- **Course corrections search small burns first.** Eight days out, the 69 m/s deep-space burn left Duna 4,971 km off and the trim found nothing: it bisected against the whole 400 m/s allowance, at whose far end the encounter 340 days ahead vanishes, so the band was never bracketed. The nudge needed was a fraction of a metre per second. Searches now try caps of 1, 5, 25 and 100 m/s before the full allowance.
- **"No path to the target" means no encounter at all.** A path that still meets the target is flown on to the mid-course check; it had meant "out of band", and the crew spent 875 m/s turning for home eight days into a flight that still met Duna.

## 0.8.174 (2026-11-02), test build

- **Burns never light on rails.** The 43 m/s intercept burn of a fresh Mk IIe flight "started" while the ship was still packed under warp, counted 32 m/s of nothing, and the ship arrived 48 km from the station. The node executor now waits for the unpack before it opens the throttle and counts nothing while packed.
- **The velocity match happens where the paths really come closest, and never into the air.** Matching the station's velocity 48 km away from it was a 145 m/s burn that put the periapsis 596 km underground. The match is now planned at the closest approach (within 25 km, else replanned), and a match that would drop the periapsis into the air is refused.
- **A periapsis in the air is raised before any other plan.** The crew had planned a second 108 m/s intercept from a falling ship.
- **The capsule on its own ends the plan.** When the survival net drops everything under the capsule for reentry, the program jumps to the reentry leg instead of showing "Meeting the tanker, burn in 531 s" under the parachutes.

## 0.8.173 (2026-11-02), test build

- **Refuelled and left for Duna** (2026-11-02, Mk IIe): docked, 29.5 t pumped in five minutes, undocked, 1,063 m/s ejection flown to the metre. The 75 m/s deep-space correction nine days out then left Duna 12,579 km off, the leg said "complete", the coast warped 336 days to the edge of Duna's space, and no 300 m/s correction reached the air from there. The crew's retry then ran the return coast out to nowhere, because entering Duna's space had logged a flyby and the resume read that as a mission done.
- **Corrections trim until the periapsis is in band.** After any correction burn that leaves an encounter out of band, the leg nudges again, up to three rounds, while the lever arm is long.
- **A mid-course check 30 days out.** Interplanetary flights, out and home, cruise to 30 days short of the planet's space, check the path and correct, then coast in and correct once more.
- **Resuming a halted flight reads the mission's own milestone.** A landing counts as done only with a landing or EVA logged, an orbit mission with an orbit, a flyby with a flyby.
- **No path to the target: the crew turns for home.** A deep-space correction that finds nothing within its allowance hands the flight to the homebound correction, which may spend the fuel aboard less 150 m/s, waits up to 200 days for a cheaper window, and weighs the arrival speed in full (from beside Duna: 1,741 m/s now at 5.2 km/s excess, or 2,780 m/s in 200 days at 3.0 km/s). Not yet checked against the supplies aboard.

## 0.8.172 (2026-11-02), test build

- **First hard dock with the Kerbin fuel station** (2026-11-02, Mk IIe, 31.8 of 160 monopropellant; the station turned its port from 148 to 1 deg off in two minutes). The refuel then failed in the same instant as the dock, before the game had merged the two craft, and the retry could not find the tanker because it was part of the ship.
- **Refuelling waits for the dock to settle, takes minutes, and leaves the tanker its deorbit fuel.** The leg waits up to a minute for the merge, pumps 15 units a second of each propellant (about three minutes for two X200-32s), empty-launched tanks first and then the top stages, and keeps 5% of the tanker's dry mass (at least 0.3 t) aboard it (T). A tanker stop that begins already docked goes straight to pumping and undocking.

## 0.8.171 (2026-11-02), test build

- **Thrusters translate only during the dock.** With RCS on, stock SAS fires the thrusters to turn the ship as well, and swinging a 40 t stack onto the port spent 80 monopropellant in three minutes (four Stratus tanks, gone at 39 m). The docking leg now turns off pitch, yaw and roll on every RCS block for the dock and gives them back after; the reaction wheel does the turning. The log shows monopropellant left with every "Station aim:" line and how much the dock used.

## 0.8.170 (2026-11-02), test build

- **Rendezvous accepts any orbit clear of the air.** A retry at 85 x 75 km was refused as "not in a stable orbit" by a check wanting the apoapsis 20 km above the atmosphere; 5 km is enough. First flown dock approach 2026-11-02: the station turned its port from 95 to 1 deg off in two minutes and the ship lined up at 39 m, 0.8 m off axis, then ran out of monopropellant.

## 0.8.169 (2026-11-02), test build

(0.8.168 was tagged with a build error in KSA.KSP and never ran; this is that build, fixed.)

- **The station turns its port toward the ship coming in** (T). Once the ports are picked, the crewless station is steered on its reaction wheel through SAS's rotation lock to point its port at our port, so the approach is straight in. Logged as "Station aim:" lines. Unflown; a station with crew aboard is left alone.
- **Never through the station.** While the ship is behind the port plane, both the approach and the line-up aim for a point beside the station, 60 m off the port axis, then come round to the front.
- **Mismatched ports fail at once.** A Clamp-O-Tron Jr cannot dock to a full-size port; the leg had said "trying anyway" and would have pushed until the timeout.
- **No monopropellant, no dock.** The docking leg stops at once when the thrusters have nothing left instead of drifting about next to the station.

## 0.8.167 (2026-11-02), test build

- **Refuelling fills the top stages first.** After the tanks that launched empty, a tanker tops up stage by stage from the top, and a nearly spent booster still attached gets nothing. The Mk IIe core, 48 t of room with 144 m/s left in it, would have taken most of the station.

## 0.8.166 (2026-11-02), test build

- **RCS only for the last few hundred metres** (T). The rendezvous now pushes on the main engine to a point 200 m short of the station, within 300 m and 2 m/s, before the docking leg takes over on thrusters; the approach on RCS is capped at 2 m/s. It had handed over at 2.5 km and 10 m/s, and 40 monopropellant did not cover it.

## 0.8.165 (2026-11-02), test build

- **Rendezvous closes the last few km.** Inside 25 km the crew push straight at the station (about five minutes, up to 20 m/s) and kill the relative speed on arrival, up to four times; 5 km at 1 m/s had been called a miss and replanned as a fresh 8-orbit phasing, twice, until the leg gave up.
- **A periapsis in the air is raised first.** The rendezvous leg no longer refuses an orbit whose periapsis dips into the atmosphere; it burns at apoapsis to lift it 15 km clear and carries on. On reload such a flight goes back to the tanker stop instead of reentry.
- **Burns carry on through a stage separation.** The node executor had called an 8 m/s burn complete at 0 m/s when the core stage burnt out under it; it now gives the next engine three seconds and keeps burning.

## 0.8.164 (2026-11-02), test build

- **RCS is for docking only** (T). The crew had switched the thrusters on to help the reaction wheels through every turn in space; aligning one 23 m/s burn on the way to the tanker spent 13 of the 40 monopropellant the dock needed. Only the docking leg uses them now.

## 0.8.163 (2026-11-02), test build

- **Rendezvous intercepts stay above the air.** The planner took the cheapest arc to the station without checking where it went; from 102 x 75 km to the Kerbin fuel station it chose a 278 m/s burn whose periapsis was 167 km under the ground, and the Mk IIe crew fell back to Kerbin. Transfers now reject any arc with a periapsis inside the atmosphere plus 10 km (TransferPlanner.Plan minRadius, RendezvousLeg), and a direct intercept over 120 m/s is replaced by a phasing orbit (KSA.Core/Flight/Phasing.cs: a small burn, up to 8 revolutions, then the short intercept when the station comes round). Unflown.

## 0.8.162 (2026-11-02), test build

- **RCS stays off through the climb.** The pre-launch check had switched it on at the pad, so the thrusters fired at every steering move in the air and drained the monopropellant the crew needs to dock (T saw them firing at liftoff). It now comes on in space only when steering or docking needs it.

## 0.8.161 (2026-11-02), test build

- **Crewless craft can take a mission from the pad.** Picking a mission for a probe or fuel station did nothing: the button threw an error because nobody was aboard, and each click left a stray project. Fixed; picking again replaces the earlier pick.

## 0.8.160 (2026-11-01), test build

- **Kerbin fuel station.** A one-launch tanker parks in Kerbin orbit. A crew leaving Kerbin with a port and RCS meets it right after the climb and fills every tank; if the stop fails it holds in orbit rather than leaving on empty tanks. The pad offers planet missions to a craft with empty tanks while a station waits. Unflown.
- **Fuel stations window** in the Office lists every station, at Kerbin or Duna, with a Deorbit button.

## 0.8.159 (2026-11-01), test build

- **Probe missions are back, for every world.** Stock probe cores, no crew, one way: flyby, orbiter and lander for each world, and the three Kerbin probes. Launch any craft with a probe core and nobody aboard and the pad offers them. Unflown beyond Kerbin.
- **Duna fuel tanker.** An uncrewed tanker with a docking port goes ahead to Duna orbit. A crew arriving with a port and RCS meets it after aerobraking, fills up and lands; with no tanker, or if the stop fails, the flight carries on. Empty side tanks stay on until the tanker is met. Unflown.

## 0.8.158 (2026-11-01), test build

- **What can this fly? no longer counts fuel no engine can reach.** Side tanks with no fuel line count as weight only, and the window warns about them.

## 0.8.157 (2026-11-01), test build

- **What can this fly?** Each warning now sits under its own mission; they were above it and read as belonging to the next one.

## 0.8.156 (2026-11-01), test build

- **Moons of other planets can be reached.** A flight to Laythe or Ike goes to the planet, into a high orbit of it, across to the moon, and back the same way. The transfer used to refuse these outright. Unflown.
- **The sea is not flat ground.** Site searches now rule out water, which read as perfectly flat.

## 0.8.155 (2026-11-01), test build

- **Any low fall in aerobraking goes to the landing**, not only a failed climb: a path that meets the ground, low and falling, hands over at once.

## 0.8.154 (2026-11-01), test build

- **A crew falling low in the air lands.** Below half the height of the air and falling, the aerobrake leg stops trying to climb out and hands over to the landing.

## 0.8.153 (2026-11-01), test build

- **Long orbits turn past moons.** On the way down to an air pass the crew checks every moon and, if one is in the way, turns the orbit at apoapsis for a few m/s, keeping the low point where it was. Caught by a moon anyway, they clear its surface and ride through instead of giving up.
- **No more ascents away from home.** A halt near a moon restarted the flight at Ascent to orbit and burned all the fuel; resuming now picks the right leg, and the ascent refuses to run anywhere but the home world.
- Trims near apoapsis no longer repeat 0 m/s burns or slip a whole lap.

## 0.8.152 (2026-11-01), test build

- **An arrival above the air still aerobrakes.** With the low point just over the top of the air on a long orbit, aerobraking was skipped and the landing search found nothing in reach. The crew now brings the low point down at apoapsis.

## 0.8.151 (2026-11-01), test build

- **The mission list shows the window the crew will really take.** It looks for a window in the next 30 days costing up to 30% more, as the crew does, instead of the cheapest date in the whole cycle. The crew now looks 30 days ahead too.

## 0.8.150 (2026-11-01), test build

- **Window dates survive a load.** After loading an earlier save the mission list showed dates from the later game, thousands of days out.

## 0.8.149 (2026-11-01), test build

- **What can this fly? warns (KSA-70, 71).** Under each mission: thrust for that world's gravity, thrust after the first stage, food, water, oxygen for the real trip, batteries, radiation, slow departure burns, heat shield and the KV pod decoupler. Warnings, not refusals.
- **Real trip lengths.** Missions to other planets count the real days out, waiting and back, so the supply check is no longer fooled by the 120 days listed for Duna.
- **"Parachutes for 0.0 t" on KV pods is fixed.** The pod's built-in decoupler no longer counts as dropping the capsule.
- **No more "crew safe" for a dead crew.** Anyone aboard no other craft when a vessel is destroyed is reported lost.

## 0.8.148 (2026-11-01), test build

- **Simulator stories.** The five 2026-10-31 failures, long burns, light and heavy reference rockets, Moho and the climb rules now run in the tests. A 10-minute ejection burn on a Terrier loses nearly half its speed, against 3% for a 2.5-minute Poodle burn.

## 0.8.147 (2026-11-01), test build

- **The whole Duna trip in the simulator.** The climb (AscentRules, AscentSim), long burns (BurnRules), lift-off from Duna (LiftoffSim) and the capsule's reentry now fly in the tests. The crew's pitch program and burn limit run from Core. A round trip adds up to about 4,500 m/s from Kerbin orbit; a 30 km return comes down on the first pass.

## 0.8.146 (2026-11-01), test build

- **Duna descents in the simulator.** The crew's descent rules moved to Core (KSA-77) and fly in the tests on Duna, the Mun and Minmus. The landing engine now waits for 10 km, not 15, and a deorbit aims for 20 km over Duna: about 900 m/s down instead of about 1,220 in the simulator. Going below 8 km ends in a crash.

## 0.8.145 (2026-11-01), test build

- **Strong engines park low.** A low apoapsis burns from 75 km on the way up only if the engine needs that long; a strong engine waits for the top. In the simulator the Mk II ends 89 x 75 km for 197 m/s instead of about 300 x 75 km for 342 m/s, and the weak Terrier still burns early. The start rule now lives in Core (KSA-76).

## 0.8.144 (2026-11-01), test build

- **Duna arrivals tested off-game.** New KSA.Core/Flight/Sim: a pass through the air, fitted to three real Duna passes, and the crew's aerobraking rules moved into Core. Every arrival from 20 to 49 km is flown in the tests. That showed passes at 36 to 40 km taking 12 laps, so a pass over 31 km is now brought down to 30 km.

## 0.8.143 (2026-11-01), test build

- **Empty drop tanks really drop.** The rule stopped at the tanks themselves, because they carry the same stage number as their decouplers.
- **A pass that skims the air is deepened.** Above 40 km on Duna, a small burn at apoapsis brings the low point down to 30 km.

## 0.8.142 (2026-11-01), test build

- **Mission reports wait for the end of a warp.** They no longer open during time warp.
- **An empty agency can hire.** With nobody left on the staff, a hiring freeze no longer blocks recruiting.

## 0.8.141 (2026-11-01), test build

- **A far pass gets a real correction.** An encounter passing far out is now fixed with a fresh transfer out around the sun; Duna had been passed at 25,334 km and captured there for 1,455 m/s.
- **No capture far above the air.** An aerobraking arrival over 1,000 km high stops for the Director rather than spending the fuel.
- **Slow orbits do not wait for ever.** With a lap over two days the site search covers one lap, not eight.

## 0.8.140 (2026-11-01), test build

- **Dead crew stay dead, and the game carries on.** A kerbal lost on a flight, including one you terminate, goes off the game's crew list too, so the VAB cannot seat them. A rocket that still has them aboard is told why no mission is offered, by name, instead of the Flight Director staying silent.

## 0.8.139 (2026-11-01), test build

- **A window just closing is still a window.** After reaching orbit the crew takes a transfer in the next 10 days if it costs up to 30% more excess speed, instead of the cheapest date, which could be two years off.

## 0.8.138 (2026-11-01), test build

- **The window days count down.** The days on the mission buttons update as time passes, with the Projects window open.

## 0.8.137 (2026-11-01), test build

- **Windows in the Projects window.** Missions to other planets show the days to their window on the button, green within 10 days and yellow beyond, and a chosen mission that must wait says so.

## 0.8.136 (2026-11-01), test build

- **Science leaves power for the crew.** Kerbalism experiments now pause below 50% charge and restart only above 90%. With 21 running, the batteries were empty in the dark.

## 0.8.135 (2026-11-01), test build

- **Stopped warp stays stopped.** Pressing the warp stop or slow-down key during the crew's warp holds it off for as long as that warp was to run, so it does not start again by itself. You can still warp by hand.

## 0.8.134 (2026-11-01), test build

- **Your pick, with a warning.** On the pad, missions to other planets show green (good window) or yellow (wait N days). A yellow mission can still be picked: a warning gives the date the crew will leave and the days they will wait, with Launch anyway and Back to the list. Projects still wait for their window. A waiting project's note no longer calls it waiting for fuel.

## 0.8.133 (2026-11-01), test build

- **Missions to other planets wait for their window.** A flight to a planet or one of its moons launches only when the window is 10 days away or less (agency.cfg windowWaitDays), for the Director, the engineers and the AI Director alike. On the pad the mission list greys such a mission and shows the days left. Rescues do not wait.

## 0.8.132 (2026-11-01), test build

- **The crew comes down out of the radiation.** With Kerbalism, an orbit reaching over 130 km with a window more than two days off is lowered to 110 km before the wait. A wait at 197 x 75 km killed all three by day 198. Unflown.

## 0.8.131 (2026-11-01), test build

- **The round-out burn burns.** The runaway rule from 0.8.130 cancelled the small burn at apoapsis as soon as it started, so the crew coasted on through the top of the air.

## 0.8.130 (2026-11-01), test build

- **The parking orbit stays out of the radiation belt.** The circularisation burn now stops when the apoapsis runs 40 km past the target with the periapsis nearly clear of the air, and finishes at apoapsis. A strong engine had pushed the orbit to 390 x 75 km, and with Kerbalism the crew died during the wait for the Duna window.
- **No long wait in the belt.** With Kerbalism, a flight whose orbit reaches over 200 km holds for the Director instead of warping to a window more than two days away.

## 0.8.129 (2026-11-01), test build

- **Help: stiffening and tumbling.** Troubleshooting now explains Autostrut, Rigid Attachment, fin placement and the booster thrust limiter.

## 0.8.128 (2026-11-01), test build

- **The Director works after a revert.** A flight that the reverted save knows nothing about is forgotten, so the rocket on the pad gets its mission list.

## 0.8.127 (2026-11-01), test build

- **The crew drops empty drop tanks.** When the next stage is only radial decouplers and everything they hold is out of fuel, it goes, even with the engine still burning. Unflown.

## 0.8.126 (2026-11-01), test build

- **Handing back on a deorbit path goes to the landing.** It went back to aerobraking and called a full-power climb out of the air at 26 km.

## 0.8.125 (2026-11-01), test build

- **The log follows a lander as it settles.** One "Settling:" line a second after touchdown, to find out why a lander that touched down upright at 3.3 m/s went over on its side.

## 0.8.124 (2026-11-01), test build

- **Warp works on the way down to a world with air.** The coast judged warp by the height the engine would have needed without air, so it cancelled warp from 74 km over Duna.

## 0.8.123 (2026-11-01), test build

- **The Director's site also leaves a long orbit from the high end.** A picked site used its own search, which could still burn at the low end for about 300 m/s.

## 0.8.122 (2026-11-01), test build

- **Long orbits search properly for a site.** Limiting the deorbit to near apoapsis had cut the search to about three moments an orbit, so no flat ground was ever found. It now looks at about 40 an orbit, over a wider stretch around apoapsis.

## 0.8.121 (2026-11-01), test build

- **Long orbits are left from the high end.** The landing's deorbit burn now waits for apoapsis on a long orbit, about 25 m/s from 742 x 70 km where the low end would cost about 300.
- **The safety net works when the Director is flying.** It had been switched off during aerobraking whoever had the controls, so a hand-flown fall on Duna got no chutes or legs.

## 0.8.120 (2026-11-01), test build

- **The climb out of the air climbs.** A crew caught in the air now pitches 35 degrees above the horizon instead of flying along its path, where the thrust only fought the drag.

## 0.8.119 (2026-11-01), test build

- **No thrust in the air on an aerobraking arrival.** The capture burn now runs 80 km above the top of the air, for the same periapsis, and the crew flies the passes nose first. Engine first, with the fins in front, the rocket tumbled.

## 0.8.118 (2026-11-01), test build

- **Aerobraking aims for 35 km.** The last correction has landed 7 km low on every Duna arrival, so an aim of 28 km arrived at 21 km, too deep to rely on.

## 0.8.117 (2026-11-01), test build

- **Let the air slow it down.** On a world with air the landing engine now waits until 15 km above the ground, and the crew falls as fast as the engine can still stop, down to 2 km. The first Duna descent braked at 65 km, drove down at 60 m/s and ran dry at 2,262 m.
- **Legs down, whatever else went wrong.** The safety net lowers the gear under 1,500 m on any fall.
- **A landing out of fuel is not aerobraking.** A restart during a descent no longer sends the crew back to the aerobraking step.

## 0.8.116 (2026-11-01), test build

- **Aerobraking at 29 km.** Passes now aim for 29 km on Duna instead of 18; 27 km did the job on the first arrival, and 10 km on the second destroyed the lander.
- **The survival net leaves the capsule's chutes alone.** It no longer arms chutes during a capture or an aerobraking pass, and away from home it arms only the lander's, unless the ground is under 1,500 m and the craft is still fast.

## 0.8.115 (2026-11-01), test build

- **Antennas fold as soon as the path dips into the air**, not half an hour before it: the half-hour rule from 0.8.113 could fall inside a long warp, when nothing folds. The saves show all four HG-5s went from extended to broken on the first Duna pass.

## 0.8.114 (2026-11-01), test build

- **Aerobraking knows when to stop.** The crew stops when another pass like the last would take too much, and lifts the low point clear of the air at apoapsis. A crew caught with the whole orbit inside the air now climbs out at full power. On the first Duna arrival, one pass took 14,271 km of apoapsis and the second put the lander on the ground.

## 0.8.113 (2026-11-01), test build

- **Antennas fold before the air.** With the path dipping into an atmosphere, antennas and panels now fold half an hour ahead; warp used to run almost to the air, and nothing folds under warp.

## 0.8.112 (2026-11-01), test build

- **The ejection trim no longer needs the game's preview.** The game shows no escape for a node months ahead, so the whole path, out of Kerbin's sphere and across to the target, is now worked out by the two-body sums (Ejection.ExitState).

## 0.8.111 (2026-11-01), test build

- **A burn stops at what it was meant to spend.** Every burn now measures the delta-v really burned and stops 3% over; a 1,047 m/s ejection had run on to 1,567 m/s and gone far past Duna.
- **The ejection trim works.** It now follows the path by the two-body sums from the edge of Kerbin's sphere, because the game's preview does not reach that far ahead of a node.
- **Deep-space corrections say why.** They report when no transfer is found or it costs too much, and they fly on the sums when the preview does not show the encounter yet.

## 0.8.110 (2026-11-01), test build

- **A saved burn is replanned.** An interplanetary burn still to come is planned afresh on load, so an older save picks up the trimmed ejection; only a burn already under way is resumed.

## 0.8.109 (2026-11-01), test build

- **Burns aimed at the planet, not just its orbit.** The ejection burn is now trimmed against the game's own path before it is flown (a perfect 1,047 m/s burn had missed Duna), and a flight with no encounter out around the sun works out a fresh transfer from where it is, instead of giving up. Unflown.

## 0.8.108 (2026-11-01), test build

- **Aerobraking at Duna.** Landings on a thin-air world now aim for an 18 km periapsis, burn only enough to stay captured, let the air shorten the orbit pass by pass (up to 12), then lift out of the air at apoapsis with a small burn. Eve and Laythe, with thick air, still capture by burn alone. Unflown.

## 0.8.107 (2026-11-01), test build

- **Tall rockets get their mission list.** A craft standing on the pad now counts as on the pad whatever its height; a 30 m rocket read 101 m and no offer appeared.

## 0.8.106 (2026-11-01), test build

- **The capsule keeps its parachutes.** A descent through another world's air arms only the chutes that leave with the lander; it used to arm every chute aboard, so a Duna landing would have spent the capsule's and left none for Kerbin.

## 0.8.105 (2026-11-01), test build

- **No false takeovers.** Walking a kerbal on EVA or a key touched while time is compressed no longer takes the craft from the crew. Other keys must be held for a third of a second; stage, full throttle and cut throttle still count on a tap, and the Take Control button always works.
- **Home on the first pass.** Returns from moons and planets aim for a 30 km periapsis (26 to 34 km) instead of 35 km; the Minmus return skipped off the air from 41 km and took two passes.
- **Heat shield first.** Warp now ends 90 seconds before the air, not 5, so the capsule has time to turn retrograde (attitude freezes on rails; it reached the air 125 degrees off).

## 0.8.104 (2026-11-01), test build

- **No rescue that would arrive too late.** A rescue is refused, for the Director and the AI Director alike, when design, build and the flight out take longer than the stranded crew has left.

## 0.8.103 (2026-11-01), test build

- **No touchdown moving sideways.** Below 60 m the crew holds height until drift is under 0.7 m/s, and on weak-gravity worlds it leans further to stop drift.
- **A better landing point estimate** for strong landers, from the shape of the fall, not a quarter of the orbit.
- **Nothing fit to land on within reach:** the crew climbs back to orbit once and chooses again, fuel allowing.
- **A halted flight lets go of the throttle.**
- **Crew stranded asks first,** and stays hidden while a kerbal is outside and just after boarding, so it cannot jump under a click.

## 0.8.102 (2026-11-01), test build

- **4x on the last of a long coast down.** On airless worlds the crew uses physics warp until well before the braking burn, and the Director can too.

## 0.8.101 (2026-11-01), test build

- **No wave-off from the 20 km survey on airless worlds.** The look at 2 km deals with rough ground.
- **After a wave-off the crew plans its next pass again,** with time warp working.

## 0.8.100 (2026-10-31), test build

- **No encounter, no burn.** A transfer burns only when the game shows the path reaching the target. If the plan misses, the crew trims it, tries later orbits, and otherwise stays in orbit with its fuel and tells the Director.

## 0.8.99 (2026-10-31), test build

- **Transfer burns finish the plan.** The stop at the moon's orbit could cut a burn short when the planned path itself ran past the moon; it now allows for the plan.

## 0.8.98 (2026-10-31), test build

- **Transfers wait for a clear path.** If another world sits on the way to the target, as the Mun did on the way to Minmus, the crew stays in its parking orbit and tries later orbits, up to 40.

## 0.8.97 (2026-10-31), test build

- **Pick the site from orbit.** The crew holds its deorbit while the Director is picking, and plans again for any new site chosen before the burn.

## 0.8.96 (2026-10-31), test build

- **Pick landing site.** A button in the Flight Director opens the map; a short right click on the world marks the site and shows its slope and sun. The crew times the deorbit for it, then finds the flattest ground near the click, sliding up to 4 km. It only reaches ground its orbit crosses: no plane change yet, and no guided descent, so 1 km is not promised.

## 0.8.95 (2026-10-31), test build

- **A much flatter climb off airless worlds.** It leans over from 300 m, starts at 40 degrees and lies down as speed builds, keeping 1.5 km clear of the ground ahead.
- **No landing in shadow.** Sites need the sun 30 degrees up, and the look at 2 km tests each spot against the terrain, so a crater wall counts. If everything nearby is dark, the crew goes straight down and says so.

## 0.8.94 (2026-10-31), test build

- **Liftoff from an airless world heads east by the compass.** A yaw kick followed however the lander had rolled, and one flight reached an orbit tilted 113 degrees that no fuel aboard could bring home.
- **A flatter climb off airless worlds,** which should save fuel on the way home.

## 0.8.93 (2026-10-31), test build

- **The crew works out the burn home from a moon by the sums.** It checks one whole orbit, every 30 s and every 1 m/s, and stays within the fuel aboard; the old search through the game's path preview is the fallback.

## 0.8.92 (2026-10-31), test build

- **The descent eases from 5 m/s at 40 m to 2 m/s at 10 m,** because a sudden drop at 10 m left a weak engine no room to slow.

## 0.8.91 (2026-10-31), test build

- **The last 10 m at 2 m/s.** At 5 m/s a lander bounced on the Mun and tipped.
- **The crew holds the lander upright through the bounces.**
- **Nobody steps out of a lander on its side.**

## 0.8.90 (2026-10-31), test build

- **The side-step picks a place.** It ends as soon as the ground underneath is 7% or flatter, after 90 seconds, or when fuel falls to 1,250 m/s.

## 0.8.89 (2026-10-31), test build

- **The 500 m patch really is in now.** 0.8.88 listed it, but the change had not applied.

## 0.8.88 (2026-10-31), test build

- **The final landing spot is judged on a 500 m patch,** sampled every 125 m, so it has room for error.

## 0.8.87 (2026-10-31), test build

- **The side-step starts at 2 km and stops first.** From 400 m a weak lander could not stop, and it slid into the slope. It now checks the ground at 2 km, brakes to a hover, then slides at least 200 m up.

## 0.8.86 (2026-10-31), test build

- **Landing sites are judged on a 1 km patch,** down from 4 km, so a crater floor can count as flat. The look at 400 m now reaches 1 km.

## 0.8.85 (2026-10-31), test build

- **The Director can stop a time warp.** The crew asks for a long warp once, so stopping it by hand holds, and Take control always ends it.

## 0.8.84 (2026-10-31), test build

- **The landing site search finds ground again.** Counting every 1 km step at full value ruled out nearly the whole Mun; one steep step now counts for less, and the look at 400 m handles those.

## 0.8.83 (2026-10-31), test build

- **No landing on anything steeper than 7%.** With no such site the crew stays in orbit and looks again (24 orbits in all), then hands the decision to the Director.
- **A look at the ground at 400 m.** If the ground below is too steep, the crew holds height and slides up to 600 m to the flattest spot nearby.

## 0.8.82 (2026-10-31), test build

- **A wider, stricter search for landing sites.** The crew looks along eight orbits, not one, since the world turns under the orbit, and a site's slope now counts the steepest step across it, so crater walls show up.

## 0.8.81 (2026-10-31), test build

- **Physics warp works during circularisation.** Only rails warp, which stops the engines, is cancelled there now.

## 0.8.80 (2026-10-31), test build

- **Landing sites: flat anywhere in the light.** The crew takes 3% or flatter anywhere with the sun at least 15 degrees up, then 7%, and no longer holds out for morning or high sun.

## 0.8.79 (2026-10-31), test build

- **Landing sites want 3% or flatter,** with 7% accepted when an orbit has nothing better.

## 0.8.78 (2026-10-31), test build

- **Landing sites want 5% or flatter.** 8% is accepted only when an orbit has nothing better, and anything steeper only as a last resort.

## 0.8.77 (2026-10-31), test build

- **No liftoff lying down.** A lander more than 30 degrees off upright stays put and tells the Director, instead of lighting its engine along the ground.

## 0.8.76 (2026-10-31), test build

- **Flat ground comes before morning light.** A site steeper than 8% is taken only if the whole orbit offers nothing better. A 15% site chosen for its morning sun tipped the lander over.

## 0.8.75 (2026-10-31), test build

- **Landings prefer the morning side.** With the sun in the east the crew has the whole day ahead; the afternoon side is used only when an orbit offers nothing else.

## 0.8.74 (2026-10-31), test build

- **The Board button shows within 5 m of a free seat.** At 3 m a kerbal stuck on the ladder could not reach it.

## 0.8.73 (2026-10-31), test build

- **Handing back sticks.** For 3 seconds after Hand back to crew, no key counts as the Director taking over. Above the air, a takeover also closes the crew's throttle, so an engine nobody is minding cannot empty the tanks.

## 0.8.72 (2026-10-31), test build

- **The Board button shows within 3 m of a free seat.**

## 0.8.71 (2026-10-31), test build

- **The Board button shows within 2 m of a free seat,** down from 5 m: halfway up the ladder was too far.

## 0.8.70 (2026-10-31), test build

- **A save with a kerbal outside really keeps the flight.** 0.8.69 looked for the craft before the game had finished loading it.

## 0.8.69 (2026-10-31), test build

- **Loading a save with a kerbal outside keeps the flight.** The Flight Director now finds the craft nearby instead of looking only at the kerbal.

## 0.8.68 (2026-10-31), test build

- **The Board button shows within 5 m of a free seat,** down from 30 m.

## 0.8.67 (2026-10-31), test build

- **A Board button.** A kerbal on EVA within 30 m of a free seat gets a Board button in the Flight Director window, so finding the hatch is no longer needed.

## 0.8.66 (2026-10-31), test build

- **The first kerbal out waits for the lander to settle.** Stepping out in the touchdown frame threw Jeb clear while the lander was still bouncing on its legs.

## 0.8.65 (2026-10-31), test build

- **Landings wait for higher sun.** The crew now looks for ground with the sun at least 50 degrees up, and settles for 15 degrees only if the orbit offers nothing better.

## 0.8.64 (2026-10-31), test build

- **Landers hold 5 m/s to the ground.** The final descent no longer slows to 2 m/s, which burned fuel on weak engines.

## 0.8.63 (2026-10-31), test build

- **Landers stop hovering at 50 m.** A safety reserve read as "too fast" at any speed below 50 m, so the crew went to full throttle, rose, sank and hovered there. Full throttle now needs real speed and too little room to stop.

## 0.8.62 (2026-10-31), test build

- **The sunny landing site allows for weak engines.** A lander that brakes straight after the deorbit comes down close under the burn, not a quarter of an orbit on, so the crew now waits until that ground is in the light.

## 0.8.61 (2026-10-31), test build

- **Landers stay upright in the last metres.** The final hold used the radial marker, which lies sideways on a vertical descent, and laid the lander over into a nose-first crash. It now points straight up, leaning a little against sideways drift, and the engine waits if the craft is more than 45 degrees off.

## 0.8.60 (2026-10-31), test build

- **Long descents run at 4x.** Once a slow descent settles, the crew goes to physics warp and comes back to real time 2 km above the ground. The Director's own physics warp is no longer cancelled above that height.

## 0.8.59 (2026-10-31), test build

- **Landings pick sunny, flat ground.** Before the deorbit burn the crew looks along one orbit, keeps ground with the sun at least 15 degrees up, takes the flattest, warps there and burns.

## 0.8.58 (2026-10-31), test build

- **The low-apoapsis burn is back at 75 km.** It flew well from there; 80 km was never flown.

## 0.8.57 (2026-10-31), test build

- **The low-apoapsis burn starts at 80 km,** up from 75 km, so less of it goes into raising the apoapsis.

## 0.8.56 (2026-10-31), test build

- **The circularisation burn follows the Director's rule.** With a low apoapsis (under 110 km) it starts at 75 km on the way up. If it falls 5 km past apoapsis with the periapsis at 65 km or more, the engine stops and the crew rounds out the orbit at the next apoapsis.

## 0.8.55 (2026-10-31), test build

- **Long circularisation burns stop once clear of the air.** With the periapsis past 70 km and apoapsis behind it, the engine stops, the crew warps to the next apoapsis and rounds out the orbit there, then goes for the Mun or Minmus.

## 0.8.54 (2026-10-31), test build

- **Circularising climbs when it has to.** A periapsis cannot rise above the craft, so a weak stage burning flat at 69 km only pushed its apoapsis out. The nose now rises while the craft is low and not climbing.
- **A full ship is not sent home.** With the periapsis still in the air, the burn runs on to 2,000 km of apoapsis before it gives up.

## 0.8.53 (2026-10-31), test build

- **KV pods keep what is under them.** Their base holds a decoupler; staged on the way home, it threw off the stabilizer and heat shield. Crews now switch it out of the staging before launch.

## 0.8.52 (2026-10-31), test build

- **Circularising no longer gives up just short of orbit.** On a weak stage the long burn lifts apoapsis as well as periapsis; the limit that stopped it was too tight and sent a healthy rocket home from 235 x 67 km. The burn now carries on until apoapsis is 500 km over target.

## 0.8.51 (2026-10-31), test build

- **The pad offers every mission the craft can fly.** The list stopped at the ten hardest, so easier ones such as Minmus landing never showed. It now scrolls.

## 0.8.50 (2026-10-31), test build

- **Transfer burns stop at the Mun.** A long, low-thrust burn could drift off its node and keep going: a 5-minute burn ran 9 minutes and left Kerbin's space. The burn now ends once the path reaches the moon's orbit, and the course correction trims it.
- **No warping on a path that misses.** With no encounter ahead, the crew stops and hands the Director the controls, instead of warping until the oxygen runs out.
- **A steeper climb for weak stages.** The nose now rises sooner to keep the top of the climb 45 s ahead, so a weak upper stage does not skim the air at 42 km.
- **KV pods need a real heat shield** to return from other worlds. Their own ablator is 20 units; a 1.25 m shield holds 200.
- **The fastest-flight record** reads orbital speed above the air (a "record" 18,364 m/s was Kerbin turning under a distant craft).

## 0.8.49 (2026-10-31), test build

- **Landings no longer give up on a closed throttle.** While braking from high up, a closed throttle read as an empty tank; the crew stopped flying and fell onto the Mun with 2,711 m/s aboard. Only an open throttle with no thrust, or no delta-v left, counts now.
- **A halted descent restarts as a descent.** A retry part-way down went back to the course correction; it now picks the landing up again.
- **Three batteries on the lander templates.**

## 0.8.48 (2026-10-31), test build

- **A later level-off.** The climb now flattens by 60 km, up from 50 km, so strong rockets are not flying near orbital speed inside the air.

## 0.8.47 (2026-10-31), test build

- **Heavy rockets keep climbing.** When a low-thrust stage nears the top of its climb, the crew raises the nose as far as the thrust needs, down to 5 km, instead of following the program flat.
- **The Director's lander on Kickbacks.** Four Thumpers burnt out at 45 seconds and left the Skipper too weak to climb. The templates now use four Kickbacks.
- **Staging spam.** A staging attempt that was refused no longer logs as done, every frame.

## 0.8.46 (2026-10-31), test build

- **The Director's lander is now a template.** Mun and Minmus landings can use T's lander, with one pod swap for the crew: KV-1, KV-2 or KV-3 with a stabilizer when Making History is installed, a Mk1 pod with or without a crew cabin when it is not. Design rockets saves one craft of each.

## 0.8.45 (2026-10-31), test build

- **No base parts on rockets.** A 3-seat lander used a Planetary Base Systems airlock as its cabin, stacked on the pod. Those parts stay on bases.

## 0.8.44 (2026-10-31), test build

- **Landers stand on their legs.** The engineers put legs on the top tank of the lander stage, metres above the engine. Legs now go low on the bottom tank, and the longest researched legs are used.

## 0.8.43 (2026-10-31), test build

- **Parking orbits clear the air by 5 km.** The crew now circularises until periapsis reaches 75 km at Kerbin, up from 72 km.

## 0.8.42 (2026-10-31), test build

- **Weak upper stages reach orbit.** A low-thrust stage laid flat at 50 km could top out inside the air, cut off while falling, and coast back down. The crew now keeps the nose up while the top of the climb is close, and if it is falling in the air it keeps burning along the horizon.

## 0.8.41 (2026-10-31), test build

- **Design rockets replaces its own craft.** Each press redraws every "Engineers ..." craft, so there is nothing to delete by hand. Save a craft under another name to keep it.

## 0.8.40 (2026-10-31), test build

- **Rockets with side boosters keep the right way up.** The engineers hung boosters from the bottom tank and set big fins above the weight, so the rocket flipped at the first turn. Boosters now sit alongside the stage, and rockets with them carry no upper-stage fins.
- **Abort.** A rocket tumbling in Kerbin's air for 2 seconds now drops everything under the capsule and arms the chutes, even with engines burning.

## 0.8.39 (2026-10-31), test build

- **Rockets burn oxidizer too.** The engineers could fill a stage with jet-fuel tanks, which hold no oxidizer, so that stage could never fire. Those tanks are now for planes only, and an engine missing one propellant no longer counts as delta-v in What can this fly? Delete saved "Engineers ..." craft and draw them again.

## 0.8.38 (2026-10-31), test build

- **Kerbals on the pad are free.** A reloaded save could list a kerbal as still flying an old test, which blocked the mission list. Anyone seated in a rocket on the pad can now fly it unless they work on a project still on the ground.

## 0.8.37 (2026-10-31), test build

- **The mission list on the pad, really.** Kerbals on the ground team of a flight already in the air counted as busy, so a rocket they sat in was offered no missions. Only crew flying, or working on a project still on the ground, are busy now.

## 0.8.36 (2026-10-31), test build

- **Design rockets works again.** The heat shield rule tried to add a KV pod as a shield, which 0.8.35 had ruled out, so nearly every design failed. Crew cabins are now found with Kerbalism installed, so 3-seat rockets can be drawn.

## 0.8.35 (2026-10-31), test build

- **Stopping a tumble with no reaction wheel.** KV pods have no reaction wheel, so a craft spinning with its engine off never stopped and the circularisation burn never lit. With no wheel or RCS aboard, the crew now steadies it on a little throttle from a swivelling engine.
- **Engineers avoid those pods.** They pick a pod with a reaction wheel when one will do.

## 0.8.34 (2026-10-31), test build

- **The kit T fitted, on every engineers' rocket.** Crewed flights from orbit or beyond get a heat shield, even on a pod with its own ablator. Flights leaving Kerbin also get 8 solar panels and 4 high-gain antennas on the top stage. Delete saved "Engineers ..." craft to have them redrawn.

## 0.8.33 (2026-10-31), test build

- **Shorter, stiffer rockets.** The engineers' stacks may now be 6 times as tall as they are wide per stage, down from 10, which keeps a two-stage rocket near Orbiter mk III's proportions. Their craft also use autostrut and rigid attachment. Designs not yet flown are redrawn; saved "Engineers ..." craft must be deleted to be drawn again.

## 0.8.32 (2026-10-31), test build

- **Build a rocket, then pick a mission.** Kerbals left assigned to a stopped flight test blocked the mission list on the pad with no message. Leftover test projects are now cleared in sandbox saves, and the pad says why when no missions can be offered.
- **Engineers craft read their fuel.** Saved engineers' rockets showed 0 m/s to KSA, so no mission fitted them. Tanks with no fuel listed in the craft file now count as full.

## 0.8.31 (2026-10-31), test build

- **Design rockets (research 100 or less).** A new Flight tests button has the engineers draw the Mun, Minmus and planet missions from parts in tech nodes of 100 Science or less, saves each as an "Engineers ..." craft in the VAB, and writes a table to PluginData/FlightTests/designs.txt. Those craft can be opened, edited and flown on the bench.
- **Crew cabins.** With no two- or three-seat pod available, the engineers add Mk1 Crew Cabins to a Mk1 pod.

## 0.8.30 (2026-10-31), test build

- **Landers seat three.** The engineers draw a landing mission's rocket with room for three whenever a three-seat pod is researched and the rocket can carry it; otherwise for the crew the mission needs. Designs not yet flown are redrawn. Your own craft still need only the mission's crew.

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
