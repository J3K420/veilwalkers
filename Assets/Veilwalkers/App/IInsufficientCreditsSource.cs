using System;
using Veilwalkers.Economy;

namespace Veilwalkers.App
{
    /// <summary>
    /// A raiser of the insufficient-credits shortfall (Story 6.3, AC-2). <see cref="ICreditService"/>
    /// already exposes <c>OnInsufficientCredits</c> directly; this narrow interface exists so the
    /// <see cref="AppStateMachine"/> can ALSO subscribe to the SECOND shortfall source —
    /// <c>EncounterService</c>, which raises the same <see cref="InsufficientCreditsEvent"/> for
    /// composed actions (Lure/Slay) — WITHOUT <c>Veilwalkers.App</c> (and its test assembly) having to
    /// depend on the shape of <c>EncounterService</c>.
    /// <para>
    /// Bootstrap adapts the real <c>EncounterService.OnInsufficientCredits</c> onto this when Encounter
    /// is registered; it is null (no second source) while Encounter is the deferred Bootstrap seam. The
    /// App tests fake it to drive the AC-2 shortfall deterministically.
    /// </para>
    /// </summary>
    public interface IInsufficientCreditsSource
    {
        /// <summary>Raised when a spend (a composed Encounter action) fails for want of credits — the
        /// same payload <see cref="ICreditService.OnInsufficientCredits"/> carries.</summary>
        event Action<InsufficientCreditsEvent> OnInsufficientCredits;
    }
}
