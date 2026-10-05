using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using Dir = OuterCraft.Assets.Dir;
using UnityEngine;
using UnityEngine.Rendering;

namespace OuterCraft.World
{
    /// All Minecraft blocks on one planet: storage, chunk meshes (with Minecraft-style ambient
    /// occlusion) and colliders. Lives under the planet's body, so orbit and spin come for free and
    /// Outer Wilds' own character controller walks on the blocks like on any other ground.
    public sealed class PlanetBlocks : MonoBehaviour
    {
        public const int ChunkBits = 4; // 16 x 16 x 16 cells

        public string Key;
        public OWRigidbody Body;
        public CubeSphere Grid;
        public int Layer;

        private readonly Dictionary<Cell, ushort> _blocks = new Dictionary<Cell, ushort>(); // id | state << 8
        private readonly Dictionary<ChunkKey, HashSet<Cell>> _byChunk = new Dictionary<ChunkKey, HashSet<Cell>>();
        private readonly Dictionary<ChunkKey, Chunk> _chunks = new Dictionary<ChunkKey, Chunk>();
        private readonly HashSet<ChunkKey> _dirty = new HashSet<ChunkKey>();

        public bool Changed;
        public int Count => _blocks.Count;
        public IEnumerable<KeyValuePair<Cell, ushort>> AllBlocks => _blocks;
        public int MaxH;

        private static Material _material;

        private struct ChunkKey : IEquatable<ChunkKey>
        {
            public int Face, X, Y, Z;
            public static ChunkKey Of(Cell c) => new ChunkKey { Face = c.Face, X = c.U >> ChunkBits, Y = c.V >> ChunkBits, Z = c.H >> ChunkBits };
            public bool Equals(ChunkKey o) => Face == o.Face && X == o.X && Y == o.Y && Z == o.Z;
            public override bool Equals(object obj) => obj is ChunkKey k && Equals(k);
            public override int GetHashCode() { unchecked { return ((Face * 397 ^ X) * 397 ^ Y) * 397 ^ Z; } }
        }

        private sealed class Chunk
        {
            public GameObject Go;
            public Mesh Mesh, ColMesh;
            public MeshCollider Collider;
        }

        // ---------------------------------------------------------------- access

        public byte Get(Cell c) => _blocks.TryGetValue(c, out var v) ? (byte)(v & 0xFF) : (byte)0;
        public int State(Cell c) => _blocks.TryGetValue(c, out var v) ? v >> 8 : 0;
        public BlockDef Def(Cell c) => Blocks.Get(Get(c));

        public void Set(Cell c, byte id, int state = 0)
        {
            var key = ChunkKey.Of(c);
            if (id == 0)
            {
                if (!_blocks.Remove(c)) return;
                if (_byChunk.TryGetValue(key, out var set)) set.Remove(c);
            }
            else
            {
                _blocks[c] = (ushort)(id | (state << 8));
                if (!_byChunk.TryGetValue(key, out var set)) _byChunk[key] = set = new HashSet<Cell>();
                set.Add(c);
                if (c.H > MaxH) MaxH = c.H;
            }
            Changed = true;
            // This chunk, plus any neighbour whose faces or AO touch the cell.
            for (int du = -1; du <= 1; du++)
                for (int dv = -1; dv <= 1; dv++)
                    for (int dh = -1; dh <= 1; dh++)
                        _dirty.Add(ChunkKey.Of(Grid.Step(c, du, dv, dh)));
        }

        public void LoadRaw(Cell c, byte id, int state)
        {
            if (Blocks.Get(id) == null) return;
            _blocks[c] = (ushort)(id | (state << 8));
            if (c.H > MaxH) MaxH = c.H;
            var key = ChunkKey.Of(c);
            if (!_byChunk.TryGetValue(key, out var set)) _byChunk[key] = set = new HashSet<Cell>();
            set.Add(c);
            _dirty.Add(key);
        }

        private bool Solid(Cell c) => _blocks.ContainsKey(c);

        private bool HidesFace(Cell neighbour, BlockDef self)
        {
            if (!_blocks.TryGetValue(neighbour, out var v)) return false;
            var n = Blocks.Get((byte)(v & 0xFF));
            if (n == null) return false;
            if (n.Opaque) return true;
            return n.Id == self.Id && self.Key == "glass"; // glass next to glass: no inner faces (leaves keep theirs)
        }

        // ---------------------------------------------------------------- meshing

        private void LateUpdate()
        {
            if (_dirty.Count == 0) return;
            int budget = 8;
            var done = new List<ChunkKey>();
            foreach (var k in _dirty)
            {
                Rebuild(k);
                done.Add(k);
                if (--budget == 0) break;
            }
            foreach (var k in done) _dirty.Remove(k);
        }

        public void MarkAllDirty()
        {
            foreach (var k in _byChunk.Keys) _dirty.Add(k);
        }

        public void RebuildAllNow()
        {
            foreach (var k in new List<ChunkKey>(_dirty)) Rebuild(k);
            _dirty.Clear();
        }

        private static readonly List<Vector3> Pos = new List<Vector3>();
        private static readonly List<Vector3> Nrm = new List<Vector3>();
        private static readonly List<Vector2> Uv = new List<Vector2>();
        private static readonly List<Vector3> Uv2 = new List<Vector3>();
        private static readonly List<Color32> Col = new List<Color32>();
        private static readonly List<int> Idx = new List<int>();
        private static readonly List<int> GIdx = new List<int>(); // glowing blocks: the emissive material

        // The 6 faces: outward step (du, dv, dh), then the 4 corners as (du, dv, dh) in {0,1}.
        private static readonly int[][] FaceStep =
        {
            new[] { 0, 0, -1 }, new[] { 0, 0, 1 }, new[] { -1, 0, 0 }, new[] { 1, 0, 0 }, new[] { 0, -1, 0 }, new[] { 0, 1, 0 },
        };
        private static readonly int[][][] FaceCorners =
        {
            new[] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 0, 1, 0 } }, // bottom
            new[] { new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } }, // top
            new[] { new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 1, 1 }, new[] { 0, 0, 1 } }, // -u
            new[] { new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 }, new[] { 1, 0, 1 } }, // +u
            new[] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 0, 1 }, new[] { 0, 0, 1 } }, // -v
            new[] { new[] { 0, 1, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } }, // +v
        };
        // Minecraft's face shading by direction: bottom, top, -u/+u (west/east), -v/+v (north/south)
        private static readonly float[] FaceShade = { 0.5f, 1f, 0.6f, 0.6f, 0.8f, 0.8f };
        private static readonly float[] AoLevel = { 0.45f, 0.62f, 0.8f, 1f };

        private void Rebuild(ChunkKey key)
        {
            _byChunk.TryGetValue(key, out var cells);
            if (cells == null || cells.Count == 0)
            {
                if (_chunks.TryGetValue(key, out var gone))
                {
                    Destroy(gone.Go);
                    Destroy(gone.Mesh);
                    Destroy(gone.ColMesh);
                    _chunks.Remove(key);
                }
                return;
            }

            Pos.Clear(); Nrm.Clear(); Uv.Clear(); Uv2.Clear(); Col.Clear(); Idx.Clear(); GIdx.Clear();
            CPos.Clear(); CIdx.Clear();
            foreach (var c in cells)
            {
                var raw = _blocks[c];
                var def = Blocks.Get((byte)(raw & 0xFF));
                if (def == null) continue;
                if (def.IsModel) { AddModel(c, def, raw >> 8); continue; }
                var center = Grid.Center(c);
                for (int f = 0; f < 6; f++)
                {
                    var st = FaceStep[f];
                    if (HidesFace(Grid.Step(c, st[0], st[1], st[2]), def)) continue;
                    AddFace(c, def, f, center);
                }
            }

            if (!_chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk();
                chunk.Go = new GameObject($"mc_chunk_{key.Face}_{key.X}_{key.Y}_{key.Z}") { layer = Layer };
                chunk.Go.transform.SetParent(transform, false);
                chunk.Mesh = new Mesh { name = chunk.Go.name };
                chunk.Go.AddComponent<MeshFilter>().sharedMesh = chunk.Mesh;
                var mr = chunk.Go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { Material, GlowMaterial };
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                chunk.Collider = chunk.Go.AddComponent<MeshCollider>();
                chunk.ColMesh = new Mesh { name = chunk.Go.name + "_col" };
                _chunks[key] = chunk;
            }
            var m = chunk.Mesh;
            m.Clear();
            m.indexFormat = Pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(Pos);
            m.SetNormals(Nrm);
            m.SetUVs(0, Uv);
            m.SetUVs(1, Uv2);
            m.SetColors(Col);
            m.subMeshCount = 2;
            m.SetTriangles(Idx, 0, true);
            m.SetTriangles(GIdx, 1, true);
            var cm = chunk.ColMesh;
            cm.Clear();
            cm.indexFormat = CPos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            cm.SetVertices(CPos);
            cm.SetTriangles(CIdx, 0, true);
            chunk.Collider.sharedMesh = null; // force PhysX to re-cook
            chunk.Collider.sharedMesh = CPos.Count > 0 ? cm : null;
            chunk.Collider.enabled = CPos.Count > 0;
        }

        private void AddFace(Cell c, BlockDef def, int f, Vector3 center)
        {
            var corners = FaceCorners[f];
            var st = FaceStep[f];
            var uvRect = f == 0 ? def.BottomUv : f == 1 ? def.TopUv : def.SideUv;
            int baseIdx = Pos.Count;
            var p = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                var k = corners[i];
                p[i] = Grid.Corner(c, k[0], k[1], k[2]);
            }
            var faceCenter = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
            var outward = (faceCenter - center).normalized;
            float glow = def.Light / 15f;

            for (int i = 0; i < 4; i++)
            {
                var k = corners[i];
                Pos.Add(p[i]);
                Nrm.Add(outward);
                // Texture: top/bottom across the face; sides with "up" along the layer axis.
                float s, t;
                if (f <= 1) { s = k[0]; t = k[1]; }
                else if (f <= 3) { s = k[1]; t = k[2]; }
                else { s = k[0]; t = k[2]; }
                Uv.Add(new Vector2(uvRect.x + s * uvRect.width, uvRect.y + t * uvRect.height));
                Uv2.Add(new Vector3(glow, 1f, 0f));
                float ao = (glow > 0 ? 1f : AoLevel[Ao(c, st, k)]) * FaceShade[f] * Shade.Brightness;
                byte b = (byte)(ao * 255f);
                Col.Add(new Color32(b, b, b, 255));
            }

            // Two triangles, wound so the face points outward (Unity: normal = cross(b - a, c - a)).
            bool flip = Vector3.Dot(Vector3.Cross(p[1] - p[0], p[2] - p[0]), outward) < 0;
            Wind(def.Light > 0 ? GIdx : Idx, baseIdx, flip);
            AddCollision(p[0], p[1], p[2], p[3], flip);
        }

        private static void Wind(List<int> idx, int i, bool flip)
        {
            if (!flip)
            {
                idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
                idx.Add(i); idx.Add(i + 2); idx.Add(i + 3);
            }
            else
            {
                idx.Add(i); idx.Add(i + 2); idx.Add(i + 1);
                idx.Add(i); idx.Add(i + 3); idx.Add(i + 2);
            }
        }

        private static readonly List<Vector3> CPos = new List<Vector3>();
        private static readonly List<int> CIdx = new List<int>();

        private static void AddCollision(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
        {
            int i = CPos.Count;
            CPos.Add(a); CPos.Add(b); CPos.Add(c); CPos.Add(d);
            Wind(CIdx, i, flip);
        }

        // ---------------------------------------------------------------- JSON-model blocks

        private readonly CubeSphere.CellFrame _frame = new CubeSphere.CellFrame();

        /// The connection properties a fence or pane sees around it.
        public Func<string, string> Connections(Cell c, BlockDef def)
        {
            if (def.Connects == null) return null;
            bool Conn(Dir d)
            {
                var n = Def(Grid.Step(c, d));
                return n != null && (n.Opaque || n.Connects == def.Connects);
            }
            bool north = Conn(Dir.North), south = Conn(Dir.South), east = Conn(Dir.East), west = Conn(Dir.West);
            return k => k == "north" ? (north ? "true" : "false") : k == "south" ? (south ? "true" : "false") :
                k == "east" ? (east ? "true" : "false") : k == "west" ? (west ? "true" : "false") : "false";
        }

        public BakedModel ModelAt(Cell c)
        {
            var def = Def(c);
            if (def == null) return null;
            if (!def.IsModel) return null;
            return def.ModelFor(State(c), Connections(c, def));
        }

        private void AddModel(Cell c, BlockDef def, int state)
        {
            var model = def.ModelFor(state, Connections(c, def));
            if (model == null) return;
            _frame.Set(Grid, c);
            float glow = def.Light / 15f;
            var tint = def.TintColor;
            foreach (var q in model.Quads)
            {
                if (q.Cull >= 0)
                {
                    var n = Def(Grid.Step(c, (Dir)q.Cull));
                    if (n != null && n.Opaque) continue;
                }
                var p0 = _frame.P(q.P0); var p1 = _frame.P(q.P1); var p2 = _frame.P(q.P2); var p3 = _frame.P(q.P3);
                var outward = _frame.D(q.N).normalized;
                int i = Pos.Count;
                Pos.Add(p0); Pos.Add(p1); Pos.Add(p2); Pos.Add(p3);
                Uv.Add(q.T0); Uv.Add(q.T1); Uv.Add(q.T2); Uv.Add(q.T3);
                var col = Shade.Apply(q.Tint ? tint : new Color32(255, 255, 255, 255), Shade.Of(q.N));
                for (int k = 0; k < 4; k++)
                {
                    Nrm.Add(outward);
                    Uv2.Add(new Vector3(glow, 1f, 0f));
                    Col.Add(col);
                }
                var cr = Vector3.Cross(p1 - p0, p2 - p0);
                if (cr.sqrMagnitude < 1e-12f) cr = Vector3.Cross(p2 - p0, p3 - p0);
                bool flip = Vector3.Dot(cr, outward) < 0;
                Wind(def.Light > 0 ? GIdx : Idx, i, flip);
                if (def.Collide) AddCollision(p0, p1, p2, p3, flip);
            }
        }

        /// Minecraft's smooth-lighting corner occlusion: look at the two edge neighbours and the
        /// diagonal one in the layer the face looks into. 3 = open, 0 = fully tucked in.
        private int Ao(Cell c, int[] outStep, int[] corner)
        {
            // The two in-plane axes of this face and which way this corner leans along them.
            int a1 = -1, a2 = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                if (outStep[axis] != 0) continue;
                if (a1 < 0) a1 = axis; else a2 = axis;
            }
            int s1 = corner[a1] == 1 ? 1 : -1;
            int s2 = corner[a2] == 1 ? 1 : -1;
            int[] o1 = { outStep[0], outStep[1], outStep[2] };
            int[] o2 = { outStep[0], outStep[1], outStep[2] };
            int[] oc = { outStep[0], outStep[1], outStep[2] };
            o1[a1] += s1;
            o2[a2] += s2;
            oc[a1] += s1;
            oc[a2] += s2;
            bool side1 = OccludesAo(Grid.Step(c, o1[0], o1[1], o1[2]));
            bool side2 = OccludesAo(Grid.Step(c, o2[0], o2[1], o2[2]));
            if (side1 && side2) return 0;
            bool cornerBlock = OccludesAo(Grid.Step(c, oc[0], oc[1], oc[2]));
            return 3 - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (cornerBlock ? 1 : 0));
        }

        private bool OccludesAo(Cell c)
        {
            if (!_blocks.TryGetValue(c, out var v)) return false;
            var d = Blocks.Get((byte)(v & 0xFF));
            return d != null && d.Opaque;
        }

        private static Material _glow;
        public static Material GlowMaterial => _glow != null ? _glow : (_glow = BlockMaterials.ForAtlas(true));

        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    _material = BlockMaterials.ForAtlas();
                }
                return _material;
            }
        }

        // ---------------------------------------------------------------- lights

        public IEnumerable<(Vector3 local, int level)> Emitters()
        {
            foreach (var kv in _blocks)
            {
                var d = Blocks.Get((byte)(kv.Value & 0xFF));
                if (d != null && d.Light > 0) yield return (Grid.Center(kv.Key), d.Light);
            }
        }
    }
}
