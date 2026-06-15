using System;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The badge a pack carries in the Shop (AC-1). Purely informational — it changes no
    /// economy outcome, only the (Epic-6) presentation. <see cref="None"/> is the default.
    /// </summary>
    public enum PackBadge
    {
        /// <summary>No badge (the Starter pack).</summary>
        None = 0,

        /// <summary>"POPULAR" (the Hunter pack).</summary>
        Popular = 1,

        /// <summary>"BEST VALUE" (the Veil pack).</summary>
        BestValue = 2,
    }

    /// <summary>
    /// One purchasable Credit Pack (Story 5.1, AC-1). An immutable, TRANSPARENT description: every
    /// grantable thing the pack delivers is a declared field — base Credits, bonus Credits, and the
    /// Veil-only Guaranteed-Rare Lure flag. There is NO randomized / hidden / mystery-box reward
    /// (Teen-rating + the no-gacha canon, CLAUDE.md / docs/architecture.md). The on-screen PRICE is
    /// NOT here: Google Play returns the localized, formatted price string at runtime (AC-1/AC-2);
    /// <see cref="FallbackPriceUsd"/> is only the reference USD shown if the store price is unavailable.
    /// <para>
    /// Built via <see cref="Create"/> (the private-ctor + factory + null-safe-getter pattern — the
    /// LureResult / MaterializationPlan struct-invariant precedent), so a malformed pack cannot exist
    /// and <c>default(CreditPack)</c> still honors the contract (empty id, zero credits).
    /// </para>
    /// </summary>
    public readonly struct CreditPack
    {
        private readonly string _packId;

        /// <summary>The stable Google Play product id (e.g. <c>"credits_starter"</c>). Never null — the
        /// getter coerces, so <c>default(CreditPack)</c> honors the contract.</summary>
        public string PackId => _packId ?? string.Empty;

        /// <summary>The base Credits granted (50 / 150 / 400).</summary>
        public int BaseCredits { get; }

        /// <summary>The bonus Credits granted on top of the base (0 / 20 / 100).</summary>
        public int BonusCredits { get; }

        /// <summary>The total Credits granted on a successful purchase (base + bonus, AC-3).</summary>
        public int TotalCredits => BaseCredits + BonusCredits;

        /// <summary>
        /// Whether the pack includes the one-shot Guaranteed-Rare Lure (true ONLY for the Veil pack).
        /// 5.1 only DECLARES this for catalog transparency (AC-1); GRANTING the item (via
        /// <c>PurchaseReconciler</c>) and CONSUMING it through <c>LureSystem</c> (the reserved
        /// <c>LureKind.GuaranteedRare</c> + <c>RarityThresholds.GuaranteedRareFloor</c>) is Story 5.3.
        /// </summary>
        public bool IncludesGuaranteedRareLure { get; }

        /// <summary>The Shop badge (AC-1: Hunter = Popular, Veil = BestValue). Presentation only.</summary>
        public PackBadge Badge { get; }

        /// <summary>The reference USD price, shown ONLY if Play's localized price is unavailable. The
        /// canonical display price is the store's localized string (AC-1/AC-2) — never this value.</summary>
        public decimal FallbackPriceUsd { get; }

        private CreditPack(
            string packId,
            int baseCredits,
            int bonusCredits,
            bool includesGuaranteedRareLure,
            PackBadge badge,
            decimal fallbackPriceUsd)
        {
            _packId = packId;
            BaseCredits = baseCredits;
            BonusCredits = bonusCredits;
            IncludesGuaranteedRareLure = includesGuaranteedRareLure;
            Badge = badge;
            FallbackPriceUsd = fallbackPriceUsd;
        }

        /// <summary>
        /// Build a pack, validating its invariants. A blank id, a non-positive base, a negative bonus,
        /// or a non-positive fallback price is a programming/authoring error and throws (the
        /// argument-guard family, NOT the expected-failure path — a pack is static canon, not user input).
        /// </summary>
        public static CreditPack Create(
            string packId,
            int baseCredits,
            int bonusCredits,
            bool includesGuaranteedRareLure,
            PackBadge badge,
            decimal fallbackPriceUsd)
        {
            if (string.IsNullOrWhiteSpace(packId))
            {
                throw new ArgumentException("A Credit Pack requires a non-blank Play product id.", nameof(packId));
            }

            if (baseCredits <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baseCredits), baseCredits, "A Credit Pack must grant a positive base of Credits.");
            }

            if (bonusCredits < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bonusCredits), bonusCredits, "A Credit Pack bonus cannot be negative.");
            }

            if (fallbackPriceUsd <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fallbackPriceUsd), fallbackPriceUsd, "A Credit Pack must carry a positive fallback price.");
            }

            return new CreditPack(packId, baseCredits, bonusCredits, includesGuaranteedRareLure, badge, fallbackPriceUsd);
        }
    }
}
