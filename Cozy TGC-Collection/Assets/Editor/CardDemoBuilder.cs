using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Generates everything the card demo needs: the card quad mesh, one material
    /// per foil tier, the card prefab and the demo scene. Re-running it rebuilds
    /// the generated assets from scratch.
    /// </summary>
    public static class CardDemoBuilder
    {
        const string ShaderPath = "Assets/Shaders/CardHolo.shader";
        const string MeshPath = "Assets/Meshes/CardQuad.asset";
        const string PrefabPath = "Assets/Prefabs/Card.prefab";
        const string ScenePath = "Assets/Scenes/CardHoloDemo.unity";
        const string MaterialFolder = "Assets/Materials";

        const string TarotFolder = CardArtLibrary.Root + "tarot_free - monochrome";
        const string SpanishFolder = CardArtLibrary.Root + "spanish deck";

        static readonly Vector2 CardPixels = new Vector2(73f, 113f);
        static readonly Vector2 CardSize = new Vector2(0.73f, 1.13f);

        static readonly string[] Tiers = { "Common", "Shiny", "Holo", "Galaxy", "Chrome" };

        [MenuItem("Tools/Cozy TGC/Build Demo Scene", false, 0)]
        public static void BuildDemo()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            PixelArtCardImporter.ApplyToAll();

            EnsureFolder("Assets/Meshes");
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Scenes");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[Cozy TGC] Shader not found at {ShaderPath}");
                return;
            }
            ReportShaderMessages(shader);

            Mesh mesh = BuildCardMesh();
            Material[] materials = BuildMaterials(shader);
            GameObject prefab = BuildPrefab(mesh, materials[0]);
            BuildScene(prefab, materials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Cozy TGC] Demo built. Open {ScenePath} and press Play.");
        }

        // -------------------------------------------------------------------
        // Mesh
        // -------------------------------------------------------------------
        static Mesh BuildCardMesh()
        {
            float w = CardSize.x * 0.5f;
            float h = CardSize.y * 0.5f;

            var mesh = new Mesh { name = "CardQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-w, -h, 0f), new Vector3(w, -h, 0f),
                new Vector3(-w,  h, 0f), new Vector3(w,  h, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            // Face normal is -Z, same as Unity's built-in Quad: the card's face
            // points at a camera sitting on -Z with an unrotated transform, which
            // is what keeps the artwork reading the right way round on screen.
            // The card's visible side is therefore -transform.forward.
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            // w = -1 so the bitangent lines up with +V of the UVs.
            var tangent = new Vector4(1f, 0f, 0f, -1f);
            mesh.tangents = new[] { tangent, tangent, tangent, tangent };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(mesh, MeshPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        }

        // -------------------------------------------------------------------
        // Materials, one per foil tier
        // -------------------------------------------------------------------
        static Material[] BuildMaterials(Shader shader)
        {
            var faces = LoadDeckTextures(TarotFolder, out Texture2D back);
            var result = new Material[Tiers.Length];

            for (int i = 0; i < Tiers.Length; i++)
            {
                string path = $"{MaterialFolder}/Card_{Tiers[i]}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);

                var mat = new Material(shader) { name = $"Card_{Tiers[i]}" };
                ConfigureShared(mat);
                ConfigureTier(mat, Tiers[i]);

                if (faces != null && faces.Length > i) mat.SetTexture("_FrontTex", faces[i]);
                if (back != null) mat.SetTexture("_BackTex", back);

                AssetDatabase.CreateAsset(mat, path);
                result[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return result;
        }

        static void ConfigureShared(Material mat)
        {
            mat.SetVector("_CardPixels", new Vector4(CardPixels.x, CardPixels.y, 0f, 0f));
            mat.SetFloat("_Cutoff", 0.5f);
            SetToggle(mat, "_PixelAA", "_PIXELAA", true);
            SetToggle(mat, "_HoloPixelate", "_HOLOPIXELATE", true);
            SetToggle(mat, "_UseMaskTex", "_MASKTEX", false);
            mat.SetFloat("_FoilBlend", 0.65f);
            mat.SetFloat("_MaskFromLuma", 0.45f);
            mat.SetFloat("_MaskContrast", 2f);
            mat.SetFloat("_MaskBias", 0.1f);
        }

        static void ConfigureTier(Material mat, string tier)
        {
            // Sensible defaults, then per tier overrides.
            mat.SetFloat("_FoilIntensity", 1f);
            mat.SetFloat("_TiltGain", 1f);
            mat.SetFloat("_RainbowStrength", 0f);
            mat.SetFloat("_SparkleStrength", 0f);
            mat.SetFloat("_ChromeStrength", 0f);
            mat.SetFloat("_SweepStrength", 0f);
            mat.SetFloat("_FresnelStrength", 0f);
            mat.SetFloat("_ColorSteps", 0f);

            switch (tier)
            {
                case "Common":
                    mat.SetFloat("_FoilIntensity", 0f);
                    break;

                case "Shiny":
                    mat.SetFloat("_RainbowStrength", 0.1f);
                    mat.SetFloat("_SweepStrength", 0.4f);
                    mat.SetFloat("_SweepWidth", 0.22f);
                    mat.SetFloat("_FresnelStrength", 0.2f);
                    break;

                case "Holo":
                    mat.SetFloat("_RainbowStrength", 0.55f);
                    mat.SetFloat("_RainbowScale", 3.5f);
                    mat.SetFloat("_RainbowTilt", 1.4f);
                    mat.SetFloat("_RainbowSteps", 8f);
                    mat.SetFloat("_SweepStrength", 0.25f);
                    mat.SetFloat("_FresnelStrength", 0.2f);
                    mat.SetFloat("_MaskFromLuma", 0.35f);
                    break;

                case "Galaxy":
                    mat.SetFloat("_RainbowStrength", 0.4f);
                    mat.SetFloat("_RainbowScale", 2f);
                    mat.SetFloat("_RainbowSteps", 6f);
                    mat.SetFloat("_SparkleStrength", 1.1f);
                    mat.SetFloat("_SparkleDensity", 30f);
                    mat.SetFloat("_SparkleSize", 0.28f);
                    mat.SetFloat("_SweepStrength", 0.2f);
                    mat.SetFloat("_FresnelStrength", 0.25f);
                    mat.SetColor("_SparkleColor", new Color(1f, 0.95f, 0.8f, 1f));
                    break;

                case "Chrome":
                    mat.SetFloat("_FoilIntensity", 0.9f);
                    mat.SetFloat("_ChromeStrength", 0.45f);
                    mat.SetFloat("_ChromeSharp", 64f);
                    mat.SetFloat("_RainbowStrength", 0.18f);
                    mat.SetFloat("_RainbowSteps", 10f);
                    mat.SetFloat("_SweepStrength", 0.5f);
                    mat.SetFloat("_SweepWidth", 0.18f);
                    mat.SetFloat("_FresnelStrength", 0.3f);
                    mat.SetFloat("_MaskFromLuma", 0.2f);
                    break;
            }
        }

        static void SetToggle(Material mat, string property, string keyword, bool on)
        {
            mat.SetFloat(property, on ? 1f : 0f);
            if (on) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }

        // -------------------------------------------------------------------
        // Prefab
        // -------------------------------------------------------------------
        static GameObject BuildPrefab(Mesh mesh, Material material)
        {
            var root = new GameObject("Card");
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(CardSize.x, CardSize.y, 0.02f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var view = root.AddComponent<CardView>();
            view.EditorBind(visual.transform, renderer, CardSize);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // -------------------------------------------------------------------
        // Scene
        // -------------------------------------------------------------------
        static void BuildScene(GameObject prefab, Material[] materials)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CardInteractor));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            // On -Z looking at the cards, far enough back that the whole row
            // still fits in a 4:3 game view.
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5.2f), Quaternion.identity);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.055f, 0.08f, 1f);
            cam.GetUniversalAdditionalCameraData();
            camGO.GetComponent<CardInteractor>().EditorBind(cam);

            var lightGO = new GameObject("Directional Light", typeof(Light));
            var light = lightGO.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightGO.transform.rotation = Quaternion.Euler(50f, 160f, 0f);

            var cards = new List<CardView>();
            var labels = new List<string>();
            float spacing = 0.88f;
            float startX = -(Tiers.Length - 1) * spacing * 0.5f;

            for (int i = 0; i < Tiers.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = $"Card_{Tiers[i]}";
                instance.transform.position = new Vector3(startX + i * spacing, 0f, 0f);
                instance.GetComponentInChildren<MeshRenderer>().sharedMaterial = materials[i];
                cards.Add(instance.GetComponent<CardView>());
                labels.Add(Tiers[i]);
            }

            var decks = new List<CardDemoController.Deck>
            {
                new CardDemoController.Deck { resourceFolder = TarotFolder, backName = "back" },
                new CardDemoController.Deck { resourceFolder = SpanishFolder, backName = "back" },
            };

            var demoGO = new GameObject("Demo", typeof(CardDemoController));
            demoGO.GetComponent<CardDemoController>()
                  .EditorBind(cam, camGO.GetComponent<CardInteractor>(), cards, labels, decks);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------
        static Texture2D[] LoadDeckTextures(string folder, out Texture2D back)
        {
            back = null;
            string dir = $"Assets/Resources/{folder}";
            if (!Directory.Exists(dir)) return null;

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
            var faces = new List<Texture2D>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                if (tex.name.ToLowerInvariant() == "back") back = tex;
                else faces.Add(tex);
            }
            faces.Sort((a, b) =>
            {
                bool na = int.TryParse(a.name, out int ia);
                bool nb = int.TryParse(b.name, out int ib);
                if (na && nb) return ia.CompareTo(ib);
                return string.CompareOrdinal(a.name, b.name);
            });
            return faces.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void ReportShaderMessages(Shader shader)
        {
            int count = ShaderUtil.GetShaderMessageCount(shader);
            if (count == 0)
            {
                Debug.Log("[Cozy TGC] Card shader compiled without messages.");
                return;
            }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
            {
                string text = $"[Cozy TGC] Shader {message.severity}: {message.message} {message.messageDetails} ({message.file}:{message.line})";
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Debug.LogError(text);
                else Debug.LogWarning(text);
            }
        }
    }
}
