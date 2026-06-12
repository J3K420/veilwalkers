using System;

namespace Veilwalkers.Core
{
    /// <summary>
    /// Production <see cref="IClock"/> backed by the system clock. Tests substitute
    /// a fake clock to control time.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
