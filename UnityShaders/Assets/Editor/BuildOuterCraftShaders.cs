// Unity 2019.4 (the version Outer Wilds is built with). Menu: OuterCraft > Build shader bundle.
// Copy Build/outercraft.shaders next to OuterCraft.dll in the mod folder.
using System.IO;
using UnityEditor;

public static class BuildOuterCraftShaders
{
    [MenuItem("OuterCraft/Build shader bundle")]
    public static void Build()
    {
        Directory.CreateDirectory("Build");
        var build = new AssetBundleBuild
        {
            assetBundleName = "outercraft.shaders",
            assetNames = new[]
            {
                "Assets/OuterCraft/Block.shader",
                "Assets/OuterCraft/BlockTranslucent.shader",
                "Assets/OuterCraft/Overlay.shader",
            },
        };
        BuildPipeline.BuildAssetBundles("Build", new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        EditorUtility.RevealInFinder("Build/outercraft.shaders");
    }

    // Batch mode: Unity.exe -batchmode -quit -projectPath <this folder> -executeMethod BuildOuterCraftShaders.Build
}
