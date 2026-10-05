using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;
using UnityEngine.UI;

namespace OuterCraft.UI
{
    /// The translator reads Nomai as the enchanting table writes: words not yet translated show in
    /// Minecraft's Standard Galactic Alphabet (the real words, letter for letter, so it can be read
    /// back), and each turns into plain text as the translator gets to it.
    ///
    /// The text field gets the whole translation, laid out once, so nothing reflows as words decode.
    /// The letters of untranslated words are hidden in its mesh and drawn again as SGA glyphs by a
    /// second graphic over the same rect.
    public sealed class NomaiRunes
    {
        private NomaiTranslatorProp _prop;
        private float _nextFind;
        private RuneHider _hider;
        private RuneLayer _layer;
        private string _lastSet;
        private bool _active;

        public void Forget()
        {
            _prop = null;
            _hider = null;
            _layer = null;
            _lastSet = null;
            _active = false;
        }

        public void LateUpdate(bool enabled)
        {
            if (McAssets.Sga == null) return;
            if (_prop == null)
            {
                if (Time.unscaledTime < _nextFind) return;
                _nextFind = Time.unscaledTime + 1f;
                _prop = Object.FindObjectOfType<NomaiTranslatorProp>();
                if (_prop == null) return;
            }
            var field = _prop._textField;
            if (field == null) return;

            string text = null;
            bool[] mask = enabled ? Build(out text) : null;
            if (mask != null && (field.text == _lastSet || !string.IsNullOrEmpty(field.text)))
            {
                if (_hider == null) _hider = field.gameObject.GetComponent<RuneHider>() ?? field.gameObject.AddComponent<RuneHider>();
                if (_layer == null) _layer = RuneLayer.Create(field);
                if (field.text != text) field.text = text;
                _lastSet = text;
                _hider.Mask = mask;
                _layer.Mask = mask;
                _layer.Color = field.color;
                field.SetVerticesDirty();
                _layer.SetVerticesDirty();
                _active = true;
            }
            else if (_active)
            {
                _active = false;
                if (_hider != null) _hider.Mask = null;
                if (_layer != null) { _layer.Mask = null; _layer.SetVerticesDirty(); }
                field.SetVerticesDirty();
            }
        }

        /// The full translation, and which of its characters are letters of words not yet translated.
        private bool[] Build(out string text)
        {
            text = _prop._translatedText;
            var words = _prop._listDisplayWords;
            if (string.IsNullOrEmpty(text) || words == null || words.Count == 0 || text.IndexOf('<') >= 0) return null;
            var mask = new bool[text.Length];
            bool any = false;
            foreach (var w in words)
            {
                if (w == null) return null;
                int a = w.StartPosition, b = w.EndPosition;
                var t = w.TranslatedText ?? "";
                // the positions must match the text (end exclusive or inclusive): otherwise leave it be
                if (a < 0 || a > text.Length) return null;
                int len = Mathf.Min(t.Length, text.Length - a);
                if (string.CompareOrdinal(text, a, t, 0, len) != 0 || len != t.Length || (b != a + len && b != a + len - 1)) return null;
                if (w.IsTranslated()) continue;
                for (int i = a; i < a + len; i++)
                    if (char.IsLetter(text[i])) { mask[i] = true; any = true; }
            }
            return any ? mask : new bool[text.Length];
        }

        // ---------------------------------------------------------------- geometry shared by both

        /// Each run of masked letters on one line: its characters and its box, in the text's local space.
        internal static List<(int start, int end, Rect box)> Runs(Text text, bool[] mask)
        {
            var runs = new List<(int, int, Rect)>();
            if (mask == null) return runs;
            var gen = text.cachedTextGenerator;
            var chars = gen.characters;
            var lines = gen.lines;
            if (chars == null || lines == null || lines.Count == 0 || chars.Count < mask.Length) return runs;
            float upp = 1f / Mathf.Max(0.0001f, text.pixelsPerUnit);
            int i = 0, line = 0;
            while (i < mask.Length)
            {
                if (!mask[i]) { i++; continue; }
                while (line + 1 < lines.Count && lines[line + 1].startCharIdx <= i) line++;
                int j = i;
                int lineEnd = line + 1 < lines.Count ? lines[line + 1].startCharIdx : int.MaxValue;
                while (j + 1 < mask.Length && mask[j + 1] && j + 1 < lineEnd) j++;
                float x0 = chars[i].cursorPos.x, x1 = chars[j].cursorPos.x + chars[j].charWidth;
                var l = lines[line];
                float top = l.topY, bottom = l.topY - l.height;
                runs.Add((i, j, Rect.MinMaxRect(x0 * upp, bottom * upp, x1 * upp, top * upp)));
                i = j + 1;
            }
            return runs;
        }
    }

    /// Hides the letters of untranslated words in the translator's own text mesh.
    public sealed class RuneHider : BaseMeshEffect
    {
        public bool[] Mask;
        private readonly List<UIVertex> _verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mask == null) return;
            var text = graphic as Text;
            if (text == null) return;
            var runs = NomaiRunes.Runs(text, Mask);
            if (runs.Count == 0) return;
            _verts.Clear();
            vh.GetUIVertexStream(_verts);
            // triangles in sixes: one glyph quad each; hide those whose centre is in a rune run
            for (int q = 0; q + 5 < _verts.Count; q += 6)
            {
                var c = Vector3.zero;
                for (int k = 0; k < 6; k++) c += _verts[q + k].position;
                c /= 6f;
                bool hide = false;
                foreach (var r in runs)
                    if (r.box.Contains(new Vector2(c.x, c.y))) { hide = true; break; }
                if (!hide) continue;
                for (int k = 0; k < 6; k++)
                {
                    var v = _verts[q + k];
                    v.color.a = 0;
                    _verts[q + k] = v;
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(_verts);
        }
    }

    /// The same runs drawn in the Standard Galactic Alphabet, over the text.
    public sealed class RuneLayer : MaskableGraphic
    {
        public bool[] Mask;
        public Color Color = Color.white;
        private Text _text;

        public static RuneLayer Create(Text text)
        {
            var go = new GameObject("OuterCraft_Runes", typeof(RectTransform));
            go.layer = text.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(text.rectTransform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.pivot = text.rectTransform.pivot;
            var layer = go.AddComponent<RuneLayer>();
            layer._text = text;
            layer.raycastTarget = false;
            return layer;
        }

        public override Texture mainTexture => McAssets.Sga != null ? McAssets.Sga : s_WhiteTexture;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_text == null || Mask == null || McAssets.Sga == null) return;
            var str = _text.text;
            if (str == null || str.Length < Mask.Length) return;
            var col = (Color32)Color;
            foreach (var (start, end, box) in NomaiRunes.Runs(_text, Mask))
            {
                // Minecraft's metrics: 8px cells, 7 above the baseline; laid out at their own widths
                // and then fitted to the width the real word takes
                float s = box.height * 0.62f / 8f;      // one SGA pixel
                float natural = 0f;
                for (int i = start; i <= end; i++) natural += McAssets.SgaWidth[Rune(str[i])];
                natural = Mathf.Max(1f, natural - 1f) * s;
                float k = box.width / natural;
                float baseline = box.yMin + box.height * 0.24f;
                float x = box.xMin;
                for (int i = start; i <= end; i++)
                {
                    int c = Rune(str[i]);
                    float adv = McAssets.SgaWidth[c] * s * k;
                    float u = (c % 16) / 16f, v = 1f - (c / 16 + 1) / 16f;
                    float x0 = x, x1 = x + 8f * s * k;
                    float y1 = baseline + 7f * s, y0 = y1 - 8f * s;
                    int n = vh.currentVertCount;
                    vh.AddVert(new Vector3(x0, y0), col, new Vector2(u, v));
                    vh.AddVert(new Vector3(x0, y1), col, new Vector2(u, v + 1f / 16f));
                    vh.AddVert(new Vector3(x1, y1), col, new Vector2(u + 1f / 16f, v + 1f / 16f));
                    vh.AddVert(new Vector3(x1, y0), col, new Vector2(u + 1f / 16f, v));
                    vh.AddTriangle(n, n + 1, n + 2);
                    vh.AddTriangle(n + 2, n + 3, n);
                    x += adv;
                }
            }
        }

        /// Latin letters are themselves in SGA; any other letter gets one by its code.
        private static int Rune(char ch)
        {
            if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z') return ch;
            int i = ch % 26;
            return char.IsUpper(ch) ? 'A' + i : 'a' + i;
        }
    }
}
