namespace Veilwalkers.Core
{
    /// <summary>
    /// A lightweight success/failure result for operations that do not return a
    /// value. Supports the project convention of returning a typed result rather
    /// than throwing for an expected failure. For operations that also report a
    /// credit balance, use <see cref="SpendResult"/> instead.
    /// </summary>
    public readonly struct Result
    {
        public bool Success { get; }

        private readonly string _message;

        /// <summary>
        /// Optional human-readable reason; empty on success. Never null — the getter
        /// coerces, so even <c>default(Result)</c> (which bypasses the constructor)
        /// honors the contract.
        /// </summary>
        public string Message => _message ?? string.Empty;

        private Result(bool success, string message)
        {
            Success = success;
            _message = message ?? string.Empty;
        }

        public static Result Ok() => new Result(true, string.Empty);

        public static Result Fail(string message) => new Result(false, message);
    }
}
