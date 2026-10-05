using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.UI
{
    /// The little Hearthian in the suit HUD (HUDCanvas._healthDisplayImage, tinted by health) drawn
    /// as Steve: the front of every part of the skin, both layers, flattened into one figure in
    /// grey so the HUD's own health colouring still works on it. Swapped back in Outer Wilds mode.
    public sealed class SuitFigure
    {
        private UnityEngine.UI.Image _image;
        private Sprite _original, _steve;
        private bool _applied;
        private float _nextLook;

        public void Clear()
        {
            _image = null;
            _original = null;
            _applied = false;
        }

        public void Update(bool minecraft)
        {
            if (_image == null)
            {
                if (Time.unscaledTime < _nextLook) return;
                _nextLook = Time.unscaledTime + 2f;
                var hud = Object.FindObjectOfType<HUDCanvas>();
                if (hud == null || hud._healthDisplayImage == null) return;
                _image = hud._healthDisplayImage;
                _original = _image.sprite;
                _applied = false;
            }
            if (minecraft == _applied) return;
            if (minecraft)
            {
                if (_steve == null) _steve = Make();
                if (_steve == null) return;
                _image.sprite = _steve;
            }
            else _image.sprite = _original;
            _applied = minecraft;
        }

        /// Line art in the style of the suit's own figure: Steve's front silhouette, each part
        /// (head, body, arms, legs) outlined on its own so the seams show, a faint fill and two eyes.
        /// Built from the part layout, so it fits any skin; drawn white for the HUD to tint.
        private static Sprite Make()
        {
            const int S = 12;                    // texture pixels per skin pixel
            const int PadX = 8, PadY = 9;        // keeps the figure inside the gauge ring like the Hearthian
            const int W = (16 + PadX * 2) * S, H = (32 + PadY * 2) * S;
            var id = new byte[W * H];
            void Part(byte k, int x, int y, int w, int h)
            {
                for (int j = (PadY + y) * S; j < (PadY + y + h) * S; j++)
                    for (int i = (PadX + x) * S; i < (PadX + x + w) * S; i++)
                        id[j * W + i] = k;
            }
            Part(1, 4, 0, 8, 8);     // head
            Part(2, 4, 8, 8, 12);    // body
            Part(3, 0, 8, 4, 12);    // right arm (viewer's left)
            Part(4, 12, 8, 4, 12);   // left arm
            Part(5, 4, 20, 4, 12);   // right leg
            Part(6, 8, 20, 4, 12);   // left leg

            const int Line = 5;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    byte k = id[y * W + x];
                    if (k == 0) continue;
                    bool edge = false;
                    for (int dy = -Line; dy <= Line && !edge; dy++)
                        for (int dx = -Line; dx <= Line; dx++)
                        {
                            if (dx * dx + dy * dy > Line * Line) continue;
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= W || yy >= H || id[yy * W + xx] != k) { edge = true; break; }
                        }
                    px[(H - 1 - y) * W + x] = edge ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 40);
                }
            // eyes: two 2x1 blocks on the face, Steve-style
            void Fill(int x, int y, int w, int h)
            {
                for (int j = (PadY + y) * S + 2; j < (PadY + y + h) * S - 2; j++)
                    for (int i = (PadX + x) * S + 2; i < (PadX + x + w) * S - 2; i++)
                        px[(H - 1 - j) * W + i] = new Color32(255, 255, 255, 255);
            }
            Fill(4 + 1, 4, 2, 1);
            Fill(4 + 5, 4, 2, 1);

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
