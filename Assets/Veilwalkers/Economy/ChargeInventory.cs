using System;
using Veilwalkers.Persistence;

namespace Veilwalkers.Economy
{
    /// <summary>
    /// The ONE place that maps a <see cref="ChargeType"/> to its backing field on the
    /// <see cref="SaveModel"/> (<c>StrongCaptureCharges</c> / <c>StabilityBoostCharges</c>
    /// / <c>NightveilFilterCharges</c>). Every charge mutation in the game flows through
    /// <see cref="ProgressionService"/>, which flows through this mapping — no other
    /// file may touch the three charge fields directly. A switch duplicated elsewhere
    /// is a defect waiting to happen (a new <see cref="ChargeType"/> added without
    /// updating that copy would silently mis-map), so this is the single point that
    /// must change when the charge set changes.
    /// <para>
    /// A static helper, not a registered service: "grants charges via
    /// <see cref="ChargeInventory"/>" in the story AC names this mapping discipline,
    /// not a locator entry. It is pure (state lives in the model) and so is trivially
    /// unit-testable and thread-agnostic.
    /// </para>
    /// </summary>
    public static class ChargeInventory
    {
        /// <summary>
        /// The remaining charge count of <paramref name="type"/> on <paramref name="model"/>.
        /// An undefined enum value throws <see cref="ArgumentOutOfRangeException"/>
        /// (a programmer error — the only valid inputs are the defined members).
        /// </summary>
        public static int GetCount(SaveModel model, ChargeType type)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            switch (type)
            {
                case ChargeType.StrongCapture:
                    return model.StrongCaptureCharges;
                case ChargeType.StabilityBoost:
                    return model.StabilityBoostCharges;
                case ChargeType.NightveilFilter:
                    return model.NightveilFilterCharges;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type), type, "Unknown charge type.");
            }
        }

        /// <summary>
        /// Set the charge count of <paramref name="type"/> on <paramref name="model"/>.
        /// Rejects a negative <paramref name="count"/> with
        /// <see cref="ArgumentOutOfRangeException"/> — defense-in-depth for the
        /// "never goes below zero" invariant, so even a buggy caller cannot persist a
        /// negative charge balance. An undefined enum value throws the same.
        /// </summary>
        public static void SetCount(SaveModel model, ChargeType type, int count)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count), count, "A charge count can never be negative.");
            }

            switch (type)
            {
                case ChargeType.StrongCapture:
                    model.StrongCaptureCharges = count;
                    break;
                case ChargeType.StabilityBoost:
                    model.StabilityBoostCharges = count;
                    break;
                case ChargeType.NightveilFilter:
                    model.NightveilFilterCharges = count;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type), type, "Unknown charge type.");
            }
        }
    }
}
