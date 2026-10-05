using System.Collections.Generic;
using System.Linq;
using OuterCraft.Assets;
using OuterCraft.World;
using UnityEngine;

namespace OuterCraft.Player
{
    /// Hitting Hearthians (and the other travellers) the way Minecraft hits a villager: 20 health,
    /// damage by weapon, half a second of red flash and invulnerability, knockback with a hop,
    /// critical hits while falling, the villager's hurt and death sounds, the sideways death fall
    /// and the puff of smoke. They come back with the next loop.
    public sealed class Combat
    {
        public float Reach = 3f; // Minecraft's entity reach in survival

        private sealed class Npc
        {
            public CharacterAnimController Anim;
            public Transform Root;
            public Renderer[] Renderers;
            public float Health = 20f;
            public float HurtTime = -10f;
            public float DeathTime = -1f;
            public Vector3 BaseLocalPos;
            public Quaternion BaseLocalRot;
            public Vector3 Knock;       // root-parent-local velocity (m/s), sideways
            public float HopY, HopV;    // height above the ground and its speed, along the character's up
            public bool Tinted;
        }

        private readonly Dictionary<CharacterAnimController, Npc> _npcs = new Dictionary<CharacterAnimController, Npc>();
        private CharacterAnimController[] _all = new CharacterAnimController[0];
        private float _nextScan;
        private static readonly System.Random Rng = new System.Random();
        private static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();

        public void Clear()
        {
            _npcs.Clear();
            _all = new CharacterAnimController[0];
            _nextScan = 0;
        }

        private void Scan()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 2f;
            _all = Object.FindObjectsOfType<CharacterAnimController>();
        }

        private static Bounds BoundsOf(Renderer[] rs)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in rs)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private Npc Get(CharacterAnimController a)
        {
            if (_npcs.TryGetValue(a, out var n)) return n;
            n = new Npc
            {
                Anim = a,
                Root = a.transform,
                Renderers = a.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray(),
                BaseLocalPos = a.transform.localPosition,
                BaseLocalRot = a.transform.localRotation,
            };
            _npcs[a] = n;
            return n;
        }

        /// The character under the crosshair, nearer than `maxDist`.
        public bool Target(Camera cam, float maxDist, out CharacterAnimController hit, out float dist)
        {
            Scan();
            hit = null;
            dist = Mathf.Min(maxDist, Reach);
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            foreach (var a in _all)
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                if (_npcs.TryGetValue(a, out var known) && known.DeathTime >= 0) continue;
                var rs = known?.Renderers ?? a.GetComponentsInChildren<Renderer>();
                var b = BoundsOf(rs);
                if (b.size == Vector3.zero || (b.center - ray.origin).sqrMagnitude > 100f) continue;
                if (b.IntersectRay(ray, out float d) && d < dist) { dist = d; hit = a; }
            }
            return hit != null;
        }

        /// Player.attack: damage (x1.5 when falling = critical), knockback, sounds, red flash.
        public void Attack(CharacterAnimController a, ItemDef held, Camera cam)
        {
            var n = Get(a);
            if (n.DeathTime >= 0 || Time.time - n.HurtTime < 0.5f) return; // invulnerable for 10 ticks
            var body = Locator.GetPlayerBody();
            var ctl = Locator.GetPlayerController();
            float dmg = held != null ? held.Damage : 1f;
            bool crit = ctl != null && !ctl.IsGrounded() && body != null &&
                        Vector3.Dot(body.GetVelocity() - (ctl.GetLastGroundBody()?.GetPointVelocity(body.GetPosition()) ?? Vector3.zero), body.transform.up) < -0.5f;
            if (crit) dmg *= 1.5f;
            n.Health -= dmg;
            n.HurtTime = Time.time;
            if (n.HopY <= 0f && n.Knock.sqrMagnitude < 1e-4f) { n.BaseLocalPos = n.Root.localPosition; n.BaseLocalRot = n.Root.localRotation; }

            var frame = n.Root.parent != null ? n.Root.parent : n.Root;
            var at = BoundsOf(n.Renderers).center;
            McSounds.Play(crit ? "attack.crit" : "attack.strong", at, frame, 1f, 1f);
            if (crit) Fx.Instance?.Crit(frame, at, cam.transform.position);

            // no knockback: Hearthians stay where they stand (sliding them sent them into walls)

            if (n.Health <= 0)
            {
                n.DeathTime = 0f;
                McSounds.Play("villager.death", at, frame, 1f, (float)(Rng.NextDouble() - Rng.NextDouble()) * 0.2f + 1f);
                // no more talking
                if (a._dialogueTree != null) a._dialogueTree.gameObject.SetActive(false);
            }
            else McSounds.Play("villager.hit", at, frame, 1f, (float)(Rng.NextDouble() - Rng.NextDouble()) * 0.2f + 1f);
        }

        // ---------------------------------------------------------------- frame

        public void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float ticks = dt * 20f;
            var dead = new List<CharacterAnimController>();
            foreach (var kv in _npcs)
            {
                var n = kv.Value;
                if (n.Root == null) { dead.Add(kv.Key); continue; }
                var parent = n.Root.parent;

                // knockback slide (ground friction 0.546 per tick) and hop (gravity 0.08 blocks/tick²)
                bool moving = n.Knock.sqrMagnitude > 1e-4f || n.HopY > 0f || n.HopV > 0f;
                if (moving)
                {
                    n.BaseLocalPos += n.Knock * dt;
                    n.Knock *= Mathf.Pow(n.HopY > 0 ? 0.91f : 0.546f, ticks);
                    n.HopV -= 0.08f * 400f * dt;
                    n.HopY = Mathf.Max(0f, n.HopY + n.HopV * dt);
                    if (n.HopY <= 0f && n.HopV < 0f) n.HopV = 0f;
                }
                if (moving || n.DeathTime >= 0f)
                {
                    var upLocal = parent != null ? parent.InverseTransformDirection(n.Root.up) : Vector3.up;
                    n.Root.localPosition = n.BaseLocalPos + upLocal * n.HopY;
                }

                // death: tip over sideways over a second, then a puff of smoke
                if (n.DeathTime >= 0f)
                {
                    n.DeathTime += dt;
                    float f = Mathf.Min(1f, Mathf.Sqrt(n.DeathTime * 20f / 20f * 1.6f));
                    n.Root.localRotation = n.BaseLocalRot * Quaternion.AngleAxis(f * 90f, Vector3.forward);
                    if (n.DeathTime >= 1f)
                    {
                        Fx.Instance?.Poof(parent != null ? parent : n.Root, BoundsOf(n.Renderers), n.Root.up);
                        n.Root.gameObject.SetActive(false);
                        dead.Add(kv.Key);
                        continue;
                    }
                }

                // red flash while hurt (and all through dying)
                bool red = n.DeathTime >= 0f || Time.time - n.HurtTime < 0.5f;
                if (red != n.Tinted) Tint(n, red);
            }
            foreach (var d in dead) _npcs.Remove(d);
        }

        private static void Tint(Npc n, bool red)
        {
            n.Tinted = red;
            foreach (var r in n.Renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (!red)
                {
                    Mpb.Clear();
                    for (int i = 0; i < mats.Length; i++) r.SetPropertyBlock(Mpb, i);
                    continue;
                }
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                    var c = mats[i].GetColor("_Color");
                    Mpb.Clear();
                    Mpb.SetColor("_Color", new Color(c.r, c.g * 0.4f, c.b * 0.4f, c.a));
                    r.SetPropertyBlock(Mpb, i);
                }
            }
        }
    }
}
