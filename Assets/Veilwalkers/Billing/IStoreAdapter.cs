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
    /// <b>5.1 surface only.</b> The exactly-once acknowledge / pending-ledger / consume surface is Story
    /// 5.2 (<c>PurchaseReconciler</c>) — deliberately NOT on this interface yet; the order id is carried on
    /// <see cref="StorePurchaseResult"/> so 5.2 can dedup on it without a seam change.
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
        /// Fetch Play's localized, formatted price strings for the given product ids (AC-1 — prices are
        /// localized by Play, never hard-coded). Returns a map of <c>packId → localized price string</c>;
        /// ids the store cannot price are simply absent (the Shop UI falls back to
        /// <see cref="CreditPack.FallbackPriceUsd"/>). Never throws — a subsystem failure returns an empty map.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> FetchLocalizedPricesAsync(IEnumerable<string> packIds);
    }
}
