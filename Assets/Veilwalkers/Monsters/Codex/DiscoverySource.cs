namespace Veilwalkers.Monsters
{
    /// <summary>
    /// How a Monster came to be discovered, recorded by
    /// <see cref="CodexService.RecordDiscoveryAsync"/>. A discovery sets exactly the
    /// flag matching its source (<see cref="Capture"/> → <c>Captured</c>,
    /// <see cref="Slay"/> → <c>Slain</c>); it never implies the other flag. Scan is
    /// deliberately NOT a discovery source — scanning does not discover (Epic 4 owns
    /// scan-flag semantics), so it is absent from this enum.
    /// </summary>
    public enum DiscoverySource
    {
        /// <summary>The Monster was captured (sets <c>CodexEntryData.Captured</c>).</summary>
        Capture,

        /// <summary>The Monster was slain (sets <c>CodexEntryData.Slain</c>).</summary>
        Slay,
    }
}
