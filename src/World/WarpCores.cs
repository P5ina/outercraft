using System.Collections.Generic;
using OuterCraft.Assets;
using OuterCraft.Player;
using UnityEngine;

namespace OuterCraft.World
{
    /// Warp cores as Minecraft things: the Nomai Vessel's advanced warp core is an End Crystal
    /// (EndCrystalModel: two glass cubes and a core, turning on Minecraft's 60-degree tilted axis,
    /// bobbing), the broken one a dead, still crystal; the small black and white cores are an Eye
    /// of Ender and an Ender Pearl. The game's own core is only hidden: picking it up, socketing it,
    /// its light and sounds all stay Outer Wilds'.
    public sealed class WarpCores
    {
        private sealed class Swap
        {
            public WarpCoreItem Item;
            public GameObject Go;
            public Transform Outer, Inner, Cube;
            public Renderer[] Hidden;
            public bool Crystal, Alive;
            public float Phase;
        }

        private readonly List<Swap> _swaps = new List<Swap>();
        private float _nextScan;
        private bool _shown;
        private static Mesh _glass, _core;
        private static Material _mat, _matDead;

        public void Clear()
        {
            _swaps.Clear();
            _shown = false;
            _nextScan = 0f;
        }

        public void Update(bool minecraftMode)
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                Scan();
            }
            _swaps.RemoveAll(s => s.Item == null);
            foreach (var s in _swaps)
            {
                if (s.Go == null) continue;
                if (minecraftMode != _shown || s.Go.activeSelf != minecraftMode)
                {
                    s.Go.SetActive(minecraftMode);
                    foreach (var r in s.Hidden) if (r != null) r.forceRenderingOff = minecraftMode;
                }
                if (minecraftMode && s.Crystal) Animate(s);
            }
            _shown = minecraftMode;
        }

        private void Scan()
        {
            foreach (var item in Object.FindObjectsOfType<WarpCoreItem>())
            {
                if (_swaps.Exists(s => s.Item == item)) continue;
                var s = Build(item);
                if (s != null) _swaps.Add(s);
            }
        }

        private Swap Build(WarpCoreItem item)
        {
            var type = item.GetWarpCoreType();
            bool crystal = type == WarpCoreType.Vessel || type == WarpCoreType.VesselBroken;
            if (crystal && McAssets.EndCrystal == null) return null;
            ItemDef small = null;
            if (!crystal)
            {
                small = Items.Get(type == WarpCoreType.Black ? "ender_eye" : "ender_pearl");
                if (small == null || ItemEntities.Instance == null) return null;
            }

            // where the core's own mesh sits, in the item's space
            var hidden = item.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds();
            bool any = false;
            foreach (var r in hidden)
            {
                if (r is ParticleSystemRenderer) continue;
                var c = item.transform.InverseTransformPoint(r.bounds.center);
                if (!any) { b = new Bounds(c, Vector3.zero); any = true; }
                else b.Encapsulate(c);
            }
            var renderers = new List<Renderer>();
            foreach (var r in hidden) if (!(r is ParticleSystemRenderer)) renderers.Add(r);

            var go = new GameObject("OuterCraft_" + (crystal ? "EndCrystal" : small.Key));
            go.transform.SetParent(item.transform, false);
            go.transform.localPosition = b.center;
            var s = new Swap { Item = item, Go = go, Hidden = renderers.ToArray(), Crystal = crystal, Alive = type == WarpCoreType.Vessel, Phase = Random.value * 100f };

            if (crystal)
            {
                EnsureCrystal();
                // EndCrystalRenderer draws at 2x: the outer glass is one block across. Here a third of that.
                go.transform.localScale = Vector3.one * 0.32f;
                var mat = s.Alive ? _mat : _matDead;
                s.Outer = Part("outer_glass", go.transform, _glass, mat, 1f);
                s.Inner = Part("inner_glass", s.Outer, _glass, mat, 0.875f);
                s.Cube = Part("cube", s.Inner, _core, mat, 0.765625f);
                Animate(s);
            }
            else
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = ItemEntities.Instance.MeshFor(small);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = PlanetBlocks.Material;
                go.transform.localScale = Vector3.one * 0.6f;
            }
            go.SetActive(false);
            return s;
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Material mat, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        // ---------------------------------------------------------------- EndCrystalModel.setupAnim

        private static readonly float Sin45 = Mathf.Sin(Mathf.PI / 4f);

        /// A Minecraft rotation (right-handed, about a Minecraft axis) as a Unity one (z flipped).
        private static Quaternion Mc(Vector3 axis, float deg) => Quaternion.AngleAxis(-deg, new Vector3(axis.x, axis.y, -axis.z));

        private static void Animate(Swap s)
        {
            float age = s.Alive ? Time.time * 20f + s.Phase : s.Phase;
            float f = age * 3f;
            // EndCrystalRenderer.getY, centred and halved so it stays in its socket
            float k = Mathf.Sin(age * 0.2f) / 2f + 0.5f;
            k = (k * k + k) * 0.4f - 1.4f;
            float bob = s.Alive ? (k + 1.0f) * 0.5f : 0f;   // -0.2 .. 0.2 blocks
            var diag = new Vector3(Sin45, 0f, Sin45);
            var tilt = Mc(diag, 60f);
            s.Outer.localPosition = new Vector3(0f, bob, 0f);
            s.Outer.localRotation = Mc(Vector3.up, f) * tilt;
            s.Inner.localRotation = tilt * Mc(Vector3.up, f);
            s.Cube.localRotation = tilt * Mc(Vector3.up, f);
        }

        // ---------------------------------------------------------------- meshes (64x32 texture)

        private static void EnsureCrystal()
        {
            if (_glass == null) _glass = Box(0, 0);
            if (_core == null) _core = Box(32, 0);
            if (_mat == null)
            {
                // the core glows like the warp core it stands in for; seen through its glass, both sides
                _mat = BlockMaterials.ForTexture(McAssets.EndCrystal, true);
                if (_mat.HasProperty("_EmissionColor")) _mat.SetColor("_EmissionColor", new Color(0.9f, 0.85f, 1f));
                if (_mat.HasProperty("_BlockLightStrength")) _mat.SetFloat("_BlockLightStrength", 1f);
                _matDead = BlockMaterials.ForTexture(McAssets.EndCrystal);
            }
        }

        /// One 8x8x8 ModelPart cube centred on the origin, in blocks (EndCrystalRenderer's 2x scale
        /// included), drawn from both sides.
        private static Mesh Box(float u, float v)
        {
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nrm = new List<Vector3>();
            var col = new List<Color32>(); var uv2 = new List<Vector3>(); var idx = new List<int>();
            McBox.Build(-4, -4, -4, 8, 8, 8, u, v, 0f, false, (p, t, n) =>
            {
                for (int side = 0; side < 2; side++)
                {
                    int i = pos.Count;
                    var un = new Vector3(n.x, n.y, -n.z) * (side == 0 ? 1f : -1f);
                    for (int c = 0; c < 4; c++)
                    {
                        pos.Add(new Vector3(p[c].x, p[c].y, -p[c].z) * (2f / 16f));
                        uv.Add(new Vector2(t[c].x / 64f, 1f - t[c].y / 32f));
                        nrm.Add(un);
                        col.Add(new Color32(255, 255, 255, 255));
                        uv2.Add(new Vector3(side == 0 ? 0.6f : 0.3f, 1f, 0f));
                    }
                    var cr = Vector3.Cross(pos[i + 1] - pos[i], pos[i + 2] - pos[i]);
                    bool flip = Vector3.Dot(cr, un) < 0;
                    if (!flip) { idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3); }
                    else { idx.Add(i); idx.Add(i + 2); idx.Add(i + 1); idx.Add(i); idx.Add(i + 3); idx.Add(i + 2); }
                }
            });
            var m = new Mesh { name = "end_crystal_box" };
            m.SetVertices(pos); m.SetUVs(0, uv); m.SetNormals(nrm); m.SetColors(col); m.SetUVs(1, uv2);
            m.SetTriangles(idx, 0, true);
            return m;
        }

        // ---------------------------------------------------------------- names

        public static bool Enabled;

        public static void DisplayNamePostfix(WarpCoreItem __instance, ref string __result)
        {
            if (!Enabled || __instance == null) return;
            switch (__instance.GetWarpCoreType())
            {
                case WarpCoreType.Vessel: __result = "End Crystal"; break;
                case WarpCoreType.VesselBroken: __result = "Cracked End Crystal"; break;
                case WarpCoreType.Black: __result = "Eye of Ender"; break;
                default: __result = "Ender Pearl"; break;
            }
        }
    }
}
