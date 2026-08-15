using UnityEditor;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Import defaults for the pixel art card textures.
    ///
    /// Bilinear + mip maps rather than point filtering on purpose: the cards are
    /// rendered on rotating 3D quads, and the shader's CardPixelUV filter snaps
    /// sampling to texel centres itself. That keeps the pixels crisp at any angle
    /// while the hardware still filters minification, so tilted cards do not crawl.
    /// </summary>
    public class PixelArtCardImporter : AssetPostprocessor
    {
        public const string CardRoot = "Assets/Resources/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CardRoot)) return;
            var importer = (TextureImporter)assetImporter;
            // Only stamp brand new assets, never fight manual changes afterwards.
            if (!importer.importSettingsMissing) return;
            Apply(importer);
        }

        public static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.5f;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;

            // Never let Unity downscale pixel art: a sheet taller than the current
            // max size (CardPacks.png is 849x3143) would be resampled at a
            // non-integer ratio, which destroys the pixel grid.
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int longest = Mathf.Max(width, height);
            if (longest > importer.maxTextureSize)
                importer.maxTextureSize = longest <= 4096 ? 4096 : 8192;
        }

        [MenuItem("Tools/Cozy TGC/Apply Pixel Art Import Settings")]
        public static void ApplyToAllCardTextures()
        {
            int changed = ApplyToAll();
            Debug.Log($"[Cozy TGC] Pixel art import settings applied to {changed} texture(s) under {CardRoot}");
        }

        public static int ApplyToAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" });
            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                    Apply(importer);
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            return changed;
        }
    }
}
