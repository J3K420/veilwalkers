namespace Veilwalkers.Persistence
{
    /// <summary>
    /// Ad-reward counters persisted on <see cref="SaveModel"/>. Only the FIELDS land
    /// in this story; the grant caps and reset rules are Story 1.9.
    /// </summary>
    public sealed class AdRewardData
    {
        /// <summary>Ad-reward grants consumed within the current day bucket.</summary>
        public int GrantsToday { get; set; }

        /// <summary>
        /// UTC day-bucket the <see cref="GrantsToday"/> counter belongs to (a
        /// <c>long</c> integer derived via <c>IClock</c>, never a serialized DateTime).
        /// </summary>
        public long DayBucket { get; set; }
    }
}
