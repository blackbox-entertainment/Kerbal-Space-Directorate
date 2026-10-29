using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Missions;

namespace KSA.Core.Design
{
    /// <summary>
    /// One burn of a mission (2026-10-29): lift-off, transfer, capture, landing, take-off, the trip home. Each has its own
    /// delta-v and its own thrust need in the gravity of the world it is flown at, so a design is checked leg by leg (a
    /// lander that cannot hover in Tylo's gravity fails even when its delta-v sum looks fine).
    /// </summary>
    public sealed class FlightLeg
    {
        public string Name { get; set; }
        public string Body { get; set; }
        public double DeltaV { get; set; }
        /// <summary>Surface gravity of the world the leg is flown at, m/s^2 (thrust-to-weight is measured against it).</summary>
        public double Gravity { get; set; }
        /// <summary>Least thrust-to-weight at the start of the leg in that gravity (0 = any).</summary>
        public double MinTwr { get; set; }
        /// <summary>Most thrust-to-weight at lift-off (0 = no limit): too much thrust in thick air wastes the fuel on drag.</summary>
        public double MaxTwr { get; set; }
        /// <summary>Burned in thick air: counted at the mean of sea-level and vacuum Isp, and thrust at sea level.</summary>
        public bool Atmospheric { get; set; }
        /// <summary>Ends on the ground: the stage that flies it carries the landing legs.</summary>
        public bool Landing { get; set; }
        /// <summary>Docked at the fuel depot: every stage still aboard is filled to the brim (2026-10-29). No delta-v.</summary>
        public bool Refuel { get; set; }
        /// <summary>A tanker's cargo pumped into the depot here, tonnes (2026-10-29). No delta-v.</summary>
        public double FuelDrop { get; set; }

        public FlightLeg() { Name = string.Empty; Body = "Kerbin"; Gravity = 9.81; }

        public override string ToString() { return Name + " " + Math.Round(DeltaV) + " m/s"; }
    }

    /// <summary>A world on the delta-v map: what it takes to get there from low Kerbin orbit, into orbit, down and home.</summary>
    public sealed class BodyData
    {
        public string Name { get; set; }
        public double Gravity { get; set; }
        public bool Atmosphere { get; set; }
        /// <summary>Low Kerbin orbit to an encounter (escape, transfer and half the plane change).</summary>
        public double Intercept { get; set; }
        /// <summary>Encounter to low orbit.</summary>
        public double Capture { get; set; }
        /// <summary>Low orbit to the surface and back up (each way; an atmosphere lowers the way down, see <see cref="Descent"/>).</summary>
        public double SurfaceToOrbit { get; set; }
        /// <summary>Low orbit to a Kerbin reentry.</summary>
        public double Return { get; set; }
        /// <summary>Kerbin's own moons: a flyby comes home on a free return.</summary>
        public bool KerbinMoon { get; set; }

        /// <summary>Low orbit to the ground: parachutes and the air do most of it on a world with an atmosphere.</summary>
        public double Descent { get { return Atmosphere ? Math.Max(250, 0.15 * SurfaceToOrbit) : SurfaceToOrbit; } }

        public BodyData() { Name = string.Empty; }
    }

    /// <summary>
    /// The stock Kerbol delta-v map (vacuum figures, rounded up a little), overridable per world by KSA_DV_BODY nodes in
    /// dv_requirements.cfg. Kerbin's ascent is 3,400 m/s to an 80 km orbit with a sensible gravity turn.
    /// </summary>
    public sealed class DeltaVMap
    {
        public const string NodeName = "KSA_DV_BODY";
        public const double KerbinAscent = 3400;
        public const double KerbinGravity = 9.81;

        private readonly Dictionary<string, BodyData> _bodies = new Dictionary<string, BodyData>(StringComparer.OrdinalIgnoreCase);

        public DeltaVMap()
        {
            //   name       g      atmo   intercept capture surface return  moon
            Add("Mun",     1.63,  false,  860,  310,   580,  310, true);
            Add("Minmus",  0.491, false, 1000,  160,   180,  200, true);
            Add("Eve",    16.7,   true,  1260, 1410,  8000, 1700, false);
            Add("Gilly",   0.049, false, 1900,  410,    30, 1100, false);
            Add("Moho",    2.70,  false, 2970, 2410,   870, 3700, false);
            Add("Duna",    2.94,  true,  1090,  610,  1450,  900, false);
            Add("Ike",     1.10,  false, 1370,  180,   390,  800, false);
            Add("Dres",    1.13,  false, 2080, 1290,   430, 2100, false);
            Add("Jool",    7.85,  true,  2070,  500, 14000, 1700, false);
            Add("Laythe",  7.85,  true,  3160, 1070,  2900, 3350, false);
            Add("Vall",    2.31,  false, 2850,  910,   860, 2900, false);
            Add("Tylo",    7.85,  false, 2630, 1100,  2270, 2850, false);
            Add("Bop",     0.589, false, 3650,  900,   220, 3700, false);
            Add("Pol",     0.373, false, 2740,  820,   130, 2700, false);
            Add("Eeloo",   1.69,  false, 2790, 1370,   620, 3000, false);
        }

        private void Add(string name, double g, bool atmo, double intercept, double capture, double surface, double ret, bool moon)
        {
            _bodies[name] = new BodyData { Name = name, Gravity = g, Atmosphere = atmo, Intercept = intercept, Capture = capture, SurfaceToOrbit = surface, Return = ret, KerbinMoon = moon };
        }

        public BodyData Get(string name)
        {
            BodyData b;
            return name != null && _bodies.TryGetValue(name, out b) ? b : null;
        }

        /// <summary>Reads KSA_DV_BODY nodes; a value left out keeps the built-in figure.</summary>
        public void Read(DataNode n)
        {
            string name = n.GetValue("name", string.Empty);
            if (name.Length == 0) return;
            BodyData b = Get(name) ?? new BodyData { Name = name, Gravity = 1, Intercept = 2000, Capture = 1000, SurfaceToOrbit = 500, Return = 2000 };
            b.Gravity = n.GetDouble("gravity", b.Gravity);
            b.Atmosphere = n.GetBool("atmosphere", b.Atmosphere);
            b.Intercept = n.GetDouble("intercept", b.Intercept);
            b.Capture = n.GetDouble("capture", b.Capture);
            b.SurfaceToOrbit = n.GetDouble("surface", b.SurfaceToOrbit);
            b.Return = n.GetDouble("return", b.Return);
            b.KerbinMoon = n.GetBool("kerbinMoon", b.KerbinMoon);
            _bodies[name] = b;
        }
    }

    /// <summary>Turns a mission into its legs, in flight order.</summary>
    public static class FlightBudget
    {
        /// <summary>
        /// The legs of a mission. <paramref name="moduleRole"/> is the project's program role: base modules and resupplies fly
        /// one way and stay; the base's "return" module is the rocket that waits on the ground, so it carries the fuel home.
        /// Null for a profile with no map (a suborbital hop: one climb, from the requirement entry).
        /// </summary>
        public static List<FlightLeg> For(MissionType type, string moduleRole, DeltaVMap map, bool viaDepot = false)
        {
            if (type == null || map == null || type.Profile == MissionProfile.Suborbital) return null;
            var legs = new List<FlightLeg>();
            // Up from Kerbin: the thick-air half at sea-level performance with a thrust cap, then the climb to orbit.
            legs.Add(new FlightLeg { Name = "lift-off and climb through the air", DeltaV = 1800, MinTwr = 1.3, MaxTwr = 2.6, Atmospheric = true });
            legs.Add(new FlightLeg { Name = "climb to Kerbin orbit", DeltaV = KerbinAscentHigh, MinTwr = 0.6 });

            bool kerbin = string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            if (type.IsTanker)
            {
                // A tanker meets the depot, pumps its cargo across and comes home (2026-10-29).
                legs.Add(new FlightLeg { Name = "rendezvous and docking with the depot", DeltaV = 200, MinTwr = 0.3 });
                legs.Add(new FlightLeg { Name = "pumping the cargo into the depot", FuelDrop = type.CargoFuel });
                legs.Add(new FlightLeg { Name = "deorbit burn", DeltaV = 150, MinTwr = 0.1 });
                return legs;
            }
            if (viaDepot && !kerbin)
            {
                // Tops up at the fuel depot in Kerbin orbit: the stages after this launch empty and fill here (2026-10-29).
                legs.Add(new FlightLeg { Name = "rendezvous and docking with the depot", DeltaV = 200, MinTwr = 0.3 });
                legs.Add(new FlightLeg { Name = "refuelling at the depot", Refuel = true });
            }
            BodyData b = kerbin ? null : map.Get(type.Body);
            string role = string.IsNullOrEmpty(moduleRole) ? type.ModuleRole ?? string.Empty : moduleRole;
            bool oneWay = type.Profile == MissionProfile.Base && !string.Equals(role, "return", StringComparison.OrdinalIgnoreCase);

            if (kerbin || b == null)
            {
                if (!kerbin && b == null)
                {
                    // A world not on the map: a generous guess, so a mission is never under-fuelled for want of data.
                    b = new BodyData { Name = type.Body, Gravity = 3, Intercept = 2500, Capture = 1500, SurfaceToOrbit = 1500, Return = 3000 };
                }
                else
                {
                    if (type.Profile == MissionProfile.Station)
                    {
                        // A core stays in orbit; a module docks and stays; a resupply docks and its crew comes home.
                        if (string.Equals(role, "core", StringComparison.OrdinalIgnoreCase)) return legs;
                        legs.Add(new FlightLeg { Name = "rendezvous and docking", DeltaV = 200, MinTwr = 0.3 });
                        if (!string.Equals(role, "resupply", StringComparison.OrdinalIgnoreCase)) return legs;
                    }
                    legs.Add(new FlightLeg { Name = "deorbit burn", DeltaV = 100, MinTwr = 0.1 });
                    return legs;
                }
            }

            legs.Add(new FlightLeg { Name = "transfer to " + b.Name, DeltaV = b.Intercept, MinTwr = 0.2 });
            if (type.Profile == MissionProfile.Flyby)
            {
                // Kerbin's moons give a free return; the planets need a burn to come back.
                legs.Add(new FlightLeg { Name = "course corrections and the trip home", DeltaV = b.KerbinMoon ? 150 : Math.Max(300, b.Intercept - 950), MinTwr = 0.1 });
                return legs;
            }
            legs.Add(new FlightLeg { Name = "capture into " + b.Name + " orbit", DeltaV = b.Capture, MinTwr = 0.25 });
            if (type.Profile == MissionProfile.Station)
            {
                legs.Add(new FlightLeg { Name = "rendezvous and docking", DeltaV = 200, MinTwr = 0.3 });
                if (!oneWay) legs.Add(new FlightLeg { Name = "the trip home from " + b.Name, DeltaV = b.Return, MinTwr = 0.2 });
                return legs;
            }
            if (type.Floating)
            {
                // A cloud city floats: the air and the airship envelopes bring it down, no landing legs.
                legs.Add(new FlightLeg { Name = "descent into the clouds of " + b.Name, DeltaV = 300, Body = b.Name, Gravity = b.Gravity });
                if (oneWay) return legs;
                legs.Add(new FlightLeg { Name = "climb out of " + b.Name + "'s air", DeltaV = b.SurfaceToOrbit * 0.6, MinTwr = 1.3, Body = b.Name, Gravity = b.Gravity, Atmospheric = true });
            }
            else if (type.IsLanding)
            {
                // Hovering and touching down: at least 1.5x the world's weight (1.3 under parachutes).
                legs.Add(new FlightLeg { Name = "descent to the surface of " + b.Name, DeltaV = b.Descent, MinTwr = b.Atmosphere ? 1.3 : 1.5, Body = b.Name, Gravity = b.Gravity, Landing = true });
                if (oneWay) return legs;
                legs.Add(new FlightLeg { Name = "take-off from " + b.Name, DeltaV = b.SurfaceToOrbit, MinTwr = 1.3, Body = b.Name, Gravity = b.Gravity, Atmospheric = b.Atmosphere });
            }
            legs.Add(new FlightLeg { Name = "the trip home from " + b.Name, DeltaV = b.Return, MinTwr = 0.2 });
            return legs;
        }

        /// <summary>The rest of Kerbin's ascent above the thick air.</summary>
        public const double KerbinAscentHigh = DeltaVMap.KerbinAscent - 1800;

        public static double Total(List<FlightLeg> legs)
        {
            double t = 0;
            if (legs != null) foreach (FlightLeg l in legs) t += l.DeltaV;
            return t;
        }
    }
}
