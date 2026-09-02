using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Builds the walkthrough scene: two cards and a panel that takes the foil and
    /// the wear map apart one step at a time.
    ///
    /// It generates almost nothing of its own. The mesh, the prefab and the five
    /// tier materials are <see cref="CardDemoBuilder"/>'s, and the step buttons read
    /// their numbers straight off those materials rather than carrying a second
    /// copy - so run <b>Build Demo Scene</b> first in a fresh clone. The one asset
    /// made here is the unsnapped material, which exists because _PIXELAA and
    /// _HOLOPIXELATE are shader keywords and a property block cannot switch those.
    /// </summary>
    public static class CardExplainerBuilder
    {
        const string ScenePath = "Assets/Scenes/CardExplainer.unity";
        const string PrefabPath = "Assets/Prefabs/Card.prefab";
        const string CardMeshPath = "Assets/Meshes/CardQuad.asset";
        const string MaterialFolder = "Assets/Materials";
        const string MeshFolder = "Assets/Meshes";
        const string BaseMaterialPath = MaterialFolder + "/Card_Holo.mat";
        const string SmoothMaterialPath = MaterialFolder + "/Card_Explain_Smooth.mat";
        const string OverlayShaderPath = "Assets/Shaders/CardOverlay.shader";
        const string OverlayMaterialPath = MaterialFolder + "/Card_Overlay.mat";
        const string WireMeshPath = MeshFolder + "/CardWire.asset";
        const string PointsMeshPath = MeshFolder + "/CardPoints.asset";
        const string FramesMeshPath = MeshFolder + "/CardFrames.asset";

        /// <summary>Half the side of a vertex dot, in world units.</summary>
        const float DotRadius = 0.005f;
        /// <summary>Sample points the tangent frame is drawn at, across the card.</summary>
        static readonly Vector2Int FrameGrid = new Vector2Int(4, 6);
        /// <summary>
        /// Loose on purpose. Culling reads bounds, the vertex stage bends the card
        /// out of them, and the frame gizmos stick out further still - an overlay
        /// culled early is a diagram that vanishes at the edge of the screen.
        /// </summary>
        static readonly Bounds OverlayBounds = new Bounds(Vector3.zero, new Vector3(1.1f, 1.5f, 0.5f));

        static readonly Color WireColor = new Color(0.35f, 0.85f, 1f, 0.5f);
        static readonly Color PointColor = new Color(1f, 0.78f, 0.25f, 1f);
        static readonly Color[] AxisColors =
        {
            new Color(1f, 0.35f, 0.35f, 1f),    // T, along +U
            new Color(0.4f, 1f, 0.45f, 1f),     // B, along +V
            new Color(0.45f, 0.65f, 1f, 1f),    // N, out of the face
        };

        const string DeckFolder = CardArtLibrary.Root + "tarot_free - monochrome";

        static readonly string[] Tiers = { "Common", "Shiny", "Holo", "Galaxy", "Chrome" };

        /// <summary>Far enough back that both cards clear the panels in a 16:9 game view.</summary>
        const float CameraDistance = 3.4f;
        const float CardSpread = 0.62f;

        [MenuItem("Tools/Cozy TGC/Build Explainer Scene", false, 2)]
        public static void BuildExplainer()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            if (prefab == null || baseMaterial == null)
            {
                Debug.LogError($"[Cozy TGC] {PrefabPath} or {BaseMaterialPath} is missing. " +
                               "Run Tools > Cozy TGC > Build Demo Scene first.");
                return;
            }

            var cardMesh = AssetDatabase.LoadAssetAtPath<Mesh>(CardMeshPath);
            if (cardMesh == null)
            {
                Debug.LogError($"[Cozy TGC] {CardMeshPath} is missing. Run Build Demo Scene first.");
                return;
            }

            EnsureFolder("Assets/Scenes");
            EnsureFolder(MeshFolder);
            Material smooth = BuildSmoothMaterial(baseMaterial);
            Material[] tiers = LoadTiers();
            Material overlay = BuildOverlayMaterial(baseMaterial);
            Mesh[] overlayMeshes = BuildOverlayMeshes(cardMesh);
            BuildScene(prefab, baseMaterial, smooth, tiers, overlay, overlayMeshes);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Cozy TGC] Explainer built. Open {ScenePath} and press Play.");
        }

        // -------------------------------------------------------------------
        // The one generated material
        // -------------------------------------------------------------------
        /// <summary>
        /// The shipped card with its two pixel keywords off, for the step that shows
        /// what they are worth. Both are shader_feature keywords: setting the float
        /// alone changes nothing, which is exactly the mistake the step is about.
        /// </summary>
        static Material BuildSmoothMaterial(Material baseMaterial)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(SmoothMaterialPath) != null)
                AssetDatabase.DeleteAsset(SmoothMaterialPath);

            var smooth = new Material(baseMaterial) { name = "Card_Explain_Smooth" };
            smooth.SetFloat("_PixelAA", 0f);
            smooth.DisableKeyword("_PIXELAA");
            smooth.SetFloat("_HoloPixelate", 0f);
            smooth.DisableKeyword("_HOLOPIXELATE");

            AssetDatabase.CreateAsset(smooth, SmoothMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(SmoothMaterialPath);
        }

        static Material[] LoadTiers()
        {
            var tiers = new List<Material>();
            foreach (string tier in Tiers)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Card_{tier}.mat");
                if (material != null) tiers.Add(material);
                else Debug.LogWarning($"[Cozy TGC] Card_{tier}.mat is missing - its tier button will not appear.");
            }
            return tiers.ToArray();
        }

        // -------------------------------------------------------------------
        // The overlay: what the mesh actually is, drawn on top of it
        // -------------------------------------------------------------------
        /// <summary>
        /// One material for all three overlay meshes - what separates them is
        /// vertex colour, which costs nothing. The bend settings are copied off
        /// the card's own material so the wireframe bows by exactly what the card
        /// bows by.
        /// </summary>
        static Material BuildOverlayMaterial(Material baseMaterial)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[Cozy TGC] Shader not found at {OverlayShaderPath}");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath) != null)
                AssetDatabase.DeleteAsset(OverlayMaterialPath);

            var material = new Material(shader) { name = "Card_Overlay" };
            material.SetColor("_Color", Color.white);
            // 0.09 was too short to tell the three axes apart at this card size.
            material.SetFloat("_AxisLength", 0.14f);
            foreach (string property in new[]
            {
                "_CardPixels", "_CardWorldSize", "_StockColor",
                "_WearSteps", "_InkLossDesat", "_DentDepth", "_DentDisplace", "_CornerRadius",
            })
            {
                int id = Shader.PropertyToID(property);
                if (!baseMaterial.HasProperty(id) || !material.HasProperty(id)) continue;
                if (property == "_CardPixels" || property == "_CardWorldSize")
                    material.SetVector(id, baseMaterial.GetVector(id));
                else if (property == "_StockColor")
                    material.SetColor(id, baseMaterial.GetColor(id));
                else
                    material.SetFloat(id, baseMaterial.GetFloat(id));
            }

            AssetDatabase.CreateAsset(material, OverlayMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
        }

        /// <summary>Wireframe, vertex dots and tangent frames, in that order.</summary>
        static Mesh[] BuildOverlayMeshes(Mesh card)
        {
            return new[]
            {
                Save(BuildWire(card), WireMeshPath),
                Save(BuildPoints(card), PointsMeshPath),
                Save(BuildFrames(card), FramesMeshPath),
            };
        }

        /// <summary>
        /// Every edge of the card mesh, once. Derived from its triangles rather
        /// than from the grid it was built on, so changing MeshCells over in
        /// CardDemoBuilder cannot leave the wireframe describing the old mesh.
        /// </summary>
        static Mesh BuildWire(Mesh card)
        {
            Vector3[] positions = card.vertices;
            Vector2[] uvs = card.uv;
            int[] triangles = card.triangles;

            var seen = new HashSet<long>();
            var indices = new List<int>(triangles.Length);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                AddEdge(seen, indices, triangles[t], triangles[t + 1]);
                AddEdge(seen, indices, triangles[t + 1], triangles[t + 2]);
                AddEdge(seen, indices, triangles[t + 2], triangles[t]);
            }

            var mesh = new Mesh { name = "CardWire" };
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.uv2 = new Vector2[positions.Length];
            mesh.colors = Filled(positions.Length, WireColor);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            mesh.bounds = OverlayBounds;
            return mesh;
        }

        static void AddEdge(HashSet<long> seen, List<int> indices, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!seen.Add(key)) return;
            indices.Add(a);
            indices.Add(b);
        }

        /// <summary>
        /// A small square at every vertex. All four of its corners carry the
        /// SOURCE VERTEX's uv, not their own - the bend is evaluated from uv, so
        /// sharing it moves the whole dot together instead of shearing it wherever
        /// the card curves.
        /// </summary>
        static Mesh BuildPoints(Mesh card)
        {
            Vector3[] source = card.vertices;
            Vector2[] sourceUV = card.uv;
            int count = source.Length;

            var positions = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var triangles = new int[count * 6];

            for (int i = 0; i < count; i++)
            {
                int v = i * 4;
                positions[v + 0] = source[i] + new Vector3(-DotRadius, -DotRadius, 0f);
                positions[v + 1] = source[i] + new Vector3(DotRadius, -DotRadius, 0f);
                positions[v + 2] = source[i] + new Vector3(DotRadius, DotRadius, 0f);
                positions[v + 3] = source[i] + new Vector3(-DotRadius, DotRadius, 0f);
                for (int c = 0; c < 4; c++) uvs[v + c] = sourceUV[i];

                int t = i * 6;
                triangles[t + 0] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            var mesh = new Mesh { name = "CardPoints" };
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.uv2 = new Vector2[positions.Length];
            mesh.colors = Filled(positions.Length, PointColor);
            mesh.triangles = triangles;
            mesh.bounds = OverlayBounds;
            return mesh;
        }

        /// <summary>
        /// T, B and N at a grid of points on the card. Each line is two vertices
        /// at the SAME position; uv2 marks one of them as the tip, and the shader
        /// pushes it along the axis after the bend has rebuilt the frame. That is
        /// the point of the gizmo: bow the card and the normals fan out, which is
        /// CardApplyBend's last three lines made visible.
        /// </summary>
        static Mesh BuildFrames(Mesh card)
        {
            var size = new Vector2(card.bounds.size.x, card.bounds.size.y);
            int cols = FrameGrid.x, rows = FrameGrid.y;
            int samples = cols * rows;

            var positions = new Vector3[samples * 6];
            var uvs = new Vector2[samples * 6];
            var axes = new Vector2[samples * 6];
            var colors = new Color[samples * 6];
            var indices = new int[samples * 6];

            int v = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var uv = new Vector2((x + 0.5f) / cols, (y + 0.5f) / rows);
                    var point = new Vector3((uv.x - 0.5f) * size.x, (uv.y - 0.5f) * size.y, 0f);

                    for (int axis = 0; axis < 3; axis++)
                    {
                        // Base and tip start at the same place. Only uv2.y differs.
                        positions[v] = point; uvs[v] = uv; axes[v] = new Vector2(axis, 0f);
                        colors[v] = AxisColors[axis]; indices[v] = v; v++;

                        positions[v] = point; uvs[v] = uv; axes[v] = new Vector2(axis, 1f);
                        colors[v] = AxisColors[axis]; indices[v] = v; v++;
                    }
                }
            }

            var mesh = new Mesh { name = "CardFrames" };
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.uv2 = axes;
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.bounds = OverlayBounds;
            return mesh;
        }

        static Color[] Filled(int count, Color color)
        {
            var colors = new Color[count];
            for (int i = 0; i < count; i++) colors[i] = color;
            return colors;
        }

        static Mesh Save(Mesh mesh, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // -------------------------------------------------------------------
        // Scene
        // -------------------------------------------------------------------
        static void BuildScene(GameObject prefab, Material baseMaterial, Material smooth, Material[] tiers,
                               Material overlayMaterial, Mesh[] overlayMeshes)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CardInteractor));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -CameraDistance), Quaternion.identity);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.055f, 0.08f, 1f);
            cam.GetUniversalAdditionalCameraData();
            var interactor = camGO.GetComponent<CardInteractor>();
            interactor.EditorBind(cam);

            var lightGO = new GameObject("Directional Light", typeof(Light));
            var light = lightGO.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightGO.transform.rotation = Quaternion.Euler(50f, 160f, 0f);

            // Left is the control, right is the variable. Every step is read as the
            // difference between the two, so they must be the same card in every
            // way the step is not about - same material, same artwork, same size.
            CardView reference = Spawn(prefab, baseMaterial, "Card_Reference", -CardSpread);
            CardView subject = Spawn(prefab, baseMaterial, "Card_Subject", CardSpread);

            // Only the subject. The reference card is the finished article, and a
            // wireframe over it would be one more thing to read past.
            CardMeshOverlay overlay = AttachOverlay(subject, overlayMaterial, overlayMeshes);

            var explainerGO = new GameObject("Explainer", typeof(CardExplainerController));
            explainerGO.GetComponent<CardExplainerController>()
                       .EditorBind(cam, interactor, reference, subject, baseMaterial, smooth, tiers,
                                   overlay, DeckFolder);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
        }

        /// <summary>
        /// Hangs the three overlay meshes under the card's Visual, so they turn,
        /// pop and scale with it, and wires the component that keeps their bend in
        /// step with the card's.
        ///
        /// The instance is unpacked first: this adds children and a component to
        /// what is otherwise a prefab instance, and a generated scene has nothing
        /// to gain from carrying that as a pile of overrides.
        /// </summary>
        static CardMeshOverlay AttachOverlay(CardView card, Material material, Mesh[] meshes)
        {
            if (card == null || material == null || meshes == null || meshes.Length < 3) return null;

            if (PrefabUtility.IsPartOfPrefabInstance(card.gameObject))
                PrefabUtility.UnpackPrefabInstance(card.gameObject, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);

            Transform visual = card.transform.childCount > 0 ? card.transform.GetChild(0) : card.transform;
            MeshRenderer wire = OverlayChild(visual, "Wireframe", meshes[0], material);
            MeshRenderer points = OverlayChild(visual, "Vertices", meshes[1], material);
            MeshRenderer frames = OverlayChild(visual, "TangentFrames", meshes[2], material);

            var overlay = card.gameObject.AddComponent<CardMeshOverlay>();
            overlay.EditorBind(card.GetComponent<CardWear>(), wire, points, frames);
            return overlay;
        }

        static MeshRenderer OverlayChild(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Off until a step asks for it.
            renderer.enabled = false;
            return renderer;
        }

        static CardView Spawn(GameObject prefab, Material material, string name, float x)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.position = new Vector3(x, 0f, 0f);
            var renderer = instance.GetComponentInChildren<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            return instance.GetComponent<CardView>();
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------
        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
