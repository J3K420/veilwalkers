using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The Billing boundary (Story 5.1, architecture.md:495-496): all real-money flow stays in
    /// <c>Billing</c>; the rest of the app calls <see cref="PurchaseAsync"/> and listens for
    /// <see cref="OnPurchaseCompleted"/>. Billing → Economy is STRICTLY one-way (architecture.md:478-480):
    /// the service grants via <c>ICreditService.GrantCreditsAsync</c>; Economy never references Billing.
    /// </summary>
    public interface IBillingService
    {
        /// <summary>The catalog packs the Shop renders (AC-1). Read-only; the Shop UI (Epic 6) binds it.</summary>
        IReadOnlyList<CreditPack> Packs { get; }

        /// <summary>
        /// Purchase a Credit Pack by its Play product id (AC-2 — the EXCLUSIVE Google Play route). On a
        /// completed purchase, grants the pack total (base + bonus, AC-3) ONE-WAY into Economy and raises
        /// <see cref="OnPurchaseCompleted"/>. Returns a typed <see cref="PurchaseResult"/>; NEVER throws for
        /// an expected outcome (cancel / fail / unknown pack / already-owned / grant-fault).
        /// </summary>
        Task<PurchaseResult> PurchaseAsync(string packId);

        /// <summary>
        /// Fetch Play's localized price strings for the catalog (AC-1 — prices localized by Play). Returns
        /// <c>packId → localized price string</c>; absent ids fall back to <see cref="CreditPack.FallbackPriceUsd"/>.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync();

        /// <summary>
        /// Raised once after a successful purchase's Credits are durably granted (AC-3). Carries the pack,
        /// the granted Credits, and the new balance. The Epic-6 Shop HUD / the 5.4 top-up sheet bind this.
        /// </summary>
        event Action<PurchaseCompletedEvent> OnPurchaseCompleted;
    }
}
