using UnityEngine;

namespace OuterCraft.Assets
{
    /// Draws a baked model the way Minecraft draws a 3D item in a slot: the model's "gui" display
    /// transform, orthographic, 16 GUI pixels (rendered at 4x), shaded like Minecraft's item lights.
    /// Done on the CPU once at load, into a sprite.
    public static class IconRaster
    {
        private const int S = 64;

        public static Image Render(BakedModel model, ModelDisplay gui, Image atlas, Color32 tint)
        {
            var img = new Image(S, S);
            var depth = new float[S * S];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.NegativeInfinity;

            // ItemRenderer: translate(t/16), rotate XYZ, scale, translate(-0.5); Minecraft is right-handed
            // with the camera looking down -z, y up.
            var m = McModels.RhRotX(gui.R.x) * McModels.RhRotY(gui.R.y) * McModels.RhRotZ(gui.R.z);
            var full = Matrix4x4.Translate(gui.T / 16f) * m * Matrix4x4.Scale(gui.S) * Matrix4x4.Translate(-Vector3.one * 0.5f);

            foreach (var q in model.Quads)
            {
                var n = m.MultiplyVector(q.N).normalized;
                if (n.z <= 1e-4f) continue; // facing away from the viewer
                float shade = Shade(n);
                var col = q.Tint ? tint : new Color32(255, 255, 255, 255);
                var p = new[] { full.MultiplyPoint3x4(q.P0), full.MultiplyPoint3x4(q.P1), full.MultiplyPoint3x4(q.P2), full.MultiplyPoint3x4(q.P3) };
                var t = new[] { q.T0, q.T1, q.T2, q.T3 };
                Tri(img, depth, atlas, p[0], p[1], p[2], t[0], t[1], t[2], shade, col);
                Tri(img, depth, atlas, p[0], p[2], p[3], t[0], t[2], t[3], shade, col);
            }
            return img;
        }

        /// Minecraft's two item lights, roughly: tops full, the left face a bit darker, the right darker still.
        private static float Shade(Vector3 n)
        {
            if (n.y > 0.5f) return 1f;
            if (n.y < -0.5f) return 0.5f;
            return n.x < 0f ? 0.8f : 0.6f;
        }

        private static Vector2 Screen(Vector3 p) => new Vector2(S * 0.5f + p.x * S, S * 0.5f - p.y * S);

        private static void Tri(Image img, float[] depth, Image atlas, Vector3 a, Vector3 b, Vector3 c,
            Vector2 ta, Vector2 tb, Vector2 tc, float shade, Color32 tint)
        {
            var sa = Screen(a); var sb = Screen(b); var sc = Screen(c);
            float area = (sb.x - sa.x) * (sc.y - sa.y) - (sb.y - sa.y) * (sc.x - sa.x);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sa.x, Mathf.Min(sb.x, sc.x))));
            int x1 = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(sa.x, Mathf.Max(sb.x, sc.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sa.y, Mathf.Min(sb.y, sc.y))));
            int y1 = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(sa.y, Mathf.Max(sb.y, sc.y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((sb.x - px) * (sc.y - py) - (sb.y - py) * (sc.x - px)) / area;
                    float w1 = ((sc.x - px) * (sa.y - py) - (sc.y - py) * (sa.x - px)) / area;
                    float w2 = 1f - w0 - w1;
                    const float e = -1e-4f;
                    if (w0 < e || w1 < e || w2 < e) continue;
                    float z = a.z * w0 + b.z * w1 + c.z * w2;
                    int idx = y * S + x;
                    if (z <= depth[idx]) continue;
                    var uv = ta * w0 + tb * w1 + tc * w2;
                    int ax = Mathf.Clamp((int)(uv.x * atlas.W), 0, atlas.W - 1);
                    int ay = Mathf.Clamp((int)((1f - uv.y) * atlas.H), 0, atlas.H - 1);
                    var t = atlas[ax, ay];
                    if (t.a < 128) continue;
                    depth[idx] = z;
                    img[x, y] = new Color32((byte)(t.r * shade * tint.r / 255f), (byte)(t.g * shade * tint.g / 255f), (byte)(t.b * shade * tint.b / 255f), 255);
                }
        }
    }
}
