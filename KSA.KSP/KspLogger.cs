using UnityEngine;

namespace KSA.KSP
{
    /// <summary>Writes Core log messages to KSP.log with the "[KSA]" prefix.</summary>
    public sealed class KspLogger : Core.ILogger
    {
        private const string Prefix = "[KSA] ";

        public void Info(string message)  { Debug.Log(Prefix + message); }
        public void Warn(string message)  { Debug.LogWarning(Prefix + message); }
        public void Error(string message) { Debug.LogError(Prefix + message); }
    }
}
