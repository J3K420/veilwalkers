using UnityEngine;
using Veilwalkers.Core;

namespace Veilwalkers.UI
{
    /// <summary>
    /// A thin, logic-free <see cref="MonoBehaviour"/> that demonstrates the descriptor→render bind
    /// pattern for the Story-6.2 chunky component kit (the <c>MaterializationView</c> precedent). It
    /// holds NO decision — the chunky-style decision lives entirely in the headless-tested descriptors
    /// (<see cref="ChunkyButtonStyle"/>, <see cref="CreditPillStyle"/>, <see cref="PackCardStyle"/>,
    /// <see cref="CodexSlotStyle"/>, <see cref="RarityBadgeStyle"/>, <see cref="WordmarkStyle"/>). This
    /// view only forwards a built <see cref="ChunkyStyle"/> to the (stub) render.
    /// <para>
    /// <b>No service-locator read.</b> The descriptors are dependency-free values (unlike
    /// <c>CodexGridView</c>, which reads <c>CodexService</c>), so this view constructs nothing from
    /// <c>GameServices</c> and needs no <c>Awake</c> graceful-degrade guard — it is inert until Story
    /// 6.3 places each component's real widget in a scene.
    /// </para>
    /// <para>
    /// <b>Render + scene placement are Story 6.3.</b> <see cref="Render"/> is a deliberate stub: the
    /// real UGUI widget binding (the 9-slice outline sprite, the offset drop-shadow, the fill image,
    /// the ALL-CAPS label / ⬡ glyph / icon, the pressed-state transition, the felt-descent pulse tween,
    /// the wordmark wobble) all land in Story 6.3. Each of the six components gets its own placed
    /// widget there; this single stub proves the bind shape headlessly.
    /// </para>
    /// </summary>
    public sealed class ChunkyComponentView : MonoBehaviour
    {
        // The bind site. Logic-free BY DESIGN — every decision (fill/outline/shadow/radius/label) is in
        // the descriptor. This story stubs the render to a log; the real widget binding lands in Story 6.3.
        // TODO(Story 6.3): bind `style` to a UGUI Image (fill + 9-slice outline) + an offset drop-shadow
        // (style.ShadowOffsetX/Y, style.ShadowColor, blur ALWAYS 0) + a tappable target ≥ 48dp.
        public void Render(ChunkyStyle style)
        {
            GameLog.Info(
                $"ChunkyComponentView: fill ({style.Fill.r},{style.Fill.g},{style.Fill.b}), " +
                $"outline {style.OutlineWidth}px, shadow {style.ShadowOffsetX},{style.ShadowOffsetY} " +
                $"blur {style.ShadowBlur}, radius {style.CornerRadius}. [render is Story 6.3]");
        }
    }
}
