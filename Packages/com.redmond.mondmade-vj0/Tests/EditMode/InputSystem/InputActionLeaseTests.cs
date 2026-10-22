using NUnit.Framework;
using UnityEngine.InputSystem;

namespace MondmadeVJ0.Tests
{
    public sealed class InputActionLeaseTests
    {
        [Test]
        public void SharedAutomaticActionRemainsEnabledUntilLastRouteUnbinds()
        {
            var action = new InputAction("Shared", InputActionType.Button);
            var leases = new InputActionLeaseManager();
            try
            {
                var firstRoute = leases.Acquire(action, InputActionOwnership.AutomaticLease);
                var secondRoute = leases.Acquire(action, InputActionOwnership.AutomaticLease);
                Assert.That(action.enabled, Is.True);

                firstRoute.Dispose();
                Assert.That(action.enabled, Is.True, "Disabling one Route must not disable another Route's shared Action.");

                secondRoute.Dispose();
                Assert.That(action.enabled, Is.False, "The last automatic lease disables an Action it enabled.");
            }
            finally
            {
                leases.ReleaseAll();
                action.Dispose();
            }
        }

        [Test]
        public void PreEnabledAndPlayerInputManagedActionsRemainEnabledAfterRelease()
        {
            var preEnabled = new InputAction("Pre-enabled", InputActionType.Button);
            var playerManaged = new InputAction("PlayerInput", InputActionType.Button);
            var leases = new InputActionLeaseManager();
            try
            {
                preEnabled.Enable();
                leases.Acquire(preEnabled, InputActionOwnership.AutomaticLease).Dispose();
                Assert.That(preEnabled.enabled, Is.True, "A lease must not disable an Action that was already enabled.");

                var playerLease = leases.Acquire(playerManaged, InputActionOwnership.PlayerInputManaged);
                Assert.That(playerManaged.enabled, Is.False, "PlayerInput-managed Actions stay under their external owner's control.");
                playerManaged.Enable();
                playerLease.Dispose();
                Assert.That(playerManaged.enabled, Is.True, "Releasing a borrowed lease must not disable the Action.");
            }
            finally
            {
                leases.ReleaseAll();
                preEnabled.Dispose();
                playerManaged.Dispose();
            }
        }

        [Test]
        public void MixingOwnershipModesForOneActionIsRejected()
        {
            var action = new InputAction("Conflicting", InputActionType.Button);
            var leases = new InputActionLeaseManager();
            try
            {
                var automatic = leases.Acquire(action, InputActionOwnership.AutomaticLease);
                Assert.That(() => leases.Acquire(action, InputActionOwnership.PlayerInputManaged),
                    Throws.InvalidOperationException.With.Message.Contains("cannot mix"));
                automatic.Dispose();
                Assert.That(action.enabled, Is.False);
            }
            finally
            {
                leases.ReleaseAll();
                action.Dispose();
            }
        }
    }
}
