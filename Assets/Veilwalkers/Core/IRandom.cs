namespace Veilwalkers.Core
{
    /// <summary>
    /// The randomness seam (Story 4.2). Logic that needs a random draw — the Lure rarity roll, the uniform
    /// monster pick (<c>LureSystem</c>) — must go through this interface rather than calling
    /// <c>UnityEngine.Random</c> or <c>System.Random</c> directly, so the draws are deterministically
    /// fakeable in tests (the same discipline as <see cref="IClock"/> for time). <see cref="SystemRandom"/>
    /// is the production implementation; tests script a fake.
    /// <para>
    /// Deliberately minimal — only the two operations the rarity roll needs: a uniform integer in
    /// <c>[0, maxExclusive)</c> for picking an index, and a uniform double in <c>[0, 1)</c> for a
    /// probability gate. (<c>UnityEngine.Random</c> is NOT used: it is a static global, not seedable per
    /// instance, and not available off the main thread / in headless EditMode tests deterministically.)
    /// </para>
    /// </summary>
    public interface IRandom
    {
        /// <summary>
        /// A uniform random integer in the half-open range <c>[0, maxExclusive)</c>. Callers pass a
        /// positive <paramref name="maxExclusive"/> (e.g. a collection's <c>Count</c>) to pick an index.
        /// </summary>
        int Next(int maxExclusive);

        /// <summary>A uniform random double in the half-open range <c>[0.0, 1.0)</c> — a probability gate.</summary>
        double NextDouble();
    }
}
