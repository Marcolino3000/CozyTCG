using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Generates the pack opening scene: the two halves of the wrapper mesh split
    /// along a torn seam, the pack material, the pack prefab and the scene itself.
    /// Re-running it rebuilds the generated assets from scratch.
    ///
    /// Needs Assets/Prefabs/Card.prefab and the Card_* materials, which
    /// Tools > Cozy TGC > Build Demo Scene produces.
    /// </summary>
    public static class CardPackBuilder
    {
        const string ShaderPath = "Assets/Shaders/CardPack.shader";
        const string SlotShaderPath = "Assets/Shaders/CardSlot.shader";
        const string SpriteShaderPath = "Assets/Shaders/SpriteSheet.shader";
        const string SheetPath = "Assets/Resources/" + CardArtLibrary.Root + "CardPacks/CardPacks.png";
        const string BodyMeshPath = "Assets/Meshes/PackBody.asset";
        const string LidMeshPath = "Assets/Meshes/PackLid.asset";
        const string UnitQuadPath = "Assets/Meshes/UnitQuad.asset";
        const string MaterialPath = "Assets/Materials/CardPack.mat";
        const string SlotMaterialPath = "Assets/Materials/CardSlot.mat";
        const string PocketMaterialPath = "Assets/Materials/AlbumPocket.mat";
        const string TraySlotMaterialPath = "Assets/Materials/PackTraySlot.mat";
        /// <summary>An album sheet used to be a dark panel of its own. The book art draws
        /// them now, so the material it needed is cleaned up rather than left to rot.</summary>
        const string StaleAlbumPageMaterialPath = "Assets/Materials/AlbumPage.mat";
        /// <summary>
        /// Authored content, so it is made once and never overwritten - the same deal
        /// Assets/Dialogs gets. Everything else this builder touches is regenerated.
        /// </summary>
        const string ShopCatalogPath = "Assets/Shop/ShopCatalog.asset";
        const string PrefabPath = "Assets/Prefabs/CardPack.prefab";
        const string SlotPrefabPath = "Assets/Prefabs/CardSlot.prefab";
        const string CardPrefabPath = "Assets/Prefabs/Card.prefab";
        const string CardMeshPath = "Assets/Meshes/CardQuad.asset";
        const string ScenePath = "Assets/Scenes/PackOpening.unity";

        const float PixelsPerUnit = 100f;

        static readonly Vector2 PackSize = new Vector2(
            CardPackSheet.PackWidth / PixelsPerUnit, CardPackSheet.PackHeight / PixelsPerUnit);

        /// <summary>
        /// The seam sits one pixel below the crimped seal, so the strip that peels
        /// away is exactly the sealed part of the wrapper.
        /// </summary>
        const int TearPixelsFromTop = CardPackSheet.TopSealPixels + 1;

        /// <summary>One column per two pixels, which keeps the rip on the pixel grid.</summary>
        const int TearColumns = CardPackSheet.PackWidth / 2;
        const float TearAmplitude = 0.03f;

        /// <summary>Body and lid overlap by a pixel so no hairline shows along the seam.</summary>
        const float SeamOverlap = 0.01f;

        static readonly string[] Tiers = { "Common", "Shiny", "Holo", "Galaxy", "Chrome" };

        const int CardsPerPack = 5;
        static readonly Vector3 PackPosition = new Vector3(0f, 0.6f, -0.08f);
        static readonly Vector3 DeckPosition = new Vector3(0f, 0.6f, 0f);

        // Row of piles along the bottom. Five at 0.88 apart still clears a 4:3 view,
        // and the slot frames sit just behind the cards that land on them.
        const int SlotCount = 5;
        const int DefaultSlot = 2;
        const float SlotSpacing = 0.88f;
        static readonly Vector3 SlotRowPosition = new Vector3(0f, -1.02f, 0.02f);
        /// <summary>Frame is the card quad grown a little, so a card sits inside it.</summary>
        const float SlotFrameScale = 1.08f;
        /// <summary>Size of CardQuad.asset, which the album's pockets are scaled against.</summary>
        static readonly Vector2 CardQuadSize = new Vector2(0.73f, 1.13f);

        /// <summary>Pocket frames draw over the book art, which is alpha tested and so
        /// already down by the time the transparent queue starts.</summary>
        const int SlotQueue = 3020;

        // Three collector albums, one per book on the RADL sheet, each with its own
        // pages and therefore its own cards.
        //
        // Their geometry is measured off the art rather than chosen: a page is the
        // parchment the open book draws, and a 2x2 grid of pockets is what fits on
        // one. Pages come in pairs - even ones hang left of the spine, odd ones
        // right - so AlbumPages stays even.
        const int AlbumPages = 12;
        const int AlbumColumns = 2;
        const int AlbumRows = 2;
        static readonly Vector3 AlbumPosition = new Vector3(0f, 0.65f, 0f);
        static readonly Vector2 AlbumPageHalfSize = new Vector2(
            AlbumBookSheet.PageWidth * 0.5f / PixelsPerUnit,
            AlbumBookSheet.PageHeight * 0.5f / PixelsPerUnit);
        /// <summary>Spine to the middle of a sheet.</summary>
        const float AlbumPageOffset = AlbumBookSheet.PageOffset / PixelsPerUnit;
        static readonly Vector2 AlbumPocketSpacing = new Vector2(
            AlbumPageHalfSize.x * 2f / AlbumColumns, AlbumPageHalfSize.y * 2f / AlbumRows);
        /// <summary>A pocket as a fraction of the room it has, so a hair of parchment shows around it.</summary>
        const float AlbumPocketMargin = 0.97f;
        /// <summary>
        /// Scaled against the slot frame rather than the card inside it: the frame is
        /// the card grown by SlotFrameScale, and with four pockets to a page fitting
        /// the card alone would leave neighbouring frames overlapping, doubling up
        /// their fill into a seam down the middle of the page.
        /// </summary>
        static readonly float AlbumPocketScale = Mathf.Min(
            AlbumPocketSpacing.x / (CardQuadSize.x * SlotFrameScale),
            AlbumPocketSpacing.y / (CardQuadSize.y * SlotFrameScale)) * AlbumPocketMargin;
        /// <summary>Book art behind the pockets, which are in turn behind the cards.</summary>
        const float AlbumBookDepth = 0.06f;

        // The book quad against the album's origin, which is the spine rather than
        // the middle of the art's cell. y is flipped on the way out: the sheet counts
        // its rows down from the top of the cell and the scene counts up.
        static readonly Vector2 AlbumBookOffset = new Vector2(
            (AlbumBookSheet.BookCellWidth * 0.5f - AlbumBookSheet.OriginX) / PixelsPerUnit,
            (AlbumBookSheet.OriginY - AlbumBookSheet.BookCellHeight * 0.5f) / PixelsPerUnit);
        static readonly Vector2 AlbumBookSize = new Vector2(
            AlbumBookSheet.BookCellWidth / PixelsPerUnit, AlbumBookSheet.BookCellHeight / PixelsPerUnit);

        // The drawn book inside that cell, which is what an album is fitted by.
        static readonly Vector2 AlbumArtSize = new Vector2(
            AlbumBookSheet.ArtWidth / PixelsPerUnit, AlbumBookSheet.ArtHeight / PixelsPerUnit);
        static readonly Vector2 AlbumArtCenter = new Vector2(
            (AlbumBookSheet.ArtLeft + AlbumBookSheet.ArtWidth * 0.5f - AlbumBookSheet.OriginX) / PixelsPerUnit,
            (AlbumBookSheet.OriginY - AlbumBookSheet.ArtTop - AlbumBookSheet.ArtHeight * 0.5f) / PixelsPerUnit);

        // The shelf of books down the left. It pins itself to the edge of the frame
        // at run time, because the camera never moves but its aspect is not fixed,
        // so only the depth is authored here: in front of the albums, behind a card
        // in hand.
        const float ShelfIconSize = 0.44f;
        const float ShelfIconSpacing = 0.56f;
        const float ShelfMargin = 0.16f;
        const float ShelfDepth = -0.3f;

        // The drawer of unopened packs, hanging under the shelf on the same column.
        // Narrow enough that it stays inside the width the shelf already reserves, or
        // a fitted album would slide under it.
        static readonly Vector2 TrayPackSize = new Vector2(0.28f, 0.51f);
        const float TrayFrameScale = 1.1f;

        /// <summary>Ordering the shop's wanted ads read the artwork by.
        /// The tarot folder is 00-21, the majors in Rider-Waite order, so 08 is
        /// Strength and 11 is Justice - and 20 is the Judgement the ads ask for.</summary>
        static readonly string[] TarotNames =
        {
            "The Fool", "The Magician", "The High Priestess", "The Empress", "The Emperor",
            "The Hierophant", "The Lovers", "The Chariot", "Strength", "The Hermit",
            "Wheel of Fortune", "Justice", "The Hanged Man", "Death", "Temperance",
            "The Devil", "The Tower", "The Star", "The Moon", "The Sun",
            "Judgement", "The World",
        };

        /// <summary>The Spanish folder is 1-40 sorted numerically: four suits of ten,
        /// in this order, each running 1-7 and then the three court cards.</summary>
        static readonly string[] SpanishSuits = { "Oros", "Copas", "Espadas", "Bastos" };
        static readonly string[] SpanishRanks =
            { "1", "2", "3", "4", "5", "6", "7", "Sota", "Caballo", "Rey" };
        const int SpanishSuitSize = 10;

        // A tenth of the cards in a pack, which at five cards a pack is one tarot card
        // every second pack.
        const float TarotPackWeight = 1f;
        const float SpanishPackWeight = 9f;

        // The camera never moves. It sits where the row of piles and a full stack both
        // fit, and the pack rig does the travelling instead: while the wrapper is sealed
        // the rig slides towards the camera, which is what used to be a dolly in. The
        // offset is exactly the old sealed shot turned inside out, so the sealed pack
        // fills the frame the way it always did. The album fits itself into what is left
        // of the frame at run time, so it needs no shot of its own.
        static readonly Vector3 Framing = new Vector3(0f, 0f, -5.6f);
        static readonly Vector3 SealedRigOffset = new Vector3(0f, -0.62f, -2.52f);

        [MenuItem("Tools/Cozy TGC/Build Pack Opening Scene", false, 1)]
        public static void BuildPackScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[Cozy TGC] Pack shader not found at {ShaderPath}");
                return;
            }

            var slotShader = AssetDatabase.LoadAssetAtPath<Shader>(SlotShaderPath);
            if (slotShader == null)
            {
                Debug.LogError($"[Cozy TGC] Slot shader not found at {SlotShaderPath}");
                return;
            }

            var spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(SpriteShaderPath);
            if (spriteShader == null)
            {
                Debug.LogError($"[Cozy TGC] Sprite sheet shader not found at {SpriteShaderPath}");
                return;
            }

            var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
            var cardMesh = AssetDatabase.LoadAssetAtPath<Mesh>(CardMeshPath);
            if (cardPrefab == null || cardMesh == null)
            {
                Debug.LogError($"[Cozy TGC] {CardPrefabPath} / {CardMeshPath} missing - " +
                               "run Tools > Cozy TGC > Build Demo Scene first.");
                return;
            }

            EnsureFolder("Assets/Meshes");
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Scenes");

            float tearY = PackSize.y * 0.5f - TearPixelsFromTop / PixelsPerUnit;
            float[] seam = BuildSeam(TearColumns, TearAmplitude);

            Mesh body = BuildPackMesh(seam, tearY, false, BodyMeshPath, "PackBody");
            Mesh lid = BuildPackMesh(seam, tearY, true, LidMeshPath, "PackLid");
            Mesh unitQuad = BuildUnitQuad();
            Material material = BuildMaterial(shader);
            Material slotMaterial = BuildSlotMaterial(slotShader);
            if (AssetDatabase.LoadAssetAtPath<Material>(StaleAlbumPageMaterialPath) != null)
                AssetDatabase.DeleteAsset(StaleAlbumPageMaterialPath);
            Material pocketMaterial = BuildPocketMaterial(slotShader);
            Material traySlotMaterial = BuildTraySlotMaterial(slotShader);
            Material trayPackMaterial = BuildTrayPackMaterial(spriteShader);
            Material[] bookMaterials = BuildBookMaterials(spriteShader);
            Material[] iconMaterials = BuildIconMaterials(spriteShader);
            GameObject prefab = BuildPrefab(body, lid, material, tearY);
            GameObject slotPrefab = BuildSlotPrefab(cardMesh, slotMaterial);
            BuildScene(prefab, slotPrefab, cardPrefab, unitQuad, pocketMaterial,
                       traySlotMaterial, trayPackMaterial, bookMaterials, iconMaterials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Cozy TGC] Pack opening scene built. Open {ScenePath} and press Play.");
        }

        // -------------------------------------------------------------------
        // Seam
        // -------------------------------------------------------------------
        /// <summary>
        /// Height offset of the tear at every column boundary. Two scales of noise:
        /// a straight cut with jitter on top reads as a print artefact, a rip needs
        /// coarse waviness as well. Snapped to whole pixels so the seam stays on
        /// the art's grid, and pinned flat at both ends so the pack keeps a square
        /// silhouette while it is still sealed.
        /// </summary>
        static float[] BuildSeam(int columns, float amplitude)
        {
            var seam = new float[columns];
            var rng = new System.Random(20260814);
            float phase = (float)rng.NextDouble() * 10f;

            for (int i = 0; i < columns; i++)
            {
                float t = i / (float)(columns - 1);
                float coarse = Mathf.Sin(t * 6.3f + phase) * 0.55f + Mathf.Sin(t * 15.7f + phase * 2.1f) * 0.25f;
                float fine = (float)rng.NextDouble() * 2f - 1f;
                float value = (coarse * 0.62f + fine * 0.38f) * amplitude;
                seam[i] = Mathf.Round(value * PixelsPerUnit) / PixelsPerUnit;
            }

            seam[0] = 0f;
            seam[columns - 1] = 0f;
            return seam;
        }

        // -------------------------------------------------------------------
        // Meshes
        // -------------------------------------------------------------------
        /// <summary>
        /// Builds one half of the wrapper as a run of column quads with flat tops,
        /// so the rip is a pixel staircase rather than a smooth diagonal. Both
        /// halves share the same seam array, which is what makes them interlock.
        ///
        /// The lid is authored around its own pivot on the seam so it can hinge
        /// there; UVs stay in cell space either way (0..1 over one pack), and the
        /// shader maps them onto the sheet through _PackRect.
        /// </summary>
        static Mesh BuildPackMesh(float[] seam, float tearY, bool isLid, string path, string name)
        {
            int columns = seam.Length;
            float w = PackSize.x * 0.5f;
            float h = PackSize.y * 0.5f;
            float pivot = isLid ? tearY : 0f;
            float hingeSpan = isLid ? h - tearY : h + tearY;

            var vertices = new Vector3[columns * 4];
            var uv = new Vector2[vertices.Length];
            var uv2 = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var triangles = new int[columns * 6];

            // Face normal is -Z, same as the card mesh: the visible side is
            // -transform.forward, with the camera on -Z and an unrotated transform.
            var normal = Vector3.back;
            var tangent = new Vector4(1f, 0f, 0f, -1f);

            for (int i = 0; i < columns; i++)
            {
                float x0 = Mathf.Lerp(-w, w, i / (float)columns);
                float x1 = Mathf.Lerp(-w, w, (i + 1) / (float)columns);
                float cut = tearY + seam[i];

                // Near edge is the seam, far edge is the outside of the pack.
                float near = isLid ? cut : cut + SeamOverlap;
                float far = isLid ? h : -h;
                float span = Mathf.Max(Mathf.Abs(far - near), 1e-4f);

                int v = i * 4;
                vertices[v + 0] = new Vector3(x0, near - pivot, 0f);
                vertices[v + 1] = new Vector3(x1, near - pivot, 0f);
                vertices[v + 2] = new Vector3(x0, far - pivot, 0f);
                vertices[v + 3] = new Vector3(x1, far - pivot, 0f);

                float u0 = (x0 + w) / PackSize.x;
                float u1 = (x1 + w) / PackSize.x;
                float vNear = (near + h) / PackSize.y;
                float vFar = (far + h) / PackSize.y;

                uv[v + 0] = new Vector2(u0, vNear);
                uv[v + 1] = new Vector2(u1, vNear);
                uv[v + 2] = new Vector2(u0, vFar);
                uv[v + 3] = new Vector2(u1, vFar);

                // x = distance from this column's own cut in texture pixels, which is
                // what the shader steps the raw torn lip along - hence pixels, not units.
                //
                // y is the hinge the peel swings on, and it is deliberately a shared
                // linear function of the undisplaced height rather than a per column
                // 0..1 ramp. Neighbouring columns are cut at different heights, so a
                // per column ramp would send the same point on their shared edge to
                // two different depths, and perspective opens that into a hairline
                // crack. For the same reason the shader must not clamp it.
                float spanPixels = span * PixelsPerUnit;
                uv2[v + 0] = new Vector2(0f, Hinge(near, tearY, hingeSpan, isLid));
                uv2[v + 1] = uv2[v + 0];
                uv2[v + 2] = new Vector2(spanPixels, Hinge(far, tearY, hingeSpan, isLid));
                uv2[v + 3] = uv2[v + 2];

                for (int k = 0; k < 4; k++)
                {
                    normals[v + k] = normal;
                    tangents[v + k] = tangent;
                }

                // Wind so the -Z face is the front, matching the card mesh.
                int t = i * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 1;
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.uv2 = uv2;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        /// <summary>
        /// 0 on the nominal tear line, 1 at the piece's outside edge. Measured from
        /// the shared line rather than each column's own cut, so every column agrees
        /// on where a given height sits along the hinge.
        /// </summary>
        static float Hinge(float y, float tearY, float span, bool isLid)
            => (isLid ? y - tearY : tearY - y) / span;

        /// <summary>
        /// A one by one quad the sheet quads are scaled from. CardQuad.asset is a
        /// card, and stretching it to a book or an icon means dividing that shape
        /// back out first - one unit here instead means the scale is the size, which
        /// is what lets the shelf pop an icon with a plain uniform scale.
        ///
        /// Faces -Z with UVs over 0..1, same as the card mesh.
        /// </summary>
        static Mesh BuildUnitQuad()
        {
            var mesh = new Mesh { name = "UnitQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var tangent = new Vector4(1f, 0f, 0f, -1f);
            mesh.tangents = new[] { tangent, tangent, tangent, tangent };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(UnitQuadPath) != null) AssetDatabase.DeleteAsset(UnitQuadPath);
            AssetDatabase.CreateAsset(mesh, UnitQuadPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(UnitQuadPath);
        }

        // -------------------------------------------------------------------
        // Material
        // -------------------------------------------------------------------
        static Material BuildMaterial(Shader shader)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null)
                AssetDatabase.DeleteAsset(MaterialPath);

            var mat = new Material(shader) { name = "CardPack" };
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
            if (sheet == null) Debug.LogError($"[Cozy TGC] Pack sheet not found at {SheetPath}");
            else mat.SetTexture("_MainTex", sheet);

            mat.SetVector("_PackRect", CardPackSheet.UvRect(sheet, 0));
            mat.SetVector("_PackPixels", new Vector4(CardPackSheet.PackWidth, CardPackSheet.PackHeight, 0f, 0f));
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_PixelAA", 1f);
            mat.EnableKeyword("_PIXELAA");

            mat.SetFloat("_PeelFeather", 0.09f);
            mat.SetFloat("_PeelCurl", 1.5f);
            mat.SetFloat("_TearEdge", 1.1f);
            mat.SetFloat("_TearEdgeWidth", 3f);
            mat.SetFloat("_TearShade", 0.4f);

            mat.SetFloat("_ShineStrength", 0.42f);
            mat.SetFloat("_ShineWidth", 0.3f);
            mat.SetFloat("_ShineTravel", 1.1f);
            mat.SetFloat("_ShineOffset", -0.35f);
            mat.SetFloat("_ShineTint", 0.35f);

            AssetDatabase.CreateAsset(mat, MaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        static Material BuildSlotMaterial(Shader shader)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(SlotMaterialPath) != null)
                AssetDatabase.DeleteAsset(SlotMaterialPath);

            var mat = new Material(shader) { name = "CardSlot" };
            // The frame is the card quad grown by SlotFrameScale, so its pixel grid
            // is the card's 73x113 scaled to match.
            mat.SetVector("_SlotPixels", new Vector4(
                Mathf.Round(73f * SlotFrameScale), Mathf.Round(113f * SlotFrameScale), 0f, 0f));
            mat.SetFloat("_Thickness", 2f);
            mat.SetFloat("_CornerLength", 16f);
            mat.SetFloat("_EdgeAlpha", 0.55f);
            mat.SetFloat("_FillAlpha", 0.06f);

            mat.SetColor("_FillColor", new Color(0.62f, 0.58f, 0.8f, 1f));
            // Above the album sheet's queue. Transparent geometry otherwise sorts by
            // distance from the camera, and a sheet sitting between the near and far
            // row of its own pockets paints the far row's frames out.
            mat.renderQueue = SlotQueue;

            AssetDatabase.CreateAsset(mat, SlotMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(SlotMaterialPath);
        }

        /// <summary>
        /// A pocket is the same frame as a pile, inked for parchment instead of for
        /// the dark backdrop the row of piles sits on: the pale lilac that reads on
        /// the table would be all but invisible on an open page.
        /// </summary>
        static Material BuildPocketMaterial(Shader shader)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(PocketMaterialPath) != null)
                AssetDatabase.DeleteAsset(PocketMaterialPath);

            var mat = new Material(shader) { name = "AlbumPocket" };
            mat.SetVector("_SlotPixels", new Vector4(
                Mathf.Round(CardQuadSize.x * PixelsPerUnit * SlotFrameScale),
                Mathf.Round(CardQuadSize.y * PixelsPerUnit * SlotFrameScale), 0f, 0f));
            mat.SetFloat("_Thickness", 2f);
            mat.SetFloat("_CornerLength", 16f);
            mat.SetFloat("_EdgeAlpha", 0.7f);
            mat.SetFloat("_FillAlpha", 0.05f);
            mat.SetFloat("_FillAlphaHi", 0.22f);

            // The book's own outline and its gilt corners, so an empty pocket looks
            // printed on the page rather than laid over it.
            mat.SetColor("_Color", new Color(0.29f, 0.16f, 0.19f, 1f));
            mat.SetColor("_FillColor", new Color(0.29f, 0.16f, 0.19f, 1f));
            mat.SetColor("_HighlightColor", new Color(0.87f, 0.62f, 0.25f, 1f));
            mat.renderQueue = SlotQueue;

            AssetDatabase.CreateAsset(mat, PocketMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(PocketMaterialPath);
        }

        /// <summary>
        /// The drawer's empty frame. Same slot shader as a pile, on the wrapper's
        /// proportions rather than a card's, so an empty drawer reads as a pack shaped
        /// hole waiting to be filled.
        /// </summary>
        static Material BuildTraySlotMaterial(Shader shader)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(TraySlotMaterialPath) != null)
                AssetDatabase.DeleteAsset(TraySlotMaterialPath);

            var mat = new Material(shader) { name = "PackTraySlot" };
            mat.SetVector("_SlotPixels", new Vector4(
                Mathf.Round(CardPackSheet.PackWidth * TrayFrameScale),
                Mathf.Round(CardPackSheet.PackHeight * TrayFrameScale), 0f, 0f));
            mat.SetFloat("_Thickness", 2f);
            // Corner brackets are a fraction of the art's own pixels, and a wrapper is
            // half again as tall as a card - so the length is scaled to match, or the
            // drawer's corners read as stubs next to the piles'.
            mat.SetFloat("_CornerLength", 22f);
            mat.SetFloat("_EdgeAlpha", 0.55f);
            mat.SetFloat("_FillAlpha", 0.08f);
            mat.SetFloat("_FillAlphaHi", 0.24f);
            mat.SetColor("_FillColor", new Color(0.62f, 0.58f, 0.8f, 1f));
            mat.renderQueue = SlotQueue;

            AssetDatabase.CreateAsset(mat, TraySlotMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(TraySlotMaterialPath);
        }

        /// <summary>
        /// The wrapper sitting in the drawer. One cell of the pack sheet, picked by
        /// PackTray through a property block - the drawer shows the wrapper of the
        /// pack that comes out next, which is not known until Play.
        /// </summary>
        static Material BuildTrayPackMaterial(Shader shader)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
            return BuildSheetMaterial(shader, "PackTrayIcon", sheet,
                                      CardPackSheet.UvRect(sheet, 0),
                                      CardPackSheet.PackWidth, CardPackSheet.PackHeight);
        }

        /// <summary>One material per book, each pointed at its own colour of the RADL sheet.</summary>
        static Material[] BuildBookMaterials(Shader shader)
        {
            var result = new Material[AlbumBookSheet.Albums];
            for (int i = 0; i < result.Length; i++)
            {
                var sheet = AlbumBookSheet.LoadBook(i);
                result[i] = BuildSheetMaterial(shader, $"AlbumBook_{AlbumBookSheet.Names[i]}", sheet,
                                               AlbumBookSheet.BookUvRect(sheet, 0),
                                               AlbumBookSheet.BookCellWidth, AlbumBookSheet.BookCellHeight);
            }
            return result;
        }

        /// <summary>
        /// One material per icon on the shelf. The cell could ride on a property
        /// block instead, but then all three would be the same book until Play, and
        /// the built scene is meant to be readable before that.
        /// </summary>
        static Material[] BuildIconMaterials(Shader shader)
        {
            var sheet = AlbumBookSheet.LoadIcons();
            var result = new Material[AlbumBookSheet.Albums];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = BuildSheetMaterial(shader, $"AlbumIcon_{AlbumBookSheet.Names[i]}", sheet,
                                               AlbumBookSheet.IconUvRect(sheet, AlbumBookSheet.Icons[i]),
                                               AlbumBookSheet.IconSize, AlbumBookSheet.IconSize);
            }
            return result;
        }

        static Material BuildSheetMaterial(Shader shader, string name, Texture2D sheet,
                                           Vector4 rect, int cellWidth, int cellHeight)
        {
            string path = $"Assets/Materials/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);

            var mat = new Material(shader) { name = name };
            if (sheet != null) mat.SetTexture("_MainTex", sheet);
            mat.SetVector("_Rect", rect);
            mat.SetVector("_CellPixels", new Vector4(cellWidth, cellHeight, 0f, 0f));
            mat.SetFloat("_Cutoff", 0.5f);

            AssetDatabase.CreateAsset(mat, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        // -------------------------------------------------------------------
        // Prefab
        // -------------------------------------------------------------------
        static GameObject BuildPrefab(Mesh body, Mesh lid, Material material, float tearY)
        {
            var root = new GameObject("CardPack");

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(visual.transform, false);
            bodyGO.AddComponent<MeshFilter>().sharedMesh = body;
            var bodyRenderer = ConfigureRenderer(bodyGO.AddComponent<MeshRenderer>(), material);

            var lidGO = new GameObject("Lid");
            lidGO.transform.SetParent(visual.transform, false);
            // Pivot on the seam so the strip hinges there, and a hair in front of
            // the body so the pixel of overlap along the seam cannot z-fight.
            lidGO.transform.localPosition = new Vector3(0f, tearY, -0.001f);
            lidGO.AddComponent<MeshFilter>().sharedMesh = lid;
            var lidRenderer = ConfigureRenderer(lidGO.AddComponent<MeshRenderer>(), material);

            var view = root.AddComponent<CardPackView>();
            view.EditorBind(null, visual.transform, lidGO.transform, bodyRenderer, lidRenderer, PackSize);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildSlotPrefab(Mesh cardMesh, Material material)
        {
            var root = new GameObject("CardSlot");
            root.AddComponent<CardSlot>();

            var frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);
            // Sat back a couple of centimetres: the frame is transparent and does not
            // write depth, so at the same z it would tint the card resting on it.
            frame.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            frame.transform.localScale = Vector3.one * SlotFrameScale;
            frame.AddComponent<MeshFilter>().sharedMesh = cardMesh;
            var renderer = ConfigureRenderer(frame.AddComponent<MeshRenderer>(), material);

            root.GetComponent<CardSlot>().EditorBind(renderer, "Stack", false);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SlotPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static MeshRenderer ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        // -------------------------------------------------------------------
        // Scene
        // -------------------------------------------------------------------
        /// <summary>
        /// One album sheet: the pockets that sit on one of the two pages the book
        /// draws. Each sheet is a CardSlotBoard, so a pocket takes a dropped card
        /// through exactly the same path a pile at the bottom of the screen does.
        ///
        /// The pockets are scaled down rather than laid out at card size, because
        /// they have to fit a page of the art. A card lands at whatever size the
        /// slot it arrives in is, so shrinking the pocket is all it takes.
        ///
        /// Even sheets hang left of the spine and odd ones right, which is the pairing
        /// CardAlbum shows as one spread. It re-applies that offset when it opens a
        /// spread, so this only has to make the scene look right before Play.
        /// </summary>
        static CardSlotBoard BuildAlbumPage(Transform parent, int index, GameObject slotPrefab,
                                            Material pocketMaterial)
        {
            var pageGO = new GameObject($"Page_{index + 1}", typeof(CardSlotBoard));
            pageGO.transform.SetParent(parent, false);
            pageGO.transform.localPosition =
                new Vector3(index % 2 == 0 ? -AlbumPageOffset : AlbumPageOffset, 0f, 0f);

            var pockets = new List<CardSlot>();
            for (int row = 0; row < AlbumRows; row++)
            {
                for (int col = 0; col < AlbumColumns; col++)
                {
                    var pocketGO = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, pageGO.transform);
                    pocketGO.name = $"Pocket_{row * AlbumColumns + col + 1}";
                    pocketGO.transform.localPosition = new Vector3(
                        (col - (AlbumColumns - 1) * 0.5f) * AlbumPocketSpacing.x,
                        ((AlbumRows - 1) * 0.5f - row) * AlbumPocketSpacing.y,
                        0f);
                    pocketGO.transform.localScale = Vector3.one * AlbumPocketScale;

                    var frame = pocketGO.transform.Find("Frame").GetComponent<MeshRenderer>();
                    frame.sharedMaterial = pocketMaterial;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(frame);

                    var pocket = pocketGO.GetComponent<CardSlot>();
                    // Catch areas half the spacing, so the pockets tile the page. Divided
                    // by the pocket's own scale, since CardSlot measures them in its
                    // local space and this one is not the size of a card.
                    //
                    // Held still: a filed card is printed onto its page, so it neither
                    // leans nor answers the pointer the way one on a pile does.
                    pocket.EditorBind(frame, string.Empty, false, 1,
                                      AlbumPocketSpacing.x * 0.5f / AlbumPocketScale,
                                      AlbumPocketSpacing.y * 0.5f / AlbumPocketScale,
                                      holdsStill: true);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pocket);
                    pockets.Add(pocket);
                }
            }

            var board = pageGO.GetComponent<CardSlotBoard>();
            board.EditorBind(pockets);
            return board;
        }

        /// <summary>
        /// One album: the book quad and the sheets of pockets laid over the pages it
        /// draws. The album's origin is the spine of the open book, which is not the
        /// middle of the art's frame - the book opens leftwards off a right edge that
        /// stays put - so the quad is nudged across by the offset the sheet declares.
        /// </summary>
        static CardAlbum BuildAlbum(int index, GameObject slotPrefab, Mesh quad,
                                    Material bookMaterial, Material pocketMaterial)
        {
            string name = AlbumBookSheet.Names[index];
            var albumGO = new GameObject($"Album_{name}", typeof(CardAlbum));
            albumGO.transform.position = AlbumPosition;

            var bookGO = new GameObject("Book", typeof(AlbumBook));
            bookGO.transform.SetParent(albumGO.transform, false);
            bookGO.transform.localPosition = new Vector3(
                AlbumBookOffset.x, AlbumBookOffset.y, AlbumBookDepth);
            bookGO.transform.localScale = new Vector3(AlbumBookSize.x, AlbumBookSize.y, 1f);
            bookGO.AddComponent<MeshFilter>().sharedMesh = quad;
            var bookRenderer = ConfigureRenderer(bookGO.AddComponent<MeshRenderer>(), bookMaterial);
            bookGO.GetComponent<AlbumBook>().EditorBind(bookRenderer);

            var sheets = new List<CardSlotBoard>();
            for (int p = 0; p < AlbumPages; p++)
            {
                var sheet = BuildAlbumPage(albumGO.transform, p, slotPrefab, pocketMaterial);
                // Same rule CardAlbum applies at run time: nothing is up until the
                // book has finished swinging open.
                sheet.gameObject.SetActive(false);
                sheets.Add(sheet);
            }

            var album = albumGO.GetComponent<CardAlbum>();
            album.EditorBind(name, bookGO.GetComponent<AlbumBook>(), sheets,
                             AlbumPageHalfSize, AlbumPageOffset, AlbumArtSize, AlbumArtCenter);
            albumGO.SetActive(false);
            return album;
        }

        /// <summary>
        /// The menu down the left: one icon per album, cut out of the same sheet of
        /// isometric books. Position and size are AlbumShelf's at run time; what is
        /// set here is only so the built scene reads before Play.
        /// </summary>
        static AlbumShelf BuildShelf(Mesh quad, Material[] iconMaterials)
        {
            var shelfGO = new GameObject("AlbumShelf", typeof(AlbumShelf));
            shelfGO.transform.position = new Vector3(0f, 0f, ShelfDepth);

            var icons = new List<MeshRenderer>();
            for (int i = 0; i < iconMaterials.Length; i++)
            {
                var iconGO = new GameObject($"Book_{AlbumBookSheet.Names[i]}");
                iconGO.transform.SetParent(shelfGO.transform, false);
                iconGO.AddComponent<MeshFilter>().sharedMesh = quad;
                icons.Add(ConfigureRenderer(iconGO.AddComponent<MeshRenderer>(), iconMaterials[i]));
            }

            var shelf = shelfGO.GetComponent<AlbumShelf>();
            shelf.EditorBind(icons, ShelfIconSize, ShelfIconSpacing, ShelfMargin);
            shelf.EditorLayoutIcons();
            return shelf;
        }

        /// <summary>
        /// The drawer of unopened packs, under the shelf. Two quads: the empty slot
        /// frame and the wrapper in it, both sized by PackTray at run time - what is
        /// set here is only so the built scene reads before Play, exactly like the
        /// shelf above it.
        /// </summary>
        static PackTray BuildTray(Mesh quad, Material slotMaterial, Material packMaterial)
        {
            var trayGO = new GameObject("PackTray", typeof(PackTray));
            trayGO.transform.position = new Vector3(0f, -1.2f, ShelfDepth);

            var frameGO = new GameObject("Frame");
            frameGO.transform.SetParent(trayGO.transform, false);
            // Behind the wrapper, the same way a slot frame sits behind the card on it:
            // it is transparent and writes no depth, so at one depth it would tint it.
            frameGO.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            frameGO.AddComponent<MeshFilter>().sharedMesh = quad;
            var frame = ConfigureRenderer(frameGO.AddComponent<MeshRenderer>(), slotMaterial);

            var packGO = new GameObject("Pack");
            packGO.transform.SetParent(trayGO.transform, false);
            packGO.AddComponent<MeshFilter>().sharedMesh = quad;
            var wrapper = ConfigureRenderer(packGO.AddComponent<MeshRenderer>(), packMaterial);

            var tray = trayGO.GetComponent<PackTray>();
            tray.EditorBind(frame, wrapper, TrayPackSize, TrayFrameScale);
            tray.EditorLayoutQuads();
            return tray;
        }

        /// <summary>
        /// The shop's shelves. Authored content: made with the shipped defaults the
        /// first time, and left alone from then on, so editing the asset survives every
        /// later rebuild of the scene. Reset it from the inspector to get the defaults
        /// back, or delete it and build again.
        /// </summary>
        static ShopCatalog LoadOrCreateShopCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            if (catalog != null) return catalog;

            EnsureFolder(Path.GetDirectoryName(ShopCatalogPath)?.Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<ShopCatalog>();
            catalog.FillWithDefaults();
            AssetDatabase.CreateAsset(catalog, ShopCatalogPath);
            Debug.Log($"[Cozy TGC] Created {ShopCatalogPath} - edit the shop's shelves there.");
            return catalog;
        }

        /// <summary>
        /// The two decks, and what their cards are called. The names are wired here
        /// rather than read off the files because they are not in them - the folders
        /// are numbered - and the shop's wanted ads are written in card names.
        ///
        /// A pack rolls its deck per card from the weights below, so it is mostly
        /// Spanish with the occasional tarot card in it: 1 against 9 is a tenth of the
        /// cards, and at <see cref="CardsPerPack"/> cards a pack that is one tarot card
        /// every second pack. The order here is what <see cref="CardDeck"/> names.
        /// </summary>
        static List<CardArtSet> BuildArtSets()
        {
            return new List<CardArtSet>
            {
                new CardArtSet
                {
                    resourceFolder = CardArtLibrary.Root + "tarot_free - monochrome",
                    backName = "back",
                    displayName = "tarot",
                    cardNames = TarotNames,
                    packWeight = TarotPackWeight,
                },
                new CardArtSet
                {
                    resourceFolder = CardArtLibrary.Root + "spanish deck",
                    backName = "back",
                    displayName = "Spanish deck",
                    suitSize = SpanishSuitSize,
                    suitNames = SpanishSuits,
                    rankNames = SpanishRanks,
                    packWeight = SpanishPackWeight,
                },
            };
        }

        static void BuildScene(GameObject packPrefab, GameObject slotPrefab, GameObject cardPrefab,
                               Mesh unitQuad, Material pocketMaterial,
                               Material traySlotMaterial, Material trayPackMaterial,
                               Material[] bookMaterials, Material[] iconMaterials)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CardInteractor));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            // Parked for good: this shot fits a full row of five piles even in a 4:3
            // view, and everything that used to need a different one moves itself.
            cam.transform.SetPositionAndRotation(Framing, Quaternion.identity);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.055f, 0.08f, 1f);
            cam.GetUniversalAdditionalCameraData();
            // Hover and click-to-flip only: in this scene a drag moves the card to
            // another slot, which PackOpeningController picks up.
            camGO.GetComponent<CardInteractor>().EditorBind(cam, CardInteractor.DragBehaviour.HandOff);

            var lightGO = new GameObject("Directional Light", typeof(Light));
            var light = lightGO.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightGO.transform.rotation = Quaternion.Euler(50f, 160f, 0f);

            // Pack and stack share a rig so they travel together: the wrapper cannot
            // ride close to the camera while the stack it is hiding sits back at half
            // the size, and the stack has to pull back out as the wrapper falls away.
            var rigGO = new GameObject("PackRig");

            var packGO = (GameObject)PrefabUtility.InstantiatePrefab(packPrefab);
            packGO.transform.SetParent(rigGO.transform, false);
            packGO.transform.localPosition = PackPosition;
            var pack = packGO.GetComponent<CardPackView>();
            pack.EditorBind(cam,
                            packGO.transform.Find("Visual"),
                            packGO.transform.Find("Visual/Lid"),
                            packGO.transform.Find("Visual/Body").GetComponent<MeshRenderer>(),
                            packGO.transform.Find("Visual/Lid").GetComponent<MeshRenderer>(),
                            PackSize);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pack);

            var deckGO = new GameObject("Deck", typeof(CardPackDeck));
            deckGO.transform.SetParent(rigGO.transform, false);
            deckGO.transform.localPosition = DeckPosition;
            var deck = deckGO.GetComponent<CardPackDeck>();
            deck.EditorBind(cardPrefab, CardsPerPack);

            var boardGO = new GameObject("Slots", typeof(CardSlotBoard));
            boardGO.transform.position = SlotRowPosition;
            var slots = new List<CardSlot>();
            float startX = -(SlotCount - 1) * SlotSpacing * 0.5f;
            for (int i = 0; i < SlotCount; i++)
            {
                var slotGO = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, boardGO.transform);
                slotGO.name = $"Slot_{i + 1}";
                slotGO.transform.localPosition = new Vector3(startX + i * SlotSpacing, 0f, 0f);

                var slot = slotGO.GetComponent<CardSlot>();
                slot.EditorBind(slotGO.transform.Find("Frame").GetComponent<MeshRenderer>(),
                                $"Stack {i + 1}", i == DefaultSlot);
                PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
                slots.Add(slot);
            }
            boardGO.GetComponent<CardSlotBoard>().EditorBind(slots);

            // One album per book. They are stacked on the same spot and all shut -
            // only one is ever out, and it fits itself into the frame when it opens.
            var albums = new List<CardAlbum>();
            for (int i = 0; i < AlbumBookSheet.Albums; i++)
                albums.Add(BuildAlbum(i, slotPrefab, unitQuad, bookMaterials[i], pocketMaterial));

            var shelf = BuildShelf(unitQuad, iconMaterials);
            var tray = BuildTray(unitQuad, traySlotMaterial, trayPackMaterial);

            var artSets = BuildArtSets();

            var materials = new List<Material>();
            var names = new List<string>();
            foreach (string tier in Tiers)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/Card_{tier}.mat");
                if (mat == null)
                {
                    Debug.LogWarning($"[Cozy TGC] Missing Assets/Materials/Card_{tier}.mat - run Build Demo Scene first.");
                    continue;
                }
                materials.Add(mat);
                names.Add(tier);
            }

            // The shop panel draws itself in OnGUI and reaches the scene only through
            // the controller, so it needs nothing but a place to live.
            var controllerGO = new GameObject("PackOpening", typeof(PackOpeningController), typeof(ShopView));
            var controller = controllerGO.GetComponent<PackOpeningController>();
            controller.EditorBind(cam, camGO.GetComponent<CardInteractor>(), pack, deck,
                                  boardGO.GetComponent<CardSlotBoard>(), albums, shelf, AlbumPosition.z,
                                  rigGO.transform, SealedRigOffset,
                                  artSets, materials, names);
            controller.EditorBindShop(cardPrefab, tray, controllerGO.GetComponent<ShopView>(),
                                      LoadOrCreateShopCatalog());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
        }

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
