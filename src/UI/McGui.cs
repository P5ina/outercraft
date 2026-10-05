using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.UI
{
    /// Drawing in Minecraft's GUI pixels: its auto GUI scale, sprites, bitmap font with drop shadow,
    /// item icons with stack counts.
    public static class McGui
    {
        public static int ConfiguredScale; // 0 = auto
        public static int S { get; private set; } = 2;
        public static float W => Screen.width / (float)S;
        public static float H => Screen.height / (float)S;

        public static void Begin()
        {
            if (ConfiguredScale > 0) { S = ConfiguredScale; return; }
            // Minecraft "Auto": the biggest scale that still leaves 320 x 240 GUI pixels.
            int s = 1;
            while (Screen.width / (s + 1) >= 320 && Screen.height / (s + 1) >= 240) s++;
            S = s;
        }

        public static Vector2 Mouse => Event.current != null ? Event.current.mousePosition / S : Vector2.zero;

        public static void Sprite(string name, float x, float y, float w, float h)
        {
            if (McAssets.Gui.TryGetValue(name, out var tex)) Tex(tex, x, y, w, h);
        }

        public static void Tex(Texture tex, float x, float y, float w, float h) =>
            GUI.DrawTexture(new Rect(Mathf.Round(x * S), Mathf.Round(y * S), w * S, h * S), tex, ScaleMode.StretchToFill, true);

        /// Part of a texture: (u, v, w, h) in its pixels, top-left origin, like Minecraft's blit.
        public static void Blit(Texture tex, float x, float y, float u, float v, float w, float h)
        {
            var r = new Rect(u / tex.width, 1f - (v + h) / tex.height, w / tex.width, h / tex.height);
            GUI.DrawTextureWithTexCoords(new Rect(Mathf.Round(x * S), Mathf.Round(y * S), w * S, h * S), tex, r, true);
        }

        private static Texture2D _white;

        public static void Fill(float x, float y, float w, float h, Color c)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(Mathf.Round(x * S), Mathf.Round(y * S), Mathf.Round(w * S), Mathf.Round(h * S)), _white);
            GUI.color = old;
        }

        public static float TextWidth(string s)
        {
            float w = 0;
            foreach (char c in s) w += c < 256 ? McAssets.GlyphWidth[c] : 6;
            return w - 1;
        }

        public static void Text(string s, float x, float y, Color color, bool shadow = true)
        {
            var font = McAssets.Font;
            if (font == null) return;
            var old = GUI.color;
            if (shadow)
            {
                // Minecraft's drop shadow: the same text a pixel down-right at a quarter brightness.
                GUI.color = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, color.a);
                Glyphs(font, s, x + 1, y + 1);
            }
            GUI.color = color;
            Glyphs(font, s, x, y);
            GUI.color = old;
        }

        private static void Glyphs(Texture2D font, string s, float x, float y)
        {
            foreach (char ch in s)
            {
                int c = ch < 256 ? ch : '?';
                if (c != ' ')
                {
                    float u = (c % 16) / 16f, v = 1f - (c / 16 + 1) / 16f;
                    GUI.DrawTextureWithTexCoords(new Rect(Mathf.Round(x * S), Mathf.Round(y * S), 8 * S, 8 * S), font,
                        new Rect(u, v, 1f / 16f, 1f / 16f), true);
                }
                x += McAssets.GlyphWidth[c];
            }
        }

        /// GuiGraphics.renderItem + renderItemDecorations: the 16x16 icon and the count, right-aligned.
        public static void Item(ItemStack s, float x, float y)
        {
            if (s == null || s.Empty) return;
            if (s.Item.Icon != null) Tex(s.Item.Icon, x, y, 16, 16);
            if (s.Count > 1)
            {
                var t = s.Count.ToString();
                Text(t, x + 19 - 2 - TextWidth(t) - 1, y + 6 + 3, Color.white);
            }
        }

        /// Minecraft's tooltip: near-black box with a purple frame, the name in white.
        public static void Tooltip(string text, float mx, float my)
        {
            float w = TextWidth(text), h = 8;
            float x = mx + 12, y = my - 12;
            if (x + w + 4 > W) x = mx - 16 - w;
            var bg = new Color(0x10 / 255f, 0, 0x10 / 255f, 0xF0 / 255f);
            Fill(x - 3, y - 4, w + 6, 1, bg);
            Fill(x - 3, y + h + 3, w + 6, 1, bg);
            Fill(x - 3, y - 3, w + 6, h + 6, bg);
            Fill(x - 4, y - 3, 1, h + 6, bg);
            Fill(x + w + 3, y - 3, 1, h + 6, bg);
            var top = new Color(0x50 / 255f, 0, 1f, 0x50 / 255f);
            var bot = new Color(0x28 / 255f, 0, 0x7F / 255f, 0x50 / 255f);
            Fill(x - 3, y - 3, w + 6, 1, top);
            Fill(x - 3, y + h + 2, w + 6, 1, bot);
            Fill(x - 3, y - 2, 1, h + 4, Color.Lerp(top, bot, 0.5f));
            Fill(x + w + 2, y - 2, 1, h + 4, Color.Lerp(top, bot, 0.5f));
            Text(text, x, y, Color.white);
        }
    }
}
