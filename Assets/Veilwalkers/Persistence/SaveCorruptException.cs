using System;

namespace Veilwalkers.Persistence
{
    /// <summary>
    /// Thrown when a save file cannot be trusted: MAC mismatch (tampering), decrypt
    /// failure, JSON parse failure, unknown schema version, or structural validation
    /// failure. Load paths throw THIS — they never return null and never silently
    /// wipe the file — so the app can surface the explicit recovery choice
    /// (retry / start fresh) required by AC-3.
    /// </summary>
    public sealed class SaveCorruptException : Exception
    {
        public SaveCorruptException(string message)
            : base(message)
        {
        }

        public SaveCorruptException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
