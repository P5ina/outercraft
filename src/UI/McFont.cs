using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OuterCraft.Assets;
using UnityEngine;
using UnityEngine.UI;
using Image = OuterCraft.Assets.Image;

namespace OuterCraft.UI
{
    /// Outer Wilds' UI text in Minecraft's font: the bitmap providers of the jar's default font
    /// (ascii, accented, nonlatin_european: Latin, Cyrillic, Greek...) packed into one texture and
    /// turned into Unity bitmap fonts, one per text size, since a bitmap font can't be scaled.
    /// Every UI Text in the scene gets the font of its size in Minecraft mode and its own back
    /// outside it. Layout keeps close to the original: Minecraft's line height of 9 per 8.
    public static class McFont
    {
        private struct Glyph
        {
            public int Code;
            public Rect Uv;           // in the packed texture
            public float Width;       // drawn width, Minecraft pixels
            public float Advance;     // Minecraft pixels
            public float Top, Height; // above the baseline, Minecraft pixels
        }

        private static readonly List<Glyph> Glyphs = new List<Glyph>();
        private static Texture2D _tex;
        private static Material _mat;
        private static bool _loaded, _failed;
        private static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();
        private static readonly Dictionary<Font, float> BaseSpacing = new Dictionary<Font, float>();
        private static readonly HashSet<Font> Ours = new HashSet<Font>();

        // ---------------------------------------------------------------- loading (FontManager)

        public static void Load(McJar jar)
        {
            if (_loaded) return;
            _loaded = true;
            var raw = jar.Read("assets/minecraft/font/include/default.json") ?? jar.Read("assets/minecraft/font/default.json");
            JObject js;
            try { js = raw != null ? JObject.Parse(System.Text.Encoding.UTF8.GetString(raw)) : new JObject(); }
            catch { js = new JObject(); }
            var providers = (js["providers"] as JArray)?.OfType<JObject>().Where(p => (string)p["type"] == "bitmap").ToList();
            if (providers == null || providers.Count == 0)
            {
                // older jars: ascii.png alone
                providers = new List<JObject> { JObject.Parse("{\"file\":\"minecraft:font/ascii.png\",\"ascent\":7,\"chars\":[]}") };
            }

            var images = new List<(Image img, JObject p)>();
            foreach (var p in providers)
            {
                var file = McModels.Norm((string)p["file"]);
                var img = jar.LoadImage("assets/minecraft/textures/" + file, false);
                if (img != null) images.Add((img, p));
            }
            if (images.Count == 0) { _failed = true; return; }

            int w = images.Max(i => i.img.W), h = images.Sum(i => i.img.H);
            var atlas = new Image(w, h);
            int y0 = 0;
            var seen = new HashSet<int>();
            foreach (var (img, p) in images)
            {
                for (int y = 0; y < img.H; y++)
                    for (int x = 0; x < img.W; x++)
                        atlas[x, y0 + y] = img[x, y];
                var rows = (p["chars"] as JArray)?.Select(r => (string)r).ToList();
                if (rows == null || rows.Count == 0) rows = AsciiRows();
                int cols = rows[0].Length;
                int cw = img.W / cols, ch = img.H / rows.Count;
                float height = p["height"] != null ? (float)p["height"] : 8f;
                float ascent = (float)(p["ascent"] ?? 7);
                float scale = height / ch;
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < rows[r].Length && c < cols; c++)
                    {
                        int code = rows[r][c];
                        if (code == 0 || !seen.Add(code)) continue; // the first provider with a glyph wins
                        int gx = c * cw, gy = r * ch;
                        int right = -1;
                        for (int x = cw - 1; x >= 0 && right < 0; x--)
                            for (int y = 0; y < ch; y++)
                                if (img[gx + x, gy + y].a > 0) { right = x; break; }
                        float width = code == ' ' ? 0f : (right + 1) * scale;
                        float adv = code == ' ' ? 4f : Mathf.Floor(0.5f + width) + 1f;
                        Glyphs.Add(new Glyph
                        {
                            Code = code,
                            Uv = new Rect(gx / (float)w, 1f - (y0 + gy + ch) / (float)h, (right + 1) / (float)w, ch / (float)h),
                            Width = width,
                            Advance = adv,
                            Top = ascent,
                            Height = height,
                        });
                    }
                y0 += img.H;
            }
            if (!seen.Contains(' ')) Glyphs.Add(new Glyph { Code = ' ', Advance = 4f, Top = 7, Height = 8 });
            _tex = atlas.ToTexture();
            _mat = new Material(Shader.Find("UI/Default")) { mainTexture = _tex, name = "OuterCraft_Font" };
            OuterCraft.Log($"Minecraft font: {Glyphs.Count} glyphs");
        }

        private static List<string> AsciiRows()
        {
            var rows = new List<string>();
            for (int r = 0; r < 16; r++)
            {
                var sb = new System.Text.StringBuilder();
                for (int c = 0; c < 16; c++) sb.Append((char)(r * 16 + c));
                rows.Add(sb.ToString());
            }
            return rows;
        }

        // ---------------------------------------------------------------- fonts per size

        /// A bitmap font drawing Minecraft's glyphs at `size` pixels per 8 Minecraft pixels, with
        /// glyphs placed under the line top the way the text generator really lays them out.
        private static Font FontFor(int size)
        {
            if (Fonts.TryGetValue(size, out var f)) return f;
            float px = size / 8f;
            f = new Font("Minecraft " + size) { material = _mat };
            f.characterInfo = Infos(px, 0f);
            // the generator's real line spacing and where it puts a glyph, for this font
            var gen = new TextGenerator();
            var st = new TextGenerationSettings
            {
                font = f, fontSize = 0, lineSpacing = 1f, richText = false, scaleFactor = 1f, color = Color.white,
                generationExtents = new Vector2(10000, 10000), pivot = new Vector2(0, 1), textAnchor = TextAnchor.UpperLeft,
                horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow, updateBounds = true,
                generateOutOfBounds = true,
            };
            gen.Populate("A\nA", st);
            float spacing = 0f, shift = 0f;
            if (gen.lineCount >= 2) spacing = Mathf.Abs(gen.lines[0].topY - gen.lines[1].topY);
            if (gen.vertexCount >= 4 && gen.lineCount >= 1)
            {
                var v = gen.verts;
                float top = Mathf.Max(v[0].position.y, v[1].position.y, v[2].position.y, v[3].position.y);
                // the cell top of 'A' (ascent 7 above the baseline) should sit half a pixel row under the line top
                shift = gen.lines[0].topY - top - 0.5f * px;
            }
            if (Mathf.Abs(shift) > 0.01f) f.characterInfo = Infos(px, shift);
            Fonts[size] = f;
            BaseSpacing[f] = spacing;
            Ours.Add(f);
            return f;
        }

        private static CharacterInfo[] Infos(float px, float shift)
        {
            var infos = new CharacterInfo[Glyphs.Count];
            for (int i = 0; i < Glyphs.Count; i++)
            {
                var g = Glyphs[i];
                var ci = new CharacterInfo { index = g.Code, advance = Mathf.RoundToInt(g.Advance * px) };
                ci.uvBottomLeft = new Vector2(g.Uv.xMin, g.Uv.yMin);
                ci.uvBottomRight = new Vector2(g.Uv.xMax, g.Uv.yMin);
                ci.uvTopLeft = new Vector2(g.Uv.xMin, g.Uv.yMax);
                ci.uvTopRight = new Vector2(g.Uv.xMax, g.Uv.yMax);
                ci.minX = 0;
                ci.maxX = Mathf.RoundToInt(g.Width * px);
                ci.maxY = Mathf.RoundToInt(g.Top * px + shift);
                ci.minY = Mathf.RoundToInt((g.Top - g.Height) * px + shift);
                infos[i] = ci;
            }
            return infos;
        }

        // ---------------------------------------------------------------- swapping

        private struct Saved
        {
            public Font Font;
            public int Size;
            public FontStyle Style;
            public float LineSpacing;
            public bool BestFit;
        }

        private static readonly Dictionary<Text, Saved> Swapped = new Dictionary<Text, Saved>();
        private static float _next;
        private static bool _on, _reported;

        public static void Update(bool enabled)
        {
            if (!_loaded || _failed) return;
            if (!enabled)
            {
                if (_on) RestoreAll();
                _on = false;
                return;
            }
            _on = true;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            int found = 0, swapped = 0, noSpacing = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (t != null && t.gameObject.scene.IsValid()) found++;
                if (t == null || !t.gameObject.scene.IsValid()) continue;
                var font = t.font;
                if (font != null && Ours.Contains(font)) continue;
                // new, or the game set its own font again (the translator does per language)
                int size = t.fontSize > 0 ? t.fontSize : 14;
                if (t.resizeTextForBestFit) size = Mathf.Max(t.resizeTextMinSize, Mathf.Min(t.resizeTextMaxSize, size));
                var mc = FontFor(size);
                float baseSpacing = BaseSpacing.TryGetValue(mc, out var b) ? b : 0f;
                if (baseSpacing < 0.01f) { noSpacing++; continue; } // can't lay out lines with it
                swapped++;
                Swapped[t] = new Saved { Font = font, Size = t.fontSize, Style = t.fontStyle, LineSpacing = t.lineSpacing, BestFit = t.resizeTextForBestFit };
                // a bitmap font has one size and no styles: those settings only produce warnings
                t.fontSize = 0;
                t.fontStyle = FontStyle.Normal;
                t.resizeTextForBestFit = false;
                t.lineSpacing = t.lineSpacing * size * 1.125f / baseSpacing;
                t.font = mc;
            }
            if (!_reported)
            {
                _reported = true;
                var tmp = System.Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
                int tmps = 0;
                if (tmp != null) foreach (var o in Resources.FindObjectsOfTypeAll(tmp)) if (o is Component c && c.gameObject.scene.IsValid()) tmps++;
                float sp = 0f;
                foreach (var kv in BaseSpacing) { sp = kv.Value; break; }
                OuterCraft.Log($"font: {found} UI texts, {swapped} swapped, {noSpacing} without line spacing (base {sp}), {tmps} TextMeshPro texts");
            }
        }

        private static void RestoreAll()
        {
            foreach (var kv in Swapped)
            {
                var t = kv.Key;
                if (t == null || !Ours.Contains(t.font)) continue;
                var s = kv.Value;
                t.font = s.Font;
                t.fontSize = s.Size;
                t.fontStyle = s.Style;
                t.lineSpacing = s.LineSpacing;
                t.resizeTextForBestFit = s.BestFit;
            }
            Swapped.Clear();
        }

        /// A new scene: the old texts are gone.
        public static void Forget()
        {
            _reported = false;
            Swapped.Clear();
            _on = false;
        }
    }
}
