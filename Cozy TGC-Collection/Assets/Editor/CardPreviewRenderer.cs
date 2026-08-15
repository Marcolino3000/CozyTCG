using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Renders the demo scene at several card angles into one contact sheet, so
    /// the foil can be judged without entering play mode. Writes CardFoilPreview.png
    /// next to the Assets folder (override with the COZY_PREVIEW_PATH env var).
    /// </summary>
    public static class CardPreviewRenderer
    {
        const string ScenePath = "Assets/Scenes/CardHoloDemo.unity";
        const int FrameWidth = 900;
        const int FrameHeight = 506;

        // yaw / pitch pairs applied to every card visual before each render
        static readonly Vector2[] Angles =
        {
            new Vector2(0f, 0f),      // at rest
            new Vector2(-24f, 8f),    // hover tilt
            new Vector2(42f, -12f),   // hard turn
            new Vector2(180f, 0f),    // flipped, showing the card back
        };

        [MenuItem("Tools/Cozy TGC/Render Foil Preview", false, 1)]
        public static void RenderPreview()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogError("[Cozy TGC] No camera in the demo scene.");
                return;
            }

            var cards = Object.FindObjectsByType<CardView>(FindObjectsSortMode.InstanceID);
            if (cards.Length == 0) Debug.LogWarning("[Cozy TGC] No cards found in the scene.");

            var shader = Shader.Find("Cozy TGC/Card Holo");
            Debug.Log($"[Cozy TGC] Shader found: {shader != null}, supported: {shader != null && shader.isSupported}");

            var rt = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var sheet = new Texture2D(FrameWidth * 2, FrameHeight * 2, TextureFormat.RGBA32, false);
            var frame = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false);

            // Warm up: the very first render in a fresh batch session can land
            // before every texture is fully resident.
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            for (int i = 0; i < Angles.Length; i++)
            {
                foreach (var card in cards)
                {
                    var visual = card.transform.Find("Visual");
                    if (visual != null) visual.localRotation = Quaternion.Euler(Angles[i].y, Angles[i].x, 0f);
                }

                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                frame.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
                frame.Apply();
                RenderTexture.active = previous;

                int col = i % 2;
                int row = i / 2;
                sheet.SetPixels(col * FrameWidth, (1 - row) * FrameHeight, FrameWidth, FrameHeight, frame.GetPixels());
            }

            sheet.Apply();

            string outPath = System.Environment.GetEnvironmentVariable("COZY_PREVIEW_PATH");
            if (string.IsNullOrEmpty(outPath))
                outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CardFoilPreview.png"));
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());

            Object.DestroyImmediate(frame);
            Object.DestroyImmediate(sheet);
            rt.Release();
            Object.DestroyImmediate(rt);

            // Leave the scene as we found it.
            foreach (var card in cards)
            {
                var visual = card.transform.Find("Visual");
                if (visual != null) visual.localRotation = Quaternion.identity;
            }

            Debug.Log($"[Cozy TGC] Foil preview written to {outPath}");
        }
    }
}
