using UnityEditor;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Import defaults for the UI kit under Assets/Resources/UI.
    ///
    /// The opposite of <see cref="PixelArtCardImporter"/> on every point that matters,
    /// because these textures do the opposite job. Cards are 3D quads that tilt, so
    /// they want bilinear filtering and mip maps and let the shader keep them crisp.
    /// The kit is drawn flat in screen space at whole multiples, so it wants point
    /// filtering and no mip maps - a mip chain on a 16 px icon is a blurred icon.
    ///
    /// Readable is the load-bearing one: <see cref="UiSheet"/> cuts pieces out of these
    /// sheets with GetPixels, because IMGUI can only nine-slice a whole texture.
    /// </summary>
    public class UiKitImporter : AssetPostprocessor
    {
        public const string UiRoot = "Assets/Resources/UI/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiRoot)) return;
            var importer = (TextureImporter)assetImporter;
            // Only stamp brand new assets, never fight manual changes afterwards.
            if (!importer.importSettingsMissing) return;
            Apply(importer);
        }

        public static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.anisoLevel = 0;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;

            // Never let Unity downscale pixel art - a non-integer resample destroys the
            // grid these sheets are measured on.
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int longest = Mathf.Max(width, height);
            if (longest > importer.maxTextureSize)
                importer.maxTextureSize = longest <= 4096 ? 4096 : 8192;
        }

        [MenuItem("Tools/Cozy TGC/Apply UI Kit Import Settings")]
        public static void ApplyToAllUiTextures()
        {
            int changed = ApplyToAll();
            Debug.Log($"[Cozy TGC] UI kit import settings applied to {changed} texture(s) under {UiRoot}");
        }

        public static int ApplyToAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { UiRoot.TrimEnd('/') });
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
