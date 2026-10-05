using OuterCraft.Assets;
using OuterCraft.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OuterCraft.Player
{
    /// Minecraft's elytra on Outer Wilds' gravity: jump in mid-air with an elytra in the chest slot
    /// to glide. The flight is LivingEntity.travel's fall-flying branch, tick for tick, worked out
    /// in the frame of the ground you left and against the local pull of the planet; firework
    /// rockets give FireworkRocketEntity's boost. Landing ends it.
    public sealed class Elytra
    {
        public bool Gliding { get; private set; }
        private float _boost;                 // seconds of rocket push left
        private float _savedAirAccel;
        private bool _savedAir;
        private AudioSource _wind;
        private float _flyTime;
        private Vector3 _lastSet;          // the velocity we set last step, blocks/tick, ground frame
        private OWRigidbody _lastGround;
        private bool _haveLast;
        private float _pullSeen = 1f;      // how much of the planet's pull Outer Wilds applied between our steps
        private static readonly System.Random Rng = new System.Random();

        public void Update(Inventory inv, bool allowed)
        {
            var c = Locator.GetPlayerController();
            if (c == null) return;
            if (!allowed || !inv.HasElytra || c.IsGrounded() || Gravity() < 0.5f || PlayerState.IsAttached())
            {
                Stop(c);
                return;
            }
            var kb = Keyboard.current;
            if (!Gliding && kb != null && kb.spaceKey.wasPressedThisFrame && Locator.GetPlayerBody() != null)
                Start(c);
            if (Gliding) UpdateWind();
        }

        private void Start(PlayerCharacterController c)
        {
            Gliding = true;
            _flyTime = 0;
            UI.Advancements.Grant("skys_the_limit");
            _haveLast = false;
            _pullSeen = 1f;
            _savedAirAccel = c._airAcceleration;
            _savedAir = true;
            c._airAcceleration = 0f; // no WASD steering in the air: you steer with your head
        }

        public void Stop(PlayerCharacterController c)
        {
            if (Gliding && _savedAir && c != null) c._airAcceleration = _savedAirAccel;
            _savedAir = false;
            Gliding = false;
            _boost = 0;
            if (_wind != null) _wind.Stop();
        }

        /// Using a firework rocket while gliding.
        public bool Boost()
        {
            if (!Gliding) return false;
            // FireworkRocketEntity: lifetime 10 * (flight + 1) + rand(6) + rand(7) ticks, flight 1
            _boost = (20 + Rng.Next(6) + Rng.Next(7)) / 20f;
            var body = Locator.GetPlayerBody();
            if (body != null) McSounds.Play("fireworks.launch", body.GetPosition(), body.transform, 3f, 1f, 48f);
            return true;
        }

        private static float Gravity()
        {
            var det = Locator.GetPlayerForceDetector();
            return det != null ? det.GetForceAcceleration().magnitude : 0f;
        }

        /// Called from FixedUpdate. Outer Wilds adds the planet's pull itself (once per step), so the
        /// "-gravity" of Minecraft's formula is left to it and everything else is applied here.
        public void FixedStep()
        {
            if (!Gliding) return;
            var c = Locator.GetPlayerController();
            var body = Locator.GetPlayerBody();
            var cam = Locator.GetPlayerCamera();
            var det = Locator.GetPlayerForceDetector();
            if (c == null || body == null || cam == null || det == null) return;
            var acc = det.GetForceAcceleration();
            float g = acc.magnitude;
            if (g < 0.5f) return;
            float dt = Time.fixedDeltaTime;
            float k = dt * 20f;                       // Minecraft ticks in this step
            _flyTime += dt;

            var up = -acc / g;
            var ground = c.GetLastGroundBody();
            var pos = body.GetPosition();
            var refVel = ground != null ? ground.GetPointVelocity(pos) : Vector3.zero;
            var v = (body.GetVelocity() - refVel) / 20f; // blocks per tick
            var look = cam.transform.forward;

            float lookUp = Vector3.Dot(look, up);
            var lookH = look - up * lookUp;
            float horizLook = lookH.magnitude;
            var dirH = horizLook > 1e-4f ? lookH / horizLook : Vector3.zero;
            float vy = Vector3.Dot(v, up);
            var vH = v - up * vy;
            float horizSpeed = vH.magnitude;
            float sinPitch = -lookUp;                 // Minecraft's pitch: positive looking down
            float f = horizLook * horizLook;          // cos(pitch)^2
            float gT = g / 400f;                      // the pull in blocks/tick²

            // gravity * (-1 + f * 0.75). The -1 is the planet's pull: check whether Outer Wilds applied
            // it since our last step (it doesn't always while we set the velocity) and add it if not.
            if (_haveLast && ground == _lastGround)
            {
                float applied = Vector3.Dot(v - _lastSet, up) / Mathf.Max(1e-5f, gT * k); // -1 = full pull applied
                _pullSeen = Mathf.Lerp(_pullSeen, Mathf.Clamp(-applied, 0f, 1.5f), 0.2f);
            }
            if (_pullSeen < 0.5f) vy -= gT * k;
            vy += gT * f * 0.75f * k;
            if (vy < 0 && horizLook > 0)
            {
                float d = vy * -0.1f * f * k;         // falling turns into forward speed
                vH += dirH * d;
                vy += d;
            }
            if (sinPitch < 0 && horizLook > 0)
            {
                float d = horizSpeed * -sinPitch * 0.04f * k; // pulling up trades speed for height
                vH -= dirH * d;
                vy += d * 3.2f;
            }
            if (horizLook > 0) vH += (dirH * horizSpeed - vH) * 0.1f * k;
            vH *= Mathf.Pow(0.99f, k);
            vy *= Mathf.Pow(0.98f, k);

            var vNew = vH + up * vy;
            if (_boost > 0f)
            {
                vNew += (look * 0.1f + (look * 1.5f - vNew) * 0.5f) * k;
                _boost -= dt;
                // the rocket's trail behind you
                if (Fx.Instance != null && ground != null && Rng.Next(2) == 0)
                    Fx.Instance.Add(ground.transform, pos - look * 0.8f - up * 0.5f, refVel - look * 2f, 6 + Rng.Next(6), 0.1f, Color.white, Fx.Sprite.Spark, drag: 0.91f);
            }
            body.SetVelocity(refVel + vNew * 20f);
            _lastSet = vNew;
            _lastGround = ground;
            _haveLast = true;
        }

        /// ElytraOnPlayerSoundInstance: the wind, louder and higher the faster you go.
        private void UpdateWind()
        {
            var body = Locator.GetPlayerBody();
            var c = Locator.GetPlayerController();
            if (body == null || c == null) return;
            if (_wind == null)
            {
                var clip = McSounds.Clip("elytra.loop");
                if (clip == null) return;
                var go = new GameObject("OuterCraft_ElytraWind");
                Object.DontDestroyOnLoad(go);
                _wind = go.AddComponent<AudioSource>();
                _wind.clip = clip;
                _wind.loop = true;
                _wind.spatialBlend = 0f;
                _wind.playOnAwake = false;
            }
            var ground = c.GetLastGroundBody();
            var v = (body.GetVelocity() - (ground != null ? ground.GetPointVelocity(body.GetPosition()) : Vector3.zero)) / 20f;
            float vol = Mathf.Clamp01(v.sqrMagnitude / 4f);
            if (_flyTime < 1f) vol *= _flyTime; // fades in over the first 20 ticks
            _wind.volume = vol * McSounds.Volume;
            _wind.pitch = vol > 0.8f ? 1f + (vol - 0.8f) : 1f;
            if (!_wind.isPlaying) _wind.Play();
        }
    }
}
