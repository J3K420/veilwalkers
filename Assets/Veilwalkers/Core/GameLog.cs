using UnityEngine;

namespace Veilwalkers.Core
{
    /// <summary>
    /// The single logging wrapper for the codebase. Everything logs through
    /// <see cref="GameLog"/> (Debug/Info/Warn/Error); this is the ONLY place raw
    /// <see cref="UnityEngine.Debug"/> logging is permitted in shipping paths, so
    /// logging can later be routed, filtered, or stripped from one location.
    /// </summary>
    public static class GameLog
    {
        /// <summary>
        /// Verbose diagnostic detail. Currently routed to the console unfiltered,
        /// identically to <see cref="Info"/> — level filtering / release stripping is
        /// not implemented yet; when it lands, it lands here.
        /// </summary>
        public static void Debug(string message) => UnityEngine.Debug.Log(message);

        /// <summary>Normal operational information.</summary>
        public static void Info(string message) => UnityEngine.Debug.Log(message);

        /// <summary>A recoverable problem worth attention.</summary>
        public static void Warn(string message) => UnityEngine.Debug.LogWarning(message);

        /// <summary>An error the code did not expect to hit on a healthy path.</summary>
        public static void Error(string message) => UnityEngine.Debug.LogError(message);
    }
}
