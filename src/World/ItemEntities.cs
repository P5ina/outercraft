using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.World
{
    /// Dropped items (Minecraft's ItemEntity): a small spinning, bobbing copy of the item's model that
    /// falls towards the planet, slides to a stop and flies into the player when they walk over it.
    public sealed class ItemEntities : MonoBehaviour
    {
        public static ItemEntities Instance;

        /// Gives the stack to the player; returns how many didn't fit.
        public Func<ItemDef, int, int> Collect;

        private sealed class E
        {
            public GameObject Go;
            public Transform Frame;
            public Vector3 Pos, Vel;   // frame-local
            public ItemStack Stack;
            public float Age, Delay, BobOffs;
            public bool OnGround;
            public float PickT = -1f;
            public Vector3 PickFrom;
        }

        private readonly List<E> _items = new List<E>();
        private readonly Dictionary<ItemDef, Mesh> _meshes = new Dictionary<ItemDef, Mesh>();
        private static readonly System.Random Rng = new System.Random();
        private static float R() => (float)Rng.NextDouble();

        private void Awake() => Instance = this;

        public void Clear()
        {
            foreach (var e in _items) if (e.Go != null) Destroy(e.Go);
            _items.Clear();
        }

        /// A block's drop: Block.popResource, a random spot in the block and a little hop.
        public void SpawnFromBlock(Transform frame, Vector3 worldPos, Vector3 up, ItemStack stack)
        {
            var tangent = Vector3.Cross(up, Vector3.right);
            if (tangent.sqrMagnitude < 1e-3f) tangent = Vector3.Cross(up, Vector3.forward);
            tangent.Normalize();
            var bitangent = Vector3.Cross(up, tangent);
            var vel = (tangent * (R() * 0.2f - 0.1f) + bitangent * (R() * 0.2f - 0.1f) + up * 0.2f) * 20f;
            Spawn(frame, worldPos - up * 0.125f, stack, vel, 0.5f);
        }

        public void Spawn(Transform frame, Vector3 worldPos, ItemStack stack, Vector3 worldVel, float pickupDelay)
        {
            if (frame == null || stack == null || stack.Empty) return;
            var go = new GameObject("OuterCraft_Item_" + stack.Item.Key);
            go.transform.SetParent(frame, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(stack.Item);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = PlanetBlocks.Material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _items.Add(new E
            {
                Go = go,
                Frame = frame,
                Pos = frame.InverseTransformPoint(worldPos),
                Vel = frame.InverseTransformVector(worldVel),
                Stack = stack,
                Delay = pickupDelay,
                BobOffs = R() * Mathf.PI * 2f,
            });
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0) return;
            float ticks = dt * 20f;
            float g = 16f;
            var det = Locator.GetPlayerForceDetector();
            if (det != null) { float a = det.GetForceAcceleration().magnitude; if (a > 0.5f) g = a; }
            var body = Locator.GetPlayerBody();

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var e = _items[i];
                if (e.Go == null || e.Frame == null) { _items.RemoveAt(i); continue; }
                e.Age += dt;
                if (e.Age > 300f) { Destroy(e.Go); _items.RemoveAt(i); continue; }
                var t = e.Frame;

                // flying into the player (ItemPickupParticle, 3 ticks)
                if (e.PickT >= 0f)
                {
                    e.PickT += dt / 0.15f;
                    if (body == null || e.PickT >= 1f) { Destroy(e.Go); _items.RemoveAt(i); continue; }
                    var target = t.InverseTransformPoint(body.GetPosition() - body.transform.up * 0.5f);
                    e.Pos = Vector3.Lerp(e.PickFrom, target, e.PickT * e.PickT);
                    Place(e, dt);
                    continue;
                }

                // ItemEntity.tick: gravity 0.04 b/t², drag 0.98, ground friction 0.6 * 0.98
                var down = -e.Pos.normalized;
                e.Vel += down * g * dt;
                var from = t.TransformPoint(e.Pos - down * 0.05f);
                var step = e.Vel * dt;
                var to = t.TransformPoint(e.Pos + step);
                if (Physics.Linecast(from, to, out var hit, OWLayerMask.physicalMask, QueryTriggerInteraction.Ignore))
                {
                    e.Pos = t.InverseTransformPoint(hit.point + hit.normal * 0.01f);
                    var n = t.InverseTransformDirection(hit.normal);
                    float into = Vector3.Dot(e.Vel, n);
                    if (into < 0) e.Vel -= n * into;
                    e.OnGround = Vector3.Dot(n, -down) > 0.5f;
                }
                else
                {
                    e.Pos += step;
                    e.OnGround = false;
                }
                e.Vel *= Mathf.Pow(0.98f, ticks);
                if (e.OnGround)
                    e.Vel = Vector3.Project(e.Vel, down) + Vector3.ProjectOnPlane(e.Vel, down) * Mathf.Pow(0.6f, ticks);

                // pickup: within the player's box grown by 1 block sideways and half a block up/down
                e.Delay -= dt;
                if (e.Delay <= 0f && body != null && Collect != null && OuterCraft.CanPickUp)
                {
                    var p = t.TransformPoint(e.Pos);
                    var up = body.transform.up;
                    var rel = p - body.GetPosition();
                    float vertical = Vector3.Dot(rel, up);
                    float horizontal = (rel - up * vertical).magnitude;
                    if (horizontal < 1.3f && vertical > -1.5f && vertical < 1.5f)
                    {
                        int left = Collect(e.Stack.Item, e.Stack.Count);
                        if (left < e.Stack.Count)
                        {
                            McSounds.Pop(p, body.transform);
                            if (left > 0) e.Stack.Count = left;
                            else { e.PickT = 0f; e.PickFrom = e.Pos; }
                        }
                    }
                }
                Place(e, dt);
            }
        }

        /// Bob and spin: (age / 20 + bobOffs) radians, bob sin(age / 10 + bobOffs) * 0.1 + 0.1 blocks.
        private static void Place(E e, float dt)
        {
            var up = e.Pos.normalized;
            var tangent = Vector3.Cross(up, Vector3.right);
            if (tangent.sqrMagnitude < 1e-3f) tangent = Vector3.Cross(up, Vector3.forward);
            float ticks = e.Age * 20f;
            float spin = (ticks / 20f + e.BobOffs) * Mathf.Rad2Deg;
            float bob = Mathf.Sin(ticks / 10f + e.BobOffs) * 0.1f + 0.1f;
            e.Go.transform.localPosition = e.Pos + up * bob;
            e.Go.transform.localRotation = Quaternion.LookRotation(tangent, up) * Quaternion.Euler(0, spin, 0);
        }

        /// The item's model with its "ground" display transform, lifted by 0.25 * scale.y (ItemEntityRenderer).
        private Mesh MeshFor(ItemDef item)
        {
            if (_meshes.TryGetValue(item, out var m) && m != null) return m;
            m = new Mesh { name = "item_" + item.Key };
            var d = item.Model?.Get("ground") ?? ModelDisplay.Identity;
            var mat = Matrix4x4.Translate(new Vector3(0, 0.25f * d.S.y, 0)) * Matrix4x4.Translate(d.T / 16f) *
                      McModels.RhRotX(d.R.x) * McModels.RhRotY(d.R.y) * McModels.RhRotZ(d.R.z) *
                      Matrix4x4.Scale(d.S) * Matrix4x4.Translate(-Vector3.one * 0.5f);
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nrm = new List<Vector3>();
            var col = new List<Color32>(); var uv2 = new List<Vector3>(); var idx = new List<int>();
            float glow = item.Light / 15f;
            if (item.Model != null)
                foreach (var q in item.Model.Quads)
                {
                    Vector3 U(Vector3 p) { var w = mat.MultiplyPoint3x4(p); return new Vector3(w.x, w.y, -w.z); } // Minecraft -> Unity
                    var n = mat.MultiplyVector(q.N); n = new Vector3(n.x, n.y, -n.z).normalized;
                    int i = pos.Count;
                    pos.Add(U(q.P0)); pos.Add(U(q.P1)); pos.Add(U(q.P2)); pos.Add(U(q.P3));
                    uv.Add(q.T0); uv.Add(q.T1); uv.Add(q.T2); uv.Add(q.T3);
                    var c = Shade.Apply(q.Tint ? item.TintColor : new Color32(255, 255, 255, 255), Shade.Of(q.N));
                    for (int k = 0; k < 4; k++) { nrm.Add(n); col.Add(c); uv2.Add(new Vector3(glow, 1f, 0f)); }
                    var cr = Vector3.Cross(pos[i + 1] - pos[i], pos[i + 2] - pos[i]);
                    bool flip = Vector3.Dot(cr, n) < 0;
                    if (!flip) { idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3); }
                    else { idx.Add(i); idx.Add(i + 2); idx.Add(i + 1); idx.Add(i); idx.Add(i + 3); idx.Add(i + 2); }
                }
            m.SetVertices(pos); m.SetUVs(0, uv); m.SetNormals(nrm); m.SetColors(col); m.SetUVs(1, uv2);
            m.SetTriangles(idx, 0, true);
            _meshes[item] = m;
            return m;
        }
    }
}
