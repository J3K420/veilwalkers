using System;
using System.Collections.Generic;
using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds the
    /// <see cref="CodexGridPresenter"/> to the Codex grid widgets (Story 2.4). It holds NO
    /// decisions — every classification (which of the 67 slots is Discovered / <c>???</c> /
    /// <c>?</c>, the begun-tier rule, the X/67 numbers) lives in the headless-tested presenter.
    /// This view only resolves the service, owns the event subscription, and pushes the
    /// presenter's output to widgets.
    /// <para>
    /// <b>The ONLY locator reader in this story (AR-4).</b> Pure-logic classes are
    /// ctor-injected; only MonoBehaviours/UI may read <see cref="GameServices"/>.
    /// </para>
    /// <para>
    /// <b>Graceful degrade (required, not optional).</b> <see cref="GameServices.Get{T}"/>
    /// throws <see cref="ServicesNotReadyException"/> before wiring AND
    /// <see cref="KeyNotFoundException"/> when the table is ready but <see cref="CodexService"/>
    /// is not registered. CodexService is CURRENTLY the second case — Bootstrap seals the table
    /// (<c>MarkReady</c>) but does NOT register CodexService (the Story 2.3 deferred seam: no
    /// authored <c>MonsterDatabase.asset</c> yet). So this view catches BOTH and renders an inert
    /// placeholder grid, never throwing at <c>Awake</c>. It lights up once the <c>.asset</c> is
    /// authored and Bootstrap registers CodexService (mirrors 2.3's constructed-but-unregistered
    /// precedent).
    /// </para>
    /// <para>
    /// <b>Epic 6 owns the pixels.</b> Real art, tier colors, the caught stamp, the
    /// silhouette→art flip + count-tick animation, and the chunky 3-state slot component are
    /// UX-DR2/DR5/DR15 (Stories 6.1/6.2/6.5). <see cref="Render"/> is a deliberate stub here.
    /// This view is wired into NO scene yet — Epic 6's Story 6.3 places it (mirrors
    /// Bootstrap-not-in-a-scene at Story 1.2).
    /// </para>
    /// </summary>
    public sealed class CodexGridView : MonoBehaviour
    {
        private CodexService _codex;
        private CodexGridPresenter _presenter;

        private void Awake()
        {
            // The locator read — guarded against BOTH failure modes. Never throw at Awake: an
            // unbindable grid is the correct inert seam until CodexService is registered.
            try
            {
                _codex = GameServices.Get<CodexService>();
                _presenter = new CodexGridPresenter(_codex);
            }
            catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
            {
                _codex = null;
                _presenter = null;
                GameLog.Warn(
                    "CodexGridView: CodexService is not available yet (services not ready, or the " +
                    "service is unregistered pending the MonsterDatabase.asset). Rendering an inert " +
                    "grid. " + ex.Message);
            }
        }

        private void OnEnable()
        {
            // Symmetric subscribe/unsubscribe with OnDisable (AR-6, an acceptance criterion).
            // No-op when inert (_codex == null) — nothing to subscribe to, nothing to rebuild.
            if (_codex == null)
            {
                return;
            }

            _codex.OnMonsterDiscovered += HandleDiscovered;
            Rebuild();
        }

        private void OnDisable()
        {
            // Exactly pairs the OnEnable subscribe. Guarded so a never-resolved view (inert)
            // does not touch a null service.
            if (_codex == null)
            {
                return;
            }

            _codex.OnMonsterDiscovered -= HandleDiscovered;
        }

        // A first discovery may simultaneously flip its own slot to Discovered AND raise the
        // high-water mark (flipping lower blanks to ???). A single Rebuild() handles both
        // (AC-2 + AC-3); the presenter is a pure per-build recompute.
        private void HandleDiscovered(string id) => Rebuild();

        private void Rebuild()
        {
            if (_presenter == null)
            {
                return;
            }

            IReadOnlyList<CodexSlot> slots = _presenter.BuildSlots();
            Render(slots, _presenter.DiscoveredCount, _presenter.UniverseCount);
        }

        // The bind site. Logic-free BY DESIGN: it must contain NO classification/tier branching
        // (no `if (discovered) … else if (begun) …`) — all of that is in the presenter. This
        // story stubs the render to a state breakdown log; the real binding lands in Epic 6.
        // TODO(Epic 6): bind to the chunky 3-state Codex-slot component (UX-DR5) + tier-color
        // tokens (UX-DR2) + the flip/tick animation (UX-DR15).
        private void Render(IReadOnlyList<CodexSlot> slots, int discovered, int universe)
        {
            GameLog.Info($"CodexGridView: rebuilt {slots.Count}-slot grid — {discovered} / {universe} discovered.");
        }
    }
}
