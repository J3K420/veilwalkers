using Veilwalkers.Core.Contracts;

namespace Veilwalkers.AR
{
    /// <summary>
    /// The kind of outcome <see cref="PlaneAnchorService.TryPlace"/> produced — a small explicit set,
    /// the single source of truth the consumer reads (mirrors <c>ArSessionState</c>/
    /// <c>CameraPermissionState</c>/<c>AnchorRestoreResult</c>). Carried inside <see cref="PlacementResult"/>.
    /// </summary>
    public enum PlacementOutcome
    {
        /// <summary>A plane was found and an anchor was created — the placement succeeded (AC-1). The
        /// <see cref="PlacementResult.Token"/> holds the serializable anchor handle.</summary>
        Placed,

        /// <summary>No trackable plane yet (or no placement pose in front of the player) — friendly
        /// coaching is shown and NO object is spawned into empty space (AC-2). The
        /// <see cref="PlacementResult.Message"/> holds the coaching copy.</summary>
        NeedsCoaching,

        /// <summary>A plane existed and a pose was found, but anchor creation failed — guidance, never a
        /// crash (NFR-3). No object is placed.</summary>
        Failed,
    }

    /// <summary>
    /// The typed result of a placement attempt (Story 3.4) — a value, not a side effect, so the consumer
    /// reads a decision rather than inferring one. <see cref="PlaneAnchorService.TryPlace"/> NEVER throws;
    /// every path returns one of these (NFR-3).
    /// </summary>
    public readonly struct PlacementResult
    {
        /// <summary>Which outcome occurred.</summary>
        public PlacementOutcome Outcome { get; }

        /// <summary>The created anchor handle — meaningful only when <see cref="Outcome"/> is
        /// <see cref="PlacementOutcome.Placed"/>; otherwise <c>default</c>.</summary>
        public AnchorToken Token { get; }

        /// <summary>The coaching copy — non-null only when <see cref="Outcome"/> is
        /// <see cref="PlacementOutcome.NeedsCoaching"/>; otherwise <c>null</c>.</summary>
        public string Message { get; }

        private PlacementResult(PlacementOutcome outcome, AnchorToken token, string message)
        {
            Outcome = outcome;
            Token = token;
            Message = message;
        }

        /// <summary>A successful placement carrying the created anchor (AC-1).</summary>
        public static PlacementResult Placed(AnchorToken token)
            => new PlacementResult(PlacementOutcome.Placed, token, null);

        /// <summary>Coaching needed — no plane / no placeable pose yet; no object spawned (AC-2).</summary>
        public static PlacementResult NeedsCoaching(string message)
            => new PlacementResult(PlacementOutcome.NeedsCoaching, default, message);

        /// <summary>Anchor creation failed — guidance, no crash (NFR-3).</summary>
        public static PlacementResult Failed()
            => new PlacementResult(PlacementOutcome.Failed, default, null);
    }
}
