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
    /// A configurable <see cref="IArAnchorProvider"/> fake driving BOTH the recovery
    /// (<see cref="EncounterService.Recover"/>) path AND the Story 4.2 forward placement path.
    /// <para>
    /// <b>Recovery:</b> set <see cref="ReacquireSucceeds"/> for the <c>Restored</c> case, supply
    /// <see cref="Candidates"/> for <c>RelocatedToPlane</c>, or neither for <c>Failed</c>;
    /// <see cref="ReacquireCalls"/>/<see cref="GetRelocationCandidatesCalls"/> pin the AC-3 "TryRestore was
    /// called" seam.
    /// </para>
    /// <para>
    /// <b>Forward placement (4.2):</b> set <see cref="AvailablePlacements"/> to the number of successful
    /// placements this provider will grant — each <c>TryPlace()</c> consumes one. With a placement available,
    /// <see cref="HasTrackablePlane"/> is true, <see cref="TryGetPlacementPose"/> yields a unique pose, and
    /// <see cref="TryCreateAnchor"/> returns a real <see cref="AnchorToken"/>; once exhausted it reports no
    /// plane (the coaching path). This lets a Multi-Lure test grant exactly one placement (→ block) or two
    /// (→ spawn both).
    /// </para>
    /// </summary>
    internal sealed class FakeArAnchorProvider : IArAnchorProvider
    {
        public bool ReacquireSucceeds;
        public Pose ReacquirePose = new Pose(Vector3.zero, Quaternion.identity);
        public PlaneCandidate[] Candidates;

        public int ReacquireCalls { get; private set; }
        public int GetRelocationCandidatesCalls { get; private set; }

        /// <summary>How many successful forward placements remain (each <c>TryPlace()</c> consumes one).
        /// 0 (default) = the recovery-only behaviour (no plane), preserving the 4.1 tests' expectations.</summary>
        public int AvailablePlacements;

        /// <summary>How many anchors this provider has created (the placement seam-call pin).</summary>
        public int CreateAnchorCalls { get; private set; }

        private int _anchorSeq;

        public bool HasTrackablePlane => AvailablePlacements > 0;

        public bool TryGetPlacementPose(out Pose pose, out string planeId)
        {
            if (AvailablePlacements <= 0)
            {
                pose = default;
                planeId = null;
                return false;
            }

            // A unique-per-call pose/plane so two Multi placements are distinguishable.
            int n = _anchorSeq + 1;
            pose = new Pose(new Vector3(n, 0f, 0f), Quaternion.identity);
            planeId = $"plane-{n}";
            return true;
        }

        public bool TryCreateAnchor(in Pose pose, string planeId, out AnchorToken token)
        {
            if (AvailablePlacements <= 0)
            {
                token = default;
                return false;
            }

            AvailablePlacements--;
            _anchorSeq++;
            CreateAnchorCalls++;
            token = new AnchorToken($"anchor-{_anchorSeq}", pose.position, pose.rotation);
            return true;
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

    /// <summary>
    /// A scripted <see cref="IRandom"/> for the deterministic Lure rarity roll (Story 4.2, AC-2). Queue the
    /// exact <see cref="NextDouble"/> values (the rare-gate draws) and <see cref="Next"/> values (the uniform
    /// index picks) the test needs; draws past the end of a queue return a safe default (0). This lets a test
    /// force "the roll lands above Basic's rare chance but below Premium's" to pin the strict inequality.
    /// </summary>
    internal sealed class FakeRandom : IRandom
    {
        private readonly Queue<double> _doubles = new Queue<double>();
        private readonly Queue<int> _ints = new Queue<int>();

        public FakeRandom EnqueueDouble(params double[] values)
        {
            foreach (double v in values)
            {
                _doubles.Enqueue(v);
            }

            return this;
        }

        public FakeRandom EnqueueNext(params int[] values)
        {
            foreach (int v in values)
            {
                _ints.Enqueue(v);
            }

            return this;
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }

            int v = _ints.Count > 0 ? _ints.Dequeue() : 0;
            // Clamp into range so an over-long script (or a fallback pool of a different size) is still safe.
            return ((v % maxExclusive) + maxExclusive) % maxExclusive;
        }

        public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.0;
    }

    /// <summary>
    /// A counting <see cref="ISpawnSink"/> for the Story 4.2 spawn pins — tracks how many instances are live
    /// so a test can assert "two monsters spawned" (Multi) or "none leaked" (a blocked Lure). Mirrors the
    /// AR.Tests fake's shape; authored here so Encounter.Tests is self-contained.
    /// </summary>
    internal sealed class FakeSpawnSink : ISpawnSink
    {
        public int LiveCount { get; private set; }
        public int InstantiateCalls { get; private set; }
        private int _seq;

        public int Instantiate(in Pose pose)
        {
            InstantiateCalls++;
            LiveCount++;
            return _seq++;
        }

        public void Activate(int id, in Pose pose) => LiveCount++;

        public void Deactivate(int id) => LiveCount--;
    }
}
