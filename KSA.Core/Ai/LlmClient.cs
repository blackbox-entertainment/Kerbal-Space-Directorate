using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Ai
{
    public sealed class LlmRequest
    {
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        public string Body { get; set; }
        public int TimeoutSeconds { get; set; }
        /// <summary>Wait this long before sending (retry backoff). The host honours it without blocking.</summary>
        public double DelaySeconds { get; set; }

        public LlmRequest() { Url = string.Empty; Body = string.Empty; TimeoutSeconds = 90; }
    }

    public sealed class LlmResponse
    {
        public bool Ok { get; set; }
        public int Status { get; set; }
        public string Body { get; set; }
        public string Error { get; set; }

        public LlmResponse() { Body = string.Empty; Error = string.Empty; }
    }

    /// <summary>
    /// The host's HTTP. Must be asynchronous: the callback fires later, on the host's main thread,
    /// and the game never waits (hard rule). Tests use a scripted fake.
    /// </summary>
    public interface ILlmTransport
    {
        void Send(LlmRequest request, Action<LlmResponse> callback);
    }

    public sealed class LlmResult
    {
        public bool Ok { get; set; }
        public string Text { get; set; }
        public string Error { get; set; }
        public int Attempts { get; set; }
        public int PromptChars { get; set; }
        public int ReplyChars { get; set; }
        public string Model { get; set; }

        public LlmResult() { Text = string.Empty; Error = string.Empty; Model = string.Empty; }

        /// <summary>Rough token count for cost estimates (4 chars per token).</summary>
        public int EstimatedTokens { get { return (PromptChars + ReplyChars) / 4; } }
    }

    /// <summary>OpenAI-compatible chat completions over any transport, with retries (Jira KSA-41).</summary>
    public sealed class LlmClient
    {
        public const int MaxAttempts = 3;

        private readonly ILlmTransport _transport;
        private readonly ILogger _log;

        public LlmClient(ILlmTransport transport, ILogger log)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (log == null) throw new ArgumentNullException(nameof(log));
            _transport = transport;
            _log = log;
        }

        public void Chat(LlmEndpoint endpoint, string systemPrompt, string userPrompt, double temperature, bool jsonMode, Action<LlmResult> done)
        {
            if (endpoint == null || string.IsNullOrEmpty(endpoint.Url)) { done(new LlmResult { Ok = false, Error = "No endpoint configured." }); return; }
            string body = BuildBody(endpoint.Model, systemPrompt, userPrompt, temperature, jsonMode);
            int promptChars = (systemPrompt ?? string.Empty).Length + (userPrompt ?? string.Empty).Length;
            Attempt(endpoint, body, promptChars, 1, done);
        }

        private void Attempt(LlmEndpoint endpoint, string body, int promptChars, int attempt, Action<LlmResult> done)
        {
            var req = new LlmRequest { Url = endpoint.Url, Body = body, DelaySeconds = attempt == 1 ? 0 : Math.Pow(2, attempt - 1), TimeoutSeconds = endpoint.IsLocal ? 900 : 120 };
            req.Headers["Content-Type"] = "application/json";
            if (!string.IsNullOrEmpty(endpoint.ApiKey)) req.Headers["Authorization"] = "Bearer " + endpoint.ApiKey;
            if (!endpoint.IsLocal)
            {
                req.Headers["HTTP-Referer"] = "https://github.com/blackbox-entertainment/Kerbal-Space-Directorate";
                req.Headers["X-Title"] = "Kerbal Space Directorate";
            }
            _log.Info("LLM request to " + endpoint.Provider + " model " + endpoint.Model + " (attempt " + attempt + ", " + body.Length + " chars)");

            _transport.Send(req, resp =>
            {
                LlmResult result = Interpret(resp, endpoint, promptChars, attempt);
                if (result.Ok || attempt >= MaxAttempts || !IsTransient(resp))
                {
                    if (!result.Ok) _log.Warn("LLM request failed after " + attempt + " attempt(s): " + result.Error);
                    done(result);
                    return;
                }
                _log.Warn("LLM request attempt " + attempt + " failed (" + result.Error + "); retrying");
                Attempt(endpoint, body, promptChars, attempt + 1, done);
            });
        }

        private static bool IsTransient(LlmResponse resp)
        {
            if (resp == null) return true;
            if (!resp.Ok && resp.Status == 0) return true;      // network / timeout
            return resp.Status == 408 || resp.Status == 429 || resp.Status >= 500;
        }

        public static LlmResult Interpret(LlmResponse resp, LlmEndpoint endpoint, int promptChars, int attempt)
        {
            var r = new LlmResult { Attempts = attempt, PromptChars = promptChars, Model = endpoint != null ? endpoint.Model : string.Empty };
            if (resp == null) { r.Error = "No response."; return r; }
            if (!resp.Ok || resp.Status < 200 || resp.Status >= 300)
            {
                r.Error = "HTTP " + resp.Status + (string.IsNullOrEmpty(resp.Error) ? string.Empty : ": " + resp.Error) + ExtractApiError(resp.Body);
                return r;
            }
            object parsed;
            string err;
            if (!Json.TryParse(resp.Body, out parsed, out err)) { r.Error = "Reply was not JSON: " + err; return r; }
            Dictionary<string, object> obj = Json.AsObject(parsed);
            Dictionary<string, object> apiError = Json.GetObject(obj, "error");
            if (apiError != null) { r.Error = "API error: " + Json.GetString(apiError, "message", "unknown"); return r; }
            List<object> choices = Json.GetArray(obj, "choices");
            if (choices == null || choices.Count == 0) { r.Error = "Reply had no choices."; return r; }
            Dictionary<string, object> message = Json.GetObject(Json.AsObject(choices[0]), "message");
            string content = Json.GetString(message, "content", null);
            if (content == null) { r.Error = "Reply had no message content."; return r; }
            r.Ok = true;
            r.Text = content;
            r.ReplyChars = content.Length;
            return r;
        }

        private static string ExtractApiError(string body)
        {
            object parsed;
            string err;
            if (string.IsNullOrEmpty(body) || !Json.TryParse(body, out parsed, out err)) return string.Empty;
            Dictionary<string, object> e = Json.GetObject(Json.AsObject(parsed), "error");
            string msg = Json.GetString(e, "message", null);
            return msg != null ? " (" + msg + ")" : string.Empty;
        }

        public static string BuildBody(string model, string systemPrompt, string userPrompt, double temperature, bool jsonMode)
        {
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
                messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } });
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", userPrompt ?? string.Empty } });
            var body = new Dictionary<string, object>
            {
                { "model", model ?? string.Empty },
                { "messages", messages },
                { "temperature", temperature }
            };
            if (jsonMode) body["response_format"] = new Dictionary<string, object> { { "type", "json_object" } };
            return Json.Write(body);
        }
    }
}
