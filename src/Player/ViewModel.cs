using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.Player
{
    /// Minecraft's first-person hand, done the way Minecraft does it:
    /// - the exact pose-stack math of ItemInHandRenderer (renderPlayerArm / applyItemArmTransform /
    ///   applyItemArmAttackTransform, the block model's firstperson_righthand display transform),
    ///   evaluated in Minecraft's right-handed camera space and mirrored into Unity's;
    /// - the hand lagging behind the mouse (xBob / yBob) and view bobbing while walking;
    /// - a real object in the world, lit by Outer Wilds' sun, shadows and lamps.
    public sealed class ViewModel
    {
        private Camera _main;
        private Transform _root;
        private Mesh _mesh;
        private Material _skinMat, _atlasMat, _glowMat;
        private MeshRenderer _renderer;

        private float _swingStart = -10f;
        private const float SwingTime = 0.3f;   // 6 ticks
        private const float EquipTime = 0.15f;  // height drops 0.4 per tick, ~3 ticks each way

        // xBob / yBob: the hand trails the camera a little
        private float _yaw, _pitch, _yawBob, _pitchBob;
        private Vector3 _lastFwd;
        private bool _haveFwd;

        // view bob
        private float _walkDist, _bob;
        public float WalkDist => _walkDist; // Minecraft's moveDist: a footstep every whole number
        public bool ViewBobbing = true;
        private bool _bobApplied, _viewMatrixSet;
        private Vector3 _savedPos;
        private Quaternion _savedRot;

        public void Swing()
        {
            float now = Time.unscaledTime;
            if (now - _swingStart > SwingTime * 0.5f) _swingStart = now;
        }

        // ================================================================ frame

        /// 0: first person, 1: third person from behind, 2: from the front (F5).
        public int Perspective;
        public bool Sneaking;
        private const float SneakDrop = 1.62f - 1.27f; // Minecraft's standing and crouching eye heights
        private float _sneakOffset;
        private bool _thirdThisFrame;
        public float Speed { get; private set; }          // m/s over the ground
        public float Pitch => _pitch;                     // Minecraft's pitch, degrees, + looking down
        public float SwingProgress
        {
            get { float s = (Time.unscaledTime - _swingStart) / SwingTime; return s < 0 || s >= 1 ? 0 : s; }
        }

        public void Update(Camera main, ItemDef held, float heldChangedAt, bool show)
        {
            if (main == null) return;
            if (_main != main) Build(main);
            bool third = show && Perspective > 0;
            _thirdThisFrame = third;
            _root.gameObject.SetActive(show && !third);
            _bobEnabledThisFrame = show && !third && ViewBobbing;
            if (!show) { _sneakOffset = 0f; return; }

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float ticks = dt * 20f;
            _sneakOffset += ((Sneaking ? SneakDrop : 0f) - _sneakOffset) * (1f - Mathf.Pow(0.5f, ticks));
            UpdateLook(main, ticks);
            UpdateWalk(ticks);
            if (third) return;

            float now = Time.unscaledTime;
            float swing = (now - _swingStart) / SwingTime;
            if (swing < 0 || swing >= 1) swing = 0;
            float equip = 1f - Mathf.Clamp01(Mathf.Abs(now - heldChangedAt - EquipTime) / EquipTime);
            // equip: 0 normally, rises to 1 at the bottom of the switch dip
            if (now - heldChangedAt > 2 * EquipTime) equip = 0;

            // ---- Minecraft's pose stack, in its own camera space (x right, y up, -z forward)
            var m = Mat.Identity;
            // (view bob: comes from the camera's view matrix, which the hand renders with too)
            m = m * Mat.RotX((_pitch - _pitchBob) * 0.1f) * Mat.RotY((_yaw - _yawBob) * 0.1f);

            _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear(); _norms.Clear(); _uv2s.Clear();
            _fovScale = Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(35f * Mathf.Deg2Rad);
            _glow = held != null ? held.Light / 15f : 0f;
            if (held == null || held.Model == null)
            {
                Arm(m, swing, equip);
                _renderer.sharedMaterial = _skinMat;
            }
            else
            {
                Item(m, held, swing, equip);
                _renderer.sharedMaterial = held.Light > 0 ? (_glowMat != null ? _glowMat : (_glowMat = World.BlockMaterials.ForAtlas(true))) : _atlasMat;
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_cols);
            _mesh.SetNormals(_norms);
            _mesh.SetUVs(1, _uv2s);
            _mesh.SetTriangles(_tris, 0, true);
        }

        // ---------------------------------------------------------------- look lag (LocalPlayer.xBob / yBob)

        private void UpdateLook(Camera main, float ticks)
        {
            var body = Locator.GetPlayerBody();
            var camCtl = Locator.GetPlayerCameraController();
            if (body == null) return;
            // Yaw: how far the body turned around its up axis since last frame.
            var up = body.transform.up;
            var fwd = Vector3.ProjectOnPlane(body.transform.forward, up).normalized;
            if (_haveFwd) _yaw += Vector3.SignedAngle(_lastFwd, fwd, up);
            _lastFwd = fwd;
            _haveFwd = true;
            // Minecraft's pitch is positive looking down.
            _pitch = camCtl != null ? -camCtl.GetDegreesY() : 0f;
            float k = 1f - Mathf.Pow(0.5f, ticks); // bob += (rot - bob) * 0.5 per tick
            _yawBob += (_yaw - _yawBob) * k;
            _pitchBob += (_pitch - _pitchBob) * k;
            // keep numbers small
            if (Mathf.Abs(_yaw) > 3600f) { _yawBob -= _yaw; _yaw = 0; }
        }

        // ---------------------------------------------------------------- view bob (GameRenderer.bobView)

        private Vector3 _lastLocal;
        private OWRigidbody _lastGround;

        /// Horizontal speed measured ourselves, relative to the ground we stand on (Outer Wilds'
        /// planets move and spin, so world-space velocity is meaningless here).
        private void UpdateWalk(float ticks)
        {
            var c = Locator.GetPlayerController();
            var body = Locator.GetPlayerBody();
            if (c == null || body == null || ticks <= 0f) return;
            var ground = c.GetLastGroundBody();
            float speed = 0f;
            if (ground != null)
            {
                var local = ground.transform.InverseTransformPoint(body.GetPosition());
                if (ground == _lastGround)
                {
                    var moved = ground.transform.TransformVector(local - _lastLocal);
                    moved = Vector3.ProjectOnPlane(moved, body.transform.up);
                    speed = moved.magnitude / (ticks / 20f); // m/s
                    if (speed > 30f) speed = 0f;              // a teleport or a respawn, not walking
                }
                _lastLocal = local;
                _lastGround = ground;
            }
            bool grounded = c.IsGrounded();
            Speed = speed;
            _walkDist += speed / 20f * 0.6f * ticks;                 // walkDist += blocks moved * 0.6 per tick
            float target = grounded ? Mathf.Min(0.1f, speed / 20f) : 0f;
            _bob += (target - _bob) * (1f - Mathf.Pow(0.6f, ticks)); // bob += (f - bob) * 0.4 per tick
        }

        private Mat BobView(Mat m)
        {
            float f1 = -_walkDist;
            float f2 = _bob;
            m = m * Mat.Translate(Mathf.Sin(f1 * Mathf.PI) * f2 * 0.5f, -Mathf.Abs(Mathf.Cos(f1 * Mathf.PI) * f2), 0f);
            m = m * Mat.RotZ(Mathf.Sin(f1 * Mathf.PI) * f2 * 3f);
            m = m * Mat.RotX(Mathf.Abs(Mathf.Cos(f1 * Mathf.PI - 0.2f) * f2) * 5f);
            return m;
        }

        /// The same bob for the world: applied to the main camera just for its render.
        private bool _bobEnabledThisFrame;

        private void OnPreCull(Camera cam)
        {
            if (cam != _main || _bobApplied || !(_bobEnabledThisFrame || _thirdThisFrame || _sneakOffset > 0.001f)) return;
            var t = cam.transform;
            _savedPos = t.localPosition;
            _savedRot = t.localRotation;
            // sneaking: the eyes come down (1.62 -> 1.27 blocks), eased half the way per tick
            var bodyUp = Locator.GetPlayerBody() != null ? Locator.GetPlayerBody().transform.up : t.up;
            var shift = t.parent != null ? t.parent.InverseTransformDirection(-bodyUp) * _sneakOffset : -bodyUp * _sneakOffset;
            if (_thirdThisFrame)
            {
                // GameRenderer / Camera.setup: 4 blocks behind (or in front, looking back), pulled in
                // short of walls
                var eye = t.position - bodyUp * _sneakOffset;
                var fwd = t.forward;
                var dir = Perspective == 1 ? -fwd : fwd;
                float dist = 4f;
                if (Physics.SphereCast(eye, 0.1f, dir, out var hit, dist, OWLayerMask.physicalMask, QueryTriggerInteraction.Ignore))
                    dist = Mathf.Max(0.3f, hit.distance - 0.05f);
                t.position = eye + dir * dist;
                if (Perspective == 2) t.rotation = Quaternion.LookRotation(-fwd, t.up);
                _bobApplied = true;
                return;
            }
            // Minecraft moves the world by B in view space. Done on the view matrix only, not the
            // camera's transform: the game's HUD markers (worked out from the transform) then stay
            // steady instead of shaking with every step. The hand, a child of the camera, gets the
            // same B from the view matrix.
            var local = Matrix4x4.TRS(_savedPos + shift, _savedRot, Vector3.one);
            if (_bobEnabledThisFrame) local *= BobView(Mat.Identity).ToUnity().inverse;
            var world = t.parent != null ? t.parent.localToWorldMatrix * local : local;
            cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * world.inverse;
            _viewMatrixSet = true;
            _bobApplied = true;
        }

        private void OnPostRender(Camera cam)
        {
            if (cam != _main || !_bobApplied) return;
            if (_viewMatrixSet) { cam.ResetWorldToCameraMatrix(); _viewMatrixSet = false; }
            else
            {
                cam.transform.localPosition = _savedPos;
                cam.transform.localRotation = _savedRot;
            }
            _bobApplied = false;
        }

        // ---------------------------------------------------------------- arm (renderPlayerArm)

        private void Arm(Mat m, float swing, float equip)
        {
            const float f = 1f; // right arm
            float f1 = Mathf.Sqrt(swing);
            float f2 = -0.3f * Mathf.Sin(f1 * Mathf.PI);
            float f3 = 0.4f * Mathf.Sin(f1 * Mathf.PI * 2f);
            float f4 = -0.4f * Mathf.Sin(swing * Mathf.PI);
            m = m * Mat.Translate(f * (f2 + 0.64000005f), f3 - 0.6f + equip * -0.6f, f4 - 0.71999997f);
            m = m * Mat.RotY(f * 45f);
            float f5 = Mathf.Sin(swing * swing * Mathf.PI);
            float f6 = Mathf.Sin(f1 * Mathf.PI);
            m = m * Mat.RotY(f * f6 * 70f);
            m = m * Mat.RotZ(f * f5 * -20f);
            m = m * Mat.Translate(f * -1f, 3.6f, 3.5f);
            m = m * Mat.RotZ(f * 120f);
            m = m * Mat.RotX(200f);
            m = m * Mat.RotY(f * -135f);
            m = m * Mat.Translate(f * 5.6f, 0f, 0f);
            // PlayerModel.rightArm: pivot (-5, 2, 0), box (-3, -2, -2) size 4 x 12 x 4, skin uv (40, 16); 1/16 units
            m = m * Mat.Translate(-5f / 16f, 2f / 16f, 0f);
            Box(m, -3, -2, -2, 4, 12, 4, 40, 16, 64f, 64f, 1f / 16f);
        }

        /// A Minecraft ModelPart cube with its standard box UV unwrap.
        private void Box(Mat m, float x0, float y0, float z0, float dx, float dy, float dz, float u, float v, float texW, float texH, float unit)
        {
            McBox.Build(x0, y0, z0, dx, dy, dz, u, v, 0f, false, (p, t, n) =>
            {
                Vector3 P(Vector3 q) => m.MulPoint(q.x * unit, q.y * unit, q.z * unit);
                Vector2 T(Vector2 q) => new Vector2(q.x / texW, 1f - q.y / texH);
                Quad(P(p[0]), P(p[1]), P(p[2]), P(p[3]), T(t[0]), T(t[1]), T(t[2]), T(t[3]), m.MulDir(n.x, n.y, n.z));
            });
        }

        // ---------------------------------------------------------------- held block (renderArmWithItem)

        private void Item(Mat m, ItemDef item, float swing, float equip)
        {
            const float i = 1f;
            float s = Mathf.Sqrt(swing);
            m = m * Mat.Translate(i * -0.4f * Mathf.Sin(s * Mathf.PI), 0.2f * Mathf.Sin(s * Mathf.PI * 2f), -0.2f * Mathf.Sin(swing * Mathf.PI));
            // applyItemArmTransform
            m = m * Mat.Translate(i * 0.56f, -0.52f + equip * -0.6f, -0.72f);
            // applyItemArmAttackTransform
            float f = Mathf.Sin(swing * swing * Mathf.PI);
            m = m * Mat.RotY(i * (45f + f * -20f));
            float f1 = Mathf.Sin(s * Mathf.PI);
            m = m * Mat.RotZ(i * f1 * -20f);
            m = m * Mat.RotX(f1 * -80f);
            m = m * Mat.RotY(i * -45f);
            // the model's display.firstperson_righthand: translation (1/16), rotation XYZ, scale
            var d = item.Model.Get("firstperson_righthand");
            m = m * Mat.Translate(d.T.x / 16f, d.T.y / 16f, d.T.z / 16f) * Mat.RotX(d.R.x) * Mat.RotY(d.R.y) * Mat.RotZ(d.R.z) * Mat.Scale(d.S.x, d.S.y, d.S.z);
            // ItemRenderer: the model's 0..1 cube centred
            m = m * Mat.Translate(-0.5f, -0.5f, -0.5f);
            Vector3 P(Vector3 p) => m.MulPoint(p.x, p.y, p.z);
            var white = new Color32(255, 255, 255, 255);
            foreach (var q in item.Model.Quads)
                Quad(P(q.P0), P(q.P1), P(q.P2), P(q.P3), q.T0, q.T1, q.T2, q.T3, m.MulDir(q.N.x, q.N.y, q.N.z), Shade.Apply(q.Tint ? item.TintColor : white, Shade.Of(q.N)));
        }

        // ---------------------------------------------------------------- output

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Color32> _cols = new List<Color32>();
        private readonly List<int> _tris = new List<int>();

        /// A quad given in Minecraft camera space, mirrored into Unity's camera space (z flips). It is
        /// a real mesh in the world, lit by Outer Wilds' sun, shadows and lamps; faces turned away
        /// from the eye are dropped (convex shapes, so nothing is lost).
        ///
        /// Minecraft draws the hand with a fixed 70 degree FOV; we draw it with the game camera, so x
        /// and y are scaled by tan(fov/2) / tan(35) to land on the same spot of the screen.
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 normal)
            => Quad(a, b, c, d, ua, ub, uc, ud, normal, new Color32(255, 255, 255, 255));

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 normal, Color32 color)
        {
            var n = normal.normalized;
            var centre = (a + b + c + d) * 0.25f;
            if (Vector3.Dot(n, centre) >= 0f) return; // facing away from the eye at the origin
            float k = _fovScale;
            int i0 = _verts.Count;
            foreach (var p in new[] { a, b, c, d }) _verts.Add(new Vector3(p.x * k, p.y * k, -p.z));
            var nu = new Vector3(n.x / k, n.y / k, -n.z).normalized;
            for (int i = 0; i < 4; i++)
            {
                _norms.Add(nu);
                _cols.Add(color);
                _uv2s.Add(new Vector3(_glow, 1f, 0f));
            }
            _uvs.Add(ua); _uvs.Add(ub); _uvs.Add(uc); _uvs.Add(ud);
            var ua3 = _verts[i0]; var ub3 = _verts[i0 + 1]; var uc3 = _verts[i0 + 2];
            bool flip = Vector3.Dot(Vector3.Cross(ub3 - ua3, uc3 - ua3), nu) < 0;
            if (!flip) { _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2); _tris.Add(i0); _tris.Add(i0 + 2); _tris.Add(i0 + 3); }
            else { _tris.Add(i0); _tris.Add(i0 + 2); _tris.Add(i0 + 1); _tris.Add(i0); _tris.Add(i0 + 3); _tris.Add(i0 + 2); }
        }

        private readonly List<Vector3> _norms = new List<Vector3>();
        private readonly List<Vector3> _uv2s = new List<Vector3>();
        private float _fovScale = 1f;
        private float _glow;

        // ================================================================ setup

        public void Destroy()
        {
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
            if (_bobApplied && _main != null) OnPostRender(_main);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _main = null;
            _haveFwd = false;
        }

        private void Build(Camera main)
        {
            Destroy();
            _main = main;

            // A real object in the world, glued to the game camera: Outer Wilds' own sun, shadows,
            // lamps and fog light it, exactly like the blocks you place.
            _root = new GameObject("OuterCraft_ViewModel").transform;
            _root.SetParent(main.transform, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.gameObject.layer = 0;
            _mesh = new Mesh { name = "hand" };
            _mesh.MarkDynamic();
            _root.gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _root.gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // Minecraft's hand casts none
            _renderer.receiveShadows = true;

            _skinMat = World.BlockMaterials.ForTexture(McAssets.Skin != null ? (Texture)McAssets.Skin : Texture2D.whiteTexture);
            _atlasMat = World.BlockMaterials.ForAtlas();

            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
        }
    }

    /// Minimal right-handed 4x4 matrix with Minecraft PoseStack semantics (each op multiplies on the right).
    public struct Mat
    {
        private float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23;

        public static Mat Identity => new Mat { m00 = 1, m11 = 1, m22 = 1 };

        public static Mat operator *(Mat a, Mat b) => new Mat
        {
            m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20,
            m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21,
            m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
            m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03,
            m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20,
            m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21,
            m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
            m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13,
            m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20,
            m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21,
            m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22,
            m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23,
        };

        public static Mat Translate(float x, float y, float z) => new Mat { m00 = 1, m11 = 1, m22 = 1, m03 = x, m13 = y, m23 = z };
        public static Mat Scale(float s) => new Mat { m00 = s, m11 = s, m22 = s };
        public static Mat Scale(float x, float y, float z) => new Mat { m00 = x, m11 = y, m22 = z };

        public static Mat RotX(float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Mat { m00 = 1, m11 = c, m12 = -s, m21 = s, m22 = c };
        }

        public static Mat RotY(float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Mat { m00 = c, m02 = s, m11 = 1, m20 = -s, m22 = c };
        }

        public static Mat RotZ(float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Mat { m00 = c, m01 = -s, m10 = s, m11 = c, m22 = 1 };
        }

        public Vector3 MulPoint(float x, float y, float z) => new Vector3(
            m00 * x + m01 * y + m02 * z + m03, m10 * x + m11 * y + m12 * z + m13, m20 * x + m21 * y + m22 * z + m23);

        public Vector3 MulDir(float x, float y, float z) => new Vector3(
            m00 * x + m01 * y + m02 * z, m10 * x + m11 * y + m12 * z, m20 * x + m21 * y + m22 * z);

        /// The same transform in Unity's left-handed space (z mirrored on both sides).
        public Matrix4x4 ToUnity()
        {
            var u = Matrix4x4.identity;
            u.m00 = m00; u.m01 = m01; u.m02 = -m02; u.m03 = m03;
            u.m10 = m10; u.m11 = m11; u.m12 = -m12; u.m13 = m13;
            u.m20 = -m20; u.m21 = -m21; u.m22 = m22; u.m23 = -m23;
            return u;
        }
    }
}
