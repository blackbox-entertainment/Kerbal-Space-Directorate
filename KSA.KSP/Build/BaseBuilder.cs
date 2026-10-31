using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KSA.Core;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.Build
{
    /// <summary>
    /// Surface bases that look like bases (2026-10-30, T: "I want it to look like an actual base on a planet"; "bases grow
    /// with subsequent flights to them ... then they grow on their own"). The agency says which sections a base has
    /// (<see cref="Agency.BaseSectionsFor"/>); this builds them as one connected vessel of Kerbal Planetary Base Systems
    /// parts (stock parts where the mod is missing or a part is not researched yet) and stands it on a flat spot at the
    /// site. When the layout changes the old vessel is taken away and the bigger base put in its place. Only at the Space
    /// Center or the Tracking Station, where no base is loaded. The base vessel carries no crew: the agency keeps its
    /// residents.
    /// </summary>
    public static class BaseBuilder
    {
        private sealed class Piece
        {
            public AvailablePart Ap;
            public string Id;
            public Vector3 Pos;
            public Quaternion Rot = Quaternion.identity;
            public Piece Parent;
            public string ParentNode = string.Empty, OwnNode = string.Empty;
            public bool Radial;
            public readonly List<Piece> Children = new List<Piece>();
        }

        /// <summary>An open end a section can attach to: the part, its node, where the node is and which way it faces.</summary>
        private sealed class Arm
        {
            public Piece Last;
            public string Node;
            public Vector3 At, Dir;
            public bool Closed;
        }

        private static double _lastTick;

        /// <summary>From the core tick: rebuilds at most one base whose layout (or the parts it would use) changed.</summary>
        public static void Tick(Agency agency)
        {
            if (agency == null || HighLogic.CurrentGame == null || FlightGlobals.Bodies == null) return;
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION) return;
            double now = Time.realtimeSinceStartup;
            if (now - _lastTick < 5) return;
            _lastTick = now;
            foreach (Program g in agency.Programs)
            {
                List<BaseSection> sections = agency.BaseSectionsFor(g);
                string key = sections.Count == 0 ? string.Empty : BaseLayout.Key(sections) + "|" + PartsKey();
                if (key == g.BaseLayoutKey) continue;
                try { Rebuild(g, sections, key); }
                catch (Exception e)
                {
                    KsaCore.Log.Error("Base " + g.Name + " could not be built: " + e);
                    g.BaseLayoutKey = key; // not again until something changes
                }
                return;
            }
        }

        // ------------------------------------------------------------------ parts

        private static AvailablePart Usable(string name)
        {
            AvailablePart ap = PartLoader.getPartInfoByName(name);
            if (ap == null || ap.partPrefab == null) return null;
            if (ResearchAndDevelopment.Instance != null && !ResearchAndDevelopment.PartModelPurchased(ap)) return null;
            return ap;
        }

        private static AvailablePart First(params string[] names)
        {
            foreach (string n in names) { AvailablePart ap = Usable(n); if (ap != null) return ap; }
            return null;
        }

        private static AvailablePart PartFor(BaseSection s)
        {
            switch (s)
            {
                case BaseSection.Core: return First("KKAOSS_Control_g", "KKAOSS_Habitat_MK1_g", "crewCabin");
                case BaseSection.Habitat: return First("KKAOSS_Habitat_MK2_g", "KKAOSS_Habitat_MK1_g", "crewCabin");
                case BaseSection.Lab: return First("KKAOSS_Science_g", "Large_Crewed_Lab");
                case BaseSection.Greenhouse: return First("KKAOSS_Greenhouse_g") ?? PartFor(BaseSection.Habitat);
                case BaseSection.Industry: return First("KKAOSS_ISRU_g", "ISRU");
                case BaseSection.Power: return First("KKAOSS_Nuclear_Reactor"); // otherwise solar panels on the base
                default: return null;
            }
        }

        private static AvailablePart Hub() { return First("KKAOSS_corridor_4"); }
        private static AvailablePart Leg() { return First("KKAOSS_Landing_Leg_g"); }

        /// <summary>The best researched solar panel that fixes to a surface.</summary>
        private static AvailablePart SolarPanel()
        {
            AvailablePart best = null;
            float bestRate = 0;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                if (ap == null || ap.partPrefab == null || ap.partPrefab.srfAttachNode == null || !ap.partPrefab.attachRules.srfAttach) continue;
                ModuleDeployableSolarPanel sp = ap.partPrefab.FindModuleImplementing<ModuleDeployableSolarPanel>();
                if (sp == null || Usable(ap.name) == null) continue;
                if (sp.chargeRate > bestRate) { bestRate = sp.chargeRate; best = ap; }
            }
            return best;
        }

        /// <summary>Which parts the base would use today: research of a better part rebuilds it.</summary>
        private static string PartsKey()
        {
            var sb = new StringBuilder();
            foreach (BaseSection s in new[] { BaseSection.Core, BaseSection.Habitat, BaseSection.Lab, BaseSection.Greenhouse, BaseSection.Industry, BaseSection.Power })
            { AvailablePart ap = PartFor(s); sb.Append(ap != null ? ap.name.Length.ToString(CultureInfo.InvariantCulture) : "-"); }
            sb.Append(Hub() != null ? "H" : "-").Append(Leg() != null ? "L" : "-");
            AvailablePart solar = SolarPanel();
            sb.Append(solar != null ? solar.name : "-");
            return sb.ToString();
        }

        private static AttachNode Node(AvailablePart ap, string id)
        {
            if (ap == null || ap.partPrefab == null || ap.partPrefab.attachNodes == null) return null;
            foreach (AttachNode n in ap.partPrefab.attachNodes) if (n != null && n.id == id && n.nodeType == AttachNode.NodeType.Stack) return n;
            return null;
        }

        // ------------------------------------------------------------------ layout
        // Built in a base frame with Z up and the ground in X-Y; turned to the editor's Y-up frame for the craft file.
        // KPBS modules lie on their sides: their top and bottom nodes (local Y) join end to end, "front" faces the ground
        // (a foot goes there) and "back" faces the sky.

        private static List<Piece> Layout(List<BaseSection> sections, out string why)
        {
            why = null;
            var all = new List<Piece>();
            int uid = 1000000;
            Func<AvailablePart, Piece> make = ap => { var p = new Piece { Ap = ap, Id = ap.name + "_" + (uid++).ToString(CultureInfo.InvariantCulture) }; all.Add(p); return p; };

            AvailablePart coreAp = PartFor(BaseSection.Core);
            if (coreAp == null) { why = "no crew module researched to build it from"; return null; }
            AvailablePart hubAp = Hub(), legAp = Leg(), solarAp = SolarPanel();
            var arms = new List<Arm>();
            var modules = new List<Piece>();
            if (hubAp != null)
            {
                // A corridor junction in the middle, four arms out from it.
                Piece hub = make(hubAp);
                foreach (string n in new[] { "right", "left", "top", "bottom" })
                {
                    AttachNode an = Node(hubAp, n);
                    if (an != null) arms.Add(new Arm { Last = hub, Node = n, At = an.position, Dir = an.orientation.normalized });
                }
                Attach(arms[0], make(coreAp), modules);
            }
            else
            {
                // No junction yet: a line, out from both ends of the core.
                Piece core = make(coreAp);
                modules.Add(core);
                AttachNode top = Node(coreAp, "top"), bottom = Node(coreAp, "bottom");
                if (top != null) arms.Add(new Arm { Last = core, Node = "top", At = top.position, Dir = Vector3.up });
                if (bottom != null) arms.Add(new Arm { Last = core, Node = "bottom", At = bottom.position, Dir = Vector3.down });
            }
            if (arms.Count == 0) { why = coreAp.name + " has no nodes to build on"; return null; }

            int next = hubAp != null ? 1 : 0;
            int panels = 0;
            for (int i = 1; i < sections.Count; i++)
            {
                AvailablePart ap = PartFor(sections[i]);
                if (ap == null)
                {
                    // Power without a reactor: solar panels on the base. Anything else not researched is left out for now.
                    if (sections[i] == BaseSection.Power && solarAp != null && panels < 6)
                    {
                        Piece host = modules[Math.Min(modules.Count - 1, panels / 2)];
                        AttachPanel(host, make(solarAp), panels % 2 == 0 ? 0.6f : -0.6f);
                        AttachPanel(host, make(solarAp), panels % 2 == 0 ? -0.6f : 0.6f);
                        panels += 2;
                    }
                    continue;
                }
                Arm arm = null;
                for (int k = 0; k < arms.Count && arm == null; k++) { Arm a = arms[(next + k) % arms.Count]; if (!a.Closed) { arm = a; next = (next + k + 1) % arms.Count; } }
                if (arm == null) break;
                Attach(arm, make(ap), modules);
            }

            // Feet under every module that has a place for one.
            if (legAp != null)
            {
                AttachNode legTop = Node(legAp, "top");
                foreach (Piece m in modules)
                {
                    AttachNode front = Node(m.Ap, "front");
                    if (front == null || legTop == null) continue;
                    Piece leg = make(legAp);
                    leg.Rot = m.Rot * Quaternion.AngleAxis(-90f, Vector3.up); // the foot's node (+X) faces up into the module
                    leg.Pos = m.Pos + m.Rot * front.position - leg.Rot * legTop.position;
                    leg.Parent = m; leg.ParentNode = "front"; leg.OwnNode = "top";
                    m.Children.Add(leg);
                }
            }

            // Into the editor frame: the base's up (Z) becomes the craft's up (Y).
            Quaternion toCraft = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            foreach (Piece p in all) { p.Pos = toCraft * p.Pos + new Vector3(0, 10f, 0); p.Rot = toCraft * p.Rot; }
            return all;
        }

        /// <summary>Joins a module end-on to the arm's open node, lying along the arm, and moves the arm's end past it.</summary>
        private static void Attach(Arm arm, Piece m, List<Piece> modules)
        {
            AttachNode bottom = Node(m.Ap, "bottom"), top = Node(m.Ap, "top");
            if (bottom == null) { arm.Closed = true; return; }
            float angle = Mathf.Atan2(-arm.Dir.x, arm.Dir.y) * Mathf.Rad2Deg; // turns the module's +Y onto the arm
            m.Rot = Quaternion.AngleAxis(angle, Vector3.forward);
            m.Pos = arm.At - m.Rot * bottom.position;
            m.Parent = arm.Last; m.ParentNode = arm.Node; m.OwnNode = "bottom";
            arm.Last.Children.Add(m);
            modules.Add(m);
            if (top == null) { arm.Closed = true; return; }
            arm.Last = m; arm.Node = "top"; arm.At = m.Pos + m.Rot * top.position;
        }

        /// <summary>A solar panel on the sky side of a lying module, <paramref name="along"/> metres from its middle.</summary>
        private static void AttachPanel(Piece host, Piece panel, float along)
        {
            AttachNode srf = panel.Ap.partPrefab.srfAttachNode;
            AttachNode back = Node(host.Ap, "back");
            float radius = back != null ? back.position.magnitude : 1.25f;
            Vector3 axis = host.Rot * Vector3.up;
            Vector3 normal = Vector3.forward; // the sky
            Vector3 orient = srf.orientation.sqrMagnitude > 0 ? srf.orientation.normalized : new Vector3(1, 0, 0);
            // As the editor does a surface attach: LookRotation(surface normal, host up) * LookRotation(node orientation, up).
            panel.Rot = Quaternion.LookRotation(normal, axis) * Quaternion.LookRotation(orient, Vector3.up);
            Vector3 point = host.Pos + normal * radius + axis * along;
            panel.Pos = point - panel.Rot * srf.position;
            panel.Parent = host; panel.ParentNode = "srf"; panel.OwnNode = "srfAttach"; panel.Radial = true;
            host.Children.Add(panel);
        }

        // ------------------------------------------------------------------ craft file

        private static string Craft(List<Piece> parts, string shipName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ship = " + shipName);
            sb.AppendLine("version = " + Versioning.GetVersionString());
            sb.AppendLine("description = A surface base of the Kerbal Space Directorate.");
            sb.AppendLine("type = VAB");
            sb.AppendLine("size = 20,10,20");
            sb.AppendLine("persistentId = " + NewId());
            sb.AppendLine("rot = 0,0,0,1");
            sb.AppendLine("missionFlag = Squad/Flags/default");
            sb.AppendLine("vesselType = Base");
            foreach (Piece p in parts)
            {
                sb.AppendLine("PART");
                sb.AppendLine("{");
                sb.AppendLine("\tpart = " + p.Id);
                sb.AppendLine("\tpartName = Part");
                sb.AppendLine("\tpersistentId = " + NewId());
                sb.AppendLine("\tpos = " + V(p.Pos));
                sb.AppendLine("\tattPos = 0,0,0");
                sb.AppendLine("\tattPos0 = " + V(p.Parent != null ? p.Pos - p.Parent.Pos : p.Pos));
                sb.AppendLine("\trot = " + Q(p.Rot));
                sb.AppendLine("\tattRot = 0,0,0,1");
                sb.AppendLine("\tattRot0 = " + Q(p.Rot));
                sb.AppendLine("\tmir = 1,1,1");
                sb.AppendLine("\tsymMethod = Radial");
                sb.AppendLine("\tautostrutMode = Off");
                sb.AppendLine("\trigidAttachment = False");
                sb.AppendLine("\tistg = -1");
                sb.AppendLine("\tresPri = 0");
                sb.AppendLine("\tdstg = 0");
                sb.AppendLine("\tsidx = -1");
                sb.AppendLine("\tsqor = -1");
                sb.AppendLine("\tsepI = 0");
                sb.AppendLine("\tattm = " + (p.Radial ? 1 : 0));
                sb.AppendLine("\tsameVesselCollision = False");
                sb.AppendLine("\tmodCost = 0");
                sb.AppendLine("\tmodMass = 0");
                sb.AppendLine("\tmodSize = 0,0,0");
                foreach (Piece c in p.Children) sb.AppendLine("\tlink = " + c.Id);
                if (p.Parent != null && !p.Radial) sb.AppendLine("\tattN = " + p.OwnNode + "," + p.Parent.Id + Suffix(p.Ap, p.OwnNode));
                foreach (Piece c in p.Children) if (!c.Radial) sb.AppendLine("\tattN = " + c.ParentNode + "," + c.Id + Suffix(p.Ap, c.ParentNode));
                if (p.Radial && p.Parent != null) sb.AppendLine("\tsrfN = srfAttach," + p.Parent.Id);
                sb.AppendLine("\tEVENTS");
                sb.AppendLine("\t{");
                sb.AppendLine("\t}");
                sb.AppendLine("\tACTIONS");
                sb.AppendLine("\t{");
                sb.AppendLine("\t}");
                sb.AppendLine("\tPARTDATA");
                sb.AppendLine("\t{");
                sb.AppendLine("\t}");
                sb.AppendLine("}");
            }
            return sb.ToString();
        }

        private static string Suffix(AvailablePart ap, string nodeId)
        {
            AttachNode n = Node(ap, nodeId);
            return n == null ? string.Empty : "_" + F(n.position.x) + "|" + F(n.position.y) + "|" + F(n.position.z);
        }

        private static string V(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
        private static string Q(Quaternion q) { return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); }
        private static string F(float f) { return f.ToString("R", CultureInfo.InvariantCulture); }
        private static uint NewId() { return (uint)UnityEngine.Random.Range(1000000, int.MaxValue); }

        // ------------------------------------------------------------------ on the ground

        private static void Rebuild(Program g, List<BaseSection> sections, string key)
        {
            CelestialBody body = FlightGlobals.GetBodyByName(g.Body);
            if (body == null) { g.BaseLayoutKey = key; return; }
            if (!RemoveOld(g)) return; // the old base is loaded somewhere: try again later
            if (sections.Count == 0) { g.BaseLayoutKey = key; return; }
            string why;
            List<Piece> pieces = Layout(sections, out why);
            if (pieces == null)
            {
                KsaCore.Log.Info("Base " + g.Name + " not drawn yet: " + why);
                g.BaseLayoutKey = key;
                return;
            }
            if (double.IsNaN(g.SiteLat) || double.IsNaN(g.SiteLon)) ChooseSite(g, body);
            string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSA", "PluginData", "Bases", CraftWriter.SafeFileName(g.Id) + ".craft");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Craft(pieces, g.Name));
            Vessel v = Spawn(body, g.SiteLat, g.SiteLon, path, g.Name);
            g.BaseLayoutKey = key;
            if (v == null) return;
            g.BaseVesselId = v.persistentId.ToString(CultureInfo.InvariantCulture);
            string msg = g.Name + " now stands at " + body.bodyName + " with " + sections.Count + " section" + (sections.Count == 1 ? "" : "s") + " (" + pieces.Count + " parts).";
            g.Notes.Add(msg);
            KsaCore.Log.Info("Base built: " + msg + " Site " + g.SiteLat.ToString("0.000", CultureInfo.InvariantCulture) + ", " + g.SiteLon.ToString("0.000", CultureInfo.InvariantCulture) + "; craft " + path);
            ScreenMessages.PostScreenMessage("[KSA] " + msg, 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>Takes the program's earlier base vessel away. False when it is loaded and cannot be touched now.</summary>
        private static bool RemoveOld(Program g)
        {
            uint id;
            if (string.IsNullOrEmpty(g.BaseVesselId) || !uint.TryParse(g.BaseVesselId, out id)) return true;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v.persistentId != id) continue;
                if (v.loaded) return false;
                if (HighLogic.CurrentGame.flightState != null && v.protoVessel != null) HighLogic.CurrentGame.flightState.protoVessels.Remove(v.protoVessel);
                v.Die();
                break;
            }
            g.BaseVesselId = string.Empty;
            return true;
        }

        /// <summary>
        /// Where the base stands: beside the program's live base vessel when there is one, otherwise the flattest dry spot
        /// in a grid round a starting point picked from the program's id (the same every time).
        /// </summary>
        private static void ChooseSite(Program g, CelestialBody body)
        {
            uint id;
            if (!string.IsNullOrEmpty(g.VesselId) && uint.TryParse(g.VesselId, out id))
                foreach (Vessel v in FlightGlobals.Vessels)
                    if (v != null && v.persistentId == id && v.mainBody == body && (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.PRELAUNCH))
                    {
                        g.SiteLat = v.latitude;
                        g.SiteLon = v.longitude + 60.0 / (body.Radius * Math.Max(0.1, Math.Cos(v.latitude * Math.PI / 180))) * 180 / Math.PI; // 60 m east
                        g.Site = "beside the landed module, " + Coords(g);
                        return;
                    }
            int hash = 17;
            foreach (char c in g.Id + g.Body) hash = unchecked(hash * 31 + c);
            hash = Math.Abs(hash % 100000);
            double lat0 = (hash % 30) - 15, lon0 = ((hash / 30) % 360) - 180;
            double step = 400.0 / body.Radius * 180 / Math.PI;
            double bestLat = lat0, bestLon = lon0, bestSlope = double.MaxValue;
            for (int i = -4; i <= 4; i++)
                for (int j = -4; j <= 4; j++)
                {
                    double lat = lat0 + i * step, lon = lon0 + j * step / Math.Max(0.1, Math.Cos(lat * Math.PI / 180));
                    double h = Height(body, lat, lon);
                    if (body.ocean && h < 5) continue;
                    double slope = Slope(body, lat, lon);
                    if (slope < bestSlope) { bestSlope = slope; bestLat = lat; bestLon = lon; }
                }
            g.SiteLat = bestLat; g.SiteLon = bestLon;
            g.Site = "flat ground at " + Coords(g) + " (slope " + (Math.Atan(bestSlope) * 180 / Math.PI).ToString("0.0", CultureInfo.InvariantCulture) + " deg)";
        }

        private static string Coords(Program g) { return g.SiteLat.ToString("0.000", CultureInfo.InvariantCulture) + ", " + g.SiteLon.ToString("0.000", CultureInfo.InvariantCulture); }

        private static double Height(CelestialBody body, double lat, double lon)
        {
            if (body.pqsController == null) return 0;
            Vector3d n = body.GetRelSurfaceNVector(lat, lon);
            return body.pqsController.GetSurfaceHeight(n) - body.pqsController.radius;
        }

        /// <summary>Rise over run across 30 m east-west and north-south.</summary>
        private static double Slope(CelestialBody body, double lat, double lon)
        {
            double d = 15.0 / body.Radius * 180 / Math.PI, dLon = d / Math.Max(0.1, Math.Cos(lat * Math.PI / 180));
            double ew = Height(body, lat, lon + dLon) - Height(body, lat, lon - dLon);
            double ns = Height(body, lat + d, lon) - Height(body, lat - d, lon);
            return Math.Sqrt(ew * ew + ns * ns) / 30.0;
        }

        /// <summary>Puts the craft on the ground as a landed base vessel (the usual landed-vessel spawn of KSP mods).</summary>
        private static Vessel Spawn(CelestialBody body, double lat, double lon, string craftPath, string name)
        {
            ConfigNode editorShip = ShipConstruction.ShipConfig;
            ShipConstruct ship = ShipConstruction.LoadShip(craftPath);
            ShipConstruction.ShipConfig = editorShip; // the VAB keeps the player's own ship
            if (ship == null || ship.parts.Count == 0) throw new InvalidOperationException("the base craft did not load: " + craftPath);

            double alt = Math.Max(Height(body, lat, lon), body.ocean ? 0 : double.MinValue);
            Vector3d pos = body.GetWorldSurfacePosition(lat, lon, alt);
            var orbit = new Orbit(0, 0, 0, 0, 0, 0, 0, body);
            orbit.UpdateFromStateVectors(pos, body.getRFrmVel(pos), body, Planetarium.GetUniversalTime());

            uint missionId = (uint)Guid.NewGuid().GetHashCode();
            uint launchId = HighLogic.CurrentGame.launchID++;
            float lowest = float.MaxValue;
            foreach (Part p in ship.parts)
            {
                p.flightID = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                p.missionID = missionId;
                p.launchID = launchId;
                p.flagURL = HighLogic.CurrentGame.flagURL;
                p.temperature = 1.0;
                p.UpdateOrgPosAndRot(ship.parts[0]);
                foreach (Collider c in p.GetComponentsInChildren<Collider>())
                    if (c.enabled && c.gameObject.layer != 21) lowest = Mathf.Min(lowest, c.bounds.min.y);
            }
            if (lowest == float.MaxValue) lowest = 0;

            // Parts to snapshots through a throwaway vessel, the format a saved vessel uses.
            var proto = new ProtoVessel(new ConfigNode(), null);
            Vessel holder = new GameObject().AddComponent<Vessel>();
            holder.parts = ship.parts;
            proto.vesselRef = holder;
            foreach (Part p in ship.parts) { p.vessel = holder; proto.protoPartSnapshots.Add(new ProtoPartSnapshot(p, proto)); }
            foreach (ProtoPartSnapshot s in proto.protoPartSnapshots) s.storePartRefs();
            var partNodes = new List<ConfigNode>();
            foreach (ProtoPartSnapshot s in proto.protoPartSnapshots) { var n = new ConfigNode("PART"); s.Save(n); partNodes.Add(n); }
            float rootY = ship.parts[0].localRoot.attPos0.y;
            foreach (Part p in ship.parts) UnityEngine.Object.Destroy(p.gameObject);
            UnityEngine.Object.Destroy(holder.gameObject);

            ConfigNode[] extra = { ProtoVessel.CreateDiscoveryNode(DiscoveryLevels.Owned, UntrackedObjectClass.C, double.PositiveInfinity, double.PositiveInfinity) };
            ConfigNode node = ProtoVessel.CreateVesselNode(name, VesselType.Base, orbit, 0, partNodes.ToArray(), extra);
            node.SetValue("sit", Vessel.Situations.LANDED.ToString());
            node.SetValue("landed", "True");
            node.SetValue("splashed", "False");
            node.SetValue("lat", lat.ToString("R", CultureInfo.InvariantCulture));
            node.SetValue("lon", lon.ToString("R", CultureInfo.InvariantCulture));
            node.SetValue("alt", alt.ToString("R", CultureInfo.InvariantCulture));
            node.SetValue("landedAt", body.bodyName);
            Vector3d norm = body.GetRelSurfaceNVector(lat, lon);
            Quaternion normal = Quaternion.LookRotation(new Vector3((float)norm.x, (float)norm.y, (float)norm.z));
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward); // an editor (VAB) craft stands up
            node.SetValue("hgt", (rootY - lowest + 0.3f).ToString("R", CultureInfo.InvariantCulture)); // a little above the ground; it settles
            node.SetValue("rot", KSPUtil.WriteQuaternion(rotation * normal));
            Vector3 nrm = rotation * Vector3.forward;
            node.SetValue("nrm", nrm.x.ToString("R", CultureInfo.InvariantCulture) + "," + nrm.y.ToString("R", CultureInfo.InvariantCulture) + "," + nrm.z.ToString("R", CultureInfo.InvariantCulture));
            node.SetValue("prst", "False");

            var pv = new ProtoVessel(node, HighLogic.CurrentGame);
            pv.Load(HighLogic.CurrentGame.flightState);
            return pv.vesselRef;
        }
    }
}
