using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Ai;
using KSA.KSP.Ai;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// AI Setup (Jira KSA-40): edits the user-local ksa_settings.cfg (design mode, cloud API, local model, caps),
    /// tests the connection, shows usage, and dumps the part list. Works on a private copy of the settings until Save.
    /// </summary>
    public sealed class AiSetupWindow
    {
        private const string DialogName = "KSA_AiSetup";
        private const float Width = 720f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 700f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 150f; } }
        private const float LabelWidth = 150f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float ButtonHeight = 26f;
        private const float ModelButtonWidth = 300f;
        private const int MaxInputLength = 400;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        // Unsaved copy of the settings; text inputs write straight into it, Save writes it to disk.
        private readonly AiSettings _settings;
        private readonly List<ModelOption> _models;
        private bool _testing;

        public AiSetupWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
            _settings = KsaSettingsFile.Load();
            _models = KsaSettingsFile.Models();
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            ProbeLocal();
            Spawn();
        }

        public void Close()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Refresh()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
            Spawn();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            KsaCore.Log.Info("AI Setup window: mode " + _settings.DesignMode + ", " + _models.Count + " curated model(s)");

            BuildModeSection(items, textWidth);
            BuildApiSection(items, textWidth);
            BuildLocalSection(items, textWidth);
            BuildUsageSection(items, textWidth);
            items.Add(new DialogGUISpace(SectionGap * 5)); // wrapped labels under-report their height; keep the last row reachable

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var buttons = new DialogGUIHorizontalLayout(true, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton(_testing ? "Testing..." : "Test connection", OnTestConnection, 130f, 30f, false),
                new DialogGUIButton("Save", OnSave, 90f, 30f, false),
                new DialogGUIButton("Dump part list", OnDumpParts, 130f, 30f, false),
                new DialogGUIFlexibleSpace(),
                new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Choose a design mode, enter your key or local model, then Save.", Width - 40f, 2 * LineHeight),
                list,
                buttons);

            var dialog = new MultiOptionDialog(DialogName, "", "AI Setup", KsaUi.Skin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        // --- Design mode ---

        private void BuildModeSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "Design mode", textWidth);
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                ModeButton(DesignMode.Off, "Off"),
                ModeButton(DesignMode.Local, "Local"),
                ModeButton(DesignMode.Api, "API"),
                ModeButton(DesignMode.Hybrid, "Hybrid")));
            Line(items, "Off: the engineering team flies the template library. No AI calls are ever made.", textWidth);
            Line(items, "Local: Ollama or LM Studio on this PC does design and narrative. Free, slower, needs a capable model.", textWidth);
            Line(items, "API: OpenRouter (cloud) does design and after-action reports. Needs a key; spend is capped below.", textWidth);
            Line(items, "Hybrid: cloud for design and after-action reports, local for narrative; each falls back to the other.", textWidth);
            Line(items, "Current file: " + (_settings.AiEnabled ? "<color=#8be28b>AI route configured</color>" : "<color=#ffd27f>no usable AI route; templates will fly</color>")
                + "  |  agency now: " + (_agency.AiDesignAvailable ? "AI design available" : "AI design off"), textWidth);
        }

        private DialogGUIButton ModeButton(DesignMode mode, string label)
        {
            bool sel = _settings.DesignMode == mode;
            return new DialogGUIButton((sel ? "> " : "") + label, () => OnSetMode(mode), 110f, ButtonHeight, false);
        }

        // --- Cloud API ---

        private void BuildApiSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "Cloud API (OpenRouter)", textWidth);
            if (_settings.HasApiKey && !_editingKey)
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("API key", LabelWidth, LineHeight),
                    new DialogGUILabel(MaskKey(_settings.ApiKey) + "  (stored encrypted)", 260f, LineHeight),
                    new DialogGUIButton("Change key", () => { _editingKey = true; Refresh(); }, 110f, ButtonHeight, false),
                    new DialogGUIButton("Remove key", () => { _settings.ApiKey = string.Empty; _status = "API key removed (not saved yet)."; Refresh(); }, 110f, ButtonHeight, false)));
            else
                items.Add(Input("API key", _editingKey ? string.Empty : _settings.ApiKey, textWidth, v => { string k = (v ?? string.Empty).Trim(); if (k.Length > 0) _settings.ApiKey = k; return k; },
                    _editingKey ? "paste the new key; the old one stays until you Save" : "no key"));
            items.Add(Input("Endpoint", _settings.ApiEndpoint, textWidth, v => { _settings.ApiEndpoint = (v ?? string.Empty).Trim(); return _settings.ApiEndpoint; }, null));

            Line(items, "Design model (" + (_settings.DesignModel.Length > 0 ? _settings.DesignModel : "none") + "):", textWidth);
            List<ModelOption> design = ModelsFor(AiTask.Design, false);
            if (design.Count == 0) Line(items, "No curated design models in ai_models.cfg; type a model id below.", textWidth);
            ButtonRows(items, design.Count, textWidth, ModelButtonWidth, i =>
            {
                ModelOption m = design[i];
                string id = m.Id;
                bool sel = string.Equals(_settings.DesignModel, id, StringComparison.OrdinalIgnoreCase);
                return new DialogGUIButton((sel ? "> " : "") + ModelLabel(m), () => OnPickDesignModel(id), ModelButtonWidth, ButtonHeight, false);
            });
            items.Add(Input("Custom design model", _settings.DesignModel, textWidth, v => { _settings.DesignModel = (v ?? string.Empty).Trim(); return _settings.DesignModel; }, null));

            Line(items, "After-action report model (" + (_settings.PostMortemModel.Length > 0 ? _settings.PostMortemModel : "same as design") + "):", textWidth);
            List<ModelOption> post = ModelsFor(AiTask.PostMortem, false);
            ButtonRows(items, post.Count, textWidth, ModelButtonWidth, i =>
            {
                ModelOption m = post[i];
                string id = m.Id;
                bool sel = string.Equals(_settings.PostMortemModel, id, StringComparison.OrdinalIgnoreCase);
                return new DialogGUIButton((sel ? "> " : "") + ModelLabel(m), () => OnPickPostMortemModel(id), ModelButtonWidth, ButtonHeight, false);
            });
            items.Add(Input("Custom after-action report model", _settings.PostMortemModel, textWidth, v => { _settings.PostMortemModel = (v ?? string.Empty).Trim(); return _settings.PostMortemModel; }, null));

            items.Add(Input("Max calls per day", _settings.MaxCallsPerDay.ToString(), textWidth, v =>
            {
                int n;
                if (int.TryParse((v ?? string.Empty).Trim(), out n) && n >= 0) _settings.MaxCallsPerDay = n;
                return _settings.MaxCallsPerDay.ToString();
            }, null));
            items.Add(Input("Monthly cap (USD)", _settings.MonthlySpendCapUsd.ToString("0.##"), textWidth, v =>
            {
                double d;
                if (double.TryParse((v ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) && d >= 0)
                    _settings.MonthlySpendCapUsd = d;
                return _settings.MonthlySpendCapUsd.ToString("0.##");
            }, null));
        }

        // --- Local ---

        private void BuildLocalSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "Local AI (free, runs on this PC)", textWidth);
            string model = RecommendedLocalModel();
            string size = RecommendedLocalSize();
            bool haveRecommended = HasDiscovered(_settings.LocalModel) || HasDiscovered(model);
            if (!_probeDone)
            {
                Line(items, "Checking for Ollama on this PC...", textWidth);
            }
            else if (!_probeOk)
            {
                Line(items, Amber("Step 1: install Ollama. It runs in the background and needs no setup."), textWidth);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    new DialogGUIButton("Get Ollama (opens ollama.com)", () => Application.OpenURL("https://ollama.com/download"), 230f, ButtonHeight, false),
                    new DialogGUIButton("Check again", ProbeLocal, 110f, ButtonHeight, false),
                    new DialogGUILabel("Install it, let it start, then press Check again.", textWidth - 380f, LineHeight)));
            }
            else if (_pulling)
            {
                Line(items, "Downloading " + _pullingModel + " (" + size + ")... keep playing; this line changes when it is done.", textWidth);
            }
            else if (_discovered.Count == 0)
            {
                Line(items, Green("Ollama is running.") + " Step 2: pick a size. One press downloads it, tests it and saves it; nothing to type.", textWidth);
                SizeRow(items, textWidth);
            }
            else
            {
                string current = _settings.LocalModel.Length > 0 && HasDiscovered(_settings.LocalModel) ? _settings.LocalModel : _discovered[0];
                bool on = _settings.LocalEnabled && (_settings.DesignMode == DesignMode.Local || _settings.DesignMode == DesignMode.Hybrid);
                Line(items, Green("Ollama is running with " + _discovered.Count + " model(s).") + " Using: <b>" + current + "</b>" + (on ? "" : "  (press Use it to switch local AI on)"), textWidth);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    new DialogGUIButton("Use it", () => UseLocal(current), 110f, ButtonHeight, false),
                    new DialogGUIButton("Check again", ProbeLocal, 110f, ButtonHeight, false),
                    new DialogGUILabel("Or switch size (downloads if needed, then tests and saves):", textWidth - 250f, LineHeight)));
                SizeRow(items, textWidth);
                if (_discovered.Count > 1)
                {
                    Line(items, "Models on this PC (click to switch):", textWidth);
                    ButtonRows(items, _discovered.Count, textWidth, ModelButtonWidth, i =>
                    {
                        string id = _discovered[i];
                        bool sel = string.Equals(_settings.LocalModel, id, StringComparison.OrdinalIgnoreCase);
                        return new DialogGUIButton((sel ? "> " : "") + id, () => UseLocal(id), ModelButtonWidth, ButtonHeight, false);
                    });
                }
            }

            items.Add(new DialogGUISpace(4f));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton((_advanced ? "[x] " : "[ ] ") + "Advanced", () => { _advanced = !_advanced; Refresh(); }, 120f, ButtonHeight, false),
                new DialogGUILabel("Other servers (llama.cpp, LM Studio), custom endpoints and models.", textWidth - 150f, LineHeight)));
            if (!_advanced) return;

            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton((_settings.LocalEnabled ? "[x] " : "[ ] ") + "Local enabled", OnToggleLocal, 160f, ButtonHeight, false),
                new DialogGUILabel("The endpoint is the server's OpenAI-compatible chat URL.", textWidth - 190f, LineHeight)));
            items.Add(Input("Local endpoint", _settings.LocalEndpoint, textWidth, v => { _settings.LocalEndpoint = (v ?? string.Empty).Trim(); return _settings.LocalEndpoint; }, null));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUILabel("Presets:", 60f, LineHeight),
                new DialogGUIButton("Ollama :11434", () => OnLocalPreset("http://localhost:11434/v1/chat/completions"), 120f, ButtonHeight, false),
                new DialogGUIButton("llama.cpp :8081", () => OnLocalPreset("http://localhost:8081/v1/chat/completions"), 120f, ButtonHeight, false),
                new DialogGUIButton("LM Studio :1234", () => OnLocalPreset("http://localhost:1234/v1/chat/completions"), 120f, ButtonHeight, false),
                new DialogGUIButton("List models on server", ProbeLocal, 170f, ButtonHeight, false)));
            List<ModelOption> local = ModelsFor(AiTask.Narrative, true);
            if (local.Count > 0)
            {
                Line(items, "Suggested models (download with the button below, or load one on your own server):", textWidth);
                ButtonRows(items, local.Count, textWidth, ModelButtonWidth, i =>
                {
                    ModelOption m = local[i];
                    string id = m.Id;
                    bool sel = string.Equals(_settings.LocalModel, id, StringComparison.OrdinalIgnoreCase);
                    return new DialogGUIButton((sel ? "> " : "") + m.Label, () => OnPickLocalModel(id), ModelButtonWidth, ButtonHeight, false);
                });
            }
            items.Add(Input("Custom local model", _settings.LocalModel, textWidth, v => { _settings.LocalModel = (v ?? string.Empty).Trim(); return _settings.LocalModel; }, null));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUIButton(_pulling ? "Downloading..." : "Download this model on Ollama", () => PullModel(_settings.LocalModel, false), 220f, ButtonHeight, false),
                new DialogGUIButton((_settings.AutoStartLocalServer ? "[x] " : "[ ] ") + "Start server with the game", () => { _settings.AutoStartLocalServer = !_settings.AutoStartLocalServer; Refresh(); }, 220f, ButtonHeight, false),
                new DialogGUILabel("llama.cpp only: KSA starts the exe below when nothing answers.", textWidth - 470f, 2 * LineHeight)));
            items.Add(Input("Server exe", _settings.LocalServerExe, textWidth, v => { _settings.LocalServerExe = (v ?? string.Empty).Trim(); return _settings.LocalServerExe; }, null));
            items.Add(Input("Server arguments", _settings.LocalServerArgs, textWidth, v => { _settings.LocalServerArgs = (v ?? string.Empty).Trim(); return _settings.LocalServerArgs; }, null));
        }

        private readonly List<string> _discovered = new List<string>();
        private bool _pulling;
        private string _pullingModel = string.Empty;
        private bool _advanced;
        private bool _probeDone;
        private bool _probeOk;
        private bool _saveAfterTest;
        private bool _editingKey;

        private static string Green(string s) { return "<color=#8be28b>" + s + "</color>"; }
        private static string Amber(string s) { return "<color=#ffd27f>" + s + "</color>"; }

        private bool HasDiscovered(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (string d in _discovered) if (string.Equals(d, id, StringComparison.OrdinalIgnoreCase) || string.Equals(d, id + ":latest", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private sealed class LocalSize { public string Label, Model, Size; public int MinVram; }
        private static readonly LocalSize[] Sizes =
        {
            new LocalSize { Label = "Tiny", Model = "gemma3:1b", Size = "0.8 GB", MinVram = 0 },
            new LocalSize { Label = "Small", Model = "gemma3:4b", Size = "3.3 GB", MinVram = 6000 },
            new LocalSize { Label = "Standard", Model = "qwen2.5:7b", Size = "4.7 GB", MinVram = 11000 }
        };

        /// <summary>One button per model size: the one that fits this card is marked; pressing downloads if needed, then tests and saves.</summary>
        private void SizeRow(List<DialogGUIBase> items, float textWidth)
        {
            string best = RecommendedLocalModel();
            foreach (LocalSize s in Sizes)
            {
                string model = s.Model;
                bool fits = SystemInfo.graphicsMemorySize >= s.MinVram;
                bool have = HasDiscovered(model);
                string text = (model == best ? "> " : "") + s.Label + " (" + s.Size + ")";
                string note = (have ? "on this PC. " : "") + (model == best ? "Fits this PC." : fits ? "" : "Too big for this card.");
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUISpace(16f),
                    new DialogGUIButton(text, () => EnsureAndUse(model), 190f, ButtonHeight, false),
                    new DialogGUILabel(s.Model + "  " + note, textWidth - 220f, LineHeight)));
            }
            Line(items, "Graphics memory on this PC: " + SystemInfo.graphicsMemorySize + " MB. The game keeps 2 to 3 GB of it; the rest is for the model.", textWidth);
        }

        private void EnsureAndUse(string model)
        {
            if (HasDiscovered(model)) UseLocal(model); else PullModel(model, true);
        }

        /// <summary>KSP itself holds 2 to 3 GB of graphics memory while running, and a 7B model wants 6 GB beside it: only cards of 11 GB and up get the 7B, the rest the 4B.</summary>
        private static string RecommendedLocalModel() { return SystemInfo.graphicsMemorySize >= 11000 ? "qwen2.5:7b" : SystemInfo.graphicsMemorySize >= 6000 ? "gemma3:4b" : "gemma3:1b"; }
        private static string RecommendedLocalSize() { return SystemInfo.graphicsMemorySize >= 11000 ? "about 4.7 GB" : SystemInfo.graphicsMemorySize >= 6000 ? "about 3.3 GB" : "about 0.8 GB"; }

        private string LocalBase()
        {
            string url = _settings.LocalEndpoint ?? string.Empty;
            int cut = url.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
            return cut > 0 ? url.Substring(0, cut) : url.TrimEnd('/');
        }

        /// <summary>Asks Ollama (or any OpenAI-compatible server) what it has; drives the step-by-step text.</summary>
        private void ProbeLocal()
        {
            string modelsUrl = LocalBase() + "/v1/models";
            _probeDone = false;
            Ai.UnityWebTransport.Instance.GetText(modelsUrl, 4, (ok, body) =>
            {
                _probeDone = true;
                _probeOk = ok;
                _discovered.Clear();
                if (ok)
                {
                    try
                    {
                        object parsed = KSA.Core.Data.Json.Parse(body);
                        var map = parsed as Dictionary<string, object>;
                        object data;
                        if (map != null && map.TryGetValue("data", out data) && data is List<object>)
                            foreach (object o in (List<object>)data)
                            {
                                var m = o as Dictionary<string, object>;
                                object id;
                                if (m != null && m.TryGetValue("id", out id) && id is string && _discovered.Count < 24) _discovered.Add((string)id);
                            }
                    }
                    catch (Exception e) { KsaCore.Log.Warn("Model list unreadable: " + e.Message); }
                }
                KsaCore.Log.Info("AI Setup: local probe " + (ok ? _discovered.Count + " model(s)" : "no server (" + body + ")"));
                if (_dialog != null) Refresh();
            });
        }

        /// <summary>Selects a local model, switches local AI on, tests it, and saves when the test passes.</summary>
        private void UseLocal(string model)
        {
            _settings.LocalModel = model;
            _settings.LocalEnabled = true;
            if (_settings.DesignMode == DesignMode.Off) _settings.DesignMode = DesignMode.Local;
            else if (_settings.DesignMode == DesignMode.Api) _settings.DesignMode = DesignMode.Hybrid;
            _saveAfterTest = true;
            OnTestConnection();
        }

        /// <summary>Ollama only: POST /api/pull downloads a model (several GB; the game keeps running). With setup, it is then selected, tested and saved.</summary>
        private void PullModel(string model, bool setup)
        {
            if (_pulling) return;
            model = (model ?? string.Empty).Trim();
            if (model.Length == 0) { _status = Amber("Pick or type a local model first."); Refresh(); return; }
            string pullUrl = LocalBase() + "/api/pull";
            _pulling = true;
            _pullingModel = model;
            _status = "Downloading " + model + "... keep playing; the status changes when it is done.";
            Refresh();
            var req = new LlmRequest { Url = pullUrl, Body = "{\"name\": \"" + model.Replace("\"", "") + "\", \"stream\": false}", TimeoutSeconds = 7200 };
            req.Headers["Content-Type"] = "application/json";
            Ai.UnityWebTransport.Instance.Send(req, resp =>
            {
                _pulling = false;
                bool ok = resp != null && resp.Ok;
                KsaCore.Log.Info("AI Setup: pull " + model + " " + (ok ? "ok" : "failed: " + (resp != null ? resp.Error : "no reply")));
                if (!ok)
                {
                    _status = "<color=#ff8080>Download failed: " + (resp != null ? (resp.Error ?? ("HTTP " + resp.Status)) : "no reply") + ".</color> Is Ollama running? Press Check again and retry.";
                    if (_dialog != null) Refresh();
                    return;
                }
                _discovered.Add(model);
                _status = Green("Downloaded " + model + ".");
                if (setup) UseLocal(model); else if (_dialog != null) Refresh();
            });
        }

        private void OnLocalPreset(string url)
        {
            _settings.LocalEndpoint = url;
            _settings.LocalEnabled = true;
            _discovered.Clear();
            _status = "Local endpoint set to " + url + " (not saved yet).";
            ProbeLocal();
            Refresh();
        }

        // --- Usage ---

        private void BuildUsageSection(List<DialogGUIBase> items, float textWidth)
        {
            Section(items, "Usage", textWidth);
            int day = _agency.CurrentDay;
            Line(items, "Calls today: " + _agency.Costs.CallsOn(day) + " of " + _settings.MaxCallsPerDay
                + "  |  last 30 days: " + _agency.Costs.CallsLast30Days(day) + " calls, $" + _agency.Costs.SpendLast30Days(day).ToString("0.00")
                + " of $" + _settings.MonthlySpendCapUsd.ToString("0.##"), textWidth);
            Line(items, "Parts offered to the team: " + (_agency.Parts != null ? _agency.Parts.All.Count : 0)
                + "  |  prompt logging " + (_settings.LogPrompts ? "on" : "off"), textWidth);
            Line(items, "<color=#ffd27f>Keys are stored only in " + KsaSettingsFile.SettingsPath + "; never share that file.</color>", textWidth);
        }

        // --- actions ---

        private void OnSetMode(DesignMode mode)
        {
            _settings.DesignMode = mode;
            _status = "Design mode set to " + ModeText(mode) + " (not saved yet).";
            Refresh();
        }

        private void OnPickDesignModel(string id)
        {
            _settings.DesignModel = id;
            _status = "Design model: " + id + " (not saved yet).";
            Refresh();
        }

        private void OnPickPostMortemModel(string id)
        {
            _settings.PostMortemModel = id;
            _status = "After-action report model: " + id + " (not saved yet).";
            Refresh();
        }

        private void OnPickLocalModel(string id)
        {
            _settings.LocalModel = id;
            _status = "Local model: " + id + " (not saved yet).";
            Refresh();
        }

        private void OnToggleLocal()
        {
            _settings.LocalEnabled = !_settings.LocalEnabled;
            _status = "Local AI " + (_settings.LocalEnabled ? "enabled" : "disabled") + " (not saved yet).";
            Refresh();
        }

        private void OnSave()
        {
            string error;
            bool ok = KsaSettingsFile.Save(_settings, out error);
            if (ok)
            {
                KsaCore.ReloadAiSettings();
                _status = "Saved to " + KsaSettingsFile.SettingsPath + ". AI design is now " + (_agency.AiDesignAvailable ? "available." : "off.");
            }
            else _status = "<color=#ff8080>Save failed: " + error + "</color>";
            Refresh();
        }

        private void OnDumpParts()
        {
            KSA.Core.Parts.PartCatalog catalog = _agency.Parts as KSA.Core.Parts.PartCatalog ?? KSA.KSP.Parts.KspPartCatalog.Current;
            string path = KSA.KSP.Parts.KspPartCatalog.Dump(catalog);
            _status = "Part list (" + catalog.All.Count + " parts) written to " + path;
            Refresh();
        }

        /// <summary>Sends one tiny chat to the Design endpoint of the unsaved settings. Never blocks: the result arrives on a later frame.</summary>
        private void OnTestConnection()
        {
            if (_testing) { _status = "A test is already running."; Refresh(); return; }
            LlmEndpoint endpoint = _settings.EndpointFor(AiTask.Design);
            if (endpoint == null)
            {
                _status = "<color=#ffd27f>Nothing to test: mode " + ModeText(_settings.DesignMode)
                    + " has no usable route. Check the key, model, or local settings.</color>";
                Refresh();
                return;
            }
            _testing = true;
            _status = "Testing " + endpoint.Provider + " (" + endpoint.Model + ") at " + endpoint.Url + "...";
            Refresh();
            try
            {
                var client = new LlmClient(UnityWebTransport.Instance, KsaCore.Log);
                client.Chat(endpoint, "You are a connectivity check.", "Reply with the single word OK.", 0.0, false, OnTestDone);
            }
            catch (Exception e)
            {
                _testing = false;
                _status = "<color=#ff8080>Test could not start: " + e.Message + "</color>";
                Refresh();
            }
        }

        private void OnTestDone(LlmResult result)
        {
            _testing = false;
            if (result != null && result.Ok)
            {
                string text = (result.Text ?? string.Empty).Trim();
                if (text.Length > 40) text = text.Substring(0, 40) + "...";
                _status = "<color=#8be28b>Connection OK</color> after " + result.Attempts + " attempt" + (result.Attempts == 1 ? "" : "s") + ": \"" + text + "\"";
                if (_saveAfterTest) { _saveAfterTest = false; string err; if (KsaSettingsFile.Save(_settings, out err)) { KsaCore.ReloadAiSettings(); _status += " Saved: local AI is on."; } else _status += " (save failed: " + err + ")"; }
                KsaCore.Log.Info("AI Setup: connection test passed");
            }
            else
            {
                _saveAfterTest = false;
                string err = result != null ? result.Error : "no result";
                string hint = string.Empty;
                if (err != null && err.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 && err.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                    hint = " The server is up but that model is not on it: press 'Pull on Ollama' below, or press 'List models on server' and pick one it has.";
                else if (err != null && (err.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0))
                    hint = " Nothing is listening at the endpoint: start Ollama / llama-server, or tick 'Start server with the game'.";
                _status = "<color=#ff8080>Connection failed: " + err + "</color>" + hint;
                KsaCore.Log.Warn("AI Setup: connection test failed: " + err);
            }
            if (_dialog != null) Refresh();
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private List<ModelOption> ModelsFor(AiTask task, bool local)
        {
            var list = new List<ModelOption>();
            foreach (ModelOption m in _models)
                if (m.Task == task && m.Local == local) list.Add(m);
            return list;
        }

        private static string ModelLabel(ModelOption m)
        {
            return m.Label + " (" + m.Tier + ", about $" + (m.CostPer1k * 7.0).ToString("0.000") + "/design)";
        }

        private static string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "none";
            if (key.Length <= 4) return "****";
            return "****" + key.Substring(key.Length - 4);
        }

        private static string ModeText(DesignMode m)
        {
            return m == DesignMode.Api ? "API" : m.ToString();
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

        /// <summary>A labelled single-line text input that writes into the unsaved settings; an optional note sits to its right.</summary>
        private static DialogGUIHorizontalLayout Input(string label, string value, float textWidth, Func<string, string> set, string note)
        {
            float noteWidth = note != null ? 150f : 0f;
            float inputWidth = textWidth - LabelWidth - noteWidth - 12f;
            var input = new DialogGUITextInput(value ?? string.Empty, false, MaxInputLength, set, LineHeight + 4f);
            input.size = new Vector2(inputWidth, LineHeight + 4f);
            var parts = new List<DialogGUIBase>();
            parts.Add(new DialogGUILabel("<b>" + label + "</b>", LabelWidth, LineHeight));
            parts.Add(input);
            if (note != null) parts.Add(new DialogGUILabel(note, noteWidth, LineHeight));
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, parts.ToArray());
        }

        /// <summary>Lays out <paramref name="count"/> buttons in rows that fit the text width.</summary>
        private static void ButtonRows(List<DialogGUIBase> items, int count, float textWidth, float buttonWidth, Func<int, DialogGUIBase> make)
        {
            int perRow = Math.Max(1, (int)((textWidth - 16f) / (buttonWidth + 6f)));
            for (int start = 0; start < count; start += perRow)
            {
                var row = new List<DialogGUIBase>();
                row.Add(new DialogGUISpace(16f));
                for (int i = start; i < count && i < start + perRow; i++) row.Add(make(i));
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, row.ToArray()));
            }
        }

        private static void Section(List<DialogGUIBase> items, string title, float textWidth)
        {
            items.Add(new DialogGUISpace(SectionGap));
            items.Add(new DialogGUILabel("<b>" + title + "</b>", textWidth, LineHeight + 2f));
        }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUIHorizontalLayout(false, false, 0f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUILabel(text, textWidth - 16f, KsaUi.LabelHeight(text, textWidth - 16f, LineHeight))));
        }
    }
}
