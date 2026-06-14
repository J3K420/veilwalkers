using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Veilwalkers.AR;
using Veilwalkers.Core;
using Veilwalkers.Core.Contracts;
using Veilwalkers.Persistence;

namespace Veilwalkers.Encounter.Tests
{
    /// <summary>
    /// A fixed-instant <see cref="IClock"/> for the Codex first-discovered date stamp (so the composed
    /// write's codex stage is deterministic). Authored fresh here — each test assembly owns its own
    /// (the Monsters/Economy copies are <c>internal</c> to their assemblies).
    /// </summary>
    internal sealed class FakeClock : IClock
    {
        private readonly DateTime _now;

        public FakeClock(DateTime now)
        {
            _now = now;
        }

        public DateTime UtcNow => _now;
    }

    /// <summary>
    /// A DEEP-cloning in-memory <see cref="IProgressStore"/> for the Encounter atomic-write tests. The
    /// AC-2 rollback/commit assertions touch BOTH the economy slice (charges) AND the codex slice, so the
    /// clone must copy <c>Codex</c> too (the Economy fake drops it — reused unchanged, the codex assertions
    /// would be tautologically green; the [[tautological-test-trap]] lesson). Mirrors the production store:
    /// <see cref="SaveAsync"/> stores a SNAPSHOT, <see cref="LoadAsync"/> returns a fresh copy, so
    /// <see cref="Stored"/> never aliases the live model. <see cref="FailNextSave"/> drives the persist-fault
    /// rollback path; <see cref="SaveCalls"/> is the AR-8 "exactly one persist per action" pin.
    /// </summary>
    internal sealed class FakeEncounterProgressStore : IProgressStore
    {
        private int _saveCalls;

        public SaveModel Stored;
        public bool FailNextSave;

        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public Task<SaveModel> LoadAsync()
        {
            if (Stored == null)
            {
                throw new FileNotFoundException("FakeEncounterProgressStore: no save exists.");
            }

            return Task.FromResult(Clone(Stored));
        }

        public Task SaveAsync(SaveModel model)
        {
            Interlocked.Increment(ref _saveCalls);

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("FakeEncounterProgressStore: simulated save failure.");
            }

            Stored = Clone(model);
            return Task.CompletedTask;
        }

        public bool Exists() => Stored != null;

        public Task DeleteAsync()
        {
            Stored = null;
            return Task.CompletedTask;
        }

        /// <summary>Deep copy: scalars value-copied AND <c>Codex</c> fully cloned (new dict, fresh entry per
        /// key, fresh variant-flags list) — so "persisted? / rolled back?" assertions are honest.</summary>
        private static SaveModel Clone(SaveModel model)
        {
            var copy = new SaveModel
            {
                SchemaVersion = model.SchemaVersion,
                Credits = model.Credits,
                StartingCreditsGranted = model.StartingCreditsGranted,
                Xp = model.Xp,
                Level = model.Level,
                StrongCaptureCharges = model.StrongCaptureCharges,
                StabilityBoostCharges = model.StabilityBoostCharges,
                NightveilFilterCharges = model.NightveilFilterCharges,
                DailyClaim = model.DailyClaim,
                FirstZeroCreditDay = model.FirstZeroCreditDay,
                Codex = new Dictionary<string, CodexEntryData>(),
            };

            if (model.Codex != null)
            {
                foreach (KeyValuePair<string, CodexEntryData> pair in model.Codex)
                {
                    CodexEntryData src = pair.Value;
                    copy.Codex[pair.Key] = new CodexEntryData
                    {
                        Scanned = src?.Scanned ?? false,
                        Captured = src?.Captured ?? false,
                        Slain = src?.Slain ?? false,
                        Discovered = src?.Discovered,
                        VariantFlags = src?.VariantFlags == null
                            ? new List<string>()
                            : new List<string>(src.VariantFlags),
                    };
                }
            }

            return copy;
        }
    }

    /// <summary>
    /// A minimal <see cref="IArSession"/> fake — enough to drive <see cref="ArSessionService"/> to
    /// <c>Running</c> and then through the backgrounded-resume interruption path (which raises
    /// <c>OnArSessionInterrupted</c>). <see cref="StartAsync"/> completes synchronously (editor-stub shape).
    /// </summary>
    internal sealed class FakeArSession : IArSession
    {
        public bool IsSupported { get; set; } = true;

        public Task StartAsync() => Task.CompletedTask;

        public void Pause() { }

        public void Resume() { }

        public void Stop() { }
    }

    /// <summary>
    /// A settable <see cref="ICameraPermission"/> fake. Flip <see cref="HasCameraPermission"/> to revoke
    /// permission while backgrounded — the resume path then tears down + raises <c>OnArSessionInterrupted</c>,
    /// the real AC-3 trigger.
    /// </summary>
    internal sealed class FakeCameraPermission : ICameraPermission
    {
        public bool HasCameraPermission { get; set; } = true;

        public void RequestCameraPermission() { }

        public void OpenAppSettings() { }
    }

    /// <summary>
    /// A minimal <see cref="IArAnchorProvider"/> fake for the recovery (<see cref="EncounterService.Recover"/>)
    /// path. Drives the <see cref="AnchorRestoreService"/> decision: set <see cref="ReacquireSucceeds"/> for
    /// the <c>Restored</c> case, supply <see cref="Candidates"/> for <c>RelocatedToPlane</c>, or neither for
    /// <c>Failed</c>. Exposes call counts so the AC-3 "TryRestore was called" seam pin is real. The forward
    /// (placement) members are unused here and return safe defaults.
    /// </summary>
    internal sealed class FakeArAnchorProvider : IArAnchorProvider
    {
        public bool ReacquireSucceeds;
        public Pose ReacquirePose = new Pose(Vector3.zero, Quaternion.identity);
        public PlaneCandidate[] Candidates;

        public int ReacquireCalls { get; private set; }
        public int GetRelocationCandidatesCalls { get; private set; }

        public bool HasTrackablePlane => false;

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            pose = default;
            planeId = null;
            return false;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            token = default;
            return false;
        }

        public bool TryReacquireAnchor(in AnchorToken token, out Pose pose)
        {
            ReacquireCalls++;
            pose = ReacquirePose;
            return ReacquireSucceeds;
        }

        public bool TryGetRelocationCandidates(out PlaneCandidate[] candidates)
        {
            GetRelocationCandidatesCalls++;
            candidates = Candidates ?? Array.Empty<PlaneCandidate>();
            return candidates.Length > 0;
        }
    }
}
