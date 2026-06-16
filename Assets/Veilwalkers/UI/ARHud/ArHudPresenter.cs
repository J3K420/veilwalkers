using System;
using System.Collections.Generic;
using Veilwalkers.App;
using Veilwalkers.Economy;

namespace Veilwalkers.UI
{
    /// <summary>
    /// Pure-logic read model for the AR Hunt HUD chrome (Story 6.3, AC-3). Composes the floating credit
    /// pill (live balance + the near-empty felt-descent state) and the action-bar chunky buttons (Lure /
    /// Scan / Capture / Slay descriptors) into an <see cref="ArHudViewModel"/>, and surfaces the
    /// <c>AppStateMachine</c> overlay depth so the HUD knows whether a sheet is up. NO <c>MonoBehaviour</c>,
    /// NO service-locator reads — ctor-injected (the <see cref="CodexGridPresenter"/> / <see cref="HomePresenter"/>
    /// precedent). The thin <c>ArHudView</c> binds the model, lays the islands out over the full-bleed
    /// camera, and wires the pill tap → <c>AppStateMachine.NavigateToShopAsync</c> and the action buttons
    /// → <c>EncounterService</c>; that view + the scene are the deferred Epic-6 view layer.
    /// <para>
    /// The presenter owns NO encounter logic — it only assembles descriptors + reads the overlay state.
    /// Degrades gracefully: a null credit service or a before-load <see cref="ICreditService.Balance"/>
    /// read shows a calm "0" pill (the pill never locks — UX-DR4).
    /// </para>
    /// </summary>
    public sealed class ArHudPresenter
    {
        // Action-bar labels. Slay's label is ignored by the descriptor (it forces the literal "SLAY" +
        // the icon — UX-DR17), but it is passed for symmetry.
        public const string LureLabel = "Lure";
        public const string ScanLabel = "Scan";
        public const string CaptureLabel = "Capture";
        public const string SlayLabel = "Slay";

        private readonly ICreditService _credits;      // may be null (defensive)
        private readonly AppStateMachine _app;          // may be null (defensive)

        public ArHudPresenter(ICreditService credits, AppStateMachine app)
        {
            _credits = credits;
            _app = app;
        }

        /// <summary>
        /// Build the immutable HUD view-model from current state. A PURE RECOMPUTE; the view rebuilds on
        /// <c>OnCreditsChanged</c> and on encounter/overlay changes. Never throws: a missing service or a
        /// before-load read degrades to its calm default.
        /// </summary>
        public ArHudViewModel Build()
        {
            int balance = ReadBalance();

            // The action bar: Lure (primary affordance), Scan + Capture (secondary), Slay (the
            // destructive variant — SLAY-red + icon + "SLAY", AC-3 of the 6.2 never-alone rule).
            var actionBar = new List<ChunkyButtonStyle>(4)
            {
                ChunkyButtonStyle.For(ChunkyButtonKind.Primary, LureLabel),
                ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, ScanLabel),
                ChunkyButtonStyle.For(ChunkyButtonKind.Secondary, CaptureLabel),
                ChunkyButtonStyle.For(ChunkyButtonKind.Slay, SlayLabel),
            };

            return new ArHudViewModel(
                creditPill: CreditPillStyle.For(balance),
                actionBar: actionBar,
                overlay: MapOverlay());
        }

        private int ReadBalance()
        {
            if (_credits == null)
            {
                return 0;
            }

            try
            {
                return _credits.Balance;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        private OverlayLevelView MapOverlay()
        {
            if (_app == null)
            {
                return OverlayLevelView.None;
            }

            return _app.Overlay == OverlayLevel.Sheet ? OverlayLevelView.Sheet : OverlayLevelView.None;
        }
    }
}
