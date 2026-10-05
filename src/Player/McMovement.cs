using UnityEngine.InputSystem;

namespace OuterCraft.Player
{
    /// Movement is Outer Wilds' own: its walk and run speeds, charge jump and jetpack. All that's
    /// read here is the crouch (Space held on the ground, charging the jump), which is Minecraft's
    /// sneak for Steve's pose.
    public sealed class McMovement
    {
        public bool Sneaking { get; private set; }
        public bool Gliding;   // the elytra has the air

        public void Apply(PlayerCharacterController c)
        {
            var kb = Keyboard.current;
            Sneaking = c != null && kb != null && kb.spaceKey.isPressed && c.IsGrounded() && !Gliding;
        }

        public void Restore() => Sneaking = false;

        public void Forget() => Sneaking = false;
    }
}
