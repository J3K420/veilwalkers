using System.Collections.Generic;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The pure-logic view-model for the AR Hunt HUD chrome (Story 6.3, AC-3). It is the set of
    /// floating chunky chrome islands — the credit pill + the action-bar buttons (Scan / Capture / Slay
    /// / Lure affordances) — plus the current overlay depth. The thin <c>ArHudView</c> (deferred) lays
    /// these out as discrete floating islands over the full-bleed camera (NEVER a docked frame — that
    /// layout property is the deferred view/scene layer, with no headless logic to test).
    /// <para>
    /// The buttons are DESCRIPTORS only; the encounter actions they trigger live in
    /// <c>EncounterService</c> (Epic 4), and the credit-pill tap routes to the <c>AppStateMachine</c>
    /// Shop navigation. This model carries the display data + the overlay state; the view wires intents.
    /// </para>
    /// </summary>
    public readonly struct ArHudViewModel
    {
        /// <summary>The floating credit pill (Story 6.2 <see cref="CreditPillStyle"/>) — live balance,
        /// the felt-descent pulse near empty, tappable → Shop.</summary>
        public CreditPillStyle CreditPill { get; }

        /// <summary>The action-bar buttons, in display order (Lure / Scan / Capture / Slay). The Slay
        /// entry is the <see cref="ChunkyButtonKind.Slay"/> descriptor — SLAY-red + the mandated icon +
        /// the "SLAY" label (UX-DR17 — red is never the sole signifier, even here).</summary>
        public IReadOnlyList<ChunkyButtonStyle> ActionBar { get; }

        /// <summary>The current overlay depth (AC-3 — one level deep). The view knows whether a sheet is
        /// up over the HUD.</summary>
        public OverlayLevelView Overlay { get; }

        public ArHudViewModel(
            CreditPillStyle creditPill,
            IReadOnlyList<ChunkyButtonStyle> actionBar,
            OverlayLevelView overlay)
        {
            CreditPill = creditPill;
            ActionBar = actionBar;
            Overlay = overlay;
        }
    }

    /// <summary>
    /// The UI-tier mirror of the App-tier <c>OverlayLevel</c> (Story 6.3, AC-3). The presenter maps the
    /// <c>AppStateMachine.Overlay</c> onto this so the UI assembly does not surface an App enum in its
    /// own view-model contract (a small decoupling; the values correspond one-to-one).
    /// </summary>
    public enum OverlayLevelView
    {
        /// <summary>No overlay over the HUD.</summary>
        None,

        /// <summary>One sheet is open over the HUD (the maximum — AC-3).</summary>
        Sheet,
    }
}
