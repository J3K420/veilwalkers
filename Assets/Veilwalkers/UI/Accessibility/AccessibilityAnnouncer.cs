namespace Veilwalkers.UI
{
    /// <summary>
    /// The screen-reader ANNOUNCE-string decisions (Story 6.6, AC-4; UX-DR17) — pure value-in /
    /// string-out, dependency-free (the <see cref="MaterializationPresenter"/> value-in precedent). Maps
    /// a credit-balance change (the <c>ICreditService.OnCreditsChanged(int)</c> payload) and a Codex
    /// count tick-up (the <c>CodexService</c> <c>DiscoveredCount</c>/<c>UniverseCount</c>) to the exact
    /// TalkBack announcement string.
    /// <para>
    /// <b>The pure layer NEVER subscribes (Decision C).</b> This class maps a value → a string. The live
    /// subscription to <c>OnCreditsChanged</c> / <c>OnMonsterDiscovered</c> / <c>OnCodexCompleted</c> and
    /// the platform <c>AnnounceForAccessibility</c> call are the deferred Epic-6 view's job (symmetric
    /// subscribe/unsubscribe lives there). Keeping the announcer pure keeps it headless-testable and free
    /// of any event-lifecycle burden.
    /// </para>
    /// <para>
    /// <b>Plain, not diegetic (Decision E).</b> Announcements are functional accessibility output, the
    /// plain register — "20 credits", "12 of 67 discovered" — never the wry-eerie Veil voice.
    /// </para>
    /// </summary>
    public static class AccessibilityAnnouncer
    {
        /// <summary>
        /// The credit balance as a TalkBack phrase: "0 credits" / "1 credit" / "N credits". A negative
        /// balance is clamped to 0 (a balance is never negative; the world never locks — the
        /// <c>CreditPillStyle.ClampToFloor</c> precedent). Singular "1 credit" vs plural otherwise.
        /// </summary>
        public static string Credits(int balance)
        {
            int b = balance < 0 ? 0 : balance;
            return b == 1 ? "1 credit" : $"{b} credits";
        }

        /// <summary>
        /// The credit-pill balance announcement (AC-4): the new balance phrased as
        /// <see cref="Credits(int)"/>. The deferred view calls this on every
        /// <c>OnCreditsChanged(newBalance)</c> and announces the result.
        /// </summary>
        public static string AnnounceBalance(int newBalance) => Credits(newBalance);

        /// <summary>
        /// The Codex count-tick-up announcement (AC-4): "X of Y discovered", e.g. "12 of 67 discovered".
        /// At completion (<paramref name="discoveredCount"/> &gt;= <paramref name="universeCount"/> and
        /// the universe is non-empty) it announces the completion variant "Y of Y — Codex complete". The
        /// deferred view calls this on every <c>OnMonsterDiscovered</c> tick (and at
        /// <c>OnCodexCompleted</c>). Counts are clamped non-negative.
        /// </summary>
        public static string AnnounceDiscovery(int discoveredCount, int universeCount)
        {
            int discovered = discoveredCount < 0 ? 0 : discoveredCount;
            int universe = universeCount < 0 ? 0 : universeCount;

            if (universe > 0 && discovered >= universe)
            {
                return $"{universe} of {universe} — Codex complete";
            }

            return $"{discovered} of {universe} discovered";
        }
    }
}
