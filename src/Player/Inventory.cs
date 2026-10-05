using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OuterCraft.Player
{
    /// The player's 36 slots (0-8 the hotbar), Minecraft style: 1-9 and the wheel pick the slot,
    /// stacks merge, survival uses items up, creative never runs out. Resets with the loop.
    public sealed class Inventory
    {
        public readonly ItemStack[] Slots = new ItemStack[36];
        public int Selected { get; private set; }
        public float ChangedAt { get; private set; } = -10f;
        public bool Creative;
        public bool Dirty;
        private ItemDef _lastHeld;

        public ItemStack Current => Slots[Selected];
        public ItemStack Chest;   // the chestplate slot: where the elytra goes
        public bool HasElytra => Chest != null && Chest.Item?.Key == "elytra";
        public ItemDef CurrentItem => Current != null && !Current.Empty ? Current.Item : null;

        public void Select(int i)
        {
            i = ((i % 9) + 9) % 9;
            if (i == Selected) return;
            Selected = i;
        }

        /// Called every frame: digits, wheel, and the equip animation when the held item changes.
        public void Update(bool input)
        {
            if (input)
            {
                var kb = Keyboard.current;
                if (kb != null)
                    for (int i = 0; i < 9; i++)
                        if (kb[Key.Digit1 + i].wasPressedThisFrame) Select(i);
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (scroll > 0.1f) Select(Selected - 1);
                    else if (scroll < -0.1f) Select(Selected + 1);
                }
            }
            Tidy();
            var held = CurrentItem;
            if (held != _lastHeld)
            {
                _lastHeld = held;
                ChangedAt = Time.unscaledTime;
            }
        }

        private void Tidy()
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i] != null && Slots[i].Empty) Slots[i] = null;
        }

        /// Middle click: creative puts the block in hand; survival selects it if it's in the hotbar,
        /// or swaps it in from the inventory.
        public void Pick(BlockDef def)
        {
            var item = Items.Of(def);
            if (item == null) return;
            for (int i = 0; i < 9; i++)
                if (Slots[i]?.Item == item) { Select(i); return; }
            if (Creative)
            {
                int slot = Selected;
                if (Slots[slot] != null)
                    for (int i = 0; i < 9; i++)
                        if (Slots[i] == null) { slot = i; break; }
                Select(slot);
                Slots[slot] = new ItemStack(item, item.MaxStack);
                Dirty = true;
                return;
            }
            for (int i = 9; i < 36; i++)
                if (Slots[i]?.Item == item)
                {
                    var t = Slots[Selected];
                    Slots[Selected] = Slots[i];
                    Slots[i] = t;
                    Dirty = true;
                    return;
                }
        }

        /// Creative: cycle the held slot through every item ('[' and ']').
        public void Cycle(int dir)
        {
            if (!Creative || Items.All.Count == 0) return;
            int idx = CurrentItem == null ? -1 : Items.All.IndexOf(CurrentItem);
            idx = ((idx + dir) % Items.All.Count + Items.All.Count) % Items.All.Count;
            var it = Items.All[idx];
            Slots[Selected] = new ItemStack(it, it.MaxStack);
            Dirty = true;
        }

        /// Adds what fits (existing stacks first, then the first empty slot, hotbar first); returns the rest.
        public int Add(ItemDef item, int count)
        {
            if (item == null || count <= 0) return 0;
            for (int i = 0; i < 36 && count > 0; i++)
            {
                var s = Slots[i];
                if (s == null || s.Item != item || s.Count >= item.MaxStack) continue;
                int n = Math.Min(count, item.MaxStack - s.Count);
                s.Count += n;
                count -= n;
                Dirty = true;
            }
            for (int i = 0; i < 36 && count > 0; i++)
            {
                if (Slots[i] != null) continue;
                int n = Math.Min(count, item.MaxStack);
                Slots[i] = new ItemStack(item, n);
                count -= n;
                Dirty = true;
            }
            return count;
        }

        public bool CanFit(ItemDef item)
        {
            foreach (var s in Slots)
                if (s == null || (s.Item == item && s.Count < item.MaxStack)) return true;
            return false;
        }

        /// One of the held item used up (placing a block). Creative keeps it.
        public void UseOne()
        {
            if (Creative) return;
            var s = Current;
            if (s == null) return;
            s.Count--;
            if (s.Count <= 0) Slots[Selected] = null;
            Dirty = true;
        }

        public ItemStack TakeFromCurrent(int n)
        {
            var s = Current;
            if (s == null || s.Empty) return null;
            n = Math.Min(n, s.Count);
            var taken = new ItemStack(s.Item, n);
            if (!Creative) { s.Count -= n; if (s.Count <= 0) Slots[Selected] = null; }
            Dirty = true;
            return taken;
        }

        // ---------------------------------------------------------------- save

        [Serializable]
        public class SaveData
        {
            public List<string> keys = new List<string>();
            public List<int> counts = new List<int>();
            public int selected;
        }

        private const string File = "outercraft_inventory.json";

        public void Load()
        {
            var d = OuterCraft.Helper.Storage.Load<SaveData>(File);
            Array.Clear(Slots, 0, Slots.Length);
            if (d == null || d.keys == null || d.keys.Count == 0)
            {
                GiveKit();
                Dirty = true;
                return;
            }
            for (int i = 0; i < 36 && i < d.keys.Count; i++)
            {
                var it = Items.Get(d.keys[i]);
                if (it != null && i < d.counts.Count && d.counts[i] > 0) Slots[i] = new ItemStack(it, Math.Min(d.counts[i], it.MaxStack));
            }
            Select(d.selected);
        }

        public void Save()
        {
            if (!Dirty) return;
            Dirty = false;
            var d = new SaveData { selected = Selected };
            foreach (var s in Slots)
            {
                d.keys.Add(s?.Item?.Key ?? "");
                d.counts.Add(s?.Count ?? 0);
            }
            OuterCraft.Helper.Storage.Save(d, File);
        }

        /// A builder's kit, like SkyCraft hands out: Outer Wilds' own ground can't be mined, so the
        /// first blocks have to come from somewhere.
        public void ResetToKit()
        {
            Array.Clear(Slots, 0, Slots.Length);
            Chest = null;
            Select(0);
            GiveKit();
        }

        public void GiveKit()
        {
            void Put(int slot, string key, int n)
            {
                var it = Items.Get(key);
                if (it != null) Slots[slot] = new ItemStack(it, Math.Min(n, it.MaxStack));
            }
            Put(0, "diamond_pickaxe", 1);
            Put(1, "diamond_axe", 1);
            Put(2, "firework_rocket", 64);
            Put(3, "torch", 64);
            Put(4, "oak_planks", 64);
            Put(5, "cobblestone", 64);
            Put(6, "glass", 64);
            Put(7, "oak_log", 64);
            Put(8, "crafting_table", 1);
            var ely = Items.Get("elytra");
            Chest = ely != null ? new ItemStack(ely, 1) : null;
            string[] rest =
            {
                "diamond_sword", "diamond_shovel",
                "stone_bricks", "bricks", "oak_stairs", "oak_slab", "cobblestone_stairs", "stone_brick_slab", "oak_door", "ladder",
                "lantern", "oak_fence", "glass_pane", "dirt", "grass_block", "stone", "glowstone", "furnace", "poppy", "dandelion",
                "obsidian", "flint_and_steel", "sand", "coal", "iron_ingot",
            };
            int i = 9;
            foreach (var k in rest)
            {
                if (i >= 36) break;
                int n = k == "oak_door" ? 8 : k == "furnace" ? 4 : k == "obsidian" ? 14 : 64;
                var it = Items.Get(k);
                if (it == null) continue;
                Slots[i++] = new ItemStack(it, Math.Min(n, it.MaxStack));
            }
        }
    }
}
