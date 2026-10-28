using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Ai
{
    public enum DesignMode { Off = 0, Local = 1, Api = 2, Hybrid = 3 }

    public enum AiTask { Design = 0, PostMortem = 1, Narrative = 2, Director = 3 }

    /// <summary>
    /// The user-local AI configuration (GDD 6.5), read from PluginData/ksa_settings.cfg by the host.
    /// The API key lives only here and in memory; it is never logged (Jira KSA-40).
    /// </summary>
    public sealed class AiSettings
    {
        public const string NodeName = "KSA_SETTINGS";

        public DesignMode DesignMode { get; set; } = DesignMode.Off;

        // API
        public string Provider { get; set; } = "OpenRouter";
        public string ApiEndpoint { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
        public string ApiKey { get; set; } = string.Empty;
        public string DesignModel { get; set; } = string.Empty;
        public string PostMortemModel { get; set; } = string.Empty;
        /// <summary>OpenRouter model for the AI Director (KSA-68); empty = <see cref="DefaultDirectorModel"/>.</summary>
        public string DirectorModel { get; set; } = string.Empty;
        public const string DefaultDirectorModel = "anthropic/claude-haiku-5.5";
        public int MaxCallsPerDay { get; set; } = 50;
        public double MonthlySpendCapUsd { get; set; } = 5;

        // Local
        public string LocalEndpoint { get; set; } = "http://localhost:11434/v1/chat/completions";
        public string LocalModel { get; set; } = string.Empty;
        public bool LocalEnabled { get; set; } = true;
        /// <summary>Optional: a local server the host starts with the game when the endpoint is not answering (e.g. llama-server.exe).</summary>
        public string LocalServerExe { get; set; } = string.Empty;
        public string LocalServerArgs { get; set; } = string.Empty;
        public bool AutoStartLocalServer { get; set; } = false;

        // Fallback
        public string OnApiFailure { get; set; } = "UseTemplateLibrary";
        public string OnLocalFailure { get; set; } = "UseCannedText";

        public bool LogPrompts { get; set; } = false;

        public bool HasApiKey { get { return !string.IsNullOrEmpty(ApiKey) && ApiKey != "YOUR_KEY_HERE"; } }

        /// <summary>True when any AI route is usable for design work.</summary>
        public bool AiEnabled
        {
            get
            {
                switch (DesignMode)
                {
                    case DesignMode.Api: return HasApiKey && DesignModel.Length > 0;
                    case DesignMode.Local: return LocalEnabled && LocalModel.Length > 0;
                    case DesignMode.Hybrid: return (HasApiKey && DesignModel.Length > 0) || (LocalEnabled && LocalModel.Length > 0);
                    default: return false;
                }
            }
        }

        /// <summary>Where a task should go under the current mode, or null when nothing is configured.</summary>
        public LlmEndpoint EndpointFor(AiTask task)
        {
            bool apiOk = HasApiKey && DesignModel.Length > 0;
            bool localOk = LocalEnabled && LocalModel.Length > 0;
            switch (DesignMode)
            {
                case DesignMode.Api:
                    return apiOk ? Api(task) : null;
                case DesignMode.Local:
                    return localOk ? Local() : null;
                case DesignMode.Hybrid:
                    // Design and post-mortems go to the cloud when possible; narrative stays local (GDD 6.1).
                    if (task == AiTask.Narrative) return localOk ? Local() : (apiOk ? Api(task) : null);
                    return apiOk ? Api(task) : (localOk ? Local() : null);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Where the AI Director thinks (KSA-68): the API key and the director model when there is a key, whatever the design
        /// mode; else the local model; else nowhere. Separate from <see cref="EndpointFor"/> so turning design AI off does not
        /// stop a Director the player switched on.
        /// </summary>
        public LlmEndpoint DirectorEndpoint()
        {
            if (HasApiKey) return new LlmEndpoint { Url = ApiEndpoint, ApiKey = ApiKey, Model = DirectorModel.Length > 0 ? DirectorModel : DefaultDirectorModel, Provider = Provider, IsLocal = false };
            if (LocalEnabled && LocalModel.Length > 0) return Local();
            return null;
        }

        private LlmEndpoint Api(AiTask task)
        {
            string model = task == AiTask.PostMortem && PostMortemModel.Length > 0 ? PostMortemModel : DesignModel;
            return new LlmEndpoint { Url = ApiEndpoint, ApiKey = ApiKey, Model = model, Provider = Provider, IsLocal = false };
        }

        private LlmEndpoint Local()
        {
            return new LlmEndpoint { Url = LocalEndpoint, ApiKey = string.Empty, Model = LocalModel, Provider = "Local", IsLocal = true };
        }

        /// <summary>KSP's config reader treats "//" as a comment, so URLs are stored with the slashes percent-encoded.</summary>
        public static string EscapeUrl(string url) { return (url ?? string.Empty).Replace("//", "%2F%2F"); }

        /// <summary>Decodes a stored URL; a value cut off at "http:" by an older plain-text save falls back to the default.</summary>
        public static string UnescapeUrl(string stored, string fallback)
        {
            string u = (stored ?? string.Empty).Replace("%2F", "/").Replace("%2f", "/").Trim();
            if (u.Length == 0 || u.EndsWith(":") || (u.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) && !u.Contains("//"))) return fallback;
            return u;
        }

        public static AiSettings Parse(DataNode node)
        {
            var s = new AiSettings();
            if (node == null) return s;
            DesignMode mode;
            string modeText = node.GetValue("designMode", "Off");
            if (string.Equals(modeText, "API", StringComparison.OrdinalIgnoreCase)) mode = DesignMode.Api;
            else if (!Enum.TryParse(modeText, true, out mode)) mode = DesignMode.Off;
            s.DesignMode = mode;
            s.LogPrompts = node.GetBool("logPrompts", false);

            DataNode api = node.GetChild("API");
            if (api != null)
            {
                s.Provider = api.GetValue("provider", s.Provider);
                s.ApiEndpoint = UnescapeUrl(api.GetValue("endpoint", s.ApiEndpoint), s.ApiEndpoint);
                s.ApiKey = api.GetValue("apiKey", string.Empty).Trim();
                s.DesignModel = CleanModel(api.GetValue("designModel", string.Empty));
                s.PostMortemModel = CleanModel(api.GetValue("postMortemModel", string.Empty));
                s.DirectorModel = CleanModel(api.GetValue("directorModel", string.Empty));
                s.MaxCallsPerDay = api.GetInt("maxCallsPerDay", s.MaxCallsPerDay);
                s.MonthlySpendCapUsd = api.GetDouble("monthlySpendCapUSD", s.MonthlySpendCapUsd);
            }
            DataNode local = node.GetChild("LOCAL");
            if (local != null)
            {
                s.LocalEndpoint = UnescapeUrl(local.GetValue("endpoint", s.LocalEndpoint), s.LocalEndpoint);
                s.LocalModel = CleanModel(local.GetValue("model", string.Empty));
                s.LocalEnabled = local.GetBool("enabled", true);
                s.LocalServerExe = local.GetValue("serverExe", s.LocalServerExe);
                s.LocalServerArgs = local.GetValue("serverArgs", s.LocalServerArgs);
                s.AutoStartLocalServer = local.GetBool("autoStart", s.AutoStartLocalServer);
            }
            DataNode fb = node.GetChild("FALLBACK");
            if (fb != null)
            {
                s.OnApiFailure = fb.GetValue("onApiFailure", s.OnApiFailure);
                s.OnLocalFailure = fb.GetValue("onLocalFailure", s.OnLocalFailure);
            }
            return s;
        }

        /// <summary>Placeholders from the example file count as unset.</summary>
        private static string CleanModel(string m)
        {
            m = (m ?? string.Empty).Trim();
            return m.StartsWith("<") ? string.Empty : m;
        }

        public DataNode ToDataNode()
        {
            var n = new DataNode(NodeName);
            n.AddValue("designMode", DesignMode == DesignMode.Api ? "API" : DesignMode.ToString());
            n.AddValue("logPrompts", LogPrompts);
            DataNode api = n.AddChild("API");
            api.AddValue("provider", Provider);
            api.AddValue("endpoint", EscapeUrl(ApiEndpoint));
            api.AddValue("apiKey", ApiKey);
            api.AddValue("designModel", DesignModel);
            api.AddValue("postMortemModel", PostMortemModel);
            api.AddValue("directorModel", DirectorModel);
            api.AddValue("maxCallsPerDay", MaxCallsPerDay);
            api.AddValue("monthlySpendCapUSD", MonthlySpendCapUsd);
            DataNode local = n.AddChild("LOCAL");
            local.AddValue("endpoint", EscapeUrl(LocalEndpoint));
            local.AddValue("model", LocalModel);
            local.AddValue("enabled", LocalEnabled);
            local.AddValue("serverExe", LocalServerExe);
            local.AddValue("serverArgs", LocalServerArgs);
            local.AddValue("autoStart", AutoStartLocalServer);
            DataNode fb = n.AddChild("FALLBACK");
            fb.AddValue("onApiFailure", OnApiFailure);
            fb.AddValue("onLocalFailure", OnLocalFailure);
            return n;
        }
    }

    /// <summary>One curated model option from ai_models.cfg (decision 13).</summary>
    public sealed class ModelOption
    {
        public const string NodeName = "KSA_AI_MODEL";

        public AiTask Task { get; set; }
        public string Id { get; set; }
        public string Label { get; set; }
        /// <summary>"budget" or "flagship".</summary>
        public string Tier { get; set; }
        public bool Local { get; set; }
        /// <summary>Rough USD per 1000 tokens (input + output averaged) for the spend estimate.</summary>
        public double CostPer1k { get; set; }
        public bool Default { get; set; }

        public ModelOption() { Id = string.Empty; Label = string.Empty; Tier = "budget"; }

        public static List<ModelOption> Parse(IEnumerable<DataNode> nodes)
        {
            var list = new List<ModelOption>();
            if (nodes == null) return list;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                AiTask task;
                if (!Enum.TryParse(node.GetValue("task", "Design"), true, out task)) continue;
                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0) continue;
                list.Add(new ModelOption
                {
                    Task = task,
                    Id = id,
                    Label = node.GetValue("label", id),
                    Tier = node.GetValue("tier", "budget"),
                    Local = node.GetBool("local", false),
                    CostPer1k = node.GetDouble("costPer1k", 0),
                    Default = node.GetBool("default", false)
                });
            }
            return list;
        }
    }

    /// <summary>Where to send a request.</summary>
    public sealed class LlmEndpoint
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string Provider { get; set; }
        public bool IsLocal { get; set; }

        public LlmEndpoint() { Url = string.Empty; ApiKey = string.Empty; Model = string.Empty; Provider = string.Empty; }
    }
}
