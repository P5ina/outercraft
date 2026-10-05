using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.Player
{
    /// Steve (or whichever skin is set) as Minecraft draws him: PlayerModel's parts with both skin
    /// layers, HumanoidModel's walk, idle, attack, item-holding and sneaking poses, the held item
    /// (thirdperson_righthand), the elytra wings and the face-down glide. Shown in third person,
    /// standing in for the Hearthian, lit by the scene like everything else.
    public sealed class SteveModel
    {
        private GameObject _go;
        private Transform _model;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private Transform _body;
        private float _limbPos, _limbAmt, _glide;

        private const float Scale = 0.9375f; // PlayerRenderer: 15/16

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
            _go = null;
            _body = null;
        }

        public void Update(bool show, ViewModel vm, ItemDef held, bool sneaking, bool gliding, bool hasElytra)
        {
            var body = Locator.GetPlayerBody();
            if (!show || body == null || McAssets.Skin == null)
            {
                if (_go != null) _go.SetActive(false);
                return;
            }
            if (_go == null || _body != body.transform) Build(body.transform);
            _go.SetActive(true);

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float ticks = dt * 20f;
            // WalkAnimationState: speed = min(blocks per tick * 4, 1), eased 0.4 per tick
            float target = Mathf.Min(vm.Speed / 20f * 4f, 1f);
            if (gliding) target = 0f;
            _limbAmt += (target - _limbAmt) * (1f - Mathf.Pow(0.6f, ticks));
            _limbPos += _limbAmt * ticks;
            // fallFlyTicks: the body swings to horizontal over the first second
            _glide = gliding ? Mathf.Min(1f, _glide + dt) : 0f;

            // feet at the bottom of Outer Wilds' capsule
            var cap = body.GetComponentInChildren<CapsuleCollider>();
            float feet = cap != null ? cap.center.y - cap.height * 0.5f : -1f;
            _go.transform.localPosition = new Vector3(0, feet, 0);
            _go.transform.localRotation = Quaternion.AngleAxis(BodyTurn(body, vm, gliding, ticks), Vector3.up);

            // PlayerRenderer.setupRotations when fall flying: lie along the look direction
            float g = _glide * _glide; // (ticks² / 100) clamped
            var pivot = new Vector3(0, 0.9f, 0);
            var rot = Quaternion.AngleAxis(g * (90f + vm.Pitch), Vector3.right);
            _model.localRotation = rot;
            _model.localPosition = pivot - rot * pivot;

            Build(vm, held, sneaking, gliding, hasElytra, body);
        }

        // ---------------------------------------------------------------- body yaw (LivingEntity.tickHeadTurn)

        private float _rel;          // body yaw minus head yaw, degrees (Outer Wilds turns the whole Hearthian with the mouse)
        private Vector3 _lastFwd;
        private bool _haveFwd;

        /// The head turns with the mouse at once; the body follows: towards where you walk (walking
        /// backwards keeps it facing forward), straight ahead while swinging or gliding, otherwise
        /// only once the head is more than 50 degrees off — easing 0.3 of the way per tick.
        private float BodyTurn(OWRigidbody body, ViewModel vm, bool gliding, float ticks)
        {
            var up = body.transform.up;
            var fwd = Vector3.ProjectOnPlane(body.transform.forward, up).normalized;
            if (_haveFwd) _rel -= Vector3.SignedAngle(_lastFwd, fwd, up);
            _lastFwd = fwd;
            _haveFwd = true;

            float target = _rel;
            var ctl = Locator.GetPlayerController();
            var ground = ctl != null ? ctl.GetLastGroundBody() : null;
            var v = body.GetVelocity() - (ground != null ? ground.GetPointVelocity(body.GetPosition()) : Vector3.zero);
            var vh = Vector3.ProjectOnPlane(v, up);
            if (vh.magnitude > 1f)
            {
                target = Vector3.SignedAngle(fwd, vh, up);
                if (Mathf.Abs(target) > 95f) target = Mathf.DeltaAngle(0f, target - 180f); // backwards: face forward
            }
            if (vm.SwingProgress > 0f || gliding) target = 0f;
            _rel += Mathf.DeltaAngle(_rel, target) * (1f - Mathf.Pow(0.7f, ticks));
            _rel = Mathf.DeltaAngle(0f, _rel);
            _rel = Mathf.Clamp(_rel, -50f, 50f);    // getMaxHeadRotationRelativeToBody
            return _rel;
        }

        private void Build(Transform body)
        {
            Destroy();
            _body = body;
            _go = new GameObject("OuterCraft_Steve");
            _go.transform.SetParent(body, false);
            var model = new GameObject("model");
            model.transform.SetParent(_go.transform, false);
            _model = model.transform;
            _mesh = new Mesh { name = "steve" };
            _mesh.MarkDynamic();
            model.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = model.AddComponent<MeshRenderer>();
            _mr.sharedMaterials = new[]
            {
                World.BlockMaterials.ForTexture(McAssets.Skin),
                World.BlockMaterials.ForTexture(McAssets.Wings != null ? (Texture)McAssets.Wings : Texture2D.whiteTexture),
                World.PlanetBlocks.Material,
            };
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _mr.receiveShadows = true;
        }

        // ---------------------------------------------------------------- the pose (HumanoidModel.setupAnim)

        private struct Part { public float X, Y, Z, XRot, YRot, ZRot; }

        private void Build(ViewModel vm, ItemDef held, bool sneaking, bool gliding, bool hasElytra, OWRigidbody body)
        {
            const float Pi = Mathf.PI;
            float age = Time.time * 20f;
            float ls = _limbPos, la = _limbAmt;
            var head = new Part { XRot = Mathf.Clamp(vm.Pitch, -90f, 90f) * Mathf.Deg2Rad, YRot = -_rel * Mathf.Deg2Rad };
            // HumanoidModel: fall flying the head is held at -45 degrees (the whole body already points
            // along the look), so it faces where you fly
            if (gliding) head = new Part { XRot = -Mathf.PI / 4f };
            var torso = new Part();
            var rArm = new Part { X = -5, Y = 2, XRot = Mathf.Cos(ls * 0.6662f + Pi) * 2f * la * 0.5f };
            var lArm = new Part { X = 5, Y = 2, XRot = Mathf.Cos(ls * 0.6662f) * 2f * la * 0.5f };
            var rLeg = new Part { X = -1.9f, Y = 12, XRot = Mathf.Cos(ls * 0.6662f) * 1.4f * la };
            var lLeg = new Part { X = 1.9f, Y = 12, XRot = Mathf.Cos(ls * 0.6662f + Pi) * 1.4f * la };

            if (held != null) rArm.XRot = rArm.XRot * 0.5f - Pi / 10f; // ArmPose.ITEM

            // setupAttackAnimation
            float attack = vm.SwingProgress;
            if (attack > 0f)
            {
                torso.YRot = Mathf.Sin(Mathf.Sqrt(attack) * Pi * 2f) * 0.2f;
                rArm.Z = Mathf.Sin(torso.YRot) * 5f; rArm.X = -Mathf.Cos(torso.YRot) * 5f;
                lArm.Z = -Mathf.Sin(torso.YRot) * 5f; lArm.X = Mathf.Cos(torso.YRot) * 5f;
                rArm.YRot += torso.YRot; lArm.YRot += torso.YRot; lArm.XRot += torso.YRot;
                float f = 1f - attack; f *= f; f *= f; f = 1f - f;
                float f1 = Mathf.Sin(f * Pi);
                float f2 = Mathf.Sin(attack * Pi) * -(head.XRot - 0.7f) * 0.75f;
                rArm.XRot -= f1 * 1.2f + f2;
                rArm.YRot += torso.YRot * 2f;
                rArm.ZRot += Mathf.Sin(attack * Pi) * -0.4f;
            }

            if (sneaking && !gliding)
            {
                torso.XRot = 0.5f; rArm.XRot += 0.4f; lArm.XRot += 0.4f;
                rLeg.Z = 4f; lLeg.Z = 4f; rLeg.Y = 12.2f; lLeg.Y = 12.2f;
                head.Y = 4.2f; torso.Y = 3.2f; lArm.Y = 5.2f; rArm.Y = 5.2f;
            }

            // AnimationUtils.bobArms: breathing
            rArm.ZRot += Mathf.Cos(age * 0.09f) * 0.05f + 0.05f;
            lArm.ZRot -= Mathf.Cos(age * 0.09f) * 0.05f + 0.05f;
            rArm.XRot += Mathf.Sin(age * 0.067f) * 0.05f;
            lArm.XRot -= Mathf.Sin(age * 0.067f) * 0.05f;

            _v.Clear(); _n.Clear(); _uv.Clear(); _uv2.Clear(); _c.Clear(); _skin.Clear(); _wing.Clear(); _item.Clear();
            const float T = 64f;
            var m = M(head);
            Box(m, -4, -8, -4, 8, 8, 8, 0, 0, 0f, false, T, T, _skin);
            Box(m, -4, -8, -4, 8, 8, 8, 32, 0, 0.5f, false, T, T, _skin);          // hat
            m = M(torso);
            Box(m, -4, 0, -2, 8, 12, 4, 16, 16, 0f, false, T, T, _skin);
            Box(m, -4, 0, -2, 8, 12, 4, 16, 32, 0.25f, false, T, T, _skin);       // jacket
            var ra = M(rArm);
            Box(ra, -3, -2, -2, 4, 12, 4, 40, 16, 0f, false, T, T, _skin);
            Box(ra, -3, -2, -2, 4, 12, 4, 40, 32, 0.25f, false, T, T, _skin);     // sleeve
            m = M(lArm);
            Box(m, -1, -2, -2, 4, 12, 4, 32, 48, 0f, false, T, T, _skin);
            Box(m, -1, -2, -2, 4, 12, 4, 48, 48, 0.25f, false, T, T, _skin);
            m = M(rLeg);
            Box(m, -2, 0, -2, 4, 12, 4, 0, 16, 0f, false, T, T, _skin);
            Box(m, -2, 0, -2, 4, 12, 4, 0, 32, 0.25f, false, T, T, _skin);        // pants
            m = M(lLeg);
            Box(m, -2, 0, -2, 4, 12, 4, 16, 48, 0f, false, T, T, _skin);
            Box(m, -2, 0, -2, 4, 12, 4, 0, 48, 0.25f, false, T, T, _skin);

            if (hasElytra && McAssets.Wings != null) Wings(gliding, sneaking, body);
            if (held?.Model != null) HeldItem(ra, held);

            _mesh.Clear();
            _mesh.subMeshCount = 3;
            _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetUVs(1, _uv2.Count == _v.Count ? _uv2 : FillUv2());
            _mesh.SetTriangles(_skin, 0, true);
            _mesh.SetTriangles(_wing, 1, true);
            _mesh.SetTriangles(_item, 2, true);
        }

        private List<Vector3> FillUv2()
        {
            while (_uv2.Count < _v.Count) _uv2.Add(new Vector3(0f, 1f, 0f));
            if (_uv2.Count > _v.Count) _uv2.RemoveRange(_v.Count, _uv2.Count - _v.Count);
            return _uv2;
        }

        /// ElytraModel.setupAnim: folded on the back, spread out while gliding (more the faster you fall).
        private void Wings(bool gliding, bool sneaking, OWRigidbody body)
        {
            float xr = 0.2617994f, zr = -0.2617994f, y = 0f, yr = 0f;
            if (gliding)
            {
                float f4 = 1f;
                var ctl = Locator.GetPlayerController();
                var ground = ctl != null ? ctl.GetLastGroundBody() : null;
                var v = body.GetVelocity() - (ground != null ? ground.GetPointVelocity(body.GetPosition()) : Vector3.zero);
                float vy = Vector3.Dot(v.normalized, body.transform.up);
                if (vy < 0f) f4 = 1f - Mathf.Pow(-vy, 1.5f);
                xr = f4 * 0.34906584f + (1f - f4) * xr;
                zr = f4 * -1.5707964f + (1f - f4) * zr;
            }
            else if (sneaking) { xr = 0.6981317f; zr = -0.7853982f; y = 3f; yr = 0.08726646f; }
            var left = M(new Part { X = 5, Y = y, Z = 2, XRot = xr, YRot = yr, ZRot = zr });
            var right = M(new Part { X = -5, Y = y, Z = 2, XRot = xr, YRot = -yr, ZRot = -zr });
            // RenderType.armorCutoutNoCull: the wing's cut-out shape is seen from both sides
            _twoSided = true;
            Box(left, -10, 0, 0, 10, 20, 2, 22, 0, 1f, false, 64, 32, _wing);
            Box(right, 0, 0, 0, 10, 20, 2, 22, 0, 1f, true, 64, 32, _wing);
            _twoSided = false;
        }

        /// ItemInHandLayer: on the right arm, rotated to the hand, with the model's thirdperson_righthand transform.
        private void HeldItem(Mat arm, ItemDef item)
        {
            var d = item.Model.Get("thirdperson_righthand");
            var m = arm * Mat.RotX(-90f) * Mat.RotY(180f) * Mat.Translate(1f / 16f, 2f / 16f, -10f / 16f)
                    * Mat.Translate(d.T.x / 16f, d.T.y / 16f, d.T.z / 16f) * Mat.RotX(d.R.x) * Mat.RotY(d.R.y) * Mat.RotZ(d.R.z)
                    * Mat.Scale(d.S.x, d.S.y, d.S.z) * Mat.Translate(-0.5f, -0.5f, -0.5f);
            float glow = item.Light / 15f;
            foreach (var q in item.Model.Quads)
            {
                var col = Shade.Apply(q.Tint ? item.TintColor : new Color32(255, 255, 255, 255), Shade.Of(q.N));
                Quad(U(m.MulPoint(q.P0.x, q.P0.y, q.P0.z)), U(m.MulPoint(q.P1.x, q.P1.y, q.P1.z)),
                     U(m.MulPoint(q.P2.x, q.P2.y, q.P2.z)), U(m.MulPoint(q.P3.x, q.P3.y, q.P3.z)),
                     q.T0, q.T1, q.T2, q.T3, UD(m.MulDir(q.N.x, q.N.y, q.N.z)), col, glow, _item);
            }
        }

        // ---------------------------------------------------------------- geometry

        /// ModelPart.translateAndRotate: pivot (in 1/16), then Z, Y, X rotations.
        private static Mat M(Part p) =>
            Mat.Translate(p.X / 16f, p.Y / 16f, p.Z / 16f) * Mat.RotZ(p.ZRot * Mathf.Rad2Deg) * Mat.RotY(p.YRot * Mathf.Rad2Deg) * Mat.RotX(p.XRot * Mathf.Rad2Deg);

        /// Model space (y down, blocks) -> the renderer's scale(-1, -1, 1), translate(0, -1.501, 0),
        /// 15/16 player scale -> Unity (z mirrored), feet at 0, facing +z.
        private static Vector3 U(Vector3 w) => new Vector3(-w.x, 1.501f - w.y, -w.z) * Scale;
        private static Vector3 UD(Vector3 n) => new Vector3(-n.x, -n.y, -n.z).normalized;

        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Vector3> _uv2 = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _skin = new List<int>(), _wing = new List<int>(), _item = new List<int>();

        /// A ModelPart cube with Minecraft's box unwrap (same layout as the first-person arm),
        /// grown by `inflate` (the outer skin layer), optionally mirrored.
        private bool _twoSided;

        private void Box(Mat m, float x0, float y0, float z0, float dx, float dy, float dz, float u, float v,
            float inflate, bool mirror, float texW, float texH, List<int> tris)
        {
            var w = new Color32(255, 255, 255, 255);
            McBox.Build(x0, y0, z0, dx, dy, dz, u, v, inflate, mirror, (p, t, n) =>
            {
                Vector3 P(Vector3 q) => U(m.MulPoint(q.x / 16f, q.y / 16f, q.z / 16f));
                Vector2 T(Vector2 q) => new Vector2(q.x / texW, 1f - q.y / texH);
                Quad(P(p[0]), P(p[1]), P(p[2]), P(p[3]), T(t[0]), T(t[1]), T(t[2]), T(t[3]), UD(m.MulDir(n.x, n.y, n.z)), w, 0, tris);
            });
        }

        /// Single-sided quad facing `n` (winding picked from the normal, Unity's convention).
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td,
            Vector3 n, Color32 col, float glow, List<int> tris)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _uv.Add(ta); _uv.Add(tb); _uv.Add(tc); _uv.Add(td);
            for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
            while (_uv2.Count < _v.Count) _uv2.Add(new Vector3(glow, 1f, 0f));
            var cr = Vector3.Cross(b - a, c - a);
            if (cr.sqrMagnitude < 1e-12f) cr = Vector3.Cross(c - a, d - a);
            if (Vector3.Dot(cr, n) >= 0) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
            if (_twoSided)
            {
                // the back side too, with its own normal (Minecraft draws armour layers without culling)
                int j = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                _uv.Add(ta); _uv.Add(tb); _uv.Add(tc); _uv.Add(td);
                for (int k = 0; k < 4; k++) { _n.Add(-n); _c.Add(col); }
                while (_uv2.Count < _v.Count) _uv2.Add(new Vector3(glow, 1f, 0f));
                if (Vector3.Dot(cr, n) >= 0) { tris.Add(j); tris.Add(j + 2); tris.Add(j + 1); tris.Add(j); tris.Add(j + 3); tris.Add(j + 2); }
                else { tris.Add(j); tris.Add(j + 1); tris.Add(j + 2); tris.Add(j); tris.Add(j + 2); tris.Add(j + 3); }
            }
        }
    }
}
