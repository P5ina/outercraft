using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace OuterCraft.Assets
{
    /// The player's own Minecraft skin. "skin" = "auto": the account signed in to the Minecraft
    /// launcher or Prism Launcher; a Minecraft username: that player's skin; a path to a .png:
    /// that file; steve, alex, ...: the default skin from the jar (loaded with the textures).
    /// Skins come from Mojang's session server, like the game's own, and are cached in the mod
    /// folder for when there's no internet. Slim (Alex-style) arms come with the skin.
    public static class McSkin
    {
        public static string AccountName { get; private set; }

        private static readonly string[] Defaults = { "steve", "alex", "ari", "efe", "kai", "makena", "noor", "sunny", "zuri" };

        public static bool IsDefault(string setting) =>
            string.IsNullOrWhiteSpace(setting) || Defaults.Contains(setting.Trim().ToLowerInvariant());

        public static IEnumerator Load(string setting, string cacheDir)
        {
            setting = (setting ?? "").Trim();
            if (IsDefault(setting) && setting.Length > 0) yield break;

            // a file
            if (setting.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(setting)) Apply(File.ReadAllBytes(setting), setting.IndexOf("slim", StringComparison.OrdinalIgnoreCase) >= 0, setting);
                else OuterCraft.Log("skin file not found: " + setting);
                yield break;
            }

            // "auto": whoever is signed in to a launcher
            string name = setting, uuid = null, url = null;
            bool? slim = null;
            if (setting.Length == 0 || setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                FindAccount(out name, out uuid, out url, out slim);
                if (name == null && uuid == null)
                {
                    OuterCraft.Log("no Minecraft account found in a launcher: default skin");
                    yield break;
                }
                AccountName = name;
            }

            var cache = Path.Combine(cacheDir, "skin_cache_" + Safe(name ?? uuid) + ".png");

            // the profile: username -> uuid -> textures
            if (url == null)
            {
                if (uuid == null)
                {
                    var req = UnityWebRequest.Get("https://api.mojang.com/users/profiles/minecraft/" + UnityWebRequest.EscapeURL(name));
                    yield return req.SendWebRequest();
                    if (Ok(req)) uuid = (string)JObject.Parse(req.downloadHandler.text)["id"];
                    req.Dispose();
                }
                if (uuid != null)
                {
                    var req = UnityWebRequest.Get("https://sessionserver.mojang.com/session/minecraft/profile/" + uuid.Replace("-", ""));
                    yield return req.SendWebRequest();
                    if (Ok(req))
                    {
                        try
                        {
                            var prof = JObject.Parse(req.downloadHandler.text);
                            var value = (string)prof["properties"]?.FirstOrDefault(p => (string)p["name"] == "textures")?["value"];
                            if (value != null)
                            {
                                var tex = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(value)))["textures"]?["SKIN"];
                                url = (string)tex?["url"];
                                slim = (string)tex?["metadata"]?["model"] == "slim";
                            }
                            if (name == null) name = (string)prof["name"];
                        }
                        catch (Exception e) { OuterCraft.Log("skin profile: " + e.Message); }
                    }
                    req.Dispose();
                }
            }

            if (url != null)
            {
                var req = UnityWebRequest.Get(url.Replace("http://", "https://"));
                yield return req.SendWebRequest();
                if (Ok(req))
                {
                    var bytes = req.downloadHandler.data;
                    if (Apply(bytes, slim == true, name))
                    {
                        try
                        {
                            File.WriteAllBytes(cache, bytes);
                            File.WriteAllText(cache + ".model", slim == true ? "slim" : "wide");
                        }
                        catch { }
                    }
                    req.Dispose();
                    yield break;
                }
                req.Dispose();
            }

            // offline: last time's skin
            if (File.Exists(cache))
            {
                bool cachedSlim = File.Exists(cache + ".model") && File.ReadAllText(cache + ".model").Trim() == "slim";
                Apply(File.ReadAllBytes(cache), cachedSlim, name + " (cached)");
            }
            else OuterCraft.Log("couldn't get the skin of " + (name ?? uuid) + ": default skin");
        }

        private static bool Ok(UnityWebRequest r) => !r.isNetworkError && !r.isHttpError;

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s ?? "skin") sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        // ---------------------------------------------------------------- launcher accounts

        private static void FindAccount(out string name, out string uuid, out string url, out bool? slim)
        {
            name = uuid = url = null;
            slim = null;
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Prism Launcher (and MultiMC-likes): accounts.json with the profile and its skin
            foreach (var path in new[]
            {
                Path.Combine(roaming, "PrismLauncher", "accounts.json"),
                Path.Combine(local, "OuterCraft", "Prism", "accounts.json"),
                Path.Combine(roaming, "PolyMC", "accounts.json"),
                Path.Combine(roaming, "MultiMC", "accounts.json"),
            })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var js = JObject.Parse(File.ReadAllText(path));
                    var accounts = js["accounts"] as JArray;
                    if (accounts == null) continue;
                    var acc = accounts.FirstOrDefault(a => (bool?)a["active"] == true) ?? accounts.FirstOrDefault(a => a["profile"] != null);
                    var prof = acc?["profile"];
                    if (prof == null || prof["name"] == null) continue;
                    name = (string)prof["name"];
                    uuid = (string)prof["id"];
                    var skin = prof["skin"];
                    if (skin != null && !string.IsNullOrEmpty((string)skin["url"]))
                    {
                        url = (string)skin["url"];
                        slim = ((string)skin["variant"] ?? "").ToUpperInvariant() == "SLIM";
                    }
                    return;
                }
                catch (Exception e) { OuterCraft.Log($"reading {path}: {e.Message}"); }
            }

            // the official launcher
            foreach (var file in new[] { "launcher_accounts_microsoft_store.json", "launcher_accounts.json" })
            {
                var path = Path.Combine(roaming, ".minecraft", file);
                try
                {
                    if (!File.Exists(path)) continue;
                    var js = JObject.Parse(File.ReadAllText(path));
                    var accounts = js["accounts"] as JObject;
                    if (accounts == null) continue;
                    var active = (string)js["activeAccountLocalId"];
                    var acc = (active != null ? accounts[active] : null) ??
                              accounts.Properties().Select(p => p.Value).FirstOrDefault(a => a["minecraftProfile"] != null);
                    var prof = acc?["minecraftProfile"];
                    if (prof == null || prof["name"] == null) continue;
                    name = (string)prof["name"];
                    uuid = (string)prof["id"];
                    return;
                }
                catch (Exception e) { OuterCraft.Log($"reading {path}: {e.Message}"); }
            }
        }

        // ---------------------------------------------------------------- applying

        /// Into the skin texture everything already draws with (Steve, the hand), so nothing
        /// needs rebuilding beyond the arm width.
        private static bool Apply(byte[] png, bool slim, string what)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, png, false)) { UnityEngine.Object.Destroy(tex); return false; }
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            if (w != 64 || (h != 64 && h != 32)) { OuterCraft.Log($"skin {what}: unexpected size {w}x{h}"); return false; }
            var img = new Image(64, 64);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < 64; x++)
                    img[x, y] = px[(h - 1 - y) * 64 + x];
            if (h == 32) Legacy(img);

            if (McAssets.Skin == null) McAssets.Skin = img.ToTexture();
            else
            {
                var flipped = new Color32[64 * 64];
                for (int y = 0; y < 64; y++) Array.Copy(img.Px, y * 64, flipped, (63 - y) * 64, 64);
                if (McAssets.Skin.width != 64 || McAssets.Skin.height != 64) McAssets.Skin.Resize(64, 64);
                McAssets.Skin.SetPixels32(flipped);
                McAssets.Skin.Apply(false, false);
            }
            McAssets.SlimArms = slim;
            OuterCraft.Log($"skin: {what}{(slim ? " (slim)" : "")}");
            return true;
        }

        /// SkinTextureDownloader.processLegacySkin: a 64x32 skin gets its left arm and leg mirrored
        /// from the right ones.
        private static void Legacy(Image img)
        {
            void Copy(int sx, int sy, int dx, int dy, int w, int h)
            {
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                        img[sx + dx + (w - 1 - i), sy + dy + j] = img[sx + i, sy + j];
            }
            Copy(4, 16, 16, 32, 4, 4);
            Copy(8, 16, 16, 32, 4, 4);
            Copy(0, 20, 24, 32, 4, 12);
            Copy(4, 20, 16, 32, 4, 12);
            Copy(8, 20, 8, 32, 4, 12);
            Copy(12, 20, 16, 32, 4, 12);
            Copy(44, 16, -8, 32, 4, 4);
            Copy(48, 16, -8, 32, 4, 4);
            Copy(40, 20, 0, 32, 4, 12);
            Copy(44, 20, -8, 32, 4, 12);
            Copy(48, 20, -16, 32, 4, 12);
            Copy(52, 20, -8, 32, 4, 12);
        }
    }
}
