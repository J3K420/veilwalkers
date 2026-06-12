namespace Veilwalkers.Persistence
{
    /// <summary>
    /// A purchase the client has seen but not finished reconciling with Google Play
    /// (AR-12). Only the persisted SHAPE lands in this story — the exactly-once
    /// reconciler that consumes it is Story 5.2.
    /// </summary>
    public sealed class PendingPurchaseRecord
    {
        /// <summary>Google Play order id (unique per purchase).</summary>
        public string OrderId { get; set; }

        /// <summary>The credit-pack product id that was purchased.</summary>
        public string PackId { get; set; }

        /// <summary>Reconciliation state tag (vocabulary defined by Story 5.2).</summary>
        public string State { get; set; }

        /// <summary>ISO-8601 UTC timestamp of when the record was written.</summary>
        public string IsoTimestampUtc { get; set; }
    }
}
