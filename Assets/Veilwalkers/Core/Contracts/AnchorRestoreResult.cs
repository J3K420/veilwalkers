namespace Veilwalkers.Core.Contracts
{
    /// <summary>
    /// Outcome of attempting to restore a saved <see cref="AnchorToken"/> in a new
    /// AR session. Used by anchor save/restore (Story 3.5) to report whether the
    /// original anchor was recovered, relocated onto a freshly detected plane, or
    /// could not be restored at all.
    /// </summary>
    public enum AnchorRestoreResult
    {
        Restored,
        RelocatedToPlane,
        Failed
    }
}
