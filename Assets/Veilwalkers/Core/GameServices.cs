using System;
using System.Collections.Generic;

namespace Veilwalkers.Core
{
    /// <summary>
    /// The single composition root: a wiring table mapping a service type to its
    /// instance. It is populated ONCE at App/Bootstrap and is read-only thereafter.
    /// <para>
    /// <b>Locator confinement (enforced by convention):</b> only <c>App/Bootstrap</c>
    /// is allowed to WIRE this table (<see cref="Register{T}"/> + <see cref="MarkReady"/>),
    /// and only MonoBehaviours / UI are allowed to READ it (<see cref="Get{T}"/>).
    /// Pure-logic areas (Economy, Encounter, Monsters, Persistence, Billing) must
    /// receive their dependencies via constructor / <c>Init(...)</c> injection and
    /// must NOT call <see cref="Get{T}"/>. Keeping the locator out of the service
    /// tiers is half of what keeps the assembly graph acyclic and the services
    /// unit-testable without this locator.
    /// </para>
    /// </summary>
    public static class GameServices
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();
        private static bool _ready;

        /// <summary>True once <see cref="MarkReady"/> has been called.</summary>
        public static bool IsReady => _ready;

        /// <summary>
        /// Register a service instance under type <typeparamref name="T"/>. Only
        /// callable during wiring (before <see cref="MarkReady"/>); registering after
        /// the table is sealed, with a null instance, or twice for the same type is
        /// rejected with an <see cref="InvalidOperationException"/>.
        /// </summary>
        public static void Register<T>(T instance) where T : class
        {
            if (_ready)
            {
                throw new InvalidOperationException(
                    $"GameServices is sealed (MarkReady already called); cannot register {typeof(T).Name}.");
            }

            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance),
                    $"Cannot register a null instance for {typeof(T).Name}.");
            }

            if (Services.ContainsKey(typeof(T)))
            {
                throw new InvalidOperationException(
                    $"Service {typeof(T).Name} is already registered.");
            }

            Services[typeof(T)] = instance;
        }

        /// <summary>
        /// Seal the table. After this call the wiring is read-only and
        /// <see cref="Get{T}"/> will resolve registered services. Calling twice is
        /// rejected so the "wire once" rule is explicit.
        /// </summary>
        public static void MarkReady()
        {
            if (_ready)
            {
                throw new InvalidOperationException("GameServices.MarkReady has already been called.");
            }

            _ready = true;
        }

        /// <summary>
        /// Resolve the service registered for <typeparamref name="T"/>. Throws
        /// <see cref="ServicesNotReadyException"/> (never returns null) if called
        /// before <see cref="MarkReady"/>, and <see cref="KeyNotFoundException"/> if
        /// no service of that type was registered.
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (!_ready)
            {
                throw new ServicesNotReadyException(
                    $"GameServices.Get<{typeof(T).Name}>() called before wiring completed. " +
                    "Services are wired once at App/Bootstrap; read them only after MarkReady().");
            }

            if (!Services.TryGetValue(typeof(T), out var instance))
            {
                throw new KeyNotFoundException(
                    $"No service registered for type {typeof(T).Name}.");
            }

            return (T)instance;
        }

        /// <summary>
        /// Clear the table and return to the unready state. FOR TESTS ONLY: lets
        /// each test start from a clean locator and register its own fakes. Not part
        /// of the production flow.
        /// </summary>
        public static void ResetForTests()
        {
            Services.Clear();
            _ready = false;
        }
    }
}
