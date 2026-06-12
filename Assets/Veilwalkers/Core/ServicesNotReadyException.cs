using System;

namespace Veilwalkers.Core
{
    /// <summary>
    /// Thrown by <see cref="GameServices.Get{T}"/> when a service is requested
    /// before the composition root has finished wiring. This is the explicit guard
    /// for the "services-not-ready race": readers that run too early get this clear,
    /// named exception instead of a <see cref="NullReferenceException"/>.
    /// </summary>
    public sealed class ServicesNotReadyException : Exception
    {
        public ServicesNotReadyException(string message) : base(message)
        {
        }
    }
}
