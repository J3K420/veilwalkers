namespace Veilwalkers.UI
{
    /// <summary>
    /// The on-brand treatment DECISION for one system moment (Story 6.5) — the data the (deferred
    /// Epic-6) view renders into the actual ritual wipe, coaching card, restore beat, re-grant path,
    /// Codex flip, or plain safety overlay. A data-only <c>readonly struct</c> (the
    /// <see cref="MaterializationPlan"/> precedent): handed to the view, it can never alias or corrupt
    /// the presenter's state.
    /// <para>
    /// <b>No progress number, by construction (AC-3).</b> A treatment is QUALITATIVE — a ritual or a
    /// coaching beat — never a spinner/percentage. This struct deliberately has NO numeric-progress
    /// member: cold-start/AR-warmup is "the veil-parting wipe" (and, on a slow device, MORE ritual via
    /// <see cref="SlowDeviceFallback"/>), never a <c>float Progress</c>. The absence IS the invariant.
    /// </para>
    /// <para>
    /// <b>The safety firewall (AC-2).</b> <see cref="IsSafetyException"/> is a constant per kind, set by
    /// the factories — true ONLY for <see cref="StateTreatmentKind.SafetyWarning"/>, whose factory
    /// accepts ONLY plain <c>VeilVoice.Safety</c> copy. A diegetic Veil-voice line can never reach a
    /// safety treatment; a safety string never reaches a diegetic treatment.
    /// </para>
    /// </summary>
    public readonly struct StateTreatment
    {
        // Backing field for the null-safe Copy property. A struct's `default` value zero-inits this to
        // null (bypassing the factory); the Copy GETTER coalesces it to empty so even a default-valued
        // treatment honors the "empty when not supplied" contract (the MaterializationPlan.MonsterId
        // null-safe-getter precedent) — a view doing treatment.Copy.Length never NREs.
        private readonly string _copy;

        /// <summary>Which treatment family this is.</summary>
        public readonly StateTreatmentKind Kind;

        /// <summary>The copy this treatment shows — a <c>VeilVoice</c> diegetic line, a passed-through
        /// <c>PlacementResult.Message</c> coaching string, or a plain <c>VeilVoice.Safety</c> line.
        /// Null-safe even for <c>default(StateTreatment)</c> (coalesces to empty).</summary>
        public string Copy => _copy ?? string.Empty;

        /// <summary>True ONLY for <see cref="StateTreatmentKind.SafetyWarning"/> (AC-2) — the plain
        /// exception. The view must render this plain/legible/high-contrast, never costumed. A treatment
        /// with this flag set carries ONLY <c>VeilVoice.Safety</c> copy by construction.</summary>
        public readonly bool IsSafetyException;

        /// <summary>True ONLY for <see cref="StateTreatmentKind.VeilPartingWipe"/> (AC-3): the cold-start
        /// ritual is a wipe, NOT a spinner/progress bar. Encodes the no-progress contract alongside the
        /// (deliberate) absence of any numeric-progress member.</summary>
        public readonly bool ShowsRitualNotProgress;

        /// <summary>True when a <see cref="StateTreatmentKind.VeilPartingWipe"/> is the slow-device
        /// fallback (AC-3): MORE ritual ("the Veil is thick here, hold steady…"), still NEVER a
        /// percentage. False for every other kind.</summary>
        public readonly bool SlowDeviceFallback;

        private StateTreatment(
            StateTreatmentKind kind,
            string copy,
            bool isSafetyException,
            bool showsRitualNotProgress,
            bool slowDeviceFallback)
        {
            Kind = kind;
            _copy = copy ?? string.Empty;
            IsSafetyException = isSafetyException;
            ShowsRitualNotProgress = showsRitualNotProgress;
            SlowDeviceFallback = slowDeviceFallback;
        }

        /// <summary>The calm "no treatment" value — a non-safety, non-ritual, copy-less treatment (the
        /// default for a warm session / a successful placement / a non-discovery).</summary>
        internal static StateTreatment None() =>
            new StateTreatment(StateTreatmentKind.None, null, false, false, false);

        /// <summary>The veil-parting wipe ritual (AC-3). <paramref name="slowDevice"/> selects the
        /// "more ritual" fallback copy; NEVER a percentage. <c>internal</c> so only the presenter builds
        /// it (the <see cref="MaterializationPlan.Create"/> factory precedent).</summary>
        internal static StateTreatment VeilPartingWipe(string ritualCopy, bool slowDevice) =>
            new StateTreatment(
                StateTreatmentKind.VeilPartingWipe, ritualCopy, false, true, slowDevice);

        /// <summary>Plane-not-found friendly coaching (AC-4), carrying the placement coaching copy.</summary>
        internal static StateTreatment PlaneCoaching(string coachingCopy) =>
            new StateTreatment(StateTreatmentKind.PlaneCoaching, coachingCopy, false, false, false);

        /// <summary>The "Pulled back through the Veil" anchor-restore beat (AC-4).</summary>
        internal static StateTreatment AnchorRestoreBeat(string copy) =>
            new StateTreatment(StateTreatmentKind.AnchorRestoreBeat, copy, false, false, false);

        /// <summary>The camera-denied re-grant path (AC-4) — plain-but-friendly copy.</summary>
        internal static StateTreatment CameraReGrant(string copy) =>
            new StateTreatment(StateTreatmentKind.CameraReGrant, copy, false, false, false);

        /// <summary>The Codex first-discovery flip + count-tick treatment (AC-4); the new-tier copy is
        /// present only when a new tier was revealed (the silhouette fade-in signal).</summary>
        internal static StateTreatment CodexFirstDiscovery(string copy) =>
            new StateTreatment(StateTreatmentKind.CodexFirstDiscovery, copy, false, false, false);

        /// <summary>The PLAIN safety warning (AC-2). The ONLY factory that sets
        /// <see cref="IsSafetyException"/> true — and it accepts ONLY a plain safety string. This is the
        /// firewall: a diegetic Veil-voice line can never become a safety treatment.</summary>
        internal static StateTreatment SafetyWarning(string plainSafetyCopy) =>
            new StateTreatment(StateTreatmentKind.SafetyWarning, plainSafetyCopy, true, false, false);
    }
}
