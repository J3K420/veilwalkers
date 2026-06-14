namespace Veilwalkers.AR
{
    /// <summary>
    /// The state of the mandatory AR Safety Warning gate (Story 3.2, FR-3). The single source of
    /// truth the <see cref="ArSafetyView"/> renders; computed by <see cref="ArSafetyGate"/>.
    /// <para>
    /// The interaction block (AC-1: "camera view not interactive / no Lure or Encounter action
    /// reachable until acknowledged") is expressed by <see cref="ArSafetyGate.MayInteract"/>, which
    /// is true ONLY in <see cref="Acknowledged"/>. A fast card (<see cref="BlockingFastCard"/>) is
    /// faster to read but STILL blocking — the warning can never be A/B'd away (AC-1).
    /// </para>
    /// </summary>
    public enum ArSafetyGateState
    {
        /// <summary>
        /// Initial / not in AR. No warning showing, AR not entered. <c>MayInteract</c> is false.
        /// </summary>
        Inactive,

        /// <summary>
        /// The full, deliberate-read Safety Warning is up — shown on the first cold entry of the
        /// session (AC-2). The camera view and every Encounter action are BLOCKED until acknowledged
        /// (AC-1: <c>MayInteract</c> false).
        /// </summary>
        BlockingFull,

        /// <summary>
        /// The fast on-brand Safety Warning card is up — shown on later cold entries of the session
        /// (AC-2: mandatory presence, minimal dwell). STILL blocking (<c>MayInteract</c> false): the
        /// warning is faster but never skippable (AC-1, "cannot be A/B'd away").
        /// </summary>
        BlockingFastCard,

        /// <summary>
        /// The player acknowledged the warning (or a Shop resume skipped it, AC-3). The camera view
        /// and Encounter actions are now reachable (AC-1 release: <c>MayInteract</c> true). AR may
        /// proceed — a hand-off signal only; starting the AR session is Story 3.3.
        /// </summary>
        Acknowledged,
    }
}
