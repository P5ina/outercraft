using UnityEngine;
using UnityEngine.InputSystem;

namespace OuterCraft.Player
{
    /// Minecraft's movement on Outer Wilds' own character controller: we keep its spherical gravity,
    /// moving ground, collisions, jump and jetpack, and swap in Minecraft's speeds: walk 4.317 m/s,
    /// sprint 5.612 (Ctrl or double-tap W). Holding Space crouches (Outer Wilds' charge jump): that's the sneak.
    public sealed class McMovement
    {
        public const float Walk = 4.317f, Sprint = 5.612f, Sneak = 1.295f;

        private PlayerCharacterController _c;
        private bool _saved, _active;
        private float _run, _walk, _strafe, _air, _accel, _airAccel, _minJump, _maxJump;
        private bool _chargeJump;
        private float _lastWTap = -1f;
        private bool _sprintLatched;

        public bool Sprinting { get; private set; }
        public bool Sneaking { get; private set; }

        public void Apply(PlayerCharacterController c)
        {
            if (c == null) return;
            if (_c != c) { _c = c; _saved = false; }
            if (!_saved)
            {
                _run = c._runSpeed; _walk = c._walkSpeed; _strafe = c._strafeSpeed; _air = c._airSpeed;
                _accel = c._acceleration; _airAccel = c._airAcceleration;
                _minJump = c._minJumpSpeed; _maxJump = c._maxJumpSpeed; _chargeJump = c._useChargeJump;
                _saved = true;
            }
            _active = true;

            var kb = Keyboard.current;
            bool fwd = kb != null && kb.wKey.isPressed;
            if (kb != null && kb.wKey.wasPressedThisFrame)
            {
                if (Time.unscaledTime - _lastWTap < 0.3f) _sprintLatched = true;
                _lastWTap = Time.unscaledTime;
            }
            // Outer Wilds' controls: holding Space on the ground crouches (and charges the jump,
            // released for a higher one, all Outer Wilds' own); that crouch is Minecraft's sneak.
            // Shift stays the jetpack's.
            Sneaking = kb != null && kb.spaceKey.isPressed && c.IsGrounded() && !Gliding;
            if (!fwd || Sneaking) _sprintLatched = false;
            Sprinting = fwd && !Sneaking && (_sprintLatched || (kb != null && kb.leftCtrlKey.isPressed));

            // the crouch's slowdown is Outer Wilds' own: no Minecraft sneak speed on top of it
            float speed = Sprinting ? Sprint : Walk;
            c._runSpeed = speed;
            c._walkSpeed = speed;
            c._strafeSpeed = speed;
            c._airSpeed = speed;
            // Minecraft reaches full speed in a few ticks and stops almost at once.
            c._acceleration = Mathf.Max(_accel, 0.8f);
        }

        public bool Gliding;   // the elytra has the air

        public void Restore()
        {
            if (!_active || _c == null || !_saved) return;
            _c._runSpeed = _run; _c._walkSpeed = _walk; _c._strafeSpeed = _strafe; _c._airSpeed = _air;
            _c._acceleration = _accel; _c._airAcceleration = _airAccel;
            _c._minJumpSpeed = _minJump; _c._maxJumpSpeed = _maxJump; _c._useChargeJump = _chargeJump;
            _active = false;
            Sprinting = Sneaking = false;
        }

        public void Forget()
        {
            _c = null;
            _saved = _active = false;
        }
    }
}
