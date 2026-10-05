using System;
using System.Collections.Generic;
using System.Linq;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.World
{
    /// Every planet's blocks for the current loop (they reset with the loop, like everything else), plus the nearest glowing
    /// blocks turned into real point lights.
    public sealed class BlockWorld : MonoBehaviour
    {
        public static BlockWorld Instance;

        private readonly Dictionary<OWRigidbody, PlanetBlocks> _planets = new Dictionary<OWRigidbody, PlanetBlocks>();
        private float _nextSave;
        private readonly List<Light> _lights = new List<Light>();
        private float _nextLights;
        public int MaxLights = 8;

        [Serializable]
        public class SaveData
        {
            public int n;
            public int layer;
            public List<int> cells = new List<int>(); // face, u, v, h, id | state << 8
            public List<string> palette = new List<string>(); // block key per id, so ids can move
        }

        private void Awake() => Instance = this;

        public IEnumerable<PlanetBlocks> Planets => _planets.Values.Where(p => p != null);

        /// The planet a collider belongs to (its own body, never the ship, probe or loose props).
        public static OWRigidbody PlanetOf(Collider c)
        {
            if (c == null) return null;
            var rb = c.attachedRigidbody;
            var body = rb != null ? rb.GetComponent<OWRigidbody>() : c.GetComponentInParent<OWRigidbody>();
            if (body == null) return null;
            var astro = body.GetComponent<AstroObject>() ?? body.GetComponentInParent<AstroObject>();
            if (astro == null) return null;
            var planet = astro.GetOWRigidbody();
            return planet != null ? planet : body;
        }

        public static string KeyFor(OWRigidbody body)
        {
            var astro = body.GetComponent<AstroObject>();
            if (astro != null && astro.GetAstroObjectName() != AstroObject.Name.None && astro.GetAstroObjectName() != AstroObject.Name.CustomString)
                return astro.GetAstroObjectName().ToString();
            return body.name.Replace(' ', '_');
        }

        private static string FileFor(string key) => $"outercraft_blocks_{key}.json";

        /// Existing block store for a planet, loading it from disk on first use this loop.
        public PlanetBlocks Get(OWRigidbody body)
        {
            if (body == null) return null;
            if (_planets.TryGetValue(body, out var pb) && pb != null) return pb;
            return null; // builds live for one loop only: the supernova takes them like everything else
        }

        /// Block store for a planet, creating its grid if this is the first block ever placed there.
        public PlanetBlocks GetOrCreate(OWRigidbody body, float surfaceRadius, int layer)
        {
            var pb = Get(body);
            if (pb != null) return pb;
            int n = CubeSphere.ColumnsFor(surfaceRadius);
            OuterCraft.Log($"new block grid on {KeyFor(body)}: {2 * n}x{2 * n} columns per cube face (surface radius {surfaceRadius:F0} m)");
            return Attach(body, KeyFor(body), n, layer, null, surfaceRadius);
        }

        private PlanetBlocks Attach(OWRigidbody body, string key, int n, int layer, SaveData data, float surfaceRadius = 0f)
        {
            var go = new GameObject($"OuterCraft_Blocks_{key}");
            go.transform.SetParent(body.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.layer = layer;
            var pb = go.AddComponent<PlanetBlocks>();
            go.AddComponent<BlockParticles>();
            pb.Key = key;
            pb.Body = body;
            pb.Grid = new CubeSphere(n, surfaceRadius);
            pb.Layer = layer;
            if (data != null)
            {
                var c = data.cells;
                var map = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    map[i] = (byte)i;
                    if (data.palette != null && i < data.palette.Count && data.palette[i] != null)
                        map[i] = Blocks.Get(data.palette[i])?.Id ?? 0;
                }
                for (int i = 0; i + 4 < c.Count; i += 5)
                {
                    int v = c[i + 4];
                    pb.LoadRaw(new Cell(c[i], c[i + 1], c[i + 2], c[i + 3]), map[v & 0xFF], (v >> 8) & 0xFF);
                }
                pb.RebuildAllNow();
                OuterCraft.Log($"loaded {pb.Count} blocks on {key}");
            }
            _planets[body] = pb;
            return pb;
        }

        /// Every planet that has blocks, whether or not we've been there this loop: load them all
        /// at the start so builds are visible from afar.
        public void LoadAllForScene()
        {
            foreach (var astro in FindObjectsOfType<AstroObject>())
            {
                var body = astro.GetOWRigidbody();
                if (body != null) Get(body);
            }
        }

        public void OnSceneReset()
        {
            _planets.Clear();
            _lights.Clear();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextLights)
            {
                _nextLights = Time.unscaledTime + 0.5f;
                UpdateLights();
            }
        }

        public void SaveNow()
        {
            foreach (var pb in _planets.Values)
            {
                if (pb == null || !pb.Changed) continue;
                var data = new SaveData { n = pb.Grid.N, layer = pb.Layer };
                data.palette.Add(null);
                foreach (var b in Blocks.All) data.palette.Add(b.Key);
                foreach (var kv in pb.AllBlocks)
                {
                    var c = kv.Key;
                    data.cells.Add(c.Face); data.cells.Add(c.U); data.cells.Add(c.V); data.cells.Add(c.H); data.cells.Add(kv.Value);
                }
                OuterCraft.Helper.Storage.Save(data, FileFor(pb.Key));
                pb.Changed = false;
            }
        }

        /// Glowstone and friends: real lights for the few nearest the camera.
        private void UpdateLights()
        {
            var cam = Locator.GetPlayerCamera();
            if (cam == null) return;
            var eye = cam.transform.position;
            var near = new List<(float d, Transform parent, Vector3 local, int level)>();
            foreach (var pb in _planets.Values)
            {
                if (pb == null) continue;
                foreach (var (local, level) in pb.Emitters())
                {
                    var w = pb.transform.TransformPoint(local);
                    float d = (w - eye).sqrMagnitude;
                    if (d < 60 * 60) near.Add((d, pb.transform, local, level));
                }
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            int n = Math.Min(near.Count, MaxLights);
            while (_lights.Count < n)
            {
                var go = new GameObject("OuterCraft_BlockLight");
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.85f, 0.6f);
                _lights.Add(l);
            }
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null) continue;
                if (i >= n) { l.enabled = false; continue; }
                if (l.transform.parent != near[i].parent) l.transform.SetParent(near[i].parent, false);
                l.transform.localPosition = near[i].local;
                l.range = near[i].level * 0.9f;
                l.intensity = 1.4f;
                l.enabled = true;
            }
        }
    }
}
