using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace OuterCraft.Assets
{
    /// Reads textures straight out of the player's own Minecraft client jar. Nothing from Minecraft
    /// ships with the mod: the jar Prism (or the official launcher) downloaded is the source.
    public sealed class McJar : IDisposable
    {
        private readonly ZipArchive _zip;
        private readonly Dictionary<string, ZipArchiveEntry> _entries;
        public string Path { get; }

        private McJar(string path)
        {
            Path = path;
            _zip = ZipFile.OpenRead(path);
            _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var e in _zip.Entries) _entries[e.FullName] = e;
        }

        public void Dispose() => _zip.Dispose();

        /// Configured path, else OuterCraft's Prism copy, else the official launcher's newest version.
        public static McJar Find(string configured)
        {
            foreach (var p in Candidates(configured))
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    var jar = new McJar(p);
                    // a real client jar (version folders of mod loaders can hold stubs)
                    if (jar.Has("assets/minecraft/textures/block/stone.png") && jar.Has("assets/minecraft/blockstates/stone.json")) return jar;
                    jar.Dispose();
                }
                catch (Exception e)
                {
                    OuterCraft.Log($"couldn't open {p}: {e.Message}");
                }
            }
            return null;
        }

        private static IEnumerable<string> Candidates(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) yield return configured.Trim().Trim('"');
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return System.IO.Path.Combine(local, "OuterCraft", "Prism", "libraries", "com", "mojang", "minecraft", "26.3", "minecraft-26.3-client.jar");
            // Any Prism / MultiMC / official launcher install: newest client jar wins.
            var roots = new[]
            {
                System.IO.Path.Combine(local, "OuterCraft", "Prism", "libraries", "com", "mojang", "minecraft"),
                System.IO.Path.Combine(roaming, "PrismLauncher", "libraries", "com", "mojang", "minecraft"),
                System.IO.Path.Combine(roaming, ".minecraft", "versions"),
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                IEnumerable<string> jars;
                try { jars = Directory.GetFiles(root, "*.jar", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var j in jars.OrderByDescending(File.GetLastWriteTimeUtc)) yield return j;
            }
        }

        public bool Has(string path) => _entries.ContainsKey(path);

        public IEnumerable<string> Entries(string prefix) => _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));

        public byte[] Read(string path)
        {
            if (!_entries.TryGetValue(path, out var e)) return null;
            using (var s = e.Open())
            using (var ms = new MemoryStream((int)Math.Max(16, e.Length)))
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// A PNG as readable pixels (Color32, row 0 = top, like the file). Animated strips are cut
        /// to their first frame.
        public Image LoadImage(string path, bool firstFrame = true)
        {
            var bytes = Read(path);
            if (bytes == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false)) { UnityEngine.Object.Destroy(tex); return null; }
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32(); // bottom row first
            UnityEngine.Object.Destroy(tex);
            int outH = firstFrame && h > w && h % w == 0 ? w : h;
            var img = new Image(w, outH);
            for (int y = 0; y < outH; y++)
                for (int x = 0; x < w; x++)
                    img[x, y] = px[(h - 1 - y) * w + x];
            return img;
        }

        public Image Block(string name) => LoadImage($"assets/minecraft/textures/block/{name}.png");
        public Image Gui(string sprite) => LoadImage($"assets/minecraft/textures/gui/sprites/{sprite}.png", false);
    }

    /// Plain RGBA image, row 0 at the top (Minecraft's convention), with a few helpers.
    public sealed class Image
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public Image(int w, int h)
        {
            W = w;
            H = h;
            Px = new Color32[w * h];
        }

        public Color32 this[int x, int y]
        {
            get => Px[y * W + x];
            set => Px[y * W + x] = value;
        }

        public Image Clone()
        {
            var c = new Image(W, H);
            Array.Copy(Px, c.Px, Px.Length);
            return c;
        }

        /// Multiply RGB (grayscale grass/leaf textures get their biome colour this way).
        public Image Tinted(Color32 tint)
        {
            var c = Clone();
            for (int i = 0; i < c.Px.Length; i++)
            {
                var p = c.Px[i];
                c.Px[i] = new Color32((byte)(p.r * tint.r / 255), (byte)(p.g * tint.g / 255), (byte)(p.b * tint.b / 255), p.a);
            }
            return c;
        }

        /// Alpha-blend another image of the same size over this one.
        public Image Over(Image top)
        {
            var c = Clone();
            for (int i = 0; i < c.Px.Length && i < top.Px.Length; i++)
            {
                var t = top.Px[i];
                if (t.a == 0) continue;
                var b = c.Px[i];
                float a = t.a / 255f;
                c.Px[i] = new Color32((byte)(t.r * a + b.r * (1 - a)), (byte)(t.g * a + b.g * (1 - a)), (byte)(t.b * a + b.b * (1 - a)), (byte)Math.Max(b.a, t.a));
            }
            return c;
        }

        public Texture2D ToTexture(FilterMode filter = FilterMode.Point)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = TextureWrapMode.Clamp };
            var flipped = new Color32[Px.Length];
            for (int y = 0; y < H; y++) Array.Copy(Px, y * W, flipped, (H - 1 - y) * W, W);
            tex.SetPixels32(flipped);
            tex.Apply(false, false);
            return tex;
        }
    }
}
