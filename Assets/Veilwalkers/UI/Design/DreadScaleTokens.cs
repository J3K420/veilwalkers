using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// One tier's dread-scale color token (Story 6.1; UX-DR2). T1–T4 are single-stop
    /// (<see cref="Start"/> == <see cref="End"/>); T5 (Nightmare) is the only true gradient —
    /// the bruise-rot ramp <c>#3A4C3A → #5C2A4A</c>. A uniform gradient return type keeps the
    /// <see cref="DreadScaleTokens.ForTier"/> resolver total and the consumer branch-free.
    /// </summary>
    public readonly struct TierGradient
    {
        /// <summary>Gradient start stop.</summary>
        public readonly Color32 Start;

        /// <summary>Gradient end stop (== <see cref="Start"/> for the single-stop tiers T1–T4).</summary>
        public readonly Color32 End;

        public TierGradient(Color32 start, Color32 end)
        {
            Start = start;
            End = end;
        }

        /// <summary>True for the single-stop tiers (T1–T4); false only for the T5 bruise-rot gradient.</summary>
        public bool IsSolid => Hex.Packed(Start) == Hex.Packed(End);
    }

    /// <summary>
    /// The five dread-scale tier tokens (Story 6.1; UX-DR2). Keyed to the five
    /// <see cref="Rarity"/> tiers (Common→Nightmare = T1→T5). These are a DELIBERATELY SEPARATE
    /// set from <see cref="PumpkinPatchTokens"/> (the frame palette): a tier color is never also a
    /// frame role, so chrome can never be painted with a tier color by reaching through a palette
    /// token.
    /// <para>
    /// <b>Usage (UX-DR2):</b> tier color appears ONLY in the AR-encounter treatment
    /// (tint/lighting/vignette/SLAY-glow — the diffused dread scale, Story 4.7's
    /// <c>MaterializationAmbiance</c> resolves its <c>AmbianceIntensity</c> through
    /// <see cref="ForTier"/> at render time) and on the Codex rarity-tier badge / "caught" stamp
    /// (UX-DR7) — NEVER as a visible rarity bar/strip/legend in chrome. This class only DEFINES the
    /// tokens; honoring "never a rarity bar" is the consuming component's discipline (Story 6.2+).
    /// </para>
    /// </summary>
    public static class DreadScaleTokens
    {
        // ---- Tier tokens (UX-DR2) — canonical hex ----

        /// <summary>T1 Common.</summary>
        public const string Tier1CommonHex = "#7DE0C4";

        /// <summary>T2 Uncommon.</summary>
        public const string Tier2UncommonHex = "#4FA8C9";

        /// <summary>T3 Rare.</summary>
        public const string Tier3RareHex = "#6A5AC9";

        /// <summary>T4 Epic.</summary>
        public const string Tier4EpicHex = "#4B2E6B";

        /// <summary>T5 Nightmare — bruise-rot gradient START.</summary>
        public const string Tier5NightmareStartHex = "#3A4C3A";

        /// <summary>T5 Nightmare — bruise-rot gradient END.</summary>
        public const string Tier5NightmareEndHex = "#5C2A4A";

        // ---- Tier tokens — typed gradients ----

        public static TierGradient Tier1Common => Solid(Tier1CommonHex);
        public static TierGradient Tier2Uncommon => Solid(Tier2UncommonHex);
        public static TierGradient Tier3Rare => Solid(Tier3RareHex);
        public static TierGradient Tier4Epic => Solid(Tier4EpicHex);

        /// <summary>T5 Nightmare — the only true (two-stop) gradient.</summary>
        public static TierGradient Tier5Nightmare =>
            new TierGradient(Hex.ToColor32(Tier5NightmareStartHex), Hex.ToColor32(Tier5NightmareEndHex));

        /// <summary>
        /// The SINGLE tier→token map (UX-DR2). Consumers resolve a Monster's
        /// <see cref="Rarity"/> to its dread-scale token here and nowhere else. Total over all five
        /// <see cref="Rarity"/> members.
        /// <para>
        /// An out-of-range / undefined <see cref="Rarity"/> (defensive — callers always pass a real
        /// tier) degrades to the calmest tier (T1 Common) rather than throwing, matching the
        /// <c>MaterializationPresenter</c> default-to-calmest graceful-boundary precedent (NFR-3).
        /// </para>
        /// </summary>
        public static TierGradient ForTier(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return Tier1Common;
                case Rarity.Uncommon: return Tier2Uncommon;
                case Rarity.Rare: return Tier3Rare;
                case Rarity.Epic: return Tier4Epic;
                case Rarity.Nightmare: return Tier5Nightmare;
                default:
                    GameLog.Warn($"DreadScaleTokens.ForTier: unmapped Rarity '{rarity}' — degrading to T1 Common.");
                    return Tier1Common;
            }
        }

        private static TierGradient Solid(string hex)
        {
            Color32 c = Hex.ToColor32(hex);
            return new TierGradient(c, c);
        }
    }
}
