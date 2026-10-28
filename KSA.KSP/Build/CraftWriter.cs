using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KSA.Core.Design;
using KSA.Core.Parts;
using CorePart = KSA.Core.Parts.PartInfo;
using UnityEngine;

namespace KSA.KSP.Build
{
    /// <summary>
    /// VesselBuilder, v1 (Jira KSA-45): writes a stage plan as a .craft file. Vertical stacks only: the payload
    /// on top, each stage below it, radial boosters on radial decouplers around the stage's first tank.
    /// Positions come from the prefabs' attach nodes, so the editor loads the file as a connected vessel.
    /// MODULE nodes are omitted on purpose: KSP fills them from the prefab (full tanks, default settings).
    /// </summary>
    public static class CraftWriter
    {
        private sealed class Placed
        {
            public AvailablePart Ap;
            public CorePart Info;
            public string Id;
            public Vector3 Pos;
            public Quaternion Rot;
            public Placed Parent;
            public string ParentNode;   // "top"/"bottom" node on the parent we attached to, or "srf"
            public string OwnNode;      // node on this part used for the attachment
            public int Stage = -1;
            public bool Radial;
            public List<Placed> Children = new List<Placed>();
            public List<Placed> Symmetry = new List<Placed>();
        }

        public sealed class Result
        {
            public bool Ok;
            public string Path;
            public string Error;
            public int Parts;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>Folder the craft is written to: the current save's VAB, so it shows in the editor's load list.</summary>
        public static string CraftFolder
        {
            get
            {
                string save = string.IsNullOrEmpty(HighLogic.SaveFolder) ? "default" : HighLogic.SaveFolder;
                return Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("saves", Path.Combine(save, Path.Combine("Ships", "VAB"))));
            }
        }

        /// <summary>Copies the mod's template craft into the save's VAB under "KSA Templates" so the player can open them in the editor.</summary>
        public static void PublishTemplates()
        {
            try
            {
                string source = Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", Path.Combine("KSA", "Templates")));
                if (!Directory.Exists(source)) return;
                string target = Path.Combine(CraftFolder, "KSA Templates");
                Directory.CreateDirectory(target);
                int copied = 0;
                foreach (string file in Directory.GetFiles(source, "*.craft"))
                {
                    string dest = Path.Combine(target, Path.GetFileName(file));
                    if (File.Exists(dest) && File.GetLastWriteTimeUtc(dest) >= File.GetLastWriteTimeUtc(file)) continue;
                    File.Copy(file, dest, true);
                    copied++;
                }
                if (copied > 0) KsaCore.Log.Info("Published " + copied + " template craft to " + target);
            }
            catch (Exception e) { KsaCore.Log.Warn("Could not publish template craft: " + e.Message); }
        }

        /// <summary>
        /// Re-assembles craft written by an older build of the writer: a project that is built or waiting on the pad
        /// whose craft file is missing or older than the running KSA.KSP.dll gets its file rewritten from the stored
        /// plan. A craft the player edited and saved after the update is newer than the DLL, so it is left alone.
        /// </summary>
        public static void ReassembleStale(Core.Agency agency)
        {
            if (agency == null) return;
            DateTime writerTime;
            try { writerTime = File.GetLastWriteTimeUtc(typeof(CraftWriter).Assembly.Location); }
            catch { return; }
            foreach (Core.Projects.Project p in agency.Projects)
            {
                if (p.Status != Core.Projects.ProjectStatus.Build && p.Status != Core.Projects.ProjectStatus.Ready) continue;
                DesignRecord d = p.Design;
                if (d == null || !d.IsAi || d.Verdict != DesignVerdict.Approved) continue;
                if (d.Plan == null && !string.IsNullOrEmpty(d.PlanJson)) { StagePlan plan; string err; if (StagePlan.TryParse(d.PlanJson, out plan, out err)) d.Plan = plan; }
                if (d.Plan == null) continue;
                string path = Path.Combine(CraftFolder, SafeFileName("KSA " + p.Id + " " + p.Name) + ".craft");
                bool stale;
                try { stale = !File.Exists(path) || File.GetLastWriteTimeUtc(path) < writerTime; }
                catch { continue; }
                if (!stale) continue;
                Result r = Write(d.Plan, agency.Parts, "KSA " + p.Id + " " + d.DesignName, "KSA " + p.Id + " " + p.Name);
                if (!r.Ok) continue;
                d.CraftPath = r.Path;
                p.Notes.Add("Vehicle re-assembled after a mod update: " + r.Parts + " parts, saved to the VAB as \"KSA " + p.Id + " " + p.Name + "\".");
                foreach (string w in r.Warnings) p.Notes.Add("Assembly note: " + w);
                KsaCore.Log.Info("Re-assembled stale craft for " + p.Name);
            }
        }

        public static Result Write(StagePlan plan, IPartCatalog catalog, string shipName, string fileStem)
        {
            var result = new Result();
            try
            {
                List<Placed> placed = Layout(plan, catalog, result);
                if (placed == null) { result.Ok = false; return result; }
                string text = Serialize(placed, shipName);
                Directory.CreateDirectory(CraftFolder);
                string path = Path.Combine(CraftFolder, SafeFileName(fileStem) + ".craft");
                File.WriteAllText(path, text);
                result.Ok = true;
                result.Path = path;
                result.Parts = placed.Count;
                KsaCore.Log.Info("Craft written: " + path + " (" + placed.Count + " parts)");
            }
            catch (Exception e)
            {
                result.Ok = false;
                result.Error = e.Message;
                KsaCore.Log.Error("Craft build failed: " + e);
            }
            return result;
        }

        // ------------------------------------------------------------------ layout

        private static List<Placed> Layout(StagePlan plan, IPartCatalog catalog, Result result)
        {
            if (plan == null || plan.Stages.Count == 0) { result.Error = "Empty plan."; return null; }
            var all = new List<Placed>();
            int uid = 4290000000 > int.MaxValue ? 1000000 : 1000000;
            Func<string, Placed> make = name =>
            {
                AvailablePart ap = PartLoader.getPartInfoByName(name);
                if (ap == null) throw new InvalidOperationException("Part '" + name + "' is not loaded.");
                var pl = new Placed { Ap = ap, Info = catalog != null ? catalog.Get(name) : null, Id = ap.name + "_" + (uid++).ToString(CultureInfo.InvariantCulture), Rot = Quaternion.identity };
                all.Add(pl);
                return pl;
            };

            // Root: the command part.
            if (string.IsNullOrEmpty(plan.Payload.Command)) { result.Error = "No command part."; return null; }
            Placed root = make(plan.Payload.Command);
            root.Pos = new Vector3(0, 20f, 0);

            // Extras above the pod (parachutes and anything with a bottom node and no top need), radial extras on the pod.
            Placed topMost = root;
            var belowPod = new List<string>();
            foreach (string extra in plan.Payload.Extras)
            {
                AvailablePart ap = PartLoader.getPartInfoByName(extra);
                if (ap == null) { result.Warnings.Add("Extra '" + extra + "' is not loaded; skipped."); continue; }
                CorePart info = catalog != null ? catalog.Get(extra) : null;
                PartKind kind = info != null ? info.Kind : PartKind.Other;
                if (kind == PartKind.HeatShield || kind == PartKind.Decoupler) { belowPod.Add(extra); continue; }
                AttachNode bottom = Node(ap, "bottom");
                if (bottom != null && Node(topMost.Ap, "top") != null)
                {
                    Placed e = make(extra);
                    StackAbove(topMost, e);
                    topMost = e;
                }
                else if (ap.partPrefab.srfAttachNode != null && ap.partPrefab.attachRules.srfAttach)
                {
                    Placed e = make(extra);
                    SurfaceAttach(root, e, 0f, 0f, result);
                }
                else result.Warnings.Add("Extra '" + extra + "' cannot attach; skipped.");
            }

            // Chain downward: heat shields, then stages from the top stage to the launch stage.
            Placed bottomMost = root;
            foreach (string extra in belowPod)
            {
                Placed e = make(extra);
                StackBelow(bottomMost, e);
                bottomMost = e;
            }

            int stageCount = plan.Stages.Count;
            // Staging order, first to fire → last: for each plan stage from 0: engines(+boosters), [radial decouplers], decoupler.
            // Parachutes fire last. KSP istg: 0 fires last, so istg = total - 1 - sequenceIndex.
            int seq = 0;
            var engineSeq = new int[stageCount];
            var radialSeq = new int[stageCount];
            var decouplerSeq = new int[stageCount];
            for (int i = 0; i < stageCount; i++)
            {
                engineSeq[i] = seq++;
                radialSeq[i] = plan.Stages[i].Radial.Count > 0 ? seq++ : -1;
                decouplerSeq[i] = plan.Stages[i].Decoupler.Length > 0 ? seq++ : -1;
            }
            int chuteSeq = seq++;
            int total = seq;
            Func<int, int> stageOf = s => s < 0 ? -1 : total - 1 - s;

            string radialDecouplerName = FindRadialDecoupler(catalog);

            for (int i = stageCount - 1; i >= 0; i--)
            {
                PlanStage s = plan.Stages[i];
                if (s.Decoupler.Length > 0)
                {
                    Placed d = make(s.Decoupler);
                    d.Stage = stageOf(decouplerSeq[i]);
                    StackBelow(bottomMost, d);
                    bottomMost = d;
                }
                // Core, top to bottom: tanks and structure first, engines at the very bottom.
                var ordered = new List<PlanPart>();
                var engines = new List<PlanPart>();
                foreach (PlanPart pp in s.Core)
                {
                    CorePart info = catalog != null ? catalog.Get(pp.Part) : null;
                    if (info != null && (info.Kind == PartKind.Engine || info.Kind == PartKind.Booster)) engines.Add(pp); else ordered.Add(pp);
                }
                ordered.Reverse(); // plan lists bottom-up; we build top-down
                ordered.AddRange(engines);
                Placed firstTank = null;
                foreach (PlanPart pp in ordered)
                {
                    for (int c = 0; c < Math.Max(1, pp.Count); c++)
                    {
                        Placed part = make(pp.Part);
                        CorePart info = part.Info;
                        bool thrusts = info != null && (info.Kind == PartKind.Engine || info.Kind == PartKind.Booster);
                        part.Stage = thrusts ? stageOf(engineSeq[i]) : -1;
                        StackBelow(bottomMost, part);
                        bottomMost = part;
                        if (firstTank == null && info != null && (info.Kind == PartKind.Tank || info.Kind == PartKind.Booster)) firstTank = part;
                    }
                }
                Placed anchor = firstTank ?? bottomMost;

                // Radial parts around the anchor, in symmetry.
                foreach (PlanPart rp in s.Radial)
                {
                    int n = Math.Max(1, rp.Symmetry > 1 ? rp.Symmetry : rp.Count);
                    AvailablePart rap = PartLoader.getPartInfoByName(rp.Part);
                    if (rap == null) { result.Warnings.Add("Radial '" + rp.Part + "' is not loaded; skipped."); continue; }
                    CorePart rinfo = catalog != null ? catalog.Get(rp.Part) : null;
                    bool thrusts = rinfo != null && (rinfo.Kind == PartKind.Engine || rinfo.Kind == PartKind.Booster);
                    var group = new List<Placed>();
                    for (int k = 0; k < n; k++)
                    {
                        float angle = (float)(2 * Math.PI * k / n);
                        Placed host = anchor;
                        if (thrusts && radialDecouplerName != null)
                        {
                            Placed rd = make(radialDecouplerName);
                            rd.Stage = stageOf(radialSeq[i]);
                            SurfaceAttach(anchor, rd, angle, 0f, result);
                            host = rd;
                        }
                        Placed booster = make(rp.Part);
                        booster.Stage = thrusts ? stageOf(engineSeq[i]) : -1;
                        SurfaceAttach(host, booster, angle, 0f, result);
                        group.Add(booster);
                    }
                    foreach (Placed g in group) foreach (Placed other in group) if (other != g) g.Symmetry.Add(other);
                }
            }

            // Parachutes fire last.
            foreach (Placed p in all) if (p.Info != null && p.Info.Kind == PartKind.Parachute) p.Stage = stageOf(chuteSeq);

            // Lift everything so the lowest part sits above the pad.
            float minY = float.MaxValue;
            foreach (Placed p in all) minY = Math.Min(minY, p.Pos.y);
            float lift = 5f - minY;
            foreach (Placed p in all) p.Pos += new Vector3(0, lift, 0);
            return all;
        }

        private static string FindRadialDecoupler(IPartCatalog catalog)
        {
            if (catalog == null) return null;
            CorePart best = null;
            foreach (CorePart p in catalog.All)
            {
                if (p.Kind != PartKind.Decoupler || p.IsStackable) continue;
                if (PartLoader.getPartInfoByName(p.Name) == null) continue;
                if (best == null || p.Mass < best.Mass) best = p;
            }
            return best != null ? best.Name : null;
        }

        private static AttachNode Node(AvailablePart ap, string id)
        {
            if (ap == null || ap.partPrefab == null || ap.partPrefab.attachNodes == null) return null;
            foreach (AttachNode n in ap.partPrefab.attachNodes) if (n != null && n.id == id && n.nodeType == AttachNode.NodeType.Stack) return n;
            return null;
        }

        /// <summary>Puts <paramref name="child"/> under <paramref name="parent"/>: parent's bottom node meets child's top node.</summary>
        private static void StackBelow(Placed parent, Placed child)
        {
            AttachNode pb = Node(parent.Ap, "bottom");
            AttachNode ct = Node(child.Ap, "top");
            if (pb == null) throw new InvalidOperationException(parent.Ap.name + " has no bottom node to carry " + child.Ap.name + ".");
            if (ct == null) throw new InvalidOperationException(child.Ap.name + " has no top node to hang under " + parent.Ap.name + ".");
            child.Pos = parent.Pos + pb.position - ct.position;
            child.Parent = parent; child.ParentNode = "bottom"; child.OwnNode = "top";
            parent.Children.Add(child);
        }

        private static void StackAbove(Placed parent, Placed child)
        {
            AttachNode pt = Node(parent.Ap, "top");
            AttachNode cb = Node(child.Ap, "bottom");
            child.Pos = parent.Pos + pt.position - cb.position;
            child.Parent = parent; child.ParentNode = "top"; child.OwnNode = "bottom";
            parent.Children.Add(child);
        }

        /// <summary>Surface-attaches a radial part at the given angle around the host, its top level with the host's top.</summary>
        private static void SurfaceAttach(Placed host, Placed child, float angle, float yOffset, Result result)
        {
            Part cp = child.Ap.partPrefab;
            AttachNode srf = cp.srfAttachNode;
            if (srf == null || cp.attachRules == null || !cp.attachRules.srfAttach)
                throw new InvalidOperationException(child.Ap.name + " cannot be surface-attached.");
            float hostRadius = Radius(host);
            var dir = new Vector3((float)Math.Cos(angle), 0, (float)Math.Sin(angle));
            // KSP convention (checked against stock craft): a surface node's orientation points AWAY from the host in
            // world space, along the host's surface normal, and the node's position is where the part touches the host.
            // The Flea's node sits 0.625 m off its axis, so the sign matters: the wrong one buries the booster in its host.
            Vector3 orient = srf.orientation.sqrMagnitude > 0 ? srf.orientation.normalized : new Vector3(1, 0, 0);
            Quaternion rot = Quaternion.FromToRotation(orient, dir);
            // Keep the part upright: FromToRotation may roll it; re-level by projecting its up axis.
            Vector3 up = rot * Vector3.up;
            if (Vector3.Dot(up, Vector3.up) < 0.999f) rot = Quaternion.FromToRotation(up, Vector3.up) * rot;
            child.Rot = rot;
            // Attach point on the host surface: level with the host's top node (or centre), out along dir.
            AttachNode hostTop = Node(host.Ap, "top");
            float topY = hostTop != null ? hostTop.position.y : 0f;
            float childTopY = 0f;
            AttachNode childTop = Node(child.Ap, "top");
            if (childTop != null) childTopY = childTop.position.y;
            Vector3 attachWorld = host.Pos + dir * hostRadius + new Vector3(0, Math.Min(topY, 0.0f) + yOffset, 0);
            Vector3 rotatedSrf = rot * srf.position;
            child.Pos = attachWorld - rotatedSrf;
            // Align tops: a booster's top should not poke above the tank it hangs on.
            float childTopWorld = child.Pos.y + childTopY;
            float hostTopWorld = host.Pos.y + topY;
            if (childTop != null && childTopWorld > hostTopWorld) child.Pos += new Vector3(0, hostTopWorld - childTopWorld, 0);
            child.Parent = host; child.ParentNode = "srf"; child.OwnNode = "srfAttach"; child.Radial = true;
            host.Children.Add(child);
        }

        private static float Radius(Placed p)
        {
            if (p.Info != null && p.Info.Diameter > 0) return (float)(p.Info.Diameter / 2);
            AttachNode t = Node(p.Ap, "top") ?? Node(p.Ap, "bottom");
            if (t != null) return t.size == 0 ? 0.3125f : 0.625f * t.size;
            // Radial decouplers and such: use the prefab's srf node depth as a thin plate.
            return p.Ap.partPrefab.srfAttachNode != null ? Math.Abs(p.Ap.partPrefab.srfAttachNode.position.x) * 2f + 0.1f : 0.2f;
        }

        // ------------------------------------------------------------------ serialisation

        private static string Serialize(List<Placed> parts, string shipName)
        {
            var sb = new StringBuilder();
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Placed p in parts) { min = Vector3.Min(min, p.Pos); max = Vector3.Max(max, p.Pos); }
            Vector3 size = max - min + new Vector3(2.5f, 3f, 2.5f);
            sb.AppendLine("ship = " + shipName);
            sb.AppendLine("version = " + Versioning.GetVersionString());
            sb.AppendLine("description = Designed by the Kerbal Space Directorate engineering team.");
            sb.AppendLine("type = VAB");
            sb.AppendLine("OverrideDefault = False,False,False,False");
            sb.AppendLine("OverrideActionControl = 0,0,0,0");
            sb.AppendLine("OverrideAxisControl = 0,0,0,0");
            sb.AppendLine("OverrideGroupNames = ,,,");
            sb.AppendLine("size = " + V(size));
            sb.AppendLine("steamPublishedFileId = 0");
            sb.AppendLine("persistentId = " + NewPersistentId());
            sb.AppendLine("rot = 0,0,0,1");
            sb.AppendLine("missionFlag = Squad/Flags/default");
            sb.AppendLine("vesselType = Ship");
            foreach (Placed p in parts) WritePart(sb, p);
            return sb.ToString();
        }

        private static void WritePart(StringBuilder sb, Placed p)
        {
            sb.AppendLine("PART");
            sb.AppendLine("{");
            sb.AppendLine("\tpart = " + p.Id);
            sb.AppendLine("\tpartName = Part");
            sb.AppendLine("\tpersistentId = " + NewPersistentId());
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
            sb.AppendLine("\tistg = " + p.Stage);
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
            foreach (Placed c in p.Children) sb.AppendLine("\tlink = " + c.Id);
            foreach (Placed s in p.Symmetry) sb.AppendLine("\tsym = " + s.Id);
            // Stack node bookkeeping: own node → parent, and parent's node → each stacked child.
            if (p.Parent != null && !p.Radial) sb.AppendLine("\tattN = " + p.OwnNode + "," + p.Parent.Id + NodeSuffix(p.Ap, p.OwnNode));
            foreach (Placed c in p.Children) if (!c.Radial) sb.AppendLine("\tattN = " + c.ParentNode + "," + c.Id + NodeSuffix(p.Ap, c.ParentNode));
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

        private static string NodeSuffix(AvailablePart ap, string nodeId)
        {
            AttachNode n = Node(ap, nodeId);
            if (n == null) return string.Empty;
            return "_" + F(n.position.x) + "|" + F(n.position.y) + "|" + F(n.position.z);
        }

        private static string V(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
        private static string Q(Quaternion q) { return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); }
        private static string F(float f) { return f.ToString("R", CultureInfo.InvariantCulture); }

        private static uint NewPersistentId()
        {
            return (uint)UnityEngine.Random.Range(1000000, int.MaxValue);
        }

        public static string SafeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "KSA craft")
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length > 0 ? sb.ToString() : "KSA craft";
        }
    }
}
