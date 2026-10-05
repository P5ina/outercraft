using UnityEngine;
using UnityEngine.InputSystem;

namespace OuterCraft.Player
{
    /// Minecraft's movement on Outer Wilds' own character controller: we keep its spherical gravity,
    /// moving ground and collisions, and swap in Minecraft's numbers: walk 4.317 m/s, sprint 5.612
    /// (Ctrl or double-tap W), sneak 1.295 (Shift), a jump that always reaches 1.25 blocks whatever
    /// the planet's gravity, no charge-jump; in the suit the jetpack only fires once you're airborne.
    public sealed class McMovement
    {
        public const float Walk = 4.317f, Sprint = 5.612f, Sneak = 1.295f, JumpHeight = 1.25f;

        private PlayerCharacterController _c;
        private JetpackThrusterController _jetpack;
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
            if (_c != c) { _c = c; _saved = false; _jetpack = c.GetComponentInChildren<JetpackThrusterController>() ?? Object.FindObjectOfType<JetpackThrusterController>(); }
            if (!_saved)
            {
                _run = c._runSpeed; _walk = c._walkSpeed; _strafe = c._strafeSpeed; _air = c._airSpeed;
                _accel = c._acceleration; _airAccel = c._airAcceleration;
                _minJump = c._minJumpSpeed; _maxJump = c._maxJumpSpeed; _chargeJump = c._useChargeJump;
                _saved = true;
            }
            _active = true;
            SuitJump(c);

            var kb = Keyboard.current;
            bool fwd = kb != null && kb.wKey.isPressed;
            if (kb != null && kb.wKey.wasPressedThisFrame)
            {
                if (Time.unscaledTime - _lastWTap < 0.3f) _sprintLatched = true;
                _lastWTap = Time.unscaledTime;
            }
            Sneaking = kb != null && kb.leftShiftKey.isPressed;
            if (!fwd || Sneaking) _sprintLatched = false;
            Sprinting = fwd && !Sneaking && (_sprintLatched || (kb != null && kb.leftCtrlKey.isPressed));

            float speed = Sneaking ? Sneak : Sprinting ? Sprint : Walk;
            c._runSpeed = speed;
            c._walkSpeed = speed;
            c._strafeSpeed = speed;
            c._airSpeed = speed;
            // Minecraft reaches full speed in a few ticks and stops almost at once.
            c._acceleration = Mathf.Max(_accel, 0.8f);
            c._useChargeJump = false;

            // v = sqrt(2 g h): the same 1.25-block jump on every planet.
            float g = 9.81f;
            var det = Locator.GetPlayerForceDetector();
            if (det != null)
            {
                float a = det.GetForceAcceleration().magnitude;
                if (a > 0.5f) g = a;
            }
            float v = Mathf.Sqrt(2f * g * JumpHeight);
            c._minJumpSpeed = v;
            c._maxJumpSpeed = v;
        }

        private float _jumpTime = -10f;
        public bool Gliding;   // the elytra has the air
        private bool _jumpPending;

        /// Called from FixedUpdate: Outer Wilds' own jump (speed from our min/max, ungrounding,
        /// ground-snap pause, events), which its input code won't start in the suit or with Shift held.
        public void FixedStep()
        {
            if (_enforce > 0)
            {
                _enforce--;
                EnforceJumpSpeed(Time.fixedDeltaTime * 1.5f);
            }
            if (!_jumpPending) return;
            _jumpPending = false;
            if (_c == null || !_active || !_c.IsGrounded()) return;
            _c.ApplyJump();
            // leave the ground for sure: no snapping back down this step (with Shift held the
            // controller otherwise keeps us glued to the floor)
            _c._isGrounded = false;
            _c._lastJumpTime = Time.time;
            _c._jumpNextFixedUpdate = false;
            // In the suit the controller's jump comes out weaker: make it Minecraft's jump either way,
            // the same 1.25 blocks, and check again next step in case the jump lands late.
            EnforceJumpSpeed(0f);
            _enforce = 1;
        }

        private int _enforce;

        /// Raise (never lower) the speed away from the ground to the jump speed.
        private void EnforceJumpSpeed(float slackSeconds)
        {
            var body = Locator.GetPlayerBody();
            var det = Locator.GetPlayerForceDetector();
            if (body == null || det == null || _c == null) return;
            var acc = det.GetForceAcceleration();
            float g = acc.magnitude;
            if (g < 0.5f) return;
            var up = -acc / g;
            var ground = _c.GetLastGroundBody();
            var groundVel = ground != null ? ground.GetPointVelocity(body.GetPosition()) : Vector3.zero;
            var rel = body.GetVelocity() - groundVel;
            float want = Mathf.Sqrt(2f * g * JumpHeight) - g * slackSeconds;
            float have = Vector3.Dot(rel, up);
            if (have < want) body.SetVelocity(body.GetVelocity() + up * (want - have));
        }

        /// In the suit Outer Wilds jumps through the jetpack, and with Shift held it doesn't jump at all. Here: a tap of Space is Minecraft's
        /// jump (the controller's own jump, with our speed); holding it once airborne lights the
        /// jetpack, so you can still fly. Without the suit there's simply no jetpack.
        private void SuitJump(PlayerCharacterController c)
        {
            bool suit = PlayerState.IsWearingSuit();
            bool grounded = c.IsGrounded();
            var kb = Keyboard.current;
            if ((suit || (kb != null && kb.leftShiftKey.isPressed)) && grounded && kb != null && kb.spaceKey.wasPressedThisFrame && Time.time - _jumpTime > 0.2f)
            {
                _jumpPending = true;   // done in FixedUpdate through the controller's own ApplyJump
                _jumpTime = Time.time;
            }
            if (_jetpack != null)
                _jetpack.enabled = suit && !grounded && !Gliding && Time.time - _jumpTime > 0.3f;
        }

        public void Restore()
        {
            if (!_active || _c == null || !_saved) return;
            _c._runSpeed = _run; _c._walkSpeed = _walk; _c._strafeSpeed = _strafe; _c._airSpeed = _air;
            _c._acceleration = _accel; _c._airAcceleration = _airAccel;
            _c._minJumpSpeed = _minJump; _c._maxJumpSpeed = _maxJump; _c._useChargeJump = _chargeJump;
            if (_jetpack != null) _jetpack.enabled = true;
            _active = false;
            Sprinting = Sneaking = false;
        }

        public void Forget()
        {
            _c = null;
            _jetpack = null;
            _saved = _active = false;
        }
    }
}
