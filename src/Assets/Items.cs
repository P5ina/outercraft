using System.Collections.Generic;
using UnityEngine;

namespace OuterCraft.Assets
{
    /// Something that can sit in an inventory slot: a block's item or a plain item (sticks, tools...).
    public sealed class ItemDef
    {
        public string Key;
        public BlockDef Block;       // what it places, if anything
        public ToolType Tool;
        public int Tier = -1;        // wood 0, stone 1, iron 2, diamond 3 (gold mines like wood, fast)
        public float Speed = 1f;     // mining speed on its own blocks
        public float Damage = 1f;    // attack damage (hand: 1)
        public int MaxStack = 64;
        public Texture2D Icon;
        public BakedModel Model;     // hand, dropped item
        public Color32 TintColor = new Color32(255, 255, 255, 255);
        public int Light => Block != null ? Block.Light : 0;

        public string Name => Blocks.DisplayName(Key);
        public override string ToString() => Key;
    }

    public static class Items
    {
        public static readonly List<ItemDef> All = new List<ItemDef>();
        private static readonly Dictionary<string, ItemDef> ByKey = new Dictionary<string, ItemDef>();

        public static ItemDef Get(string key)
        {
            if (key == null) return null;
            int c = key.IndexOf(':');
            if (c >= 0) key = key.Substring(c + 1);
            return ByKey.TryGetValue(key, out var i) ? i : null;
        }

        public static ItemDef Of(BlockDef b) => b == null ? null : Get(b.ItemOf);

        private static readonly string[] Plain =
        {
            "stick", "coal", "charcoal", "raw_iron", "iron_ingot", "raw_gold", "gold_ingot", "diamond", "iron_nugget",
            "glowstone_dust", "flint", "wheat_seeds", "apple", "clay_ball", "brick", "snowball", "book", "paper",
            "melon_slice", "prismarine_crystals", "string", "feather", "leather", "flint_and_steel", "elytra", "firework_rocket",
        };

        private static readonly (string mat, int tier, float speed)[] Materials =
        {
            ("wooden", 0, 2f), ("stone", 1, 4f), ("iron", 2, 6f), ("golden", 0, 12f), ("diamond", 3, 8f),
        };

        public static void Build()
        {
            if (All.Count > 0) return;
            foreach (var b in Blocks.All)
                if (b.ItemKey == null) Register(new ItemDef { Key = b.Key, Block = b });
            foreach (var k in Plain) Register(new ItemDef { Key = k, MaxStack = k == "snowball" ? 16 : k == "flint_and_steel" || k == "elytra" ? 1 : 64 });
            foreach (var (mat, tier, speed) in Materials)
            {
                // Minecraft's attack damage: sword 4/5/6/4/7, axe 7/9/9/7/9, pickaxe 2/3/4/2/5, shovel 2.5/3.5/4.5/2.5/5.5
                int t = mat == "golden" ? 0 : tier;
                Register(new ItemDef { Key = mat + "_pickaxe", Tool = ToolType.Pickaxe, Tier = tier, Speed = speed, MaxStack = 1, Damage = 2 + t });
                Register(new ItemDef { Key = mat + "_axe", Tool = ToolType.Axe, Tier = tier, Speed = speed, MaxStack = 1, Damage = t == 0 ? 7 : 9 });
                Register(new ItemDef { Key = mat + "_shovel", Tool = ToolType.Shovel, Tier = tier, Speed = speed, MaxStack = 1, Damage = 2.5f + t });
                Register(new ItemDef { Key = mat + "_hoe", Tool = ToolType.Hoe, Tier = tier, Speed = speed, MaxStack = 1, Damage = 1 });
                Register(new ItemDef { Key = mat + "_sword", Tool = ToolType.Sword, Tier = tier, Speed = 1.5f, MaxStack = 1, Damage = 4 + t });
            }
        }

        private static void Register(ItemDef i)
        {
            All.Add(i);
            ByKey[i.Key] = i;
        }

        public static void Remove(ItemDef i)
        {
            All.Remove(i);
            ByKey.Remove(i.Key);
        }

        // ---------------------------------------------------------------- mining (Minecraft's numbers)

        public static bool CanHarvest(ItemDef held, BlockDef b)
        {
            if (b.MinTier < 0) return true;
            return held != null && held.Tool == b.Tool && held.Tier >= b.MinTier;
        }

        /// Progress per tick: speed / hardness / (30 if it drops, else 100). 1 = broken.
        public static float BreakPerTick(ItemDef held, BlockDef b)
        {
            if (b.Hardness <= 0f) return 1f;
            float speed = 1f;
            if (held != null && held.Tool != ToolType.None && held.Tool == b.Tool) speed = held.Speed;
            if (held != null && held.Tool == ToolType.Sword && (b.Key.Contains("leaves") || b.Key == "melon" || b.Key == "pumpkin")) speed = 1.5f;
            return speed / b.Hardness / (CanHarvest(held, b) ? 30f : 100f);
        }
    }

    /// A stack in a slot.
    public sealed class ItemStack
    {
        public ItemDef Item;
        public int Count;

        public ItemStack(ItemDef item, int count) { Item = item; Count = count; }
        public ItemStack Copy() => new ItemStack(Item, Count);
        public bool Empty => Item == null || Count <= 0;
        public int Room => Item.MaxStack - Count;
    }
}
