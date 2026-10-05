using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;
using Dir = OuterCraft.Assets.Dir;

namespace OuterCraft.World
{
    /// Minecraft's block-breaking particles (TerrainParticle), on one planet.
    ///
    /// Breaking spawns a 4x4x4 grid of little squares cut from random 4x4-texel corners of the block's
    /// texture, darkened to 60%, flung outwards with Minecraft's velocity formula, falling, bouncing to
    /// a stop on the ground and vanishing after 4-40 ticks. They're real quads in the world (lit by the
    /// sun and lamps like everything else), kept in the planet's frame so the planet can't fly off
    /// without them, and they fall towards the planet's centre.
    public sealed class BlockParticles : MonoBehaviour
    {
        private struct P
        {
            public Vector3 Pos, Vel;     // planet-local, m and m/s
            public float Age, Life;      // seconds
            public float Half;           // half the quad's size
            public Vector2 Uv0, Uv1;
            public bool OnGround;
        }

        private readonly List<P> _ps = new List<P>();
        private Mesh _mesh;
        private MeshRenderer _mr;
        private static readonly System.Random Rng = new System.Random();
        private static float R() => (float)Rng.NextDouble();

        public float Gravity = 16f;  // Minecraft: 0.04 blocks/tick², overridden by the planet's own pull

        private void Awake()
        {
            _mesh = new Mesh { name = "mc_particles" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = PlanetBlocks.Material;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// ParticleEngine.destroy(): a 4x4x4 grid of particles inside the broken cell.
        public void Burst(CubeSphere grid, Cell cell, BlockDef b)
        {
            if (b == null) return;
            // The block model's "particle" texture: dirt for grass, the side texture otherwise.
            var tex = b.ParticleUv;
            var c000 = grid.Corner(cell, 0, 0, 0);
            var eu = grid.Corner(cell, 1, 0, 0) - c000;
            var ev = grid.Corner(cell, 0, 1, 0) - c000;
            var eh = grid.Corner(cell, 0, 0, 1) - c000;
            var center = grid.Center(cell);

            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    for (int k = 0; k < 4; k++)
                    {
                        float d0 = (i + 0.5f) / 4f, d1 = (k + 0.5f) / 4f, d2 = (j + 0.5f) / 4f; // x, up, z
                        var pos = c000 + eu * d0 + eh * d1 + ev * d2;
                        // Particle(level, x, y, z, xa, ya, za): random spread, normalised, scaled, a little up.
                        var dir = (eu.normalized * (d0 - 0.5f) + eh.normalized * (d1 - 0.5f) + ev.normalized * (d2 - 0.5f));
                        var spread = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1) * 0.4f;
                        var v = dir + spread;
                        float f = (R() + R() + 1f) * 0.15f;
                        float len = Mathf.Max(1e-4f, v.magnitude);
                        var up = center.normalized;
                        v = v / len * f * 0.4f + up * 0.1f;           // blocks per tick
                        // TerrainParticle: a random quarter of the texture, 0.1 - 0.2 blocks wide.
                        float uo = R() * 3f, vo = R() * 3f;
                        float u0 = tex.x + (uo / 4f) * tex.width, u1 = tex.x + ((uo + 1f) / 4f) * tex.width;
                        float v0 = tex.y + (vo / 4f) * tex.height, v1 = tex.y + ((vo + 1f) / 4f) * tex.height;
                        _ps.Add(new P
                        {
                            Pos = pos,
                            Vel = v * 20f,                              // -> m/s
                            Life = 4f / (R() * 0.9f + 0.1f) / 20f,      // 4..40 ticks
                            Half = 0.1f * (R() * 0.5f + 0.5f) * 2f / 2f,
                            Uv0 = new Vector2(u0, v0),
                            Uv1 = new Vector2(u1, v1),
                        });
                    }
        }

        /// ParticleEngine.crack(): one particle on the face being mined, every tick.
        public void Crack(CubeSphere grid, Cell cell, BlockDef b, Dir face, Bounds box)
        {
            if (b == null) return;
            var tex = b.ParticleUv;
            var mc = new Vector3(Mathf.Lerp(box.min.x + 0.1f, box.max.x - 0.1f, R()), Mathf.Lerp(box.min.y + 0.1f, box.max.y - 0.1f, R()),
                Mathf.Lerp(box.min.z + 0.1f, box.max.z - 0.1f, R()));
            var n = Dirs.Vec[(int)face];
            for (int a = 0; a < 3; a++)
                if (n[a] != 0) mc[a] = n[a] > 0 ? box.max[a] + 0.1f : box.min[a] - 0.1f;
            var frame = new CubeSphere.CellFrame();
            frame.Set(grid, cell);
            var pos = frame.P(mc);
            var spread = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1) * 0.4f;
            var v = (spread.normalized * (R() + R() + 1f) * 0.15f * 0.4f + frame.EH * 0.1f) * 0.2f;
            float uo = R() * 3f, vo = R() * 3f;
            _ps.Add(new P
            {
                Pos = pos,
                Vel = v * 20f,
                Life = 4f / (R() * 0.9f + 0.1f) / 20f,
                Half = 0.1f * (R() * 0.5f + 0.5f) * 0.6f,
                Uv0 = new Vector2(tex.x + uo / 4f * tex.width, tex.y + vo / 4f * tex.height),
                Uv1 = new Vector2(tex.x + (uo + 1f) / 4f * tex.width, tex.y + (vo + 1f) / 4f * tex.height),
            });
        }

        private void LateUpdate()
        {
            if (_ps.Count == 0)
            {
                if (_mesh.vertexCount > 0) _mesh.Clear();
                return;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float ticks = dt * 20f;
            float drag = Mathf.Pow(0.98f, ticks);
            float groundDrag = Mathf.Pow(0.7f, ticks);

            // Planet pull where the player stands; Minecraft's 0.04 b/t² if we can't tell.
            float g = Gravity;
            var det = Locator.GetPlayerForceDetector();
            if (det != null)
            {
                float a = det.GetForceAcceleration().magnitude;
                if (a > 0.5f) g = a;
            }

            var t = transform;
            for (int i = _ps.Count - 1; i >= 0; i--)
            {
                var p = _ps[i];
                p.Age += dt;
                if (p.Age >= p.Life) { _ps.RemoveAt(i); continue; }
                var down = -p.Pos.normalized;
                p.Vel += down * g * dt;
                var from = t.TransformPoint(p.Pos);
                var step = p.Vel * dt;
                var to = t.TransformPoint(p.Pos + step);
                // Particles collide with the world (blocks and Outer Wilds ground alike).
                if (Physics.Linecast(from, to, out var hit, OWLayerMask.physicalMask, QueryTriggerInteraction.Ignore))
                {
                    p.Pos = t.InverseTransformPoint(hit.point + hit.normal * 0.01f);
                    var n = t.InverseTransformDirection(hit.normal);
                    p.Vel -= n * Vector3.Dot(p.Vel, n); // stop along the surface normal
                    p.OnGround = Vector3.Dot(n, -down) > 0.5f;
                }
                else
                {
                    p.Pos += step;
                    p.OnGround = false;
                }
                p.Vel *= drag;
                if (p.OnGround) p.Vel = Vector3.Project(p.Vel, down) + Vector3.ProjectOnPlane(p.Vel, down) * groundDrag;
                _ps[i] = p;
            }
            Build();
        }

        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Vector3> _uv2 = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _tri = new List<int>();

        /// Camera-facing squares, in the planet's frame.
        private void Build()
        {
            var cam = Locator.GetPlayerCamera();
            if (cam == null) return;
            var right = transform.InverseTransformDirection(cam.transform.right);
            var up = transform.InverseTransformDirection(cam.transform.up);
            var toCam = transform.InverseTransformDirection(-cam.transform.forward);
            _v.Clear(); _n.Clear(); _uv.Clear(); _uv2.Clear(); _c.Clear(); _tri.Clear();
            var tint = new Color32(153, 153, 153, 255); // TerrainParticle: rCol = gCol = bCol = 0.6
            foreach (var p in _ps)
            {
                int i = _v.Count;
                var r = right * p.Half;
                var u = up * p.Half;
                _v.Add(p.Pos - r - u); _v.Add(p.Pos - r + u); _v.Add(p.Pos + r + u); _v.Add(p.Pos + r - u);
                _uv.Add(new Vector2(p.Uv1.x, p.Uv0.y)); _uv.Add(new Vector2(p.Uv1.x, p.Uv1.y));
                _uv.Add(new Vector2(p.Uv0.x, p.Uv1.y)); _uv.Add(new Vector2(p.Uv0.x, p.Uv0.y));
                for (int k = 0; k < 4; k++) { _n.Add(toCam); _c.Add(tint); _uv2.Add(new Vector3(0f, 1f, 0f)); }
                _tri.Add(i); _tri.Add(i + 1); _tri.Add(i + 2);
                _tri.Add(i); _tri.Add(i + 2); _tri.Add(i + 3);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv);
            _mesh.SetUVs(1, _uv2);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_tri, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f); // never culled away
        }
    }
}
