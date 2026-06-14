using System;
using System.Collections.Generic;
using UnityEngine;
using Veilwalkers.Core;
using Veilwalkers.Monsters;

namespace Veilwalkers.UI
{
    /// <summary>
    /// The thin, logic-free <see cref="MonoBehaviour"/> that binds the
    /// <see cref="CodexDetailPresenter"/> to the Codex detail panel widgets (Story 2.5). It
    /// holds NO decisions — discovered-vs-not, the null-safe content, the AC-2 reveal-preserving
    /// "Not yet discovered." copy all live in the headless-tested presenter. This view only
    /// resolves the service, exposes <see cref="Open"/>, and pushes the presenter's output to
    /// widgets (resolving the art + tier-badge color from the id/tier at bind time — Epic 6).
    /// <para>
    /// <b>The ONLY locator reader here (AR-4).</b> Pure-logic classes are ctor-injected; only
    /// MonoBehaviours/UI may read <see cref="GameServices"/>.
    /// </para>
    /// <para>
    /// <b>Graceful degrade (required, not optional)</b> — mirrors <see cref="CodexGridView"/>.
    /// <see cref="GameServices.Get{T}"/> throws <see cref="ServicesNotReadyException"/> before
    /// wiring AND <see cref="KeyNotFoundException"/> when the table is sealed but
    /// <see cref="CodexService"/> is unregistered (the CURRENT state — no authored
    /// <c>MonsterDatabase.asset</c>, the Story 2.2 deferral). This view catches BOTH and stays
    /// inert (an <see cref="Open"/> call no-ops + warns), never throwing at <c>Awake</c>. It
    /// lights up once the <c>.asset</c> is authored and Bootstrap registers CodexService.
    /// </para>
    /// <para>
    /// <b>Wiring is Epic 6.</b> The grid→detail tap routing and this panel's placement in a
    /// scene are Story 6.3 (surfaces &amp; navigation); the real art draw, the rarity-badge
    /// color tokens (UX-DR2), and the panel layout/typography/animation are UX-DR5 (Story 6.2).
    /// <see cref="Render"/> is a deliberate stub here — the grid already carries
    /// <c>CodexSlot.Id</c>, so the entry point is simply <c>Open(slot.Id)</c>.
    /// </para>
    /// </summary>
    public sealed class CodexDetailView : MonoBehaviour
    {
        private CodexService _codex;
        private CodexDetailPresenter _presenter;

        private void Awake()
        {
            // The locator read — guarded against BOTH failure modes. Never throw at Awake: an
            // inert panel is the correct seam until CodexService is registered.
            try
            {
                _codex = GameServices.Get<CodexService>();
                _presenter = new CodexDetailPresenter(_codex);
            }
            catch (Exception ex) when (ex is ServicesNotReadyException || ex is KeyNotFoundException)
            {
                _codex = null;
                _presenter = null;
                GameLog.Warn(
                    "CodexDetailView: CodexService is not available yet (services not ready, or the " +
                    "service is unregistered pending the MonsterDatabase.asset). The detail panel is " +
                    "inert. " + ex.Message);
            }
        }

        /// <summary>
        /// Open the detail panel for the tapped slot's <paramref name="id"/>. The entry point
        /// from the grid (Epic 6 wires <c>CodexSlot.Id → Open</c>). No-ops + warns when inert
        /// (CodexService unregistered). All discovered-vs-not / null-safe / copy decisions are
        /// in the presenter; this method only binds the result.
        /// </summary>
        public void Open(string id)
        {
            if (_presenter == null)
            {
                GameLog.Warn($"CodexDetailView.Open('{id}') ignored — the panel is inert (CodexService unregistered).");
                return;
            }

            CodexDetailViewModel vm = _presenter.BuildDetail(id);
            Render(vm);
        }

        // The bind site. Logic-free BY DESIGN: NO discovered-vs-not branching beyond choosing
        // which widget group to show — all content decisions are in the presenter. This story
        // stubs the render to a log; the real binding lands in Epic 6.
        // TODO(Epic 6): bind the discovered panel (art via the id, rarity badge via Tier + UX-DR2
        // tokens, stats, lore, the first-discovered date) OR the "Not yet discovered." copy.
        private void Render(CodexDetailViewModel vm)
        {
            if (!vm.IsDiscovered)
            {
                GameLog.Info($"CodexDetailView: slot '{vm.Id}' — {CodexDetailViewModel.NotDiscoveredCopy}");
                return;
            }

            GameLog.Info(
                $"CodexDetailView: opened '{vm.Id}' — {vm.DisplayName} ({vm.Tier?.ToString() ?? "untiered"}), " +
                $"discovered {vm.Discovered ?? "—"}.");
        }
    }
}
