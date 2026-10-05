using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using OuterCraft.Player;
using UnityEngine;
using static OuterCraft.UI.McGui;

namespace OuterCraft.UI
{
    /// Minecraft's container screens, with its own textures and slot positions: the survival
    /// inventory (2x2 crafting), the crafting table (3x3) and, in creative, the item list.
    /// Clicks work like Minecraft's: pick up / put down / swap, right click halves or places one,
    /// shift-click moves across, number keys swap with the hotbar, clicking outside throws.
    public sealed class InventoryScreen
    {
        public enum Kind { Inventory, Crafting, Creative }

        public bool IsOpen { get; private set; }
        public Kind Mode { get; private set; }
        public Action<ItemStack> Throw;   // dropped out of the screen

        private readonly ItemStack[] _grid = new ItemStack[9];
        private ItemStack _cursor;
        private Recipes.Recipe _recipe;
        private int _scroll;

        private int GridSize => Mode == Kind.Crafting ? 3 : 2;

        private enum SlotKind { Inv, Grid, Result, Palette, Armor }

        private struct Slot
        {
            public SlotKind K;
            public int I;
            public float X, Y;
        }

        public void Open(Kind kind)
        {
            Mode = kind;
            IsOpen = true;
            _scroll = 0;
            MatchRecipe();
        }

        /// Closing gives the crafting grid and the held stack back (or throws what doesn't fit).
        public void Close(Inventory inv)
        {
            if (!IsOpen) return;
            IsOpen = false;
            for (int i = 0; i < _grid.Length; i++) { Return(inv, _grid[i]); _grid[i] = null; }
            Return(inv, _cursor);
            _cursor = null;
            _recipe = null;
        }

        private void Return(Inventory inv, ItemStack s)
        {
            if (s == null || s.Empty) return;
            int left = inv.Add(s.Item, s.Count);
            if (left > 0) Throw?.Invoke(new ItemStack(s.Item, left));
        }

        // ---------------------------------------------------------------- layout

        private Texture Background(out float w, out float h)
        {
            string name = Mode == Kind.Crafting ? "crafting_table" : Mode == Kind.Creative ? "creative_inventory/tab_items" : "inventory";
            McAssets.Container.TryGetValue(name, out var t);
            w = Mode == Kind.Creative ? 195 : 176;
            h = Mode == Kind.Creative ? 136 : 166;
            return t;
        }

        private List<Slot> Slots()
        {
            var l = new List<Slot>();
            if (Mode == Kind.Creative)
            {
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < 9; x++)
                        l.Add(new Slot { K = SlotKind.Palette, I = (_scroll + y) * 9 + x, X = 9 + x * 18, Y = 18 + y * 18 });
                for (int x = 0; x < 9; x++) l.Add(new Slot { K = SlotKind.Inv, I = x, X = 9 + x * 18, Y = 112 });
                return l;
            }
            if (Mode == Kind.Crafting)
            {
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        l.Add(new Slot { K = SlotKind.Grid, I = y * 3 + x, X = 30 + x * 18, Y = 17 + y * 18 });
                l.Add(new Slot { K = SlotKind.Result, X = 124, Y = 35 });
            }
            else
            {
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        l.Add(new Slot { K = SlotKind.Grid, I = y * 2 + x, X = 98 + x * 18, Y = 18 + y * 18 });
                l.Add(new Slot { K = SlotKind.Result, X = 154, Y = 28 });
                for (int i = 0; i < 4; i++) l.Add(new Slot { K = SlotKind.Armor, I = i, X = 8, Y = 8 + i * 18 });
            }
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 9; x++)
                    l.Add(new Slot { K = SlotKind.Inv, I = 9 + y * 9 + x, X = 8 + x * 18, Y = 84 + y * 18 });
            for (int x = 0; x < 9; x++) l.Add(new Slot { K = SlotKind.Inv, I = x, X = 8 + x * 18, Y = 142 });
            return l;
        }

        private ItemStack Get(Inventory inv, Slot s)
        {
            switch (s.K)
            {
                case SlotKind.Inv: return inv.Slots[s.I];
                case SlotKind.Armor: return s.I == 1 ? inv.Chest : null;
                case SlotKind.Grid: return _grid[s.I];
                case SlotKind.Result: return _recipe != null ? new ItemStack(_recipe.Result, _recipe.Count) : null;
                default: return s.I >= 0 && s.I < Items.All.Count ? new ItemStack(Items.All[s.I], 1) : null;
            }
        }

        private void Put(Inventory inv, Slot s, ItemStack v)
        {
            if (v != null && v.Empty) v = null;
            if (s.K == SlotKind.Inv) { inv.Slots[s.I] = v; inv.Dirty = true; }
            else if (s.K == SlotKind.Armor) { if (s.I == 1) inv.Chest = v; }
            else if (s.K == SlotKind.Grid) { _grid[s.I] = v; MatchRecipe(); }
        }

        private void MatchRecipe()
        {
            int n = GridSize;
            var g = new ItemStack[n * n];
            for (int i = 0; i < n * n; i++) g[i] = _grid[i];
            _recipe = Mode == Kind.Creative ? null : Recipes.Match(g, n);
        }

        // ---------------------------------------------------------------- frame

        /// The mouse in GUI pixels, from the Input System (Outer Wilds reads input through it).
        private static Vector2 MouseGui()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return Mouse;
            var p = m.position.ReadValue();
            return new Vector2(p.x, Screen.height - p.y) / S;
        }

        private Slot? Hover(List<Slot> slots, Vector2 mouse, float left, float top)
        {
            Slot? hover = null;
            foreach (var s in slots)
                if (mouse.x >= left + s.X - 1 && mouse.x < left + s.X + 17 && mouse.y >= top + s.Y - 1 && mouse.y < top + s.Y + 17)
                    hover = s;
            return hover;
        }

        /// Clicks, wheel and number keys; called from Update.
        public void HandleInput(Inventory inv)
        {
            if (!IsOpen || !McAssets.Loaded) return;
            Begin();
            Background(out float bw, out float bh);
            float left = Mathf.Floor((W - bw) / 2f), top = Mathf.Floor((H - bh) / 2f);
            var mouse = MouseGui();
            var slots = Slots();
            var hover = Hover(slots, mouse, left, top);
            var m = UnityEngine.InputSystem.Mouse.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            if (m != null)
            {
                int button = m.leftButton.wasPressedThisFrame ? 0 : m.rightButton.wasPressedThisFrame ? 1 : -1;
                if (button >= 0)
                {
                    bool inside = mouse.x >= left && mouse.x < left + bw && mouse.y >= top && mouse.y < top + bh;
                    if (hover.HasValue) Click(inv, hover.Value, button, shift);
                    else if (!inside && _cursor != null)
                    {
                        // clicking outside the window throws the held stack (right click: one item)
                        if (button == 1) { Throw?.Invoke(new ItemStack(_cursor.Item, 1)); _cursor.Count--; if (_cursor.Count <= 0) _cursor = null; }
                        else { Throw?.Invoke(_cursor); _cursor = null; }
                    }
                }
                float wheel = m.scroll.ReadValue().y;
                if (Mode == Kind.Creative && Mathf.Abs(wheel) > 0.1f)
                {
                    int rows = Mathf.CeilToInt(Items.All.Count / 9f);
                    _scroll = Mathf.Clamp(_scroll + (wheel < 0 ? 1 : -1), 0, Mathf.Max(0, rows - 5));
                }
            }
            if (kb != null && hover.HasValue)
                for (int i = 0; i < 9; i++)
                    if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) SwapWithHotbar(inv, hover.Value, i);
        }

        public void OnGUI(Inventory inv)
        {
            if (!IsOpen || !McAssets.Loaded) return;
            if (Event.current.type != EventType.Repaint) return;
            Begin();
            var bg = Background(out float bw, out float bh);
            float left = Mathf.Floor((W - bw) / 2f), top = Mathf.Floor((H - bh) / 2f);
            var mouse = MouseGui();
            var slots = Slots();
            var hover = Hover(slots, mouse, left, top);

            // Screen.renderBackground: the world dimmed behind the window
            Fill(0, 0, W, H, new Color(0x10 / 255f, 0x10 / 255f, 0x10 / 255f, 0.78f));
            if (bg != null) Blit(bg, left, top, 0, 0, bw, bh);
            var label = new Color(0x40 / 255f, 0x40 / 255f, 0x40 / 255f);
            if (Mode == Kind.Crafting)
            {
                Text("Crafting", left + 29, top + 6, label, false);
                Text("Inventory", left + 8, top + 72, label, false);
            }
            else if (Mode == Kind.Inventory) Text("Crafting", left + 97, top + 8, label, false);
            else
            {
                Text("Items", left + 8, top + 6, label, false);
                int rows = Mathf.CeilToInt(Items.All.Count / 9f);
                float k = rows > 5 ? _scroll / (float)(rows - 5) : 0f;
                Sprite("container/creative_inventory/scroller", left + 175, top + 18 + k * 95, 12, 15);
            }

            string[] armorIcons = { "container/slot/helmet", "container/slot/chestplate", "container/slot/leggings", "container/slot/boots" };
            foreach (var s in slots)
            {
                var st = Get(inv, s);
                if (s.K == SlotKind.Armor && (st == null || st.Empty)) Sprite(armorIcons[s.I], left + s.X, top + s.Y, 16, 16);
                else McGui.Item(st, left + s.X, top + s.Y);
            }
            if (hover.HasValue)
            {
                var h = hover.Value;
                Fill(left + h.X, top + h.Y, 16, 16, new Color(1, 1, 1, 0.5f)); // slot highlight
            }
            if (_cursor != null) McGui.Item(_cursor, mouse.x - 8, mouse.y - 8);
            else if (hover.HasValue)
            {
                var st = Get(inv, hover.Value);
                if (st != null && !st.Empty) Tooltip(st.Item.Name, mouse.x, mouse.y);
            }
        }

        // ---------------------------------------------------------------- clicks (AbstractContainerMenu.doClick)

        private void Click(Inventory inv, Slot s, int button, bool shift)
        {
            if (button != 0 && button != 1) return;
            if (s.K == SlotKind.Palette) { ClickPalette(inv, s, button, shift); return; }
            if (s.K == SlotKind.Result) { ClickResult(inv, shift); return; }
            // only the chest slot, and only for an elytra (the one wearable thing here)
            if (s.K == SlotKind.Armor && (s.I != 1 || (_cursor != null && _cursor.Item.Key != "elytra"))) return;

            var slot = Get(inv, s);
            if (shift && button == 0)
            {
                if (slot == null) return;
                QuickMove(inv, s, slot);
                return;
            }
            if (button == 0)
            {
                if (_cursor == null) { _cursor = slot; Put(inv, s, null); }
                else if (slot == null) { Put(inv, s, _cursor); _cursor = null; }
                else if (slot.Item == _cursor.Item)
                {
                    int n = Math.Min(_cursor.Count, slot.Room);
                    slot.Count += n; _cursor.Count -= n;
                    if (_cursor.Count <= 0) _cursor = null;
                    Put(inv, s, slot);
                }
                else { Put(inv, s, _cursor); _cursor = slot; }
            }
            else
            {
                if (_cursor == null)
                {
                    if (slot == null) return;
                    int half = (slot.Count + 1) / 2;
                    _cursor = new ItemStack(slot.Item, half);
                    slot.Count -= half;
                    Put(inv, s, slot);
                }
                else if (slot == null) { Put(inv, s, new ItemStack(_cursor.Item, 1)); _cursor.Count--; }
                else if (slot.Item == _cursor.Item && slot.Room > 0) { slot.Count++; _cursor.Count--; Put(inv, s, slot); }
                else { Put(inv, s, _cursor); _cursor = slot; }
                if (_cursor != null && _cursor.Count <= 0) _cursor = null;
            }
            inv.Dirty = true;
        }

        private void ClickPalette(Inventory inv, Slot s, int button, bool shift)
        {
            var item = Get(inv, s)?.Item;
            if (_cursor != null) { _cursor = null; return; } // creative: dropping onto the list deletes
            if (item == null) return;
            if (shift) { inv.Add(item, item.MaxStack); return; }
            _cursor = new ItemStack(item, button == 1 ? 1 : item.MaxStack);
        }

        private void ClickResult(Inventory inv, bool shift)
        {
            if (_recipe == null) return;
            if (shift)
            {
                // craft as many as fit
                for (int guard = 0; guard < 64 && _recipe != null; guard++)
                {
                    var r = _recipe;
                    if (!inv.CanFit(r.Result)) break;
                    int left = inv.Add(r.Result, r.Count);
                    if (left > 0) Throw?.Invoke(new ItemStack(r.Result, left));
                    Consume();
                    if (_recipe != r) break;
                }
                return;
            }
            if (_cursor != null && (_cursor.Item != _recipe.Result || _cursor.Count + _recipe.Count > _recipe.Result.MaxStack)) return;
            if (_cursor == null) _cursor = new ItemStack(_recipe.Result, _recipe.Count);
            else _cursor.Count += _recipe.Count;
            Consume();
        }

        /// One of everything in the grid used up.
        private void Consume()
        {
            for (int i = 0; i < _grid.Length; i++)
            {
                if (_grid[i] == null) continue;
                _grid[i].Count--;
                if (_grid[i].Count <= 0) _grid[i] = null;
            }
            MatchRecipe();
        }

        /// Shift-click: hotbar <-> main inventory, crafting grid -> inventory.
        private void QuickMove(Inventory inv, Slot s, ItemStack st)
        {
            if (s.K == SlotKind.Inv && st.Item.Key == "elytra" && inv.Chest == null && Mode == Kind.Inventory)
            {
                inv.Chest = st;
                Put(inv, s, null);
                return;
            }
            if (s.K == SlotKind.Grid || s.K == SlotKind.Armor)
            {
                int left = inv.Add(st.Item, st.Count);
                Put(inv, s, left > 0 ? new ItemStack(st.Item, left) : null);
                return;
            }
            if (Mode == Kind.Creative) return;
            int from = s.I < 9 ? 9 : 0, to = s.I < 9 ? 36 : 9;
            int count = st.Count;
            for (int pass = 0; pass < 2 && count > 0; pass++)
                for (int i = from; i < to && count > 0; i++)
                {
                    var t = inv.Slots[i];
                    if (pass == 0 && t != null && t.Item == st.Item && t.Room > 0)
                    {
                        int n = Math.Min(count, t.Room);
                        t.Count += n; count -= n;
                    }
                    else if (pass == 1 && t == null)
                    {
                        inv.Slots[i] = new ItemStack(st.Item, count);
                        count = 0;
                    }
                }
            Put(inv, s, count > 0 ? new ItemStack(st.Item, count) : null);
        }

        private void SwapWithHotbar(Inventory inv, Slot s, int hotbar)
        {
            if (s.K == SlotKind.Result || s.K == SlotKind.Armor || (s.K == SlotKind.Inv && s.I == hotbar)) return;
            if (s.K == SlotKind.Palette)
            {
                var it = Get(inv, s)?.Item;
                if (it != null) { inv.Slots[hotbar] = new ItemStack(it, it.MaxStack); inv.Dirty = true; }
                return;
            }
            var a = Get(inv, s);
            var b = inv.Slots[hotbar];
            inv.Slots[hotbar] = a;
            Put(inv, s, b);
            inv.Dirty = true;
        }
    }
}
