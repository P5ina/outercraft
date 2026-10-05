using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OuterCraft.Assets
{
    public enum Tint { None, Grass, Foliage, Birch, Spruce }
    public enum ToolType { None, Pickaxe, Axe, Shovel, Hoe, Sword }

    /// How a block picks its state when placed.
    public enum PlaceKind { Simple, Torch, Lantern, Stairs, Slab, Door, WallAttached, FacingAway, Plant }

    /// A Minecraft block. Full cubes use the fast cube mesher (with Minecraft AO); everything else
    /// ("model" blocks) is baked from the jar's blockstate + JSON models.
    public sealed class BlockDef
    {
        public byte Id;
        public string Key;          // Minecraft id without namespace, e.g. "oak_planks"
        public string Top, Bottom, Side;
        public string SideOverlay;  // grass_block's tinted side fringe
        public Tint TopTint, SideTint;
        public bool Cutout;         // leaves, glass: see-through texels
        public int Light;           // 0..15 block light it gives off

        // survival
        public float Hardness = 1f;
        public ToolType Tool;       // the tool that mines it fast
        public int MinTier = -1;    // -1: drops by hand; else needs Tool of at least this tier
        public string Drop;         // null: itself; "": nothing; else that item
        public int DropMin = 1, DropMax = 1;
        public float DropChance = 1f;

        // model blocks
        public bool IsModel;
        public PlaceKind Place;
        public bool Collide = true;
        public string ItemKey;       // the item that places it (wall_torch <- torch)
        public string[] VariantKeys; // blockstate variant names, index = stored state
        public BakedModel[] States;  // baked per state
        public List<(Func<Func<string, string>, bool> when, BakedModel model)> Parts; // multipart (fences, panes)
        public string Connects;      // fences connect to "fence", panes to "pane"
        public bool FullCubeModel;   // a model block that's still a solid cube (furnace, crafting table)
        public Color32 TintColor = new Color32(255, 255, 255, 255);

        public bool Opaque => IsModel ? FullCubeModel : !Cutout;

        // filled by the atlas
        public Rect TopUv, BottomUv, SideUv;
        public Rect ParticleUv;

        public string ItemOf => ItemKey ?? Key;

        // ---------------------------------------------------------------- states

        public int DefaultState;
        private Dictionary<string, string>[] _props;

        public Dictionary<string, string> Props(int state)
        {
            if (VariantKeys == null || state < 0 || state >= VariantKeys.Length) return new Dictionary<string, string>();
            if (_props == null) _props = VariantKeys.Select(ParseProps).ToArray();
            return _props[state];
        }

        public string Prop(int state, string name) => Props(state).TryGetValue(name, out var v) ? v : null;

        public static Dictionary<string, string> ParseProps(string key)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(key)) return d;
            foreach (var kv in key.Split(','))
            {
                int eq = kv.IndexOf('=');
                if (eq > 0) d[kv.Substring(0, eq)] = kv.Substring(eq + 1);
            }
            return d;
        }

        /// The state whose properties include all of `want` (others free), or -1.
        public int Find(params (string k, string v)[] want)
        {
            if (VariantKeys == null) return 0;
            for (int i = 0; i < VariantKeys.Length; i++)
            {
                var p = Props(i);
                bool ok = true;
                foreach (var (k, v) in want)
                    if (!p.TryGetValue(k, out var have) || have != v) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        public int With(int state, string key, string value)
        {
            var p = new Dictionary<string, string>(Props(state)) { [key] = value };
            int s = Find(p.Select(kv => (kv.Key, kv.Value)).ToArray());
            return s >= 0 ? s : state;
        }

        /// The model for a state; multipart blocks build theirs from the neighbours.
        public BakedModel ModelFor(int state, Func<string, string> props = null)
        {
            if (Parts != null)
            {
                var b = new BakedModel();
                foreach (var (when, model) in Parts)
                    if (when == null || when(props ?? (_ => "false"))) b.AddAll(model);
                return b;
            }
            if (States == null || States.Length == 0) return null;
            return States[state >= 0 && state < States.Length ? state : DefaultState];
        }

        /// Where a block hangs from: it pops off when that neighbour goes.
        public Dir? Support(int state)
        {
            switch (Place)
            {
                case PlaceKind.Torch: return Key == "wall_torch" && Dirs.TryParse(Prop(state, "facing"), out var f) ? Dirs.Opposite(f) : Dir.Down;
                case PlaceKind.WallAttached: return Dirs.TryParse(Prop(state, "facing"), out var w) ? Dirs.Opposite(w) : (Dir?)null;
                case PlaceKind.Lantern: return Prop(state, "hanging") == "true" ? Dir.Up : Dir.Down;
                case PlaceKind.Plant: return Dir.Down;
                case PlaceKind.Door: return Prop(state, "half") == "upper" ? Dir.Down : (Dir?)null;
                default: return null;
            }
        }
    }

    public static class Blocks
    {
        public static readonly List<BlockDef> All = new List<BlockDef>();
        private static readonly Dictionary<string, BlockDef> ByKey = new Dictionary<string, BlockDef>();

        public static BlockDef Get(byte id) => id > 0 && id <= All.Count ? All[id - 1] : null;
        public static BlockDef Get(string key) => key != null && ByKey.TryGetValue(key, out var b) ? b : null;

        /// "oak_planks" -> "Oak Planks", the way Minecraft names it above the hotbar.
        public static string DisplayName(string key)
        {
            var words = key.Split('_');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words).Replace("O Lantern", "o'Lantern").Replace(" Of ", " of ");
        }

        static Blocks()
        {
            // NOTE: ids are positions in this list and are saved: only ever append.
            Add("grass_block", top: "grass_block_top", bottom: "dirt", side: "grass_block_side", overlay: "grass_block_side_overlay", topTint: Tint.Grass).Mine(0.6f, ToolType.Shovel, drop: "dirt");
            Add("dirt", "dirt").Mine(0.5f, ToolType.Shovel);
            Add("stone", "stone").Mine(1.5f, ToolType.Pickaxe, 0, "cobblestone");
            Add("cobblestone", "cobblestone").Mine(2f, ToolType.Pickaxe, 0);
            Add("mossy_cobblestone", "mossy_cobblestone").Mine(2f, ToolType.Pickaxe, 0);
            Add("deepslate", top: "deepslate_top", bottom: "deepslate_top", side: "deepslate").Mine(3f, ToolType.Pickaxe, 0);
            Add("oak_planks", "oak_planks").Mine(2f, ToolType.Axe);
            Add("spruce_planks", "spruce_planks").Mine(2f, ToolType.Axe);
            Add("birch_planks", "birch_planks").Mine(2f, ToolType.Axe);
            Add("oak_log", top: "oak_log_top", bottom: "oak_log_top", side: "oak_log").Mine(2f, ToolType.Axe);
            Add("spruce_log", top: "spruce_log_top", bottom: "spruce_log_top", side: "spruce_log").Mine(2f, ToolType.Axe);
            Add("birch_log", top: "birch_log_top", bottom: "birch_log_top", side: "birch_log").Mine(2f, ToolType.Axe);
            Add("oak_leaves", "oak_leaves", tint: Tint.Foliage, cutout: true).Mine(0.2f, ToolType.Hoe, drop: "oak_sapling", chance: 0.05f);
            Add("birch_leaves", "birch_leaves", tint: Tint.Birch, cutout: true).Mine(0.2f, ToolType.Hoe, drop: "");
            Add("glass", "glass", cutout: true).Mine(0.3f, ToolType.None, drop: "");
            Add("sand", "sand").Mine(0.5f, ToolType.Shovel);
            Add("sandstone", top: "sandstone_top", bottom: "sandstone_bottom", side: "sandstone").Mine(0.8f, ToolType.Pickaxe, 0);
            Add("gravel", "gravel").Mine(0.6f, ToolType.Shovel);
            Add("clay", "clay").Mine(0.6f, ToolType.Shovel, drop: "clay_ball", min: 4, max: 4);
            Add("bricks", "bricks").Mine(2f, ToolType.Pickaxe, 0);
            Add("stone_bricks", "stone_bricks").Mine(1.5f, ToolType.Pickaxe, 0);
            Add("smooth_stone", top: "smooth_stone", bottom: "smooth_stone", side: "smooth_stone_slab_side").Mine(2f, ToolType.Pickaxe, 0);
            Add("terracotta", "terracotta").Mine(1.25f, ToolType.Pickaxe, 0);
            Add("white_wool", "white_wool").Mine(0.8f, ToolType.None);
            Add("red_wool", "red_wool").Mine(0.8f, ToolType.None);
            Add("yellow_wool", "yellow_wool").Mine(0.8f, ToolType.None);
            Add("green_wool", "green_wool").Mine(0.8f, ToolType.None);
            Add("blue_wool", "blue_wool").Mine(0.8f, ToolType.None);
            Add("black_wool", "black_wool").Mine(0.8f, ToolType.None);
            Add("snow_block", "snow").Mine(0.2f, ToolType.Shovel, 0, "snowball", 4, 4);
            Add("ice", "ice", cutout: true).Mine(0.5f, ToolType.Pickaxe, drop: "");
            Add("packed_ice", "packed_ice").Mine(0.5f, ToolType.Pickaxe, drop: "");
            Add("obsidian", "obsidian").Mine(50f, ToolType.Pickaxe, 3);
            Add("netherrack", "netherrack").Mine(0.4f, ToolType.Pickaxe, 0);
            Add("quartz_block", top: "quartz_block_top", bottom: "quartz_block_top", side: "quartz_block_side").Mine(0.8f, ToolType.Pickaxe, 0);
            Add("iron_block", "iron_block").Mine(5f, ToolType.Pickaxe, 1);
            Add("gold_block", "gold_block").Mine(3f, ToolType.Pickaxe, 2);
            Add("diamond_block", "diamond_block").Mine(5f, ToolType.Pickaxe, 2);
            Add("bookshelf", top: "oak_planks", bottom: "oak_planks", side: "bookshelf").Mine(1.5f, ToolType.Axe, drop: "book", min: 3, max: 3);
            Model("crafting_table", fullCube: true).Mine(2.5f, ToolType.Axe);
            Add("tnt", top: "tnt_top", bottom: "tnt_bottom", side: "tnt_side").Mine(0f, ToolType.None);
            Add("hay_block", top: "hay_block_top", bottom: "hay_block_top", side: "hay_block_side").Mine(0.5f, ToolType.Hoe);
            Add("pumpkin", top: "pumpkin_top", bottom: "pumpkin_top", side: "pumpkin_side").Mine(1f, ToolType.Axe);
            Add("jack_o_lantern", top: "pumpkin_top", bottom: "pumpkin_top", side: "jack_o_lantern", light: 15).Mine(1f, ToolType.Axe);
            Add("melon", top: "melon_top", bottom: "melon_top", side: "melon_side").Mine(1f, ToolType.Axe, drop: "melon_slice", min: 3, max: 7);
            Add("glowstone", "glowstone", light: 15).Mine(0.3f, ToolType.None, drop: "glowstone_dust", min: 2, max: 4);
            Add("sea_lantern", "sea_lantern", light: 15).Mine(0.3f, ToolType.None, drop: "prismarine_crystals", min: 2, max: 3);
            // ---- 0.4: ores
            Add("coal_ore", "coal_ore").Mine(3f, ToolType.Pickaxe, 0, "coal");
            Add("iron_ore", "iron_ore").Mine(3f, ToolType.Pickaxe, 1, "raw_iron");
            Add("gold_ore", "gold_ore").Mine(3f, ToolType.Pickaxe, 2, "raw_gold");
            Add("diamond_ore", "diamond_ore").Mine(3f, ToolType.Pickaxe, 2, "diamond");
            // ---- 0.4: shaped blocks, straight from the jar's JSON models
            Model("torch", PlaceKind.Torch, collide: false, light: 14).Mine(0f, ToolType.None);
            Model("wall_torch", PlaceKind.Torch, collide: false, light: 14, item: "torch").Mine(0f, ToolType.None);
            Model("lantern", PlaceKind.Lantern, light: 15).Mine(3.5f, ToolType.Pickaxe, 0);
            Model("oak_stairs", PlaceKind.Stairs).Mine(2f, ToolType.Axe);
            Model("cobblestone_stairs", PlaceKind.Stairs).Mine(2f, ToolType.Pickaxe, 0);
            Model("stone_brick_stairs", PlaceKind.Stairs).Mine(1.5f, ToolType.Pickaxe, 0);
            Model("oak_slab", PlaceKind.Slab).Mine(2f, ToolType.Axe);
            Model("cobblestone_slab", PlaceKind.Slab).Mine(2f, ToolType.Pickaxe, 0);
            Model("stone_brick_slab", PlaceKind.Slab).Mine(2f, ToolType.Pickaxe, 0);
            Model("smooth_stone_slab", PlaceKind.Slab).Mine(2f, ToolType.Pickaxe, 0);
            Model("oak_door", PlaceKind.Door).Mine(3f, ToolType.Axe);
            Model("oak_fence", connects: "fence").Mine(2f, ToolType.Axe);
            Model("glass_pane", connects: "pane").Mine(0.3f, ToolType.None, drop: "");
            Model("ladder", PlaceKind.WallAttached).Mine(0.4f, ToolType.Axe);
            Model("furnace", PlaceKind.FacingAway, fullCube: true).Mine(3.5f, ToolType.Pickaxe, 0);
            Model("poppy", PlaceKind.Plant, collide: false).Mine(0f, ToolType.None);
            Model("dandelion", PlaceKind.Plant, collide: false).Mine(0f, ToolType.None);
            Model("cornflower", PlaceKind.Plant, collide: false).Mine(0f, ToolType.None);
            Model("oxeye_daisy", PlaceKind.Plant, collide: false).Mine(0f, ToolType.None);
            Model("short_grass", PlaceKind.Plant, collide: false, tint: Tint.Grass).Mine(0f, ToolType.None, drop: "wheat_seeds", chance: 0.125f);
            Model("fern", PlaceKind.Plant, collide: false, tint: Tint.Grass).Mine(0f, ToolType.None, drop: "wheat_seeds", chance: 0.125f);
            Model("oak_sapling", PlaceKind.Plant, collide: false).Mine(0f, ToolType.None);
            Model("red_mushroom", PlaceKind.Plant, collide: false).Mine(0f, ToolType.None);
            Model("nether_portal", PlaceKind.Simple, collide: false, light: 11, item: "").Mine(0f, ToolType.None, drop: "");
            Model("brown_mushroom", PlaceKind.Plant, collide: false, light: 1).Mine(0f, ToolType.None);
        }

        private static BlockDef Add(string key, string all = null, string top = null, string bottom = null, string side = null,
            string overlay = null, Tint tint = Tint.None, Tint topTint = Tint.None, bool cutout = false, int light = 0)
        {
            var b = new BlockDef
            {
                Id = (byte)(All.Count + 1),
                Key = key,
                Top = top ?? all,
                Bottom = bottom ?? all,
                Side = side ?? all,
                SideOverlay = overlay,
                TopTint = topTint != Tint.None ? topTint : tint,
                SideTint = tint,
                Cutout = cutout,
                Light = light,
            };
            All.Add(b);
            ByKey[key] = b;
            return b;
        }

        private static BlockDef Model(string key, PlaceKind place = PlaceKind.Simple, bool collide = true, int light = 0,
            string item = null, string connects = null, bool fullCube = false, Tint tint = Tint.None)
        {
            var b = Add(key, "stone", light: light);
            b.IsModel = true;
            b.Place = place;
            b.Collide = collide;
            b.ItemKey = item;
            b.Connects = connects;
            b.FullCubeModel = fullCube;
            b.TopTint = b.SideTint = tint;
            return b;
        }

        private static BlockDef Mine(this BlockDef b, float hardness, ToolType tool, int minTier = -1, string drop = null,
            int min = 1, int max = 1, float chance = 1f)
        {
            b.Hardness = hardness;
            b.Tool = tool;
            b.MinTier = minTier;
            b.Drop = drop;
            b.DropMin = min;
            b.DropMax = max;
            b.DropChance = chance;
            return b;
        }
    }
}
