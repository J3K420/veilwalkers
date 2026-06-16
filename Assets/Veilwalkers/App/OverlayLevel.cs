namespace Veilwalkers.App
{
    /// <summary>
    /// The overlay/sheet depth over the current surface (Story 6.3, AC-3 — "sheets/overlays stack
    /// one level deep"). Modeled as a type rather than a magic int so the one-level-deep rule is
    /// explicit and testable: a sheet over a surface is the deepest legal overlay; opening a second
    /// sheet REPLACES the first (see <see cref="AppStateMachine.OpenSheet"/>), never stacks two.
    /// The actual sheet CONTENT (top-up sheet, etc.) is the deferred UI/6.5 view layer.
    /// </summary>
    public enum OverlayLevel
    {
        /// <summary>No overlay — the bare surface.</summary>
        None,

        /// <summary>One sheet/overlay is open over the surface. The maximum depth (AC-3).</summary>
        Sheet,
    }
}
