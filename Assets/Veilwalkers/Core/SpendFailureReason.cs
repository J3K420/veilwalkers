namespace Veilwalkers.Core
{
    /// <summary>
    /// Why a credit spend did not succeed. <see cref="None"/> is the success
    /// sentinel. Extended by the credit pipeline in Story 1.4; new members must be
    /// appended (never reordered) to keep any persisted/telemetry values stable.
    /// </summary>
    public enum SpendFailureReason
    {
        None = 0,
        InsufficientCredits = 1
    }
}
