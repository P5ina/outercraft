using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OuterCraft.Assets
{
    /// Minecraft's crafting recipes, read from the jar's data/minecraft/recipe/*.json (shaped and
    /// shapeless) with item tags from data/minecraft/tags/item. Recipes that need items we don't have
    /// are dropped.
    public static class Recipes
    {
        public sealed class Recipe
        {
            public string Id;
            public bool Shaped;
            public int W, H;
            public HashSet<ItemDef>[] Grid;          // shaped: W*H, null = empty
            public List<HashSet<ItemDef>> Ingredients; // shapeless
            public ItemDef Result;
            public int Count;
        }

        public static readonly List<Recipe> All = new List<Recipe>();
        private static readonly Dictionary<string, List<string>> Tags = new Dictionary<string, List<string>>();

        public static void Load(McJar jar)
        {
            All.Clear();
            Tags.Clear();
            foreach (var path in jar.Entries("data/minecraft/tags/item/"))
            {
                if (!path.EndsWith(".json")) continue;
                var j = Parse(jar, path);
                if (!(j?["values"] is JArray vals)) continue;
                var name = path.Substring("data/minecraft/tags/item/".Length).Replace(".json", "");
                Tags[name] = vals.Select(v => v is JObject o ? (string)o["id"] : (string)v).Where(v => v != null).ToList();
            }
            foreach (var path in jar.Entries("data/minecraft/recipe/"))
            {
                if (!path.EndsWith(".json")) continue;
                try
                {
                    var r = Read(Parse(jar, path), path);
                    if (r != null) All.Add(r);
                }
                catch (Exception e)
                {
                    OuterCraft.Log($"recipe {path}: {e.Message}");
                }
            }
            OuterCraft.Log($"{All.Count} crafting recipes");
        }

        private static JObject Parse(McJar jar, string path)
        {
            var b = jar.Read(path);
            return b == null ? null : JObject.Parse(System.Text.Encoding.UTF8.GetString(b));
        }

        private static Recipe Read(JObject j, string path)
        {
            if (j == null) return null;
            string type = (string)j["type"];
            var res = j["result"];
            string rid = res is JObject ro ? (string)(ro["id"] ?? ro["item"]) : (string)res;
            int count = res is JObject ro2 && ro2["count"] != null ? (int)ro2["count"] : 1;
            var result = Items.Get(rid);
            if (result == null) return null;
            var r = new Recipe { Id = path, Result = result, Count = count };
            if (type == "minecraft:crafting_shaped")
            {
                var pattern = ((JArray)j["pattern"]).Select(t => (string)t).ToArray();
                var key = (JObject)j["key"];
                r.Shaped = true;
                r.H = pattern.Length;
                r.W = pattern.Max(p => p.Length);
                r.Grid = new HashSet<ItemDef>[r.W * r.H];
                for (int y = 0; y < r.H; y++)
                    for (int x = 0; x < r.W; x++)
                    {
                        char c = x < pattern[y].Length ? pattern[y][x] : ' ';
                        if (c == ' ') continue;
                        var ing = Ingredient(key[c.ToString()]);
                        if (ing.Count == 0) return null;
                        r.Grid[y * r.W + x] = ing;
                    }
                return r;
            }
            if (type == "minecraft:crafting_shapeless")
            {
                r.Ingredients = new List<HashSet<ItemDef>>();
                foreach (var t in (JArray)j["ingredients"])
                {
                    var ing = Ingredient(t);
                    if (ing.Count == 0) return null;
                    r.Ingredients.Add(ing);
                }
                return r.Ingredients.Count > 0 ? r : null;
            }
            return null;
        }

        /// "minecraft:stick", "#minecraft:planks", ["a", "b"], {"item": ..}, {"tag": ..}
        private static HashSet<ItemDef> Ingredient(JToken t)
        {
            var set = new HashSet<ItemDef>();
            void AddName(string s)
            {
                if (s == null) return;
                if (s.StartsWith("#")) { foreach (var n in Tag(s.Substring(1), 0)) AddName(n); return; }
                var i = Items.Get(s);
                if (i != null) set.Add(i);
            }
            if (t is JArray a) foreach (var e in a) set.UnionWith(Ingredient(e));
            else if (t is JObject o) { AddName((string)o["item"]); if (o["tag"] != null) AddName("#" + (string)o["tag"]); }
            else if (t != null) AddName((string)t);
            return set;
        }

        private static IEnumerable<string> Tag(string name, int depth)
        {
            int c = name.IndexOf(':');
            if (c >= 0) name = name.Substring(c + 1);
            if (depth > 8 || !Tags.TryGetValue(name, out var vals)) yield break;
            foreach (var v in vals)
            {
                if (v.StartsWith("#")) { foreach (var n in Tag(v.Substring(1), depth + 1)) yield return n; }
                else yield return v;
            }
        }

        // ---------------------------------------------------------------- matching

        /// The result of a size x size crafting grid (row-major), or null.
        public static Recipe Match(ItemStack[] grid, int size)
        {
            int minX = size, minY = size, maxX = -1, maxY = -1, n = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var s = grid[y * size + x];
                    if (s == null || s.Empty) continue;
                    n++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            if (n == 0) return null;
            int w = maxX - minX + 1, h = maxY - minY + 1;
            foreach (var r in All)
            {
                if (r.Shaped)
                {
                    if (r.W != w || r.H != h) continue;
                    if (ShapedFits(r, grid, size, minX, minY, false) || ShapedFits(r, grid, size, minX, minY, true)) return r;
                }
                else if (r.Ingredients.Count == n && ShapelessFits(r, grid)) return r;
            }
            return null;
        }

        private static bool ShapedFits(Recipe r, ItemStack[] grid, int size, int ox, int oy, bool mirror)
        {
            for (int y = 0; y < r.H; y++)
                for (int x = 0; x < r.W; x++)
                {
                    var need = r.Grid[y * r.W + (mirror ? r.W - 1 - x : x)];
                    var have = grid[(oy + y) * size + ox + x];
                    bool empty = have == null || have.Empty;
                    if (need == null) { if (!empty) return false; }
                    else if (empty || !need.Contains(have.Item)) return false;
                }
            return true;
        }

        private static bool ShapelessFits(Recipe r, ItemStack[] grid)
        {
            var items = grid.Where(s => s != null && !s.Empty).Select(s => s.Item).ToList();
            var used = new bool[r.Ingredients.Count];
            bool Assign(int i)
            {
                if (i == items.Count) return true;
                for (int k = 0; k < used.Length; k++)
                {
                    if (used[k] || !r.Ingredients[k].Contains(items[i])) continue;
                    used[k] = true;
                    if (Assign(i + 1)) return true;
                    used[k] = false;
                }
                return false;
            }
            return Assign(0);
        }
    }
}
