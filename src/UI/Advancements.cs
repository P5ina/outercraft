using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;
using static OuterCraft.UI.McGui;

namespace OuterCraft.UI
{
    /// Minecraft advancements earned by doing Outer Wilds things, shown with Minecraft's toast
    /// (slides in at the top right, "Advancement Made!" in yellow, the toast sounds). Each is earned
    /// once and remembered across sessions.
    public static class Advancements
    {
        private sealed class Def
        {
            public string Id, Title, Icon;
            public bool Challenge;
        }

        private static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>();

        static Advancements()
        {
            Add("minecraft", "Minecraft", "grass_block");
            Add("stone_age", "Stone Age", "cobblestone");
            Add("getting_an_upgrade", "Getting an Upgrade", "stone_pickaxe");
            Add("hot_topic", "Hot Topic", "furnace");
            Add("iron_pick", "Isn't It Iron Pick", "iron_pickaxe");
            Add("deeper", "We Need to Go Deeper", "obsidian");
            Add("skys_the_limit", "Sky's the Limit", "elytra");
            Add("monster_hunter", "Monster Hunter", "iron_sword");
            Add("hot_tourist", "Hot Tourist Destinations", "lava_bucket", true);
            Add("fishy", "Fishy Business", "cod");
            Add("the_end", "The End?", "ender_eye", true);
        }

        private static void Add(string id, string title, string icon, bool challenge = false) =>
            Defs[id] = new Def { Id = id, Title = title, Icon = icon, Challenge = challenge };

        [Serializable]
        public class SaveData { public List<string> done = new List<string>(); }

        private const string File = "outercraft_advancements.json";
        private static HashSet<string> _done;
        private static readonly Queue<Def> Pending = new Queue<Def>();

        private static void Load()
        {
            if (_done != null) return;
            _done = new HashSet<string>();
            try
            {
                var d = OuterCraft.Helper.Storage.Load<SaveData>(File);
                if (d?.done != null) _done.UnionWith(d.done);
            }
            catch { }
        }

        /// Earn an advancement (once ever). Shown as soon as the HUD is up.
        public static void Grant(string id)
        {
            Load();
            if (!Defs.TryGetValue(id, out var def) || !_done.Add(id)) return;
            try { OuterCraft.Helper.Storage.Save(new SaveData { done = new List<string>(_done) }, File); } catch { }
            Pending.Enqueue(def);
            OuterCraft.Log($"advancement: {def.Title}");
        }

        public static void OnCrafted(string key)
        {
            if (key == "stone_pickaxe") Grant("getting_an_upgrade");
            else if (key == "furnace") Grant("hot_topic");
            else if (key == "iron_pickaxe") Grant("iron_pick");
        }

        public static void OnMined(string key)
        {
            if (key == "stone" || key == "cobblestone" || key == "deepslate") Grant("stone_age");
        }

        // ---------------------------------------------------------------- toast (ToastManager + AdvancementToast)

        private static Def _showing;
        private static float _shownAt;
        private static bool _outPlayed;
        private const float Slide = 0.6f, Stay = 5f;

        public static void Draw(bool visible)
        {
            float now = Time.unscaledTime;
            if (_showing == null)
            {
                if (!visible || Pending.Count == 0) return;
                _showing = Pending.Dequeue();
                _shownAt = now;
                _outPlayed = false;
                McSounds.Play2D(_showing.Challenge ? "toast.challenge" : "toast.in", 1f, 1f);
            }
            float t = now - _shownAt;
            if (t > Stay + Slide * 2) { _showing = null; return; }
            if (!_outPlayed && t > Stay + Slide) { _outPlayed = true; if (!_showing.Challenge) McSounds.Play2D("toast.out", 1f, 1f); }
            if (Event.current.type != EventType.Repaint) return;

            Begin();
            float k = t < Slide ? t / Slide : t > Stay + Slide ? 1f - (t - Stay - Slide) / Slide : 1f;
            k = Mathf.Clamp01(k);
            k = 1f - (1f - k) * (1f - k); // ease
            float x = W - 160f * k, y = 0f;
            Sprite("toast/advancement", x, y, 160, 32);
            Text(_showing.Challenge ? "Challenge Complete!" : "Advancement Made!", x + 30, y + 7,
                _showing.Challenge ? new Color(1f, 0.33f, 1f) : new Color(1f, 1f, 0f), false);
            Text(_showing.Title, x + 30, y + 18, Color.white, false);
            var icon = Items.Get(_showing.Icon);
            if (icon != null) McGui.Item(new ItemStack(icon, 1), x + 8, y + 8);
        }
    }
}
