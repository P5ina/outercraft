using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.World
{
    /// Unlit, glowing particles (fireworks, sparks, death poofs) and firework rockets. Particles are
    /// kept in the frame they were born in (a planet, the player's ground) so they don't get left
    /// behind by Outer Wilds' moving world, and drawn as camera-facing quads around the camera.
    public sealed class Fx : MonoBehaviour
    {
        public static Fx Instance;

        public enum Sprite { Spark, Generic }

        private struct P
        {
            public Transform Frame;
            public Vector3 Pos, Vel;     // frame-local, blocks (= m) and m/s
            public float Age, Life, Size, Gravity, Drag;
            public Color Col, Fade;
            public Sprite Kind;
            public bool Twinkle, ReverseAnim;
        }

        private sealed class Rocket
        {
            public Transform Frame;
            public Vector3 Pos, Vel;
            public float Age, Life;
            public Color[] Colors;
            public bool Twinkle, Large;
        }

        private readonly List<P> _ps = new List<P>();
        private readonly List<Rocket> _rockets = new List<Rocket>();
        private Mesh _mesh;
        private GameObject _go;
        private static readonly System.Random Rng = new System.Random();
        private static float R() => (float)Rng.NextDouble();
        private static float Gauss() { float u1 = 1f - R(), u2 = R(); return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2); }

        // DyeColor.getFireworkColor
        public static readonly Color[] FireworkColors =
        {
            Hex(0xF0F0F0), Hex(0xEB8844), Hex(0xC354CD), Hex(0x6689D3), Hex(0xDECF2A), Hex(0x41CD34), Hex(0xD88198), Hex(0x434343),
            Hex(0xABABAB), Hex(0x287697), Hex(0x7B2FBE), Hex(0x253192), Hex(0x51301A), Hex(0x3B511A), Hex(0xB3312C), Hex(0x1E1B1B),
        };
        private static Color Hex(int c) => new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);

        private void Awake() => Instance = this;

        public void Clear()
        {
            _ps.Clear();
            _rockets.Clear();
        }

        public void Add(Transform frame, Vector3 worldPos, Vector3 worldVel, float lifeTicks, float size, Color col, Sprite kind,
            float gravity = 0f, float drag = 0.98f, bool twinkle = false, Color? fade = null)
        {
            if (frame == null) return;
            _ps.Add(new P
            {
                Frame = frame,
                Pos = frame.InverseTransformPoint(worldPos),
                Vel = frame.InverseTransformVector(worldVel),
                Life = lifeTicks / 20f,
                Size = size,
                Col = col,
                Fade = fade ?? col,
                Kind = kind,
                Gravity = gravity,
                Drag = drag,
                Twinkle = twinkle,
            });
        }

        /// LivingEntity death: 20 "poof" particles in the body's box (ExplodeParticle: drifting up, greyish white).
        public void Poof(Transform frame, Bounds box, Vector3 up)
        {
            for (int i = 0; i < 20; i++)
            {
                var p = new Vector3(Mathf.Lerp(box.min.x, box.max.x, R()), Mathf.Lerp(box.min.y, box.max.y, R()), Mathf.Lerp(box.min.z, box.max.z, R()));
                var v = new Vector3(Gauss(), Gauss(), Gauss()) * 0.02f * 20f;
                float g = R() * 0.3f + 0.7f;
                float life = 16f / (R() * 0.8f + 0.2f) + 2f;
                Add(frame, p, v + up * 0.1f, life, 0.1f * (R() * R() * 6f + 1f) * 1.5f, new Color(g, g, g), Sprite.Generic, gravity: -0.004f * 400f / 20f, drag: 0.9f);
            }
        }

        /// Crit hit: 16 white-ish stars flying off the target.
        public void Crit(Transform frame, Vector3 at, Vector3 from)
        {
            for (int i = 0; i < 16; i++)
            {
                var v = (new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1).normalized * 0.4f + (at - from).normalized * 0.2f) * 20f;
                Add(frame, at, v, 6 + Rng.Next(4), 0.1f, new Color(1f, 1f, 0.8f), Sprite.Spark, drag: 0.7f);
            }
        }

        // ---------------------------------------------------------------- fireworks (FireworkRocketEntity)

        /// A rocket launched from the ground: flight duration 1, with a random coloured star.
        public void Launch(Transform frame, Vector3 worldPos, Vector3 up)
        {
            if (frame == null) return;
            int n = 1 + Rng.Next(2);
            var colors = new Color[n];
            for (int i = 0; i < n; i++) colors[i] = FireworkColors[Rng.Next(FireworkColors.Length)];
            _rockets.Add(new Rocket
            {
                Frame = frame,
                Pos = frame.InverseTransformPoint(worldPos + up * 0.2f),
                Vel = frame.InverseTransformVector(up * 0.05f * 20f + new Vector3(Gauss(), 0, Gauss()) * 0.001f * 20f),
                Life = (10f * 2f + Rng.Next(6) + Rng.Next(7)) / 20f,
                Colors = colors,
                Twinkle = Rng.Next(2) == 0,
                Large = Rng.Next(3) == 0,
            });
            McSounds.Play("fireworks.launch", worldPos, frame, 3f, 1f, 48f);
        }

        /// FireworkParticles.Starter.createParticleBall: a sphere of coloured sparks.
        private void Explode(Rocket r)
        {
            var world = r.Frame.TransformPoint(r.Pos);
            float speed = r.Large ? 0.5f : 0.25f;
            int size = r.Large ? 4 : 2;
            for (int i = -size; i <= size; i++)
                for (int j = -size; j <= size; j++)
                    for (int k = -size; k <= size; k++)
                    {
                        float x = j + (R() - R()) * 0.5f, y = i + (R() - R()) * 0.5f, z = k + (R() - R()) * 0.5f;
                        float len = Mathf.Sqrt(x * x + y * y + z * z) / speed + Gauss() * 0.05f;
                        if (len < 1e-3f) continue;
                        var v = new Vector3(x / len, y / len, z / len);
                        var col = r.Colors[Rng.Next(r.Colors.Length)];
                        var fade = Color.Lerp(col, Color.white, 0.4f);
                        _ps.Add(new P
                        {
                            Frame = r.Frame,
                            Pos = r.Pos,
                            Vel = v * 20f,
                            Life = (48 + Rng.Next(12)) / 20f,
                            Size = 0.1f * 0.75f * 1.6f,
                            Col = col,
                            Fade = fade,
                            Kind = Sprite.Spark,
                            Gravity = 0.004f * 400f,
                            Drag = 0.91f,
                            Twinkle = r.Twinkle,
                        });
                        if (j != -size && j != size && i != -size && i != size) k += size * 2 - 1; // only the shell
                    }
            float dist = Camera.main != null ? (Camera.main.transform.position - world).magnitude : 0f;
            McSounds.Play(dist > 16f ? "fireworks.blast_far" : r.Large ? "fireworks.largeblast" : "fireworks.blast", world, r.Frame, 3f, 0.95f + R() * 0.1f, 96f);
            if (r.Twinkle) StartCoroutine(TwinkleLater(world, r.Frame));
        }

        private System.Collections.IEnumerator TwinkleLater(Vector3 at, Transform frame)
        {
            yield return new WaitForSeconds(0.75f);
            if (frame != null) McSounds.Play("fireworks.twinkle", frame.position + (at - frame.position), frame, 3f, 0.9f + R() * 0.15f, 96f);
        }

        // ---------------------------------------------------------------- frame

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float ticks = dt * 20f;

            for (int i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];
                if (r.Frame == null) { _rockets.RemoveAt(i); continue; }
                r.Age += dt;
                var up = r.Pos.normalized;
                // tick: horizontal * 1.15, up + 0.04 blocks/tick²
                var vUp = Vector3.Project(r.Vel, up);
                var vH = r.Vel - vUp;
                vH *= Mathf.Pow(1.15f, ticks);
                vUp += up * 0.04f * 400f * dt;
                r.Vel = vH + vUp;
                r.Pos += r.Vel * dt;
                // trail: a spark every other tick
                if (Rng.Next(2) == 0)
                    _ps.Add(new P
                    {
                        Frame = r.Frame, Pos = r.Pos - r.Vel.normalized * 0.2f,
                        Vel = new Vector3(Gauss(), Gauss(), Gauss()) * 0.05f * 20f - r.Vel * 0.5f * 0.05f,
                        Life = (4 + Rng.Next(4)) / 20f, Size = 0.08f, Col = Color.white, Fade = Color.white,
                        Kind = Sprite.Spark, Drag = 0.91f,
                    });
                if (r.Age >= r.Life)
                {
                    Explode(r);
                    _rockets.RemoveAt(i);
                }
            }

            for (int i = _ps.Count - 1; i >= 0; i--)
            {
                var p = _ps[i];
                if (p.Frame == null) { _ps.RemoveAt(i); continue; }
                p.Age += dt;
                if (p.Age >= p.Life) { _ps.RemoveAt(i); continue; }
                var down = -p.Pos.normalized;
                p.Vel += down * p.Gravity * dt;
                p.Vel *= Mathf.Pow(p.Drag, ticks);
                p.Pos += p.Vel * dt;
                _ps[i] = p;
            }
            Build();
        }

        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int> _t = new List<int>();
        private readonly Rect[] _spark = new Rect[8], _generic = new Rect[8];
        private bool _uvReady;

        private void Build()
        {
            var cam = Locator.GetPlayerCamera()?.mainCamera ?? Camera.main;
            if (cam == null) return;
            if (_go == null)
            {
                _go = new GameObject("OuterCraft_Fx");
                _mesh = new Mesh { name = "fx" };
                _mesh.MarkDynamic();
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = _go.AddComponent<MeshRenderer>();
                var sh = Shader.Find("Sprites/Default");
                mr.sharedMaterial = new Material(sh) { mainTexture = McAssets.Atlas, renderQueue = 3100 };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            if (_go.transform.parent != cam.transform)
            {
                _go.transform.SetParent(cam.transform, false);
                _go.transform.localPosition = Vector3.zero;
                _go.transform.localRotation = Quaternion.identity;
            }
            if (!_uvReady)
            {
                for (int i = 0; i < 8; i++) { _spark[i] = McAssets.Uv($"particle/spark_{i}"); _generic[i] = McAssets.Uv($"particle/generic_{i}"); }
                _uvReady = true;
            }
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            var ct = cam.transform;
            foreach (var p in _ps)
            {
                float k = p.Age / p.Life;
                // SimpleAnimatedParticle: sprite by age (sparks shrink 7 -> 0), generic smoke grows 7 -> 0 as well
                int frame = Mathf.Clamp(7 - (int)(k * 8f), 0, 7);
                var r = p.Kind == Sprite.Spark ? _spark[frame] : _generic[frame];
                var col = Color.Lerp(p.Col, p.Fade, k);
                if (k > 0.5f) col.a = 1f - (k - 0.5f) * 2f;
                if (p.Twinkle && k > 0.33f && Rng.Next(3) != 0) continue; // flicker
                var w = p.Frame.TransformPoint(p.Pos);
                var local = ct.InverseTransformPoint(w);
                if (local.z < 0.05f) continue;
                float s = p.Size;
                int i0 = _v.Count;
                _v.Add(local + new Vector3(-s, -s, 0)); _v.Add(local + new Vector3(-s, s, 0));
                _v.Add(local + new Vector3(s, s, 0)); _v.Add(local + new Vector3(s, -s, 0));
                _uv.Add(new Vector2(r.xMin, r.yMin)); _uv.Add(new Vector2(r.xMin, r.yMax));
                _uv.Add(new Vector2(r.xMax, r.yMax)); _uv.Add(new Vector2(r.xMax, r.yMin));
                for (int q = 0; q < 4; q++) _c.Add(col);
                _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2); _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 3);
            }
            // the rockets themselves: a bright spark at the head
            foreach (var r in _rockets)
            {
                if (r.Frame == null) continue;
                var local = ct.InverseTransformPoint(r.Frame.TransformPoint(r.Pos));
                if (local.z < 0.05f) continue;
                var rr = _spark[0];
                float s = 0.15f;
                int i0 = _v.Count;
                _v.Add(local + new Vector3(-s, -s, 0)); _v.Add(local + new Vector3(-s, s, 0));
                _v.Add(local + new Vector3(s, s, 0)); _v.Add(local + new Vector3(s, -s, 0));
                _uv.Add(new Vector2(rr.xMin, rr.yMin)); _uv.Add(new Vector2(rr.xMin, rr.yMax));
                _uv.Add(new Vector2(rr.xMax, rr.yMax)); _uv.Add(new Vector2(rr.xMax, rr.yMin));
                for (int q = 0; q < 4; q++) _c.Add(new Color(1f, 0.9f, 0.6f));
                _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2); _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 3);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
        }
    }
}
