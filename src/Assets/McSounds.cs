using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace OuterCraft.Assets
{
    /// Minecraft's own sounds, from the player's Minecraft assets folder (the launcher keeps them as
    /// hashed objects listed in assets/indexes/<n>.json, next to the libraries the jar came from).
    /// Clips are decoded by Unity (Ogg Vorbis) when the mod starts.
    public static class McSounds
    {
        public enum Group { Stone, Wood, Grass, Gravel, Sand, Cloth, Glass, Snow, Lantern }

        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        private static readonly List<AudioSource> Pool = new List<AudioSource>();
        private static GameObject _host;
        private static readonly System.Random Rng = new System.Random();

        public static float Volume = 1f;
        public static int Loaded => Clips.Count;

        // sound event -> files (without .ogg), as in Minecraft's sounds.json
        private static readonly Dictionary<string, string[]> Events = new Dictionary<string, string[]>
        {
            ["dig.stone"] = Range("dig/stone", 4), ["dig.wood"] = Range("dig/wood", 4), ["dig.grass"] = Range("dig/grass", 4),
            ["dig.gravel"] = Range("dig/gravel", 4), ["dig.sand"] = Range("dig/sand", 4), ["dig.cloth"] = Range("dig/cloth", 4),
            ["dig.snow"] = Range("dig/snow", 4), ["glass.break"] = Range("random/glass", 3),
            ["step.stone"] = Range("step/stone", 6), ["step.wood"] = Range("step/wood", 6), ["step.grass"] = Range("step/grass", 6),
            ["step.gravel"] = Range("step/gravel", 4), ["step.sand"] = Range("step/sand", 5), ["step.cloth"] = Range("step/cloth", 4),
            ["step.snow"] = Range("step/snow", 4),
            ["player.hurt"] = Range("damage/hit", 3),
            ["random.pop"] = new[] { "random/pop" },
            ["villager.idle"] = Range("mob/villager/idle", 3),
            ["toast.in"] = new[] { "ui/toast/in" }, ["toast.out"] = new[] { "ui/toast/out" },
            ["toast.challenge"] = new[] { "ui/toast/challenge_complete" },
            ["villager.hit"] = Range("mob/villager/hit", 4), ["villager.death"] = new[] { "mob/villager/death" },
            ["attack.strong"] = Range("entity/player/attack/strong", 6), ["attack.weak"] = Range("entity/player/attack/weak", 4),
            ["attack.crit"] = Range("entity/player/attack/crit", 3), ["attack.knockback"] = Range("entity/player/attack/knockback", 4),
            ["fireworks.launch"] = new[] { "fireworks/launch1" }, ["fireworks.blast"] = new[] { "fireworks/blast1" },
            ["fireworks.blast_far"] = new[] { "fireworks/blast_far1" }, ["fireworks.largeblast"] = new[] { "fireworks/largeblast1" },
            ["fireworks.twinkle"] = new[] { "fireworks/twinkle1" }, ["elytra.loop"] = new[] { "item/elytra/elytra_loop" },
            ["portal.portal"] = new[] { "portal/portal" }, ["portal.trigger"] = new[] { "portal/trigger" },
            ["portal.travel"] = new[] { "portal/travel" }, ["fire.ignite"] = new[] { "fire/ignite" },
            ["random.click"] = new[] { "random/click" },
            ["door.open"] = Range("block/wooden_door/open", 2), ["door.close"] = Range("block/wooden_door/close", 3),
            ["lantern.break"] = Range("block/lantern/break", 6), ["lantern.place"] = Range("block/lantern/place", 6),
        };

        // Minecraft, Clark, Sweden, Subwoofer Lullaby, Living Mice, Haggstrom, Danny, Key, Oxygène,
        // Dry Hands, Wet Hands, Mice on Venus
        private static readonly string[] C418 =
            { "calm1", "calm2", "calm3", "hal1", "hal2", "hal3", "hal4", "nuance1", "nuance2", "piano1", "piano2", "piano3" };
        public static readonly List<string> MusicFiles = new List<string>();

        private static string[] Range(string stem, int n) => Enumerable.Range(1, n).Select(i => stem + i).ToArray();

        // ---------------------------------------------------------------- loading

        /// Finds the assets index next to the jar's launcher install and decodes every clip we use.
        public static IEnumerator Load(string jarPath)
        {
            var assets = FindAssets(jarPath);
            if (assets == null)
            {
                OuterCraft.Log("Minecraft sound assets not found: playing silently");
                yield break;
            }
            var index = Directory.GetFiles(Path.Combine(assets, "indexes"), "*.json")
                .OrderByDescending(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out int n) ? n : 0)
                .FirstOrDefault();
            if (index == null) yield break;

            // Minimal parse of {"objects": {"minecraft/sounds/x.ogg": {"hash": "..."}}}
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(index));
            var objects = (Newtonsoft.Json.Linq.JObject)json["objects"];
            foreach (var name in Events.Values.SelectMany(v => v))
            {
                var entry = objects[$"minecraft/sounds/{name}.ogg"];
                var hash = (string)entry?["hash"];
                if (hash == null) continue;
                var path = Path.Combine(assets, "objects", hash.Substring(0, 2), hash);
                if (File.Exists(path)) Files[name] = path;
            }

            // C418's game music: only located here, streamed in when a loop starts (see McMusic)
            MusicFiles.Clear();
            foreach (var t in C418)
            {
                var hash = (string)objects[$"minecraft/sounds/music/game/{t}.ogg"]?["hash"];
                if (hash == null) continue;
                var path = Path.Combine(assets, "objects", hash.Substring(0, 2), hash);
                if (File.Exists(path)) MusicFiles.Add(path);
            }

            foreach (var kv in Files)
            {
                var uri = new Uri(kv.Value).AbsoluteUri;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, UnityEngine.AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (req.isNetworkError || req.isHttpError) continue;
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null)
                    {
                        clip.name = kv.Key;
                        Clips[kv.Key] = clip;
                    }
                }
            }
            OuterCraft.Log($"Minecraft sounds loaded: {Clips.Count} clips from {index}");
        }

        private static string FindAssets(string jarPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(jarPath))
            {
                // .../Prism/libraries/com/mojang/minecraft/<v>/x.jar -> .../Prism/assets
                var dir = Path.GetDirectoryName(jarPath);
                while (dir != null && Path.GetFileName(dir) != "libraries" && Path.GetFileName(dir) != "versions")
                    dir = Path.GetDirectoryName(dir);
                if (dir != null) candidates.Add(Path.Combine(Path.GetDirectoryName(dir), "assets"));
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            candidates.Add(Path.Combine(local, "OuterCraft", "Prism", "assets"));
            candidates.Add(Path.Combine(roaming, "PrismLauncher", "assets"));
            candidates.Add(Path.Combine(roaming, ".minecraft", "assets"));
            return candidates.FirstOrDefault(c => Directory.Exists(Path.Combine(c, "indexes")) && Directory.Exists(Path.Combine(c, "objects")));
        }

        // ---------------------------------------------------------------- blocks -> sound types (Minecraft's SoundType)

        public static Group GroupOf(BlockDef b)
        {
            if (b == null) return Group.Stone;
            var k = b.Key;
            if (k == "lantern") return Group.Lantern;
            if (k == "grass_block" || k.Contains("leaves") || b.Place == PlaceKind.Plant || k == "hay_block" || k == "tnt") return Group.Grass;
            if (k == "dirt" || k == "gravel" || k == "clay") return Group.Gravel;
            if (k == "sand") return Group.Sand;
            if (k.Contains("wool")) return Group.Cloth;
            if (k == "glass" || k == "glass_pane" || k == "ice" || k == "glowstone" || k == "sea_lantern") return Group.Glass;
            if (k == "snow_block") return Group.Snow;
            if (k.Contains("planks") || k.Contains("log") || k.StartsWith("oak_") || k == "ladder" || k == "torch" || k == "wall_torch" ||
                k == "bookshelf" || k == "crafting_table" ||
                k == "pumpkin" || k == "jack_o_lantern" || k == "melon") return Group.Wood;
            return Group.Stone;
        }

        private static string Dig(Group g) => g == Group.Glass || g == Group.Lantern ? "dig.stone" : "dig." + g.ToString().ToLowerInvariant();
        private static string Step(Group g) => g == Group.Glass || g == Group.Lantern ? "step.stone" : "step." + g.ToString().ToLowerInvariant();

        // SoundType: volume 1, pitch 1; break/place play at (volume + 1) / 2 and pitch * 0.8, steps at 0.15.
        // `frame`: what the sound rides on (a planet, the player), since Outer Wilds' world flies by
        // at hundreds of metres per second and a sound left in world space would be left behind.
        public static void Break(BlockDef b, Vector3 at, Transform frame)
        {
            var g = GroupOf(b);
            Play(g == Group.Glass ? "glass.break" : g == Group.Lantern ? "lantern.break" : Dig(g), at, frame, 1f, 0.8f);
        }

        public static void Place(BlockDef b, Vector3 at, Transform frame)
        {
            var g = GroupOf(b);
            Play(g == Group.Lantern ? "lantern.place" : Dig(g), at, frame, 1f, 0.8f);
        }

        /// While mining: the block's hit sound (its step sound) at volume 0.25, pitch 0.5, every 4 ticks.
        public static void Hit(BlockDef b, Vector3 at, Transform frame) => Play(Step(GroupOf(b)), at, frame, 0.25f, 0.5f);

        /// Picking up an item.
        public static void Pop(Vector3 at, Transform frame) =>
            Play("random.pop", at, frame, 0.2f, ((float)(Rng.NextDouble() - Rng.NextDouble()) * 0.7f + 1f) * 2f);

        public static void Door(bool open, Vector3 at, Transform frame) =>
            Play(open ? "door.open" : "door.close", at, frame, 1f, (float)Rng.NextDouble() * 0.1f + 0.9f);

        public static AudioClip Clip(string evt) => Events.TryGetValue(evt, out var f) ? Pick(f) : null;

        public static void Click() => Play2D("random.click", 0.25f, 1f);

        public static void Play2D(string evt, float volume, float pitch)
        {
            if (!Events.TryGetValue(evt, out var files)) return;
            var clip = Pick(files);
            if (clip == null) return;
            var src = Source();
            src.transform.SetParent(_host.transform, false);
            src.spatialBlend = 0f;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * Volume);
            src.pitch = pitch;
            src.Play();
        }

        public static void Footstep(BlockDef b, Vector3 at, Transform frame) => Play(Step(GroupOf(b)), at, frame, 0.15f, 1f);

        public static void Hurt(Vector3 at, Transform frame) =>
            Play("player.hurt", at, frame, 1f, 1f + (float)(Rng.NextDouble() - Rng.NextDouble()) * 0.2f);

        // ---------------------------------------------------------------- playback

        public static void Play(string evt, Vector3 at, Transform frame, float volume, float pitch, float range = 16f)
        {
            if (!Events.TryGetValue(evt, out var files)) return;
            var clip = Pick(files);
            if (clip == null) return;
            var src = Source();
            src.transform.SetParent(frame != null ? frame : _host.transform, true);
            src.transform.position = at;
            src.spatialBlend = 1f;
            src.maxDistance = range;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * Volume);
            src.pitch = pitch;
            src.Play();
        }

        private static AudioClip Pick(string[] files)
        {
            for (int tries = 0; tries < files.Length; tries++)
                if (Clips.TryGetValue(files[Rng.Next(files.Length)], out var c)) return c;
            foreach (var f in files) if (Clips.TryGetValue(f, out var c)) return c;
            return null;
        }

        private static AudioSource Source()
        {
            if (_host == null)
            {
                _host = new GameObject("OuterCraft_Sounds");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                Pool.Clear();
            }
            Pool.RemoveAll(p => p == null); // their planet went away with the last loop
            foreach (var s in Pool) if (!s.isPlaying) return s;
            var go = new GameObject("snd");
            go.transform.SetParent(_host.transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;           // positional, like Minecraft
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1f;
            a.maxDistance = 16f;           // Minecraft's attenuation distance
            a.dopplerLevel = 0f;
            Pool.Add(a);
            return a;
        }
    }
}
