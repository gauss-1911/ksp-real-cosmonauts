using UnityEngine;

namespace RealCosmonauts
{
    /// <summary>Thin wrapper that prefixes every message so it is easy to find in KSP.log.</summary>
    internal static class Log
    {
        private const string Prefix = "[RealCosmonauts] ";

        public static void Info(string message) => Debug.Log(Prefix + message);
        public static void Warning(string message) => Debug.LogWarning(Prefix + message);
        public static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
