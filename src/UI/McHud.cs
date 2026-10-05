using OuterCraft.Assets;
using OuterCraft.Player;
using UnityEngine;
using static OuterCraft.UI.McGui;

namespace OuterCraft.UI
{
    /// Minecraft's HUD, laid out in its own GUI pixels and scaled the same way: crosshair, hotbar
    /// with item icons, counts and selection; in survival the hearts (Outer Wilds health), hunger,
    /// air bubbles (Outer Wilds oxygen) and the XP bar; the item name after switching.
    public sealed class McHud
    {
        public void Draw(Inventory inv, float health01, float oxygen01, bool crosshair)
        {
            if (Event.current.type != EventType.Repaint || !McAssets.Loaded) return;
            Begin();
            float w = W, h = H;

            if (crosshair) Sprite("hud/crosshair", (w - 15) / 2f, (h - 15) / 2f, 15, 15);

            // hotbar
            float hx = w / 2f - 91f, hy = h - 22f;
            Sprite("hud/hotbar", hx, hy, 182, 22);
            Sprite("hud/hotbar_selection", hx - 1 + inv.Selected * 20, hy - 1, 24, 23);
            for (int i = 0; i < 9; i++) Item(inv.Slots[i], hx + 3 + i * 20, hy + 3);

            if (!inv.Creative)
            {
                // hearts: 10 containers, filled by halves of Outer Wilds' health
                int halves = Mathf.CeilToInt(Mathf.Clamp01(health01) * 20f);
                float rowY = h - 39f;
                for (int i = 0; i < 10; i++)
                {
                    float x = hx + i * 8;
                    float y = rowY + (halves <= 4 ? Random.Range(-1, 2) : 0); // shaking when low
                    Sprite("hud/heart/container", x, y, 9, 9);
                    if (halves >= i * 2 + 2) Sprite("hud/heart/full", x, y, 9, 9);
                    else if (halves == i * 2 + 1) Sprite("hud/heart/half", x, y, 9, 9);
                }

                // hunger (always full: Hearthians eat marshmallows)
                for (int i = 0; i < 10; i++)
                {
                    float x = hx + 182 - i * 8 - 9;
                    Sprite("hud/food_empty", x, rowY, 9, 9);
                    Sprite("hud/food_full", x, rowY, 9, 9);
                }

                // air: only while it's not full, above the hunger bar
                if (oxygen01 < 0.999f)
                {
                    int bubbles = Mathf.CeilToInt(Mathf.Clamp01(oxygen01) * 10f);
                    for (int i = 0; i < 10; i++)
                    {
                        float x = hx + 182 - i * 8 - 9;
                        Sprite(i < bubbles ? "hud/air" : "hud/air_empty", x, rowY - 10, 9, 9);
                    }
                }

                Sprite("hud/experience_bar_background", hx, h - 29f, 182, 5);
            }

            // the item's name for a moment after switching
            float since = Time.unscaledTime - inv.ChangedAt;
            var held = inv.CurrentItem;
            if (held != null && since < 2.2f)
            {
                float alpha = Mathf.Clamp01((2.2f - since) / 0.5f);
                string name = held.Name;
                Text(name, (w - TextWidth(name)) / 2f, h - (inv.Creative ? 37f : 59f), new Color(1, 1, 1, alpha));
            }
            DrawOverlay(w, h);
        }

        // ---------------------------------------------------------------- the action bar message

        private static string _overlay;
        private static float _overlayAt = -10f;

        /// Gui.setOverlayMessage: a line above the hotbar for 3 seconds (the last second fading).
        public static void Overlay(string text)
        {
            _overlay = text;
            _overlayAt = Time.unscaledTime;
        }

        private static void DrawOverlay(float w, float h)
        {
            float t = Time.unscaledTime - _overlayAt;
            if (_overlay == null || t > 3f) return;
            float alpha = Mathf.Clamp01((3f - t) / 1f);
            Text(_overlay, (w - TextWidth(_overlay)) / 2f, h - 68f, new Color(1, 1, 1, alpha));
        }
    }
}
