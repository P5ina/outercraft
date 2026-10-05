using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OuterCraft.Assets
{
    /// Everything we take from the Minecraft jar, loaded once: block atlas (with biome tints baked
    /// in), isometric hotbar icons, HUD sprites and the player skin.
    public static class McAssets
    {
        public static bool Loaded { get; private set; }
        public static string Source { get; private set; }

        public static Texture2D Atlas;
        public static Texture2D Skin;
        public static Texture2D Wings;
        public static Texture2D Font;
        public static readonly int[] GlyphWidth = new int[256];
        public static Texture2D Sga;
        public static Texture2D EndCrystal;
        public static bool SlimArms;
        public static Texture2D SpyglassScope;
        public static readonly int[] SgaWidth = new int[256];
        public static bool SkinSlim;
        public static readonly Dictionary<string, Texture2D> Gui = new Dictionary<string, Texture2D>();

        public static Color32 GrassTint = new Color32(0x91, 0xBD, 0x59, 255);
        public static Color32 FoliageTint = new Color32(0x77, 0xAB, 0x2F, 255);
        private static readonly Color32 BirchTint = new Color32(0x80, 0xA7, 0x55, 255);
        private static readonly Color32 SpruceTint = new Color32(0x61, 0x99, 0x61, 255);

        private const int Tile = 16;

        public static readonly string[] GuiSprites =
        {
            "hud/hotbar", "hud/hotbar_selection", "hud/crosshair",
            "hud/heart/container", "hud/heart/full", "hud/heart/half",
            "hud/food_empty", "hud/food_full", "hud/food_half",
            "hud/air", "hud/air_empty", "hud/experience_bar_background",
            "toast/advancement", "widget/button", "widget/button_highlighted",
        };

        public static readonly string[] Containers =
        {
            "inventory", "crafting_table", "creative_inventory/tab_items", "creative_inventory/tab_inventory",
        };
        public static readonly Dictionary<string, Texture2D> Container = new Dictionary<string, Texture2D>();
        public static readonly Dictionary<string, Rect> TexUv = new Dictionary<string, Rect>();
        private static readonly Dictionary<string, Image> TexImg = new Dictionary<string, Image>();
        public static Rect[] DestroyStage = new Rect[10];
        private static Image _atlasImg;

        public static bool Load(string configuredJar, string skinName)
        {
            using (var jar = McJar.Find(configuredJar))
            {
                if (jar == null)
                {
                    OuterCraft.Log("no Minecraft client jar found: set minecraftJar in the mod settings");
                    return false;
                }
                Source = jar.Path;
                LoadTints(jar);
                Items.Build();
                var models = new McModels(jar);
                var extra = CollectModels(jar, models);
                BuildAtlas(jar, extra);
                BakeModels(models);
                LoadPortalFrames(jar);
                foreach (var s in GuiSprites)
                {
                    var img = jar.Gui(s);
                    if (img != null) Gui[s] = img.ToTexture();
                }
                foreach (var c in Containers)
                {
                    var img = jar.LoadImage($"assets/minecraft/textures/gui/container/{c}.png", false);
                    if (img != null) Container[c] = img.ToTexture();
                }
                foreach (var sp in new[] { "container/slot_highlight_back", "container/slot_highlight_front",
                             "container/creative_inventory/scroller", "container/creative_inventory/tab_top_selected_1",
                             "container/creative_inventory/tab_top_unselected_1",
                             "container/slot/helmet", "container/slot/chestplate", "container/slot/leggings", "container/slot/boots" })
                {
                    var img = jar.Gui(sp);
                    if (img != null) Gui[sp] = img.ToTexture();
                }
                Recipes.Load(jar);
                var skin = jar.LoadImage($"assets/minecraft/textures/entity/player/wide/{skinName}.png", false)
                           ?? jar.LoadImage("assets/minecraft/textures/entity/player/wide/steve.png", false);
                Skin = skin?.ToTexture();
                Wings = (jar.LoadImage("assets/minecraft/textures/entity/equipment/wings/elytra.png", false)
                         ?? jar.LoadImage("assets/minecraft/textures/entity/elytra.png", false))?.ToTexture();   // before 1.21.2
                SpyglassScope = jar.LoadImage("assets/minecraft/textures/misc/spyglass_scope.png", false)?.ToTexture(FilterMode.Bilinear);
                EndCrystal = jar.LoadImage("assets/minecraft/textures/entity/end_crystal/end_crystal.png", false)?.ToTexture();
                var font = jar.LoadImage("assets/minecraft/textures/font/ascii.png", false);
                if (font != null)
                {
                    Font = font.ToTexture();
                    MeasureGlyphs(font);
                }
                UI.McFont.Load(jar);
                // the enchanting table's Standard Galactic Alphabet (font "minecraft:alt")
                var sga = jar.LoadImage("assets/minecraft/textures/font/ascii_sga.png", false);
                if (sga != null)
                {
                    Sga = sga.ToTexture();
                    MeasureGlyphs(sga, SgaWidth);
                }
            }
            Loaded = Atlas != null;
            OuterCraft.Log($"Minecraft textures loaded from {Source} ({Blocks.All.Count} blocks)");
            return Loaded;
        }

        /// Plains biome colours from Minecraft's own colour maps (temperature 0.8, downfall 0.4).
        private static void LoadTints(McJar jar)
        {
            Color32 Sample(string map, Color32 fallback)
            {
                var img = jar.LoadImage($"assets/minecraft/textures/colormap/{map}.png", false);
                if (img == null) return fallback;
                float t = 0.8f, d = 0.4f * t;
                int x = (int)((1f - t) * 255f), y = (int)((1f - d) * 255f);
                var c = img[Mathf.Clamp(x, 0, img.W - 1), Mathf.Clamp(y, 0, img.H - 1)];
                c.a = 255;
                return c;
            }
            GrassTint = Sample("grass", GrassTint);
            FoliageTint = Sample("foliage", FoliageTint);
        }

        public static Color32 TintColor(Tint t) => t switch
        {
            Tint.Grass => GrassTint,
            Tint.Foliage => FoliageTint,
            Tint.Birch => BirchTint,
            Tint.Spruce => SpruceTint,
            _ => new Color32(255, 255, 255, 255),
        };

        /// Minecraft's ascii.png: 16x16 glyphs; a glyph is as wide as its rightmost lit column (+1).
        private static void MeasureGlyphs(Image font) => MeasureGlyphs(font, GlyphWidth);

        private static void MeasureGlyphs(Image font, int[] widths)
        {
            int cell = font.W / 16;
            for (int ch = 0; ch < 256; ch++)
            {
                int gx = (ch % 16) * cell, gy = (ch / 16) * cell;
                int right = -1;
                for (int x = cell - 1; x >= 0 && right < 0; x--)
                    for (int y = 0; y < cell; y++)
                        if (font[gx + x, gy + y].a > 0) { right = x; break; }
                widths[ch] = ch == 32 ? cell / 2 : right + 2;
            }
        }

        // ---------------------------------------------------------------- atlas

        private static void BuildAtlas(McJar jar, HashSet<string> extra)
        {
            // Every distinct (texture, tint) pair gets one tile.
            var tiles = new List<Image>();
            var index = new Dictionary<string, int>();
            int TileFor(string tex, Tint tint, string overlay = null)
            {
                string key = tex + "|" + tint + "|" + overlay;
                if (index.TryGetValue(key, out int i)) return i;
                var img = jar.Block(tex) ?? Missing();
                if (img.W != Tile || img.H != Tile) img = Resize(img, Tile, Tile);
                if (tint != Tint.None) img = img.Tinted(TintColor(tint));
                if (overlay != null)
                {
                    var o = jar.Block(overlay);
                    if (o != null) img = img.Over(o.Tinted(GrassTint));
                }
                tiles.Add(img);
                index[key] = tiles.Count - 1;
                return tiles.Count - 1;
            }

            // Model, item and crack textures: untinted, by full name ("block/torch", "item/stick").
            var named = new Dictionary<string, int>();
            foreach (var name in extra)
            {
                var img = jar.LoadImage($"assets/minecraft/textures/{name}.png");
                if (img == null) continue;
                if (img.W != Tile || img.H != Tile) img = Resize(img, Tile, Tile);
                if (name.StartsWith("block/destroy_stage_")) img = CrackPixels(img);
                TexImg[name] = img;
                tiles.Add(img);
                named[name] = tiles.Count - 1;
            }

            var top = new int[Blocks.All.Count];
            var bottom = new int[Blocks.All.Count];
            var side = new int[Blocks.All.Count];
            for (int i = 0; i < Blocks.All.Count; i++)
            {
                var b = Blocks.All[i];
                top[i] = TileFor(b.Top, b.TopTint);
                bottom[i] = TileFor(b.Bottom, b.Top == b.Bottom ? b.TopTint : Tint.None);
                side[i] = TileFor(b.Side, b.SideTint, b.SideOverlay);
            }

            int cols = 16;
            int rows = Mathf.CeilToInt(tiles.Count / (float)cols);
            var atlas = new Image(cols * Tile, Mathf.NextPowerOfTwo(Mathf.Max(1, rows) * Tile));
            for (int t = 0; t < tiles.Count; t++)
            {
                int ox = (t % cols) * Tile, oy = (t / cols) * Tile;
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                        atlas[ox + x, oy + y] = tiles[t][x, y];
            }
            Atlas = atlas.ToTexture();
            _atlasImg = atlas;

            Rect UvOf(int t)
            {
                // Unity UVs: v = 0 at the bottom; our image rows count from the top.
                float u0 = (t % cols) * Tile / (float)atlas.W;
                float v1 = 1f - (t / cols) * Tile / (float)atlas.H;
                float du = Tile / (float)atlas.W, dv = Tile / (float)atlas.H;
                return new Rect(u0, v1 - dv, du, dv);
            }
            for (int i = 0; i < Blocks.All.Count; i++)
            {
                var b = Blocks.All[i];
                b.TopUv = UvOf(top[i]);
                b.BottomUv = UvOf(bottom[i]);
                b.SideUv = UvOf(side[i]);
                b.ParticleUv = b.Key == "grass_block" ? b.BottomUv : b.SideUv;
                _cubeIcons[b] = IsoIcon(tiles[top[i]], tiles[side[i]], tiles[side[i]]).ToTexture();
            }
            foreach (var kv in named) TexUv[kv.Key] = UvOf(kv.Value);
            for (int i = 0; i < 10; i++)
                if (TexUv.TryGetValue($"block/destroy_stage_{i}", out var r)) DestroyStage[i] = r;
        }

        private static readonly Dictionary<BlockDef, Texture2D> _cubeIcons = new Dictionary<BlockDef, Texture2D>();

        /// Minecraft multiplies the crack texture onto the block (x2); with cut-out shading we keep
        /// just the dark crack lines.
        private static Image CrackPixels(Image src)
        {
            var img = new Image(src.W, src.H);
            for (int i = 0; i < src.Px.Length; i++)
            {
                var p = src.Px[i];
                img.Px[i] = p.a > 128 && p.r < 128 ? new Color32(28, 28, 28, 255) : new Color32(0, 0, 0, 0);
            }
            return img;
        }

        // ---------------------------------------------------------------- JSON models

        private sealed class StateSpec
        {
            public List<string> Keys = new List<string>();
            public List<(McModels.Model m, int x, int y)> Variants = new List<(McModels.Model, int, int)>();
            public List<(JObject when, McModels.Model m, int x, int y)> Parts;
        }

        private static readonly Dictionary<BlockDef, StateSpec> _specs = new Dictionary<BlockDef, StateSpec>();
        private static readonly Dictionary<ItemDef, McModels.Model> _itemModels = new Dictionary<ItemDef, McModels.Model>();
        private static readonly Dictionary<ItemDef, List<(McModels.Model m, Vector3 off)>> _itemComposites = new Dictionary<ItemDef, List<(McModels.Model m, Vector3 off)>>();

        private static (McModels.Model, int, int) Apply(McModels models, JToken t)
        {
            if (t is JArray arr) t = arr.Count > 0 ? arr[0] : null; // weighted random models: the first
            if (!(t is JObject o)) return (null, 0, 0);
            return (models.Load((string)o["model"]), o["x"] != null ? (int)o["x"] : 0, o["y"] != null ? (int)o["y"] : 0);
        }

        /// Reads every blockstate and item model we need; returns the textures they use.
        private static HashSet<string> CollectModels(McJar jar, McModels models)
        {
            var tex = new HashSet<string>();
            for (int i = 0; i < 10; i++) tex.Add($"block/destroy_stage_{i}");
            for (int i = 0; i < 8; i++) { tex.Add($"particle/spark_{i}"); tex.Add($"particle/generic_{i}"); }
            foreach (var b in Blocks.All)
            {
                if (!b.IsModel) continue;
                var js = models.Json($"assets/minecraft/blockstates/{b.Key}.json");
                if (js == null) { OuterCraft.Log($"no blockstate for {b.Key}"); continue; }
                var spec = new StateSpec();
                if (js["variants"] is JObject variants)
                {
                    foreach (var kv in variants)
                    {
                        var v = Apply(models, kv.Value);
                        if (v.Item1 == null) continue;
                        spec.Keys.Add(kv.Key);
                        spec.Variants.Add(v);
                        foreach (var t in models.TexturesOf(v.Item1)) tex.Add(t);
                    }
                }
                else if (js["multipart"] is JArray parts)
                {
                    spec.Parts = new List<(JObject, McModels.Model, int, int)>();
                    foreach (var p in parts)
                    {
                        var v = Apply(models, p["apply"]);
                        if (v.Item1 == null) continue;
                        spec.Parts.Add((p["when"] as JObject, v.Item1, v.Item2, v.Item3));
                        foreach (var t in models.TexturesOf(v.Item1)) tex.Add(t);
                    }
                }
                _specs[b] = spec;
            }
            foreach (var item in Items.All)
            {
                if (item.Block != null && !item.Block.IsModel) continue; // cubes: built from the block
                string name = null;
                var ij = models.Json($"assets/minecraft/items/{item.Key}.json");
                if (ij?["model"] is JObject im && (string)im["type"] == "minecraft:model") name = (string)im["model"];
                // composite item models (beds since 26.1: head + foot one block apart)
                if (ij?["model"] is JObject cm && (string)cm["type"] == "minecraft:composite" && cm["models"] is JArray subs)
                {
                    var list = new List<(McModels.Model m, Vector3 off)>();
                    foreach (var sub in subs.OfType<JObject>())
                    {
                        if ((string)sub["type"] != "minecraft:model") continue;
                        var sm = models.Load((string)sub["model"]);
                        if (sm == null || sm.Elements == null) continue;
                        var off = Vector3.zero;
                        if (sub["transformation"]?["translation"] is JArray tr && tr.Count == 3)
                            off = new Vector3((float)tr[0], (float)tr[1], (float)tr[2]);
                        list.Add((sm, off));
                        foreach (var t in models.TexturesOf(sm)) tex.Add(t);
                    }
                    if (list.Count > 0) { _itemComposites[item] = list; continue; }
                }
                var m = models.Load(name ?? "item/" + item.Key);
                if (m == null || (!m.Generated && m.Elements == null)) continue;
                _itemModels[item] = m;
                foreach (var t in models.TexturesOf(m)) tex.Add(t);
            }
            return tex;
        }

        /// multipart "when": {"north": "true"}, {"north": "true|false"}, {"OR": [...]}, {"AND": [...]}
        private static Func<Func<string, string>, bool> Condition(JObject when)
        {
            if (when == null) return null;
            if (when["OR"] is JArray or)
            {
                var subs = or.OfType<JObject>().Select(Condition).ToList();
                return p => subs.Any(f => f == null || f(p));
            }
            if (when["AND"] is JArray and)
            {
                var subs = and.OfType<JObject>().Select(Condition).ToList();
                return p => subs.All(f => f == null || f(p));
            }
            var tests = when.Properties().Select(pr => (pr.Name, ((string)pr.Value).Split('|'))).ToList();
            return p => tests.All(t => Array.IndexOf(t.Item2, p(t.Name) ?? "false") >= 0);
        }

        private static void BakeModels(McModels models)
        {
            models.Uv = name => TexUv.TryGetValue(name, out var r) ? r : (Rect?)null;
            models.Pixels = name => TexImg.TryGetValue(name, out var i) ? i : null;

            foreach (var kv in _specs)
            {
                var b = kv.Key;
                var spec = kv.Value;
                b.TintColor = TintColor(b.SideTint);
                if (spec.Parts != null)
                {
                    b.Parts = spec.Parts.Select(p => (Condition(p.when), models.Bake(p.m, p.x, p.y))).ToList();
                    b.VariantKeys = new[] { "" };
                    b.States = new[] { b.ModelFor(0) };
                }
                else
                {
                    b.VariantKeys = spec.Keys.ToArray();
                    b.States = spec.Variants.Select(v => models.Bake(v.m, v.x, v.y)).ToArray();
                }
                b.DefaultState = 0;
                int def = -1;
                switch (b.Place)
                {
                    case PlaceKind.Stairs: def = b.Find(("facing", "north"), ("half", "bottom"), ("shape", "straight")); break;
                    case PlaceKind.Slab: def = b.Find(("type", "bottom")); break;
                    case PlaceKind.Door: def = b.Find(("facing", "north"), ("half", "lower"), ("hinge", "left"), ("open", "false")); break;
                    case PlaceKind.Lantern: def = b.Find(("hanging", "false")); break;
                    case PlaceKind.FacingAway: def = b.Find(("facing", "north"), ("lit", "false")); if (def < 0) def = b.Find(("facing", "north")); break;
                    case PlaceKind.WallAttached: def = b.Find(("facing", "north")); break;
                    case PlaceKind.Bed: def = b.Find(("facing", "north"), ("part", "foot")); break;
                }
                if (def >= 0) b.DefaultState = def;
                // particles: the model's "particle" texture
                var first = spec.Parts != null ? spec.Parts.FirstOrDefault().m : spec.Variants.FirstOrDefault().m;
                var ptex = first != null ? McModels.ResolveTex(first, "#particle") : null;
                if (ptex != null && TexUv.TryGetValue(ptex, out var pr)) b.ParticleUv = pr;
                else if (b.States.Length > 0 && b.States[0].Quads.Count > 0) b.ParticleUv = UvBounds(b.States[0].Quads[0]);
            }

            var blockDisplay = models.Load("block/block").Display;
            foreach (var item in Items.All.ToList())
            {
                var b = item.Block;
                if (b != null && !b.IsModel)
                {
                    item.Model = McModels.Cube(b, blockDisplay);
                    item.Icon = _cubeIcons.TryGetValue(b, out var ic) ? ic : null;
                    continue;
                }
                if (_itemComposites.TryGetValue(item, out var comp))
                {
                    var cmod = new BakedModel { Display = comp[0].m.Display };
                    foreach (var (sm, off) in comp)
                    {
                        var part = models.Bake(sm);
                        foreach (var q0 in part.Quads)
                        {
                            var q = q0;
                            q.P0 += off; q.P1 += off; q.P2 += off; q.P3 += off;
                            q.Cull = -1;
                            cmod.Quads.Add(q);
                        }
                        foreach (var bx in part.Boxes) cmod.Boxes.Add(new Bounds(bx.center + off, bx.size));
                    }
                    item.Model = cmod;
                    item.Icon = IconRaster.Render(cmod, cmod.Get("gui"), _atlasImg, item.TintColor).ToTexture();
                    continue;
                }
                if (!_itemModels.TryGetValue(item, out var m)) { Items.Remove(item); continue; }
                item.Model = models.Bake(m);
                if (item.Model.Quads.Count == 0) { Items.Remove(item); continue; }
                if (b != null) item.TintColor = b.TintColor;
                if (m.Generated)
                {
                    var l0 = McModels.ResolveTex(m, "#layer0");
                    item.Icon = l0 != null && TexImg.TryGetValue(l0, out var img) ? img.Tinted(item.TintColor).ToTexture() : null;
                }
                else
                    item.Icon = IconRaster.Render(item.Model, item.Model.Get("gui"), _atlasImg, item.TintColor).ToTexture();
            }
            OuterCraft.Log($"models baked: {_specs.Count} shaped blocks, {Items.All.Count} items");
        }

        // ---------------------------------------------------------------- the nether portal's animation

        public static Texture2D[] PortalFrames = new Texture2D[0];   // full frames, for the screen overlay
        private static Color32[][] _portalPx;                         // same frames, atlas row order
        private static int _portalX, _portalY, _portalFrame = -1;

        private static void LoadPortalFrames(McJar jar)
        {
            var full = jar.LoadImage("assets/minecraft/textures/block/nether_portal.png", false);
            if (full == null || full.W <= 0 || Atlas == null || !TexUv.TryGetValue("block/nether_portal", out var r)) return;
            int n = Math.Max(1, full.H / full.W);
            PortalFrames = new Texture2D[n];
            _portalPx = new Color32[n][];
            for (int f = 0; f < n; f++)
            {
                var img = new Image(full.W, full.W);
                for (int y = 0; y < full.W; y++)
                    for (int x = 0; x < full.W; x++)
                        img[x, y] = full[x, f * full.W + y];
                if (img.W != Tile) img = Resize(img, Tile, Tile);
                PortalFrames[f] = img.ToTexture();
                var px = new Color32[Tile * Tile];
                for (int y = 0; y < Tile; y++) Array.Copy(img.Px, y * Tile, px, (Tile - 1 - y) * Tile, Tile);
                _portalPx[f] = px;
            }
            _portalX = Mathf.RoundToInt(r.x * Atlas.width);
            _portalY = Mathf.RoundToInt(r.y * Atlas.height);
        }

        /// One frame per tick, like Minecraft's animated textures.
        public static void Animate(float time)
        {
            if (_portalPx == null || _portalPx.Length < 2 || Atlas == null) return;
            int f = (int)(time * 20f) % _portalPx.Length;
            if (f == _portalFrame) return;
            _portalFrame = f;
            Atlas.SetPixels32(_portalX, _portalY, Tile, Tile, _portalPx[f]);
            Atlas.Apply(false, false);
        }

        public static Texture2D PortalFrame(float time) =>
            PortalFrames.Length == 0 ? null : PortalFrames[(int)(time * 20f) % PortalFrames.Length];

        public static Rect Uv(string tex) => TexUv.TryGetValue(tex, out var r) ? r : new Rect(0, 0, 0, 0);

        private static Rect UvBounds(BQuad q)
        {
            float x0 = Mathf.Min(Mathf.Min(q.T0.x, q.T1.x), Mathf.Min(q.T2.x, q.T3.x));
            float x1 = Mathf.Max(Mathf.Max(q.T0.x, q.T1.x), Mathf.Max(q.T2.x, q.T3.x));
            float y0 = Mathf.Min(Mathf.Min(q.T0.y, q.T1.y), Mathf.Min(q.T2.y, q.T3.y));
            float y1 = Mathf.Max(Mathf.Max(q.T0.y, q.T1.y), Mathf.Max(q.T2.y, q.T3.y));
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static Image Missing()
        {
            var img = new Image(Tile, Tile);
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    img[x, y] = ((x / 8) + (y / 8)) % 2 == 0 ? new Color32(248, 0, 248, 255) : new Color32(0, 0, 0, 255);
            return img;
        }

        private static Image Resize(Image src, int w, int h)
        {
            var dst = new Image(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dst[x, y] = src[x * src.W / w, y * src.H / h];
            return dst;
        }

        // ---------------------------------------------------------------- isometric inventory icon

        /// Minecraft's inventory cube: top face full brightness, left side 0.8, right side 0.6,
        /// drawn into a 64x64 sprite (16 GUI pixels at 4x).
        private static Image IsoIcon(Image top, Image left, Image right)
        {
            const int S = 64;
            var img = new Image(S, S);
            // Corners of the projected cube (y grows downwards).
            var tTop = new Vector2(32, 3.5f);
            var tRight = new Vector2(60.5f, 17.5f);
            var tFront = new Vector2(32, 31.5f);
            var tLeft = new Vector2(3.5f, 17.5f);
            var down = new Vector2(0, 28f);

            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    // top: origin tLeft, s -> tTop, t -> tFront
                    if (Para(p, tLeft, tTop - tLeft, tFront - tLeft, out float s, out float t))
                        img[x, y] = Sample(top, s, t, 1f);
                    // left side: origin tLeft, s -> tFront, t -> down
                    else if (Para(p, tLeft, tFront - tLeft, down, out s, out t))
                        img[x, y] = Sample(left, s, t, 0.8f);
                    // right side: origin tFront, s -> tRight, t -> down
                    else if (Para(p, tFront, tRight - tFront, down, out s, out t))
                        img[x, y] = Sample(right, s, t, 0.6f);
                }
            return img;
        }

        private static bool Para(Vector2 p, Vector2 o, Vector2 e1, Vector2 e2, out float s, out float t)
        {
            float det = e1.x * e2.y - e1.y * e2.x;
            var d = p - o;
            s = (d.x * e2.y - d.y * e2.x) / det;
            t = (e1.x * d.y - e1.y * d.x) / det;
            return s >= 0 && s < 1 && t >= 0 && t < 1;
        }

        private static Color32 Sample(Image tex, float s, float t, float shade)
        {
            var c = tex[Mathf.Clamp((int)(s * tex.W), 0, tex.W - 1), Mathf.Clamp((int)(t * tex.H), 0, tex.H - 1)];
            return new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), c.a);
        }
    }
}
