using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds the
    /// <see cref="MaterializationPresenter"/> to the (Epic-6) entrance render (Story 4.7). It
    /// holds NO decisions — the per-tier variant/duration, the action-bar gate, the branded-glitch
    /// flag, and the reduced-motion taming all live in the headless-tested presenter. This view
    /// only owns the clock + pushes the plan to the render.
    /// <para>
    /// <b>No service-locator read here.</b> The presenter is dependency-free (it takes a
    /// <see cref="Rarity"/>, not a service), so unlike <see cref="CodexDetailView"/> this view
    /// constructs it directly — there is no <see cref="GameServices"/> read and so no
    /// <c>Awake</c> graceful-degrade guard to write. The view is inert until Epic 6 wires it into
    /// the AR-rig scene and feeds it the spawned Monster (the <c>LureResult</c> id + tier).
    /// </para>
    /// <para>
    /// <b>NFR-2.</b> The credit spend-ack is already delivered by Story 4.2's synchronous
    /// <c>LureResult</c> BEFORE <see cref="Play"/> is called — the materialization NEVER gates the
    /// ack, and this view does NOT touch Credits.
    /// </para>
    /// <para>
    /// <b>Wiring + render are Epic 6.</b> <see cref="Render"/> is a deliberate stub: the real
    /// entrance tween, the camera-feed glitch shader (for a Breach), the ambiance tint/lighting/
    /// vignette/SLAY-glow tokens, the screen-shake, the action-bar show/hide animation, the
    /// reduced-motion SETTING + its persistence, and the AR-rig scene placement all land in
    /// Epic 6. This story returns the PLAN; Epic 6 renders it.
    /// </para>
    /// </summary>
    public sealed class MaterializationView : MonoBehaviour
    {
        private readonly MaterializationPresenter _presenter = new MaterializationPresenter();

        // The reduced-motion / photosensitivity setting (Story 6.6) — the narrow port the view reads
        // and feeds to the presenter as the reducedMotion input. Defaults to the graceful motion-ON
        // no-op until the AR-rig scene wires the concrete adapter (over SaveModel.ReducedMotion); the
        // RENDER of the tamed entrance (the static branded transition) stays the deferred Epic-6 view
        // concern, but the SETTING READ now lands here (settles the 4.7 "wire the setting into the view"
        // defer). Settable so the deferred scene wiring injects the concrete adapter.
        private IReducedMotionSetting _reducedMotionSetting = NoReducedMotionSetting.Instance;

        /// <summary>The reduced-motion setting source. The (Epic-6) scene wiring assigns the concrete
        /// adapter over the player's <c>SaveModel.ReducedMotion</c>; a null assignment falls back to the
        /// graceful motion-ON no-op so the view never null-refs.</summary>
        public IReducedMotionSetting ReducedMotionSetting
        {
            get => _reducedMotionSetting;
            set => _reducedMotionSetting = value ?? NoReducedMotionSetting.Instance;
        }

        /// <summary>
        /// Play the materialization for a spawned Monster (the Epic-6 wire point from the
        /// encounter, fed by <c>LureResult</c>'s spawned id + the Monster's tier). Builds the plan
        /// via the presenter and pushes it to the (stub) render. Holds no decision beyond reading
        /// the setting and forwarding the plan.
        /// </summary>
        public void Play(string monsterId, Rarity tier)
        {
            bool reducedMotion = _reducedMotionSetting.ReducedMotionEnabled;
            MaterializationPlan plan = _presenter.PlanFor(monsterId, tier, reducedMotion);
            Render(plan);
        }

        // The bind site. Logic-free BY DESIGN — all decisions are in the presenter. This story
        // stubs the render to a log; the real binding lands in Epic 6.
        // TODO(Epic 6): drive the entrance tween for plan.Variant over plan.DurationSeconds, the
        // branded camera glitch when plan.BrandedGlitch (tamed to a static branded transition when
        // plan.ReducedMotion), the screen-shake when plan.ScreenShake (never under reduced-motion),
        // the ambiance tokens from plan.Ambiance.EffectiveIntensity (tamed amplitude when reduced),
        // the paired caption/visual cue when plan.RequiresCaptionCue (never audio-only — Story 6.6),
        // keep the action bar hidden until settle (presenter.ShouldShowActionBar), and NEVER block
        // input recovery (plan.InputRecoveryBlocked is always false).
        private void Render(MaterializationPlan plan)
        {
            GameLog.Info(
                $"MaterializationView: '{plan.MonsterId}' ({plan.Tier}) → {plan.Variant} over " +
                $"{plan.DurationSeconds:0.##}s, ambiance {plan.Ambiance.EffectiveIntensity}" +
                (plan.BrandedGlitch ? " + branded glitch" : string.Empty) +
                (plan.ScreenShake ? " + screen-shake" : string.Empty) +
                (plan.RequiresCaptionCue ? " + caption cue" : string.Empty) +
                (plan.ReducedMotion ? " (reduced-motion tamed)" : string.Empty) +
                ". Action bar hidden until settle. [render is Epic 6]");
        }
    }
}
