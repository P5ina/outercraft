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
    /// (ascii, accented, nonlatin_european: Latin, Cyrillic, Greek...) packed into one texture.
    /// Outer Wilds keeps laying its text out with its own font (so boxes, wrapping and scrolling
    /// stay right: a font made at runtime can't even be given a line height); its glyphs are made
    /// transparent and Minecraft's are drawn over them, each word in Minecraft's own spacing fitted
    /// to the width the game gave it. Rich-text colours and bold come along.
    public static class McFont
    {
        internal struct Glyph
        {
            public int Code;
            public Rect Uv;           // in the packed texture
            public float Width;       // drawn width, Minecraft pixels
            public float Advance;     // Minecraft pixels
            public float Top, Height; // above the baseline, Minecraft pixels
        }

        private static readonly List<Glyph> Glyphs = new List<Glyph>();
        private static readonly Dictionary<int, Glyph> ByCode = new Dictionary<int, Glyph>();
        private static Texture2D _tex;
        private static Material _mat;
        private static bool _loaded, _failed;

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
            foreach (var g in Glyphs) ByCode[g.Code] = g;
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

        // ---------------------------------------------------------------- texts

        private static readonly List<McTextLayer> Layers = new List<McTextLayer>();
        private static readonly Dictionary<Text, McTextLayer> Seen = new Dictionary<Text, McTextLayer>();
        private static float _next;
        private static bool _on, _reported;

        internal static Texture2D Texture => _tex;
        internal static bool TryGlyph(int code, out Glyph g) => ByCode.TryGetValue(code, out g) || ByCode.TryGetValue('?', out g);

        public static void Update(bool enabled)
        {
            if (!_loaded || _failed) return;
            if (enabled != _on)
            {
                _on = enabled;
                foreach (var l in Layers) if (l != null) l.SetOn(enabled);
            }
            if (!enabled) return;

            Layers.RemoveAll(l => l == null);
            foreach (var l in Layers) l.Follow();

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            int found = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (t == null || !t.gameObject.scene.IsValid()) continue;
                found++;
                Attach(t);
            }
            if (!_reported)
            {
                _reported = true;
                OuterCraft.Log($"font: Minecraft glyphs over {found} UI texts");
            }
        }

        private static McTextLayer Attach(Text t)
        {
            if (Seen.TryGetValue(t, out var have) && have != null) return have;
            var layer = McTextLayer.Create(t);
            layer.SetOn(_on);
            Layers.Add(layer);
            Seen[t] = layer;
            return layer;
        }

        // Harmony hooks: a text gets its layer the moment it's enabled (not at the next scan, which
        // showed new prompts in the game's font first), and the layer is rebuilt in the same pass
        // as its text (not a frame later).
        public static void TextOnEnablePostfix(Text __instance)
        {
            if (!_on || !_loaded || _failed || __instance == null || !__instance.gameObject.scene.IsValid()) return;
            if (__instance.GetComponent<McTextLayer>() != null) return;
            Attach(__instance);
        }

        public static void SetVerticesDirtyPostfix(Graphic __instance)
        {
            if (!_on || !(__instance is Text t)) return;
            if (Seen.TryGetValue(t, out var layer) && layer != null) layer.SetVerticesDirty();
        }

        /// A new scene: the old texts are gone.
        public static void Forget()
        {
            Layers.Clear();
            Seen.Clear();
            _reported = false;
        }
    }

    /// Makes a Text's own glyphs transparent while the Minecraft layer draws them.
    public sealed class McTextHider : BaseMeshEffect
    {
        public bool On;
        public bool Changed;
        private readonly List<UIVertex> _verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            Changed = true;
            if (!IsActive() || !On) return;
            var text = graphic as Text;
            if (text == null || !McTextLayer.Mappable(text)) return;
            _verts.Clear();
            vh.GetUIVertexStream(_verts);
            for (int i = 0; i < _verts.Count; i++)
            {
                var v = _verts[i];
                v.color.a = 0;
                _verts[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(_verts);
        }
    }

    /// Minecraft's glyphs where the Text put its own, over the same rect.
    public sealed class McTextLayer : MaskableGraphic
    {
        private Text _text;
        private McTextHider _hider;
        private Color _lastColor;
        private bool _on;

        public static McTextLayer Create(Text text)
        {
            var go = new GameObject("OuterCraft_McText", typeof(RectTransform));
            go.layer = text.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(text.rectTransform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.pivot = text.rectTransform.pivot;
            var layer = go.AddComponent<McTextLayer>();
            layer._text = text;
            layer.raycastTarget = false;
            layer._hider = text.GetComponent<McTextHider>() ?? text.gameObject.AddComponent<McTextHider>();
            return layer;
        }

        public override Texture mainTexture => McFont.Texture != null ? McFont.Texture : s_WhiteTexture;

        public void SetOn(bool on)
        {
            _on = on;
            if (_hider != null) { _hider.On = on; }
            if (_text != null) _text.SetVerticesDirty();
            SetVerticesDirty();
        }

        /// Every frame: the game fades and hides its texts in ways a child doesn't inherit.
        public void Follow()
        {
            if (_text == null) return;
            bool show = _on && _text.isActiveAndEnabled;
            if (enabled != show) enabled = show;
            if (!show) return;
            canvasRenderer.SetAlpha(_text.canvasRenderer.GetAlpha());
            if (_hider != null && _hider.Changed) { _hider.Changed = false; SetVerticesDirty(); }
            else if (_text.color != _lastColor) SetVerticesDirty();
            _lastColor = _text.color;
        }

        /// The generator gives one entry per character of the string (tags included).
        public static bool Mappable(Text t)
        {
            var s = t.text;
            if (string.IsNullOrEmpty(s)) return false;
            var g = t.cachedTextGenerator;
            return g.characterCount >= s.Length && g.lineCount > 0;
        }

        private static readonly System.Text.RegularExpressions.Regex Tag =
            new System.Text.RegularExpressions.Regex(@"\G<(/?)(b|i|size|color|material|quad)(=[^>]*)?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_on || _text == null || McFont.Texture == null || string.IsNullOrEmpty(_text.text)) return;
            var str = _text.text;
            // lay the text out now, whichever of the two rebuilds first (cached when unchanged)
            var gen = _text.cachedTextGenerator;
            gen.PopulateWithErrors(str, _text.GetGenerationSettings(_text.rectTransform.rect.size), _text.gameObject);
            if (!Mappable(_text)) return;
            var chars = gen.characters;
            var lines = gen.lines;
            float upp = 1f / Mathf.Max(0.0001f, _text.pixelsPerUnit);
            float size = (_text.resizeTextForBestFit ? gen.fontSizeUsedForBestFit / Mathf.Max(0.0001f, _text.pixelsPerUnit) : _text.fontSize);
            if (size <= 0) size = 14;
            float p = size / 10f;                       // one Minecraft pixel; caps come out about the game's size
            bool[] runes = _text.GetComponent<RuneHider>()?.Mask;

            // per character: hidden (a tag), colour, bold
            int n = str.Length;
            var hidden = new bool[n];
            var col = new Color[n];
            var bold = new bool[n];
            var colors = new Stack<Color>();
            colors.Push(_text.color);
            int bolds = _text.fontStyle == FontStyle.Bold || _text.fontStyle == FontStyle.BoldAndItalic ? 1 : 0;
            for (int i = 0; i < n; i++)
            {
                if (_text.supportRichText && str[i] == '<')
                {
                    var m = Tag.Match(str, i);
                    if (m.Success)
                    {
                        bool close = m.Groups[1].Value == "/";
                        var name = m.Groups[2].Value.ToLowerInvariant();
                        if (name == "color")
                        {
                            if (close) { if (colors.Count > 1) colors.Pop(); }
                            else
                            {
                                var arg = m.Groups[3].Value.TrimStart('=').Trim('"');
                                colors.Push(ColorUtility.TryParseHtmlString(arg, out var c) ? new Color(c.r, c.g, c.b, c.a * _text.color.a) : colors.Peek());
                            }
                        }
                        else if (name == "b") bolds = Mathf.Max(0, bolds + (close ? -1 : 1));
                        for (int k = 0; k < m.Length; k++) hidden[i + k] = true;
                        i += m.Length - 1;
                        continue;
                    }
                }
                col[i] = colors.Peek();
                bold[i] = bolds > 0;
            }

            // whole lines in Minecraft's own proportions, aligned like the game's text, squeezed
            // only when they'd run wider than the game's line (translator text decodes word by
            // word, so it keeps to the words' own places)
            if (runes == null)
            {
                LayLines(vh, str, chars, lines, hidden, col, bold, p, upp);
                return;
            }
            int line = 0;
            int a = 0;
            while (a < n)
            {
                if (hidden[a] || char.IsWhiteSpace(str[a]) || (runes != null && a < runes.Length && runes[a])) { a++; continue; }
                while (line + 1 < lines.Count && lines[line + 1].startCharIdx <= a) line++;
                int lineEnd = line + 1 < lines.Count ? lines[line + 1].startCharIdx : int.MaxValue;
                int b = a;
                int last = a;
                while (b + 1 < n && b + 1 < lineEnd && !char.IsWhiteSpace(str[b + 1]) && !(runes != null && b + 1 < runes.Length && runes[b + 1]))
                {
                    b++;
                    if (!hidden[b]) last = b;
                }
                float x0 = chars[a].cursorPos.x, x1 = chars[last].cursorPos.x + chars[last].charWidth;
                float natural = 0f;
                for (int i = a; i <= last; i++) if (!hidden[i] && McFont.TryGlyph(str[i], out var g)) natural += g.Advance;
                natural = Mathf.Max(1f, natural - 1f) * p / upp;
                float k = Mathf.Clamp((x1 - x0) / natural, 0.5f, 2f);
                var li = lines[line];
                float baseline = (li.topY - li.height * 0.78f) * upp;
                float x = x0 * upp;
                for (int i = a; i <= last; i++)
                {
                    if (hidden[i] || !McFont.TryGlyph(str[i], out var g)) continue;
                    float w = g.Width * p * k;
                    if (w > 0f)
                    {
                        float top = baseline + g.Top * p, bottom = baseline + (g.Top - g.Height) * p;
                        Quad(vh, x, bottom, x + w, top, g.Uv, col[i]);
                        if (bold[i]) Quad(vh, x + p * k, bottom, x + w + p * k, top, g.Uv, col[i]);
                    }
                    x += g.Advance * p * k;
                }
                a = b + 1;
            }
        }

        private void LayLines(VertexHelper vh, string str, IList<UICharInfo> chars, IList<UILineInfo> lines,
            bool[] hidden, Color[] col, bool[] bold, float p, float upp)
        {
            int n = str.Length;
            var align = _text.alignment;
            bool center = align == TextAnchor.UpperCenter || align == TextAnchor.MiddleCenter || align == TextAnchor.LowerCenter;
            bool right = align == TextAnchor.UpperRight || align == TextAnchor.MiddleRight || align == TextAnchor.LowerRight;
            for (int l = 0; l < lines.Count; l++)
            {
                int start = lines[l].startCharIdx, end = l + 1 < lines.Count ? lines[l + 1].startCharIdx : n;
                end = Mathf.Min(end, n);
                int first = -1, last = -1;
                for (int i = start; i < end; i++)
                    if (!hidden[i] && !char.IsWhiteSpace(str[i])) { if (first < 0) first = i; last = i; }
                if (first < 0) continue;
                float x0 = chars[first].cursorPos.x * upp, x1 = (chars[last].cursorPos.x + chars[last].charWidth) * upp;
                float natural = 0f;
                for (int i = first; i <= last; i++)
                {
                    if (hidden[i]) continue;
                    if (McFont.TryGlyph(str[i], out var g)) natural += g.Advance * p;
                }
                natural = Mathf.Max(p, natural - p);
                float span = Mathf.Max(p, x1 - x0);
                float k = natural > span * 1.08f ? span * 1.08f / natural : 1f;
                float w = natural * k;
                float x = center ? (x0 + x1) / 2f - w / 2f : right ? x1 - w : x0;
                var li = lines[l];
                float baseline = (li.topY - li.height * 0.78f) * upp;
                for (int i = first; i <= last; i++)
                {
                    if (hidden[i] || !McFont.TryGlyph(str[i], out var g)) continue;
                    float gw = g.Width * p * k;
                    if (gw > 0f && !char.IsWhiteSpace(str[i]))
                    {
                        float top = baseline + g.Top * p, bottom = baseline + (g.Top - g.Height) * p;
                        Quad(vh, x, bottom, x + gw, top, g.Uv, col[i]);
                        if (bold[i]) Quad(vh, x + p * k, bottom, x + gw + p * k, top, g.Uv, col[i]);
                    }
                    x += g.Advance * p * k;
                }
            }
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Rect uv, Color c)
        {
            int i = vh.currentVertCount;
            Color32 cc = c;
            vh.AddVert(new Vector3(x0, y0), cc, new Vector2(uv.xMin, uv.yMin));
            vh.AddVert(new Vector3(x0, y1), cc, new Vector2(uv.xMin, uv.yMax));
            vh.AddVert(new Vector3(x1, y1), cc, new Vector2(uv.xMax, uv.yMax));
            vh.AddVert(new Vector3(x1, y0), cc, new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
