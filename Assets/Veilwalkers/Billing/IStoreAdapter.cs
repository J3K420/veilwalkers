using System.Collections.Generic;
using System.Threading.Tasks;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The store seam (Story 5.1) over Google Play Billing via Unity IAP 5 (Play Billing 8) — the
    /// <b>OS-touching, CI-untestable edge</b>. It exists so the purchase DECISIONS (catalog lookup,
    /// grant-base+bonus-on-success, the one-way-to-Economy grant, the failure taxonomy) live in the
    /// pure-logic <see cref="BillingService"/> and are headless-testable: the service is ctor-injected
    /// with this interface and a fake drives it in EditMode, while the production
    /// <see cref="UnityIapStoreAdapter"/> is the thin, untestable adapter over the real Unity IAP
    /// subsystem. This is the EXACT shape of the AR module's <c>IArAnchorProvider</c>/<c>ArcoreAnchorProvider</c>
    /// split — the Epic-4-retrospective "Billing seam/adapter split" scope item.
    /// <para>
    /// <b>Thin glue — ZERO branching (architecture.md:466):</b> this seam holds NO decisions. It drives
    /// the Play purchase flow and reports a typed <see cref="StorePurchaseResult"/>; the grant decision
    /// lives in <see cref="BillingService"/>.
    /// </para>
    /// <para>
    /// <b>5.2 adds the acknowledge surface.</b> Story 5.1 deliberately shipped only the purchase + price
    /// surface and left the exactly-once acknowledge / pending-ledger / consume surface for Story 5.2's
    /// <c>PurchaseReconciler</c>. The order id is carried on <see cref="StorePurchaseResult"/> so the
    /// reconciler dedups on it; <see cref="AcknowledgeAsync"/> (added in 5.2) is the Play "acknowledge within
    /// the allowed window" call (AC-4 — un-acknowledged purchases auto-refund).
    /// </para>
    /// </summary>
    public interface IStoreAdapter
    {
        /// <summary>
        /// Drive the Google Play purchase flow for <paramref name="packId"/> (the EXCLUSIVE real-money
        /// route, AC-2) and report a typed <see cref="StorePurchaseResult"/>. NEVER throws for an expected
        /// outcome (cancel / fail / already-owned) — those are typed results (NFR-3). A subsystem failure
        /// degrades to <see cref="StorePurchaseOutcome.Failed"/>, logged via <c>GameLog</c>.
        /// </summary>
        Task<StorePurchaseResult> PurchaseAsync(string packId);

        /// <summary>
        /// Acknowledge (or consume) a completed purchase with Google Play, keyed by its Play order id
        /// (Story 5.2, AC-4). Play AUTO-REFUNDS a purchase not acknowledged within its allowed window (~3
        /// days), so the <c>PurchaseReconciler</c> calls this for every credited purchase, retrying on a
        /// transient failure on the next reconcile pass. Returns <c>true</c> when the purchase is
        /// acknowledged (Play treats acknowledge as idempotent — acknowledging an already-acknowledged order
        /// is a no-op success, so a re-run launch pass is safe), <c>false</c> on a transient failure (the
        /// reconciler leaves the pending record so the next pass retries). NEVER throws for an expected
        /// outcome (NFR-3) — a subsystem failure degrades to <c>false</c>, logged via <c>GameLog</c>.
        /// </summary>
        Task<bool> AcknowledgeAsync(string playOrderId);

        /// <summary>
        /// Fetch Play's localized, formatted price strings for the given product ids (AC-1 — prices are
        /// localized by Play, never hard-coded). Returns a map of <c>packId → localized price string</c>;
        /// ids the store cannot price are simply absent (the Shop UI falls back to
        /// <see cref="CreditPack.FallbackPriceUsd"/>). Never throws — a subsystem failure returns an empty map.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync(IEnumerable<string> packIds);
    }
}
