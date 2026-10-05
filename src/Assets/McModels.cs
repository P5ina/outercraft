using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OuterCraft.Assets
{
    /// Minecraft's six directions, in Minecraft's block space (x east, y up, z south).
    public enum Dir { Down = 0, Up = 1, North = 2, South = 3, West = 4, East = 5 }

    /// Minecraft's directional face shading (ModelBlockRenderer / LevelRenderer: up 1.0,
    /// north-south 0.8, east-west 0.6, down 0.5) times an overall brightness that tones the jar's
    /// bright textures down to Outer Wilds' palette.
    public static class Shade
    {
        public static float Brightness = 0.85f;

        public static float Of(Vector3 mcNormal)
        {
            float ax = Mathf.Abs(mcNormal.x), ay = Mathf.Abs(mcNormal.y), az = Mathf.Abs(mcNormal.z);
            float s = ay >= ax && ay >= az ? (mcNormal.y > 0 ? 1f : 0.5f) : az >= ax ? 0.8f : 0.6f;
            return s * Brightness;
        }

        public static Color32 Apply(Color32 c, float k) =>
            new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);
    }

    public static class Dirs
    {
        public static readonly Vector3[] Vec =
        {
            new Vector3(0, -1, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1),
            new Vector3(0, 0, 1), new Vector3(-1, 0, 0), new Vector3(1, 0, 0),
        };
        public static readonly string[] Name = { "down", "up", "north", "south", "west", "east" };

        public static Dir Opposite(Dir d) => (Dir)((int)d ^ 1);

        public static Dir Nearest(Vector3 v)
        {
            int best = 0;
            float bd = -2f;
            for (int i = 0; i < 6; i++)
            {
                float d = Vector3.Dot(v, Vec[i]);
                if (d > bd) { bd = d; best = i; }
            }
            return (Dir)best;
        }

        public static bool TryParse(string s, out Dir d)
        {
            int i = Array.IndexOf(Name, s);
            d = (Dir)Math.Max(0, i);
            return i >= 0;
        }

        public static bool Horizontal(Dir d) => d >= Dir.North;
    }

    /// One baked face: Minecraft block-space corners (0..1), atlas uvs, the face's own 0..1 uvs (for
    /// the crack overlay), its direction and the side it's culled against.
    public struct BQuad
    {
        public Vector3 P0, P1, P2, P3;
        public Vector2 T0, T1, T2, T3;
        public Vector2 F0, F1, F2, F3;
        public Vector3 N;
        public int Cull;     // -1 or a Dir
        public bool Tint;
    }

    /// A display transform from a model's "display" block (translation in 1/16, rotation XYZ in degrees).
    public sealed class ModelDisplay
    {
        public Vector3 T, R, S = Vector3.one;
        public static readonly ModelDisplay Identity = new ModelDisplay();
    }

    public sealed class BakedModel
    {
        public readonly List<BQuad> Quads = new List<BQuad>();
        public readonly List<Bounds> Boxes = new List<Bounds>();  // element boxes (selection, raycasts)
        public Dictionary<string, ModelDisplay> Display = new Dictionary<string, ModelDisplay>();
        public bool Generated;  // a flat item sprite
        public bool FullCube;   // one element filling the block

        public ModelDisplay Get(string ctx) => Display.TryGetValue(ctx, out var d) ? d : ModelDisplay.Identity;

        public void AddAll(BakedModel o)
        {
            Quads.AddRange(o.Quads);
            Boxes.AddRange(o.Boxes);
        }
    }

    /// Reads Minecraft's JSON block and item models out of the jar: parents, texture variables,
    /// elements with rotations, face uvs and rotations, display transforms; bakes them into quads
    /// with blockstate rotations applied.
    public sealed class McModels
    {
        public sealed class Model
        {
            public string Name;
            public Dictionary<string, string> Textures = new Dictionary<string, string>();
            public JArray Elements;
            public Dictionary<string, ModelDisplay> Display = new Dictionary<string, ModelDisplay>();
            public bool Generated;
        }

        private readonly McJar _jar;
        private readonly Dictionary<string, Model> _cache = new Dictionary<string, Model>();

        public McModels(McJar jar) { _jar = jar; }

        /// "minecraft:block/torch", "block/torch" -> "block/torch"
        public static string Norm(string name)
        {
            if (name == null) return null;
            int c = name.IndexOf(':');
            return c >= 0 ? name.Substring(c + 1) : name;
        }

        public JObject Json(string path)
        {
            var bytes = _jar.Read(path);
            if (bytes == null) return null;
            try { return JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes)); }
            catch (Exception e) { OuterCraft.Log($"bad json {path}: {e.Message}"); return null; }
        }

        public Model Load(string name)
        {
            name = Norm(name);
            if (name == null) return null;
            if (_cache.TryGetValue(name, out var m)) return m;
            m = new Model { Name = name };
            _cache[name] = m;
            var chain = new List<JObject>();
            string cur = name;
            for (int guard = 0; cur != null && guard < 16; guard++)
            {
                if (cur == "builtin/generated") { m.Generated = true; break; }
                var j = Json($"assets/minecraft/models/{cur}.json");
                if (j == null) break;
                chain.Add(j);
                cur = Norm((string)j["parent"]);
            }
            // root first, so children override
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var j = chain[i];
                if (j["textures"] is JObject tex)
                    foreach (var kv in tex) m.Textures[kv.Key] = kv.Value is JObject so ? (string)so["sprite"] : (string)kv.Value; // 26.x: {"sprite": .., "force_translucent": ..}
                if (j["elements"] is JArray els) m.Elements = els;
                if (j["display"] is JObject disp)
                    foreach (var kv in disp)
                        m.Display[kv.Key] = ParseDisplay(kv.Value as JObject);
            }
            return m;
        }

        private static ModelDisplay ParseDisplay(JObject o)
        {
            var d = new ModelDisplay();
            if (o == null) return d;
            d.T = V3(o["translation"], Vector3.zero);
            d.R = V3(o["rotation"], Vector3.zero);
            d.S = V3(o["scale"], Vector3.one);
            return d;
        }

        private static Vector3 V3(JToken t, Vector3 def)
        {
            if (!(t is JArray a) || a.Count < 3) return def;
            return new Vector3((float)a[0], (float)a[1], (float)a[2]);
        }

        /// "#side" -> "block/oak_planks"
        public static string ResolveTex(Model m, string tex)
        {
            for (int guard = 0; tex != null && tex.StartsWith("#") && guard < 10; guard++)
                tex = m.Textures.TryGetValue(tex.Substring(1), out var t) ? t : null;
            return Norm(tex);
        }

        /// Every texture a model's faces use (for the atlas).
        public IEnumerable<string> TexturesOf(Model m)
        {
            if (m.Generated)
            {
                var l0 = ResolveTex(m, "#layer0");
                if (l0 != null) yield return l0;
                yield break;
            }
            if (m.Elements == null) yield break;
            foreach (var el in m.Elements)
                if (el["faces"] is JObject faces)
                    foreach (var f in faces)
                    {
                        var t = ResolveTex(m, (string)f.Value["texture"]);
                        if (t != null) yield return t;
                    }
        }

        // ---------------------------------------------------------------- baking

        /// Atlas rect of a texture (Unity uv space), or null if it isn't in the atlas.
        public Func<string, Rect?> Uv;
        /// Pixels of a texture (for the item-sprite extrusion).
        public Func<string, Image> Pixels;

        public BakedModel Bake(Model m, int rotX = 0, int rotY = 0)
        {
            var b = new BakedModel { Display = m.Display, Generated = m.Generated };
            if (m.Generated) { BakeGenerated(b, m); return b; }
            if (m.Elements == null) return b;

            bool rotated = rotX != 0 || rotY != 0;
            // Minecraft: rotateYXZ(-y, -x, 0) about the block's centre (x first, then y), right-handed.
            var mv = rotated ? (RhRotY(-rotY) * RhRotX(-rotX)) : Matrix4x4.identity;

            foreach (var elTok in m.Elements)
            {
                if (!(elTok is JObject el)) continue;
                var from = V3(el["from"], Vector3.zero) / 16f;
                var to = V3(el["to"], Vector3.one * 16) / 16f;
                var er = ElementRotation(el["rotation"] as JObject);
                bool full = from == Vector3.zero && to == Vector3.one && er == null;
                var faces = el["faces"] as JObject;
                if (faces == null) continue;
                if (full && faces.Count == 6 && m.Elements.Count == 1) b.FullCube = true;

                Vector3 Xf(Vector3 p)
                {
                    if (er != null) p = er(p);
                    if (rotated) p = mv.MultiplyPoint3x4(p - Vector3.one * 0.5f) + Vector3.one * 0.5f;
                    return p;
                }

                // element box
                var bmin = Vector3.one * 9f; var bmax = -Vector3.one * 9f;
                for (int i = 0; i < 8; i++)
                {
                    var c = Xf(new Vector3((i & 1) == 0 ? from.x : to.x, (i & 2) == 0 ? from.y : to.y, (i & 4) == 0 ? from.z : to.z));
                    bmin = Vector3.Min(bmin, c); bmax = Vector3.Max(bmax, c);
                }
                // flat elements (cross plants, pane edges) still need some thickness to be hit
                for (int a = 0; a < 3; a++)
                    if (bmax[a] - bmin[a] < 0.1f) { float mid = (bmax[a] + bmin[a]) * 0.5f; bmin[a] = mid - 0.05f; bmax[a] = mid + 0.05f; }
                var box = new Bounds(); box.SetMinMax(bmin, bmax);
                b.Boxes.Add(box);

                foreach (var f in faces)
                {
                    if (!Dirs.TryParse(f.Key, out var dir)) continue;
                    var fo = f.Value as JObject;
                    if (fo == null) continue;
                    var tex = ResolveTex(m, (string)fo["texture"]);
                    var rect = tex != null ? Uv?.Invoke(tex) : null;
                    if (rect == null) continue;
                    float[] uv = fo["uv"] is JArray ua && ua.Count == 4
                        ? new[] { (float)ua[0], (float)ua[1], (float)ua[2], (float)ua[3] }
                        : DefaultUv(dir, from * 16f, to * 16f);
                    int frot = fo["rotation"] != null ? (int)fo["rotation"] : 0;
                    int cull = -1;
                    if (fo["cullface"] != null && Dirs.TryParse((string)fo["cullface"], out var cd))
                        cull = (int)(rotated ? Dirs.Nearest(mv.MultiplyVector(Dirs.Vec[(int)cd])) : cd);
                    bool tint = fo["tintindex"] != null;
                    var n = Dirs.Vec[(int)dir];
                    if (er != null) n = (er(n) - er(Vector3.zero)).normalized;
                    if (rotated) n = mv.MultiplyVector(n);
                    AddFace(b, from, to, dir, uv, rect.Value, frot, cull, tint, n, Xf);
                }
            }
            return b;
        }

        // Unity's quaternion maths is numerically the standard right-handed rotation formula, so
        // Minecraft's rotations carry over unchanged in Minecraft's own coordinates.
        public static Matrix4x4 RhRotX(float deg) => Matrix4x4.Rotate(Quaternion.AngleAxis(deg, Vector3.right));
        public static Matrix4x4 RhRotY(float deg) => Matrix4x4.Rotate(Quaternion.AngleAxis(deg, Vector3.up));
        public static Matrix4x4 RhRotZ(float deg) => Matrix4x4.Rotate(Quaternion.AngleAxis(deg, Vector3.forward));

        /// Element "rotation": {origin, axis, angle, rescale}.
        private static Func<Vector3, Vector3> ElementRotation(JObject r)
        {
            if (r == null) return null;
            float angle = r["angle"] != null ? (float)r["angle"] : 0f;
            if (Mathf.Abs(angle) < 1e-4f) return null;
            var origin = V3(r["origin"], Vector3.one * 8) / 16f;
            string axis = (string)r["axis"] ?? "y";
            Matrix4x4 rm = axis == "x" ? RhRotX(angle) : axis == "z" ? RhRotZ(angle) : RhRotY(angle);
            var scale = Vector3.one;
            if (r["rescale"] != null && (bool)r["rescale"])
            {
                float s = 1f / Mathf.Cos(angle * Mathf.Deg2Rad);
                scale = axis == "x" ? new Vector3(1, s, s) : axis == "z" ? new Vector3(s, s, 1) : new Vector3(s, 1, s);
            }
            return p => Vector3.Scale(rm.MultiplyVector(p - origin), scale) + origin;
        }

        /// Minecraft's uvs for a face that doesn't give any (from/to in 1/16).
        private static float[] DefaultUv(Dir d, Vector3 f, Vector3 t)
        {
            switch (d)
            {
                case Dir.Down: return new[] { f.x, 16 - t.z, t.x, 16 - f.z };
                case Dir.Up: return new[] { f.x, f.z, t.x, t.z };
                case Dir.North: return new[] { 16 - t.x, 16 - t.y, 16 - f.x, 16 - f.y };
                case Dir.South: return new[] { f.x, 16 - t.y, t.x, 16 - f.y };
                case Dir.West: return new[] { f.z, 16 - t.y, t.z, 16 - f.y };
                default: return new[] { 16 - t.z, 16 - t.y, 16 - f.z, 16 - f.y };
            }
        }

        /// Minecraft's FaceInfo vertex order: vertex i takes uv corner (i + rotation/90) % 4 of
        /// (u0,v0), (u0,v1), (u1,v1), (u1,v0).
        private static Vector3[] Corners(Dir d, Vector3 a, Vector3 b)
        {
            switch (d)
            {
                case Dir.Down: return new[] { new Vector3(a.x, a.y, b.z), new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z) };
                case Dir.Up: return new[] { new Vector3(a.x, b.y, a.z), new Vector3(a.x, b.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, b.y, a.z) };
                case Dir.North: return new[] { new Vector3(b.x, b.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(a.x, a.y, a.z), new Vector3(a.x, b.y, a.z) };
                case Dir.South: return new[] { new Vector3(a.x, b.y, b.z), new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z) };
                case Dir.West: return new[] { new Vector3(a.x, b.y, a.z), new Vector3(a.x, a.y, a.z), new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z) };
                default: return new[] { new Vector3(b.x, b.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z) };
            }
        }

        private static void AddFace(BakedModel b, Vector3 from, Vector3 to, Dir dir, float[] uv, Rect rect, int frot,
            int cull, bool tint, Vector3 n, Func<Vector3, Vector3> xf)
        {
            var c = Corners(dir, from, to);
            var uvc = new[]
            {
                new Vector2(uv[0], uv[1]), new Vector2(uv[0], uv[3]), new Vector2(uv[2], uv[3]), new Vector2(uv[2], uv[1]),
            };
            int shift = ((frot / 90) % 4 + 4) % 4;
            Vector2 A(int i)
            {
                var p = uvc[(i + shift) % 4];
                return new Vector2(rect.x + p.x / 16f * rect.width, rect.y + rect.height * (1f - p.y / 16f));
            }
            Vector2 F(int i)
            {
                var p = uvc[(i + shift) % 4];
                return new Vector2(p.x / 16f, 1f - p.y / 16f);
            }
            b.Quads.Add(new BQuad
            {
                P0 = xf(c[0]), P1 = xf(c[1]), P2 = xf(c[2]), P3 = xf(c[3]),
                T0 = A(0), T1 = A(1), T2 = A(2), T3 = A(3),
                F0 = F(0), F1 = F(1), F2 = F(2), F3 = F(3),
                N = n.normalized,
                Cull = cull,
                Tint = tint,
            });
        }

        /// ItemModelGenerator: the sprite as a 1/16 thick slab, front and back plus a side wall
        /// along every edge between an opaque and a transparent pixel.
        private void BakeGenerated(BakedModel b, Model m)
        {
            var tex = ResolveTex(m, "#layer0");
            var rect = tex != null ? Uv?.Invoke(tex) : null;
            if (rect == null) return;
            var img = Pixels?.Invoke(tex);
            Func<Vector3, Vector3> id = p => p;
            float z0 = 7.5f / 16f, z1 = 8.5f / 16f;
            AddFace(b, new Vector3(0, 0, z0), new Vector3(1, 1, z1), Dir.South, new float[] { 0, 0, 16, 16 }, rect.Value, 0, -1, false, Dirs.Vec[(int)Dir.South], id);
            AddFace(b, new Vector3(0, 0, z0), new Vector3(1, 1, z1), Dir.North, new float[] { 16, 0, 0, 16 }, rect.Value, 0, -1, false, Dirs.Vec[(int)Dir.North], id);
            var box = new Bounds(); box.SetMinMax(new Vector3(0, 0, z0), new Vector3(1, 1, z1));
            b.Boxes.Add(box);
            if (img == null) return;
            int w = img.W, h = img.H;
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && img[x, y].a > 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Solid(x, y)) continue;
                    float px0 = x / (float)w, px1 = (x + 1) / (float)w;
                    float py0 = 1f - (y + 1) / (float)h, py1 = 1f - y / (float)h;
                    // the pixel's own texel, inset a little so it doesn't bleed
                    float u0 = (x + 0.1f) * 16f / w, u1 = (x + 0.9f) * 16f / w, v0 = (y + 0.1f) * 16f / h, v1 = (y + 0.9f) * 16f / h;
                    var uv = new[] { u0, v0, u1, v1 };
                    if (!Solid(x - 1, y)) AddFace(b, new Vector3(px0, py0, z0), new Vector3(px0, py1, z1), Dir.West, uv, rect.Value, 0, -1, false, Dirs.Vec[(int)Dir.West], id);
                    if (!Solid(x + 1, y)) AddFace(b, new Vector3(px1, py0, z0), new Vector3(px1, py1, z1), Dir.East, uv, rect.Value, 0, -1, false, Dirs.Vec[(int)Dir.East], id);
                    if (!Solid(x, y - 1)) AddFace(b, new Vector3(px0, py1, z0), new Vector3(px1, py1, z1), Dir.Up, uv, rect.Value, 0, -1, false, Dirs.Vec[(int)Dir.Up], id);
                    if (!Solid(x, y + 1)) AddFace(b, new Vector3(px0, py0, z0), new Vector3(px1, py0, z1), Dir.Down, uv, rect.Value, 0, -1, false, Dirs.Vec[(int)Dir.Down], id);
                }
        }

        /// A plain cube from a full-cube BlockDef's three atlas rects (for hand, drops and icons).
        public static BakedModel Cube(BlockDef d, Dictionary<string, ModelDisplay> display)
        {
            var b = new BakedModel { Display = display, FullCube = true };
            Func<Vector3, Vector3> id = p => p;
            var full = new float[] { 0, 0, 16, 16 };
            for (int i = 0; i < 6; i++)
            {
                var dir = (Dir)i;
                var r = dir == Dir.Up ? d.TopUv : dir == Dir.Down ? d.BottomUv : d.SideUv;
                AddFace(b, Vector3.zero, Vector3.one, dir, full, r, 0, i, false, Dirs.Vec[i], id);
            }
            var box = new Bounds(); box.SetMinMax(Vector3.zero, Vector3.one);
            b.Boxes.Add(box);
            return b;
        }
    }
}
