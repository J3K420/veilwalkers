using UnityEngine;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The coin-burst grant-presentation descriptor (Story 6.4; AC-3). A pure-logic <c>readonly struct</c>
    /// that models the CELEBRATORY presentation of the 20-Credit first-launch grant — NOT the grant
    /// itself (the grant is already performed by Story-1.7 <c>FirstLaunchGrant.RunAsync()</c> at boot;
    /// this only PRESENTS the resulting balance). It carries the credit-gold token + the granted amount
    /// to burst + the resulting credit pill. The actual burst ANIMATION (the particle/coin tween) is the
    /// deferred Story-6.5 view layer.
    /// <para>
    /// <b>Credit-gold is the currency role token (6.1 / UX-DR1).</b> The burst uses
    /// <see cref="PumpkinPatchTokens.CreditGold"/> — the correct role token for a Credit presentation,
    /// never a tier color or a general accent.
    /// </para>
    /// </summary>
    public readonly struct CoinBurstStyle
    {
        /// <summary>The number of Credits to celebrate (the first-launch grant — canon 20). A negative
        /// amount is clamped to 0 (a grant is never negative).</summary>
        public int Amount { get; }

        /// <summary>The burst color — credit-gold (the currency role token, UX-DR1).</summary>
        public Color32 BurstColor { get; }

        /// <summary>The resulting credit pill the burst settles into (the live balance after the grant),
        /// so the presentation flows coin-burst → credit pill. Reuses the Story-6.2 <see cref="CreditPillStyle"/>.</summary>
        public CreditPillStyle Pill { get; }

        private CoinBurstStyle(int amount, Color32 burstColor, CreditPillStyle pill)
        {
            Amount = amount;
            BurstColor = burstColor;
            Pill = pill;
        }

        /// <summary>
        /// Build a coin-burst presenting a grant of <paramref name="amount"/> Credits, settling into a
        /// pill showing <paramref name="resultingBalance"/>. The burst is credit-gold (6.1 token, AC-2).
        /// BOTH values are clamped to a non-negative floor here — a grant amount and a balance are never
        /// negative. The balance clamp is symmetric with the amount clamp (not left implicitly to
        /// <see cref="CreditPillStyle.For"/>) so this descriptor's own contract is self-evident and does
        /// not silently depend on the pill helper continuing to clamp.
        /// </summary>
        public static CoinBurstStyle For(int amount, int resultingBalance)
        {
            return new CoinBurstStyle(
                ClampToFloor(amount),
                PumpkinPatchTokens.CreditGold,
                CreditPillStyle.For(ClampToFloor(resultingBalance)));
        }

        // A grant amount and a balance are never negative; the floor is 0. Centralized so the burst
        // amount and the settled-pill balance clamp identically (the CreditPillStyle.ClampToFloor parity).
        private static int ClampToFloor(int value) => value < 0 ? 0 : value;
    }
}
