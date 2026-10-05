using System.IO;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.World
{
    /// Materials for Minecraft geometry, from the OuterCraft shader bundle (outercraft.shaders, built
    /// from UnityShaders/): atlas x vertex colour (Minecraft's AO) with alpha cutout, lit by Outer
    /// Wilds' sun, plus Minecraft block light as emission. Falls back to Standard without it.
    public static class BlockMaterials
    {
        private static Shader _block;
        public static bool HasBundle { get; private set; }

        public static void Load(string modFolder)
        {
            var path = Path.Combine(modFolder, "outercraft.shaders");
            if (File.Exists(path))
            {
                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle != null)
                {
                    _block = bundle.LoadAsset<Shader>("Assets/OuterCraft/Block.shader");
                    HasBundle = _block != null && _block.isSupported;
                }
            }
            if (!HasBundle)
            {
                _block = Shader.Find("Standard");
                OuterCraft.Log("outercraft.shaders not found: blocks use Standard (no AO, flat lighting)");
            }
        }

        /// Draw blocks with Outer Wilds' own Standard shader (its version multiplies vertex colour into
        /// albedo), set up like its trees: same deferred lighting, ambient and reflections as the
        /// scene, so blocks sit in it instead of glowing pale. Off: OuterCraft's Minecraft shader.
        public static bool UseGameShader = true;

        public static Material ForAtlas(bool glow = false) => ForTexture(McAssets.Atlas, glow);

        public static Material ForTexture(Texture tex, bool glow = false)
        {
            if (UseGameShader)
            {
                var game = GameStandard(tex, glow);
                if (game != null) return game;
            }
            var m = new Material(_block) { mainTexture = tex };
            if (!HasBundle)
            {
                m.SetFloat("_Mode", 1);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Glossiness", 0f);
                m.renderQueue = 2450;
            }
            return m;
        }

        private static bool _warned;

        private static Material GameStandard(Texture tex, bool glow)
        {
            var sh = Shader.Find("Standard");
            if (sh == null) return null;
            var m = new Material(sh) { mainTexture = tex, name = glow ? "OuterCraft_BlocksGlow" : "OuterCraft_Blocks" };
            if (!m.HasProperty("_VertexColorAlbedo"))
            {
                if (!_warned) OuterCraft.Log("Outer Wilds' Standard shader not found: using the Minecraft shader");
                _warned = true;
                Object.Destroy(m);
                return null;
            }
            // cut-out, like leaves in Minecraft and foliage in Outer Wilds
            m.SetFloat("_Mode", 1);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", 1);
            m.SetInt("_DstBlend", 0);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cutoff", 0.5f);
            m.renderQueue = 2450;
            // matte, like Timber Hearth's wood and rock
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Glossiness", 0.01f);
            // Minecraft's tint, AO and face shading ride in the vertex colour
            m.SetFloat("_VertexColorAlbedo", 1f);
            m.EnableKeyword("_VERTEXCOLORALBEDO");
            m.EnableKeyword("_EMISSION");
            if (glow)
            {
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", new Color(1.1f, 1.0f, 0.85f));
            }
            else m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        /// First-person hand and held item: a fixed, soft "self light" (their own camera layer may
        /// not get the scene's lights), like Minecraft's hand which doesn't go black in the shade.
        public static Material ForViewModel(Texture tex)
        {
            var m = ForTexture(tex);
            if (HasBundle)
            {
                m.SetColor("_BlockLightColor", new Color(1f, 1f, 1f, 1f));
                m.SetFloat("_BlockLightStrength", 0.85f);
            }
            else
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.7f, 0.7f, 0.7f));
                m.SetTexture("_EmissionMap", tex);
            }
            return m;
        }
    }
}
