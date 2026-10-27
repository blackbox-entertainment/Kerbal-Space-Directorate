using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using KSA.Core.Ai;
using UnityEngine;
using UnityEngine.Networking;

namespace KSA.KSP.Ai
{
    /// <summary>
    /// Core's ILlmTransport over UnityWebRequest (Jira KSA-41). Requests run as coroutines on a persistent
    /// runner object, so the game never blocks; the callback fires on the main thread when the reply lands.
    /// The Authorization header is never logged.
    /// </summary>
    public sealed class UnityWebTransport : ILlmTransport
    {
        private static UnityWebTransport _instance;
        public static UnityWebTransport Instance { get { return _instance ?? (_instance = new UnityWebTransport()); } }

        public int Pending { get; private set; }

        public void Send(LlmRequest request, Action<LlmResponse> callback)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            Runner.Instance.StartCoroutine(Run(request, callback));
        }

        private IEnumerator Run(LlmRequest request, Action<LlmResponse> callback)
        {
            Pending++;
            if (request.DelaySeconds > 0) yield return new WaitForSecondsRealtime((float)request.DelaySeconds);

            var response = new LlmResponse();
            UnityWebRequest www = null;
            try
            {
                www = new UnityWebRequest(request.Url, "POST");
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body ?? string.Empty));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.timeout = Math.Max(10, request.TimeoutSeconds);
                foreach (KeyValuePair<string, string> h in request.Headers) www.SetRequestHeader(h.Key, h.Value);
            }
            catch (Exception e)
            {
                Pending--;
                response.Ok = false; response.Status = 0; response.Error = "Could not create request: " + e.Message;
                callback(response);
                yield break;
            }

            yield return www.SendWebRequest();

            try
            {
                response.Status = (int)www.responseCode;
                response.Body = www.downloadHandler != null ? www.downloadHandler.text ?? string.Empty : string.Empty;
#pragma warning disable 618
                bool failed = www.isNetworkError || www.isHttpError;
#pragma warning restore 618
                response.Ok = !failed && response.Status >= 200 && response.Status < 300;
                if (!response.Ok) response.Error = string.IsNullOrEmpty(www.error) ? "HTTP " + response.Status : www.error;
            }
            catch (Exception e)
            {
                response.Ok = false; response.Error = e.Message;
            }
            finally
            {
                www.Dispose();
                Pending--;
            }
            try { callback(response); }
            catch (Exception e) { KsaCore.Log.Error("LLM callback threw: " + e); }
        }

        /// <summary>A hidden object that survives scene changes so coroutines keep running across them.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner _runner;
            public static Runner Instance
            {
                get
                {
                    if (_runner != null) return _runner;
                    var go = new GameObject("KSA.LlmTransport");
                    DontDestroyOnLoad(go);
                    _runner = go.AddComponent<Runner>();
                    return _runner;
                }
            }
        }
    }
}
