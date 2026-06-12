using System.Collections.Generic;
using NUnit.Framework;
using Veilwalkers.Core;

namespace Veilwalkers.Architecture.Tests
{
    /// <summary>
    /// Verifies the composition-root contract (AC-3): <see cref="GameServices.Get{T}"/>
    /// throws <see cref="ServicesNotReadyException"/> before wiring, resolves the
    /// registered instance after wiring, and <see cref="GameServices.ResetForTests"/>
    /// returns the locator to the unready state.
    /// </summary>
    public sealed class GameServicesTests
    {
        // A trivial service contract + impl used only to exercise the locator.
        private interface ISampleService
        {
        }

        private sealed class SampleService : ISampleService
        {
        }

        [SetUp]
        [TearDown]
        public void ResetLocator()
        {
            GameServices.ResetForTests();
        }

        [Test]
        public void Get_before_wiring_throws_ServicesNotReady_not_null()
        {
            Assert.IsFalse(GameServices.IsReady);
            Assert.Throws<ServicesNotReadyException>(() => GameServices.Get<ISampleService>());
        }

        [Test]
        public void Get_after_wiring_returns_the_registered_instance()
        {
            var impl = new SampleService();
            GameServices.Register<ISampleService>(impl);
            GameServices.MarkReady();

            Assert.IsTrue(GameServices.IsReady);
            Assert.AreSame(impl, GameServices.Get<ISampleService>());
        }

        [Test]
        public void Register_after_MarkReady_is_rejected()
        {
            GameServices.MarkReady();
            Assert.Throws<System.InvalidOperationException>(
                () => GameServices.Register<ISampleService>(new SampleService()));
        }

        [Test]
        public void Register_null_instance_throws_ArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => GameServices.Register<ISampleService>(null));
        }

        [Test]
        public void Register_same_type_twice_is_rejected()
        {
            GameServices.Register<ISampleService>(new SampleService());
            Assert.Throws<System.InvalidOperationException>(
                () => GameServices.Register<ISampleService>(new SampleService()));
        }

        [Test]
        public void MarkReady_twice_is_rejected()
        {
            GameServices.MarkReady();
            Assert.Throws<System.InvalidOperationException>(() => GameServices.MarkReady());
        }

        [Test]
        public void Register_destroyed_UnityObject_is_rejected()
        {
            var doomed = new UnityEngine.GameObject("doomed");
            UnityEngine.Object.DestroyImmediate(doomed);

            // CLR null check alone would pass a destroyed (fake-null) Unity object.
            Assert.Throws<System.ArgumentException>(
                () => GameServices.Register<UnityEngine.GameObject>(doomed));
        }

        [Test]
        public void Get_for_unregistered_type_after_ready_throws_KeyNotFound()
        {
            GameServices.MarkReady();
            Assert.Throws<KeyNotFoundException>(() => GameServices.Get<ISampleService>());
        }

        [Test]
        public void ResetForTests_returns_locator_to_unready_state()
        {
            GameServices.Register<ISampleService>(new SampleService());
            GameServices.MarkReady();
            Assert.IsTrue(GameServices.IsReady);

            GameServices.ResetForTests();

            Assert.IsFalse(GameServices.IsReady);
            Assert.Throws<ServicesNotReadyException>(() => GameServices.Get<ISampleService>());
        }
    }
}
