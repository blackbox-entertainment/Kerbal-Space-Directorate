using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Renders a real head-and-shoulders portrait of a specific kerbal (Jira KSA-56), the way the in-flight
    /// portrait gallery does, so the Roster, Service Record and Hall of Fame can show faces instead of the
    /// generic suit sprite the Astronaut Complex uses.
    ///
    /// How it works: the game only draws kerbal faces from the IVA "Kerbal" prefab (kerbalMale / kerbalFemale and
    /// the Vintage / Future / Slim variants), which ships with its own portrait camera (<c>Kerbal.kerbalCam</c>).
    /// We instantiate that prefab for the crew member, apply the standard or veteran suit texture exactly as
    /// <c>Kerbal.SetupKerbalModel</c> does, hide the helmet, move the model to an unused layer far from the origin,
    /// light it, render the prefab camera into a 256x256 RenderTexture, read it back, and destroy the rig.
    /// One kerbal is rendered per frame at most, lazily, by a hidden runner that survives scene changes.
    /// Results are cached in memory and as PNG under GameData/KSA/PluginData/portraits/ (git-ignored), keyed by
    /// crew name, with the suit variant stored alongside so a changed suit re-renders.
    ///
    /// Every failure is caught and logged as "[KSA] Portrait ..."; callers fall back to the sprite.
    /// </summary>
    public static class PortraitRenderer
    {
        public const int Size = 256;

        /// <summary>Show the helmet in portraits. Off by default so faces are visible.</summary>
        public static bool ShowHelmet = false;

        /// <summary>Raised on the main thread once a batch of requested portraits has finished rendering.</summary>
        public static event Action BatchReady;

        private const string KeyVersion = "v1";
        private static readonly Color Background = new Color(0.10f, 0.12f, 0.16f, 1f);
        private static readonly Vector3 RigPosition = new Vector3(0f, -4000f, 0f);

        private sealed class Entry
        {
            public Texture2D Texture;
            public string SuitKey;
            public bool Failed;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly HashSet<string> DiskChecked = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> Queue = new List<string>();
        private static Runner _runner;
        private static bool _loggedRig;
        private static bool _disabled;

        // ------------------------------------------------------------------ public API

        /// <summary>The rendered portrait for this kerbal when it is ready, else null (and a render is requested).</summary>
        public static Texture Get(ProtoCrewMember pcm)
        {
            if (pcm == null || string.IsNullOrEmpty(pcm.name) || _disabled) return null;
            try
            {
                string key = SuitKey(pcm);
                Entry e;
                if (Cache.TryGetValue(pcm.name, out e))
                {
                    if (e.SuitKey == key)
                    {
                        if (e.Texture != null) return e.Texture;
                        if (e.Failed) return null;
                    }
                    else if (e.Texture != null)
                    {
                        Request(pcm);     // suit changed: re-render, but show the old face meanwhile
                        return e.Texture;
                    }
                }
                else if (DiskChecked.Add(pcm.name))
                {
                    e = LoadFromDisk(pcm.name);
                    if (e != null)
                    {
                        Cache[pcm.name] = e;
                        if (e.SuitKey == key) return e.Texture;
                    }
                }
                Request(pcm);
            }
            catch (Exception ex)
            {
                KsaCore.Log.Warn("Portrait lookup failed for " + pcm.name + ": " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// A portrait rendered earlier for a crew name, from memory or the PNG cache, regardless of suit. Used for
        /// crew who are no longer on the game roster (dismissed, retired). Null when nothing was ever rendered.
        /// </summary>
        public static Texture GetCached(string crewName)
        {
            if (string.IsNullOrEmpty(crewName)) return null;
            try
            {
                Entry e;
                if (Cache.TryGetValue(crewName, out e)) return e.Texture;
                if (DiskChecked.Add(crewName))
                {
                    e = LoadFromDisk(crewName);
                    if (e != null)
                    {
                        Cache[crewName] = e;
                        return e.Texture;
                    }
                }
            }
            catch (Exception ex)
            {
                KsaCore.Log.Warn("Portrait cache lookup failed for " + crewName + ": " + ex.Message);
            }
            return null;
        }

        /// <summary>Queues a render for this kerbal (no-op if one is cached for the current suit or already queued).</summary>
        public static void Request(ProtoCrewMember pcm)
        {
            if (pcm == null || string.IsNullOrEmpty(pcm.name) || _disabled) return;
            Entry e;
            if (Cache.TryGetValue(pcm.name, out e) && e.SuitKey == SuitKey(pcm) && (e.Texture != null || e.Failed)) return;
            if (Queue.Contains(pcm.name)) return;
            Queue.Add(pcm.name);
            EnsureRunner();
        }

        // ------------------------------------------------------------------ keys and disk cache

        private static string SuitKey(ProtoCrewMember pcm)
        {
            return KeyVersion + "|" + pcm.gender + "|" + pcm.suit + "|" + (pcm.veteran ? "veteran" : "standard") + "|" + pcm.type
                   + "|" + (ShowHelmet ? "helmet" : "nohelmet");
        }

        private static string PortraitDir
        {
            get { return Path.Combine(Ai.KsaSettingsFile.PluginDataDir, "portraits"); }
        }

        private static string FileStem(string crewName)
        {
            var sb = new StringBuilder(crewName.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in crewName)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
            return sb.ToString();
        }

        private static Entry LoadFromDisk(string crewName)
        {
            string stem = Path.Combine(PortraitDir, FileStem(crewName));
            string png = stem + ".png";
            string keyFile = stem + ".key";
            if (!File.Exists(png) || !File.Exists(keyFile)) return null;
            string key = File.ReadAllText(keyFile).Trim();
            byte[] bytes = File.ReadAllBytes(png);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                KsaCore.Log.Warn("Portrait PNG for " + crewName + " could not be decoded; it will be re-rendered");
                return null;
            }
            tex.name = "KSA_Portrait_" + crewName;
            tex.wrapMode = TextureWrapMode.Clamp;
            return new Entry { Texture = tex, SuitKey = key };
        }

        private static void SaveToDisk(string crewName, string key, Texture2D tex)
        {
            try
            {
                Directory.CreateDirectory(PortraitDir);
                string stem = Path.Combine(PortraitDir, FileStem(crewName));
                File.WriteAllBytes(stem + ".png", tex.EncodeToPNG());
                File.WriteAllText(stem + ".key", key);
            }
            catch (Exception ex)
            {
                KsaCore.Log.Warn("Portrait for " + crewName + " could not be saved to " + PortraitDir + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ runner

        private static void EnsureRunner()
        {
            if (_runner != null) { _runner.Kick(); return; }
            try
            {
                var go = new GameObject("KSA_PortraitRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
                _runner.Kick();
            }
            catch (Exception ex)
            {
                _disabled = true;
                KsaCore.Log.Error("Portrait renderer could not start, falling back to suit sprites: " + ex);
            }
        }

        private static string NextInQueue()
        {
            if (Queue.Count == 0) return null;
            string name = Queue[0];
            Queue.RemoveAt(0);
            return name;
        }

        private static ProtoCrewMember FindOnRoster(string crewName)
        {
            Game game = HighLogic.CurrentGame;
            if (game == null || game.CrewRoster == null) return null;
            return game.CrewRoster.Exists(crewName) ? game.CrewRoster[crewName] : null;
        }

        private static void OnBatchReady()
        {
            Action h = BatchReady;
            if (h == null) return;
            try { h(); }
            catch (Exception ex) { KsaCore.Log.Warn("Portrait BatchReady handler threw: " + ex.Message); }
        }

        /// <summary>Hidden MonoBehaviour that works through the queue, one kerbal per frame, across scene changes.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private bool _running;

            public void Kick()
            {
                if (_running) return;
                _running = true;
                StartCoroutine(Work());
            }

            private IEnumerator Work()
            {
                yield return null; // let the window that asked finish building first
                int rendered = 0;
                while (true)
                {
                    string name = NextInQueue();
                    if (name == null) break;
                    ProtoCrewMember pcm = FindOnRoster(name);
                    if (pcm == null)
                    {
                        KsaCore.Log.Warn("Portrait requested for " + name + " but they are not on the game roster");
                        continue;
                    }
                    var job = new RenderJob(pcm);
                    if (!job.Setup()) { job.Cleanup(); continue; }
                    yield return null;                      // animators pose the model
                    yield return new WaitForEndOfFrame();   // render after the scene's own cameras
                    if (job.Capture()) rendered++;
                    job.Cleanup();
                }
                _running = false;
                if (rendered > 0) OnBatchReady();
            }
        }

        // ------------------------------------------------------------------ one render

        private sealed class RenderJob
        {
            private readonly ProtoCrewMember _pcm;
            private readonly string _key;
            private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
            private GameObject _holder;
            private Kerbal _kerbal;
            private Camera _cam;
            private RenderTexture _rt;
            private string _prefabName = "?";
            private bool _ownCamera;
            private readonly List<Material> _materials = new List<Material>();

            public RenderJob(ProtoCrewMember pcm)
            {
                _pcm = pcm;
                _key = SuitKey(pcm);
            }

            public bool Setup()
            {
                try
                {
                    GameObject prefab = FindPrefab(_pcm, out _prefabName);
                    if (prefab == null)
                    {
                        Fail("no IVA kerbal prefab found (" + _prefabName + ")", null);
                        return false;
                    }

                    int layer = PickLayer();

                    _holder = new GameObject("KSA_PortraitRig_" + _pcm.name);
                    _holder.SetActive(false); // keeps Awake from running until the model is configured
                    _holder.transform.position = RigPosition;

                    GameObject model = UnityEngine.Object.Instantiate(prefab, _holder.transform);
                    model.name = "model";
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = Vector3.one;

                    // Nothing on the prefab may Start/Update: no portrait gallery registration, no expression AI, no IVA logic.
                    foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true))
                        if (mb != null) mb.enabled = false;

                    _kerbal = model.GetComponent<Kerbal>();
                    if (_kerbal != null)
                    {
                        _kerbal.protoCrewMember = _pcm;
                        _kerbal.crewMemberName = _pcm.name;
                        _kerbal.veteran = _pcm.veteran;
                        _kerbal.isBadass = _pcm.isBadass;
                        _kerbal.courage = _pcm.courage;
                        _kerbal.stupidity = _pcm.stupidity;
                        _kerbal.showHelmet = ShowHelmet;
                        ApplySuitTexture(_kerbal, _pcm.veteran);
                        if (!ShowHelmet && _kerbal.helmetTransform != null) _kerbal.helmetTransform.gameObject.SetActive(false);
                    }

                    SetLayerRecursively(model, layer);

                    _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                    _rt.name = "KSA_PortraitRT";
                    _rt.antiAliasing = 4;
                    _rt.Create();

                    _cam = _kerbal != null ? _kerbal.kerbalCam : null;
                    if (_cam == null)
                    {
                        _ownCamera = true;
                        var camGo = new GameObject("camera");
                        camGo.transform.SetParent(_holder.transform, false);
                        Transform head = _kerbal != null && _kerbal.headTransform != null ? _kerbal.headTransform : model.transform;
                        Vector3 look = head.position + model.transform.up * 0.03f;
                        camGo.transform.position = look + model.transform.forward * 0.55f + model.transform.up * 0.08f;
                        camGo.transform.LookAt(look, model.transform.up);
                        _cam = camGo.AddComponent<Camera>();
                        _cam.fieldOfView = 40f;
                        _cam.nearClipPlane = 0.05f;
                        _cam.farClipPlane = 20f;
                    }
                    _cam.enabled = false;
                    _cam.targetTexture = _rt;
                    _cam.cullingMask = 1 << layer;
                    _cam.clearFlags = CameraClearFlags.SolidColor;
                    _cam.backgroundColor = Background;
                    _cam.allowHDR = false;

                    AddLight("key", layer, _cam.transform.rotation * Quaternion.Euler(20f, -30f, 0f), 1.15f, new Color(1f, 0.97f, 0.92f));
                    AddLight("fill", layer, _cam.transform.rotation * Quaternion.Euler(5f, 40f, 0f), 0.45f, new Color(0.85f, 0.9f, 1f));

                    _holder.SetActive(true); // Awake runs now (Kerbal.Awake only disables its camera and hooks an event)

                    if (!_loggedRig)
                    {
                        _loggedRig = true;
                        KsaCore.Log.Info("Portrait rig: prefab " + _prefabName + ", layer " + layer + " (" + LayerMask.LayerToName(layer) + ")"
                                         + ", camera " + (_ownCamera ? "own" : "prefab kerbalCam fov " + _cam.fieldOfView.ToString("0") + " near " + _cam.nearClipPlane.ToString("0.###"))
                                         + ", kerbal component " + (_kerbal != null)
                                         + ", textureTargets " + (_kerbal != null && _kerbal.textureTargets != null ? _kerbal.textureTargets.Length : 0)
                                         + ", helmet " + (_kerbal != null && _kerbal.helmetTransform != null)
                                         + ", head " + (_kerbal != null && _kerbal.headTransform != null)
                                         + ", renderers " + model.GetComponentsInChildren<Renderer>(true).Length
                                         + ", prefab lights " + model.GetComponentsInChildren<Light>(true).Length);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Fail("setup threw", ex);
                    return false;
                }
            }

            public bool Capture()
            {
                try
                {
                    if (_holder == null || _cam == null || _rt == null)
                    {
                        // The scene changed under us; drop the job and let the next window request it again.
                        KsaCore.Log.Warn("Portrait rig for " + _pcm.name + " vanished before capture (scene change?), will retry on next request");
                        return false;
                    }

                    RenderWithoutCanvasRebuild(_cam);

                    RenderTexture previous = RenderTexture.active;
                    Texture2D tex;
                    try
                    {
                        RenderTexture.active = _rt;
                        tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                        tex.Apply(false, false);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                    }
                    tex.name = "KSA_Portrait_" + _pcm.name;
                    tex.wrapMode = TextureWrapMode.Clamp;

                    Entry old;
                    if (Cache.TryGetValue(_pcm.name, out old) && old.Texture != null && old.Texture != tex)
                        UnityEngine.Object.Destroy(old.Texture);
                    Cache[_pcm.name] = new Entry { Texture = tex, SuitKey = _key };
                    SaveToDisk(_pcm.name, _key, tex);

                    KsaCore.Log.Info("Portrait rendered for " + _pcm.name + " (" + _prefabName + ", " + (_pcm.veteran ? "veteran" : "standard")
                                     + " suit, helmet " + (ShowHelmet ? "shown" : "hidden") + ") in " + _watch.ElapsedMilliseconds + " ms");
                    return true;
                }
                catch (Exception ex)
                {
                    Fail("capture threw", ex);
                    return false;
                }
            }

            public void Cleanup()
            {
                try
                {
                    if (_cam != null) _cam.targetTexture = null;
                    foreach (Material m in _materials) if (m != null) UnityEngine.Object.Destroy(m);
                    _materials.Clear();
                    if (_holder != null) UnityEngine.Object.Destroy(_holder);
                    if (_rt != null)
                    {
                        if (RenderTexture.active == _rt) RenderTexture.active = null;
                        _rt.Release();
                        UnityEngine.Object.Destroy(_rt);
                    }
                }
                catch (Exception ex)
                {
                    KsaCore.Log.Warn("Portrait rig cleanup for " + _pcm.name + " threw: " + ex.Message);
                }
                _holder = null;
                _cam = null;
                _rt = null;
            }

            private void Fail(string what, Exception ex)
            {
                Cache[_pcm.name] = new Entry { SuitKey = _key, Failed = true };
                KsaCore.Log.Warn("Portrait for " + _pcm.name + " failed: " + what + (ex != null ? " - " + ex : "") + ". Using the suit sprite instead.");
            }

            /// <summary>Same as Kerbal.SetupKerbalModel: standard or veteran (orange) suit texture on every texture target.</summary>
            private void ApplySuitTexture(Kerbal kerbal, bool veteran)
            {
                if (kerbal.textureTargets == null) return;
                Texture2D suit = veteran ? kerbal.textureVeteran : kerbal.textureStandard;
                if (suit == null) return;
                foreach (Renderer r in kerbal.textureTargets)
                {
                    if (r == null) continue;
                    Material m = r.material; // instance, destroyed in Cleanup
                    _materials.Add(m);
                    m.mainTexture = suit;
                }
            }

            private void AddLight(string name, int layer, Quaternion rotation, float intensity, Color color)
            {
                var go = new GameObject("light_" + name);
                go.transform.SetParent(_holder.transform, false);
                go.transform.rotation = rotation;
                go.layer = layer;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = intensity;
                light.color = color;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << layer;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The IVA kerbal prefab the game itself would spawn for this crew member (see ProtoCrewMember._Spawn).</summary>
        private static GameObject FindPrefab(ProtoCrewMember pcm, out string prefabName)
        {
            prefabName = pcm.GetKerbalIVAName();
            GameObject prefab = null;
            try
            {
                if (prefabName.Contains("Vintage")) prefab = Expansions.Missions.MissionsUtils.MEPrefab("Kerbals/IVA/" + prefabName + ".prefab");
                else if (prefabName.Contains("Future")) prefab = Expansions.Serenity.SerenityUtils.SerenityPrefab("Kerbals/IVA/" + prefabName + ".prefab");
                else prefab = AssetBase.GetPrefab(prefabName);
            }
            catch (Exception ex)
            {
                KsaCore.Log.Warn("Portrait: prefab lookup for " + prefabName + " threw (" + ex.Message + "), trying the stock suit");
            }
            if (prefab == null)
            {
                string fallback = pcm.gender == ProtoCrewMember.Gender.Female ? "kerbalFemale" : "kerbalMale";
                prefab = AssetBase.GetPrefab(fallback);
                if (prefab != null) prefabName = prefabName + "->" + fallback;
            }
            return prefab;
        }

        /// <summary>An unnamed (unused) Unity layer so no scene camera draws the rig; Internal Space as a last resort.</summary>
        private static int PickLayer()
        {
            int[] candidates = { 3, 6, 7 };
            foreach (int l in candidates)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return 20;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static FieldInfo _canvasField;
        private static bool _canvasFieldLooked;

        /// <summary>
        /// Camera.Render() fires Canvas.willRenderCanvases, which makes every UI canvas rebuild mid-frame; the stock
        /// Kerbal class blanks that event around its render and so do we.
        /// </summary>
        private static void RenderWithoutCanvasRebuild(Camera cam)
        {
            if (!_canvasFieldLooked)
            {
                _canvasFieldLooked = true;
                try
                {
                    Type canvas = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule");
                    _canvasField = canvas != null ? canvas.GetField("willRenderCanvases", BindingFlags.Static | BindingFlags.NonPublic) : null;
                }
                catch (Exception) { _canvasField = null; }
            }
            if (_canvasField == null)
            {
                cam.Render();
                return;
            }
            object saved = _canvasField.GetValue(null);
            try
            {
                _canvasField.SetValue(null, null);
                cam.Render();
            }
            finally
            {
                _canvasField.SetValue(null, saved);
            }
        }
    }
}
