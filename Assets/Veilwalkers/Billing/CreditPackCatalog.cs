using System;
using System.Collections.Generic;

namespace Veilwalkers.Billing
{
    /// <summary>
    /// The Credit Pack catalog (Story 5.1, AC-1) — the architecture-named home (architecture.md:420).
    /// Holds the ordered launch packs (Starter / Hunter "POPULAR" / Veil "BEST VALUE") as TRANSPARENT,
    /// declared data: every grantable thing is a field on <see cref="CreditPack"/>; there is no gacha /
    /// mystery-box (the no-gacha canon).
    /// <para>
    /// <b>Pack definitions are in-code canon, not balancing tunables.</b> Unlike Lure/Slay COSTS (which
    /// live in <c>EconomyConfig</c> as OQ-9 tunables, AR-16), the three packs are lore/canon fixed by
    /// CLAUDE.md (50/$1.99 · 150+20/$4.99 · 400+100+Guaranteed-Rare/$9.99) — an in-code catalog is the
    /// right home. Localized PRICES are NEVER in-code: Google Play returns the formatted localized price
    /// string at runtime (AC-1/AC-2); the <see cref="CreditPack.FallbackPriceUsd"/> is only a reference.
    /// </para>
    /// <para>
    /// Pure C# — no Unity types, no service locator (AR-4). The Shop UI (Epic 6) reads <see cref="Packs"/>
    /// to render; <see cref="BillingService"/> reads it to resolve a purchased <see cref="CreditPack"/>.
    /// </para>
    /// </summary>
    public sealed class CreditPackCatalog
    {
        // The stable Play product ids. Public so the (Epic-6) Shop UI and tests reference them by name
        // rather than by a bare string literal.
        public const string StarterPackId = "credits_starter";
        public const string HunterPackId = "credits_hunter";
        public const string VeilPackId = "credits_veil";

        private readonly IReadOnlyList<CreditPack> _packs;
        private readonly Dictionary<string, CreditPack> _byId;

        /// <summary>
        /// Build the catalog. The default constructor seeds the three launch packs (AC-1). The packs are
        /// validated by <see cref="CreditPack.Create"/>, so a malformed catalog cannot be constructed.
        /// </summary>
        public CreditPackCatalog()
        {
            _packs = new List<CreditPack>
            {
                // Starter — 50 / $1.99, no badge, no Guaranteed-Rare.
                CreditPack.Create(StarterPackId, baseCredits: 50, bonusCredits: 0,
                    includesGuaranteedRareLure: false, badge: PackBadge.None, fallbackPriceUsd: 1.99m),

                // Hunter — 150 + 20 bonus / $4.99, "POPULAR", no Guaranteed-Rare.
                CreditPack.Create(HunterPackId, baseCredits: 150, bonusCredits: 20,
                    includesGuaranteedRareLure: false, badge: PackBadge.Popular, fallbackPriceUsd: 4.99m),

                // Veil — 400 + 100 bonus + Guaranteed-Rare Lure / $9.99, "BEST VALUE".
                // The Guaranteed-Rare flag is DECLARED here for transparency; the grant/consume is Story 5.3.
                CreditPack.Create(VeilPackId, baseCredits: 400, bonusCredits: 100,
                    includesGuaranteedRareLure: true, badge: PackBadge.BestValue, fallbackPriceUsd: 9.99m),
            };

            _byId = new Dictionary<string, CreditPack>(_packs.Count, StringComparer.Ordinal);
            foreach (CreditPack pack in _packs)
            {
                _byId[pack.PackId] = pack;
            }
        }

        /// <summary>The ordered launch packs (AC-1). At least the three canon packs, in display order.</summary>
        public IReadOnlyList<CreditPack> Packs => _packs;

        /// <summary>
        /// Resolve a pack by its Play product id. Returns <c>false</c> (default pack) for an unknown id —
        /// never throws; <see cref="BillingService"/> turns this into a typed <c>UnknownPack</c> result
        /// without ever calling the store.
        /// </summary>
        public bool TryGet(string packId, out CreditPack pack)
        {
            if (string.IsNullOrEmpty(packId))
            {
                pack = default;
                return false;
            }

            return _byId.TryGetValue(packId, out pack);
        }
    }
}
