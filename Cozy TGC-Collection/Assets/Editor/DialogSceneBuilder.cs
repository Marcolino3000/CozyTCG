using System.Collections.Generic;
using System.IO;
using Core;
using Nodes;
using Nodes.Decorator;
using Tree;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Generates the dialog overlay: one prefab holding both the Dialog Builder rig and
    /// the HUD, plus a demo scene that plays a sample conversation.
    ///
    /// The prefab and the scene are rebuilt from scratch on every run, the same as the
    /// card assets. The dialog *content* is not: the CharacterData assets, the sample tree
    /// and the marker asset are created once and then left alone, because those are meant
    /// to be edited by hand (portraits, lines, the node graph under Tools > DialogBuilder,
    /// paragraph markers in the Audio Player window).
    /// </summary>
    public static class DialogSceneBuilder
    {
        const string PrefabPath = "Assets/Prefabs/DialogSystem.prefab";
        const string ScenePath = "Assets/Scenes/DialogDemo.unity";
        const string DialogFolder = "Assets/Dialogs";
        const string CharacterFolder = DialogFolder + "/Characters";
        const string TreePath = DialogFolder + "/SampleConversation.asset";
        const string MarkerPath = DialogFolder + "/DialogMarkers.asset";

        const string PlayerName = "Marlene";
        const string PartnerName = "Hilde";

        // Canvas units at the 1920x1080 reference resolution.
        const float Margin = 64f;
        const float LineHeight = 240f;
        const float LineBottom = 48f;
        const float PortraitWidth = 300f;
        const float PortraitHeight = 340f;
        const float Gap = 20f;
        const float StackBottom = LineBottom + LineHeight + Gap;

        static readonly Color PanelColour = new Color(0.10f, 0.09f, 0.13f, 0.90f);
        static readonly Color PlateColour = new Color(0.05f, 0.045f, 0.07f, 0.95f);
        static readonly Color TextColour = new Color(0.94f, 0.93f, 0.96f, 1f);
        static readonly Color SpeakerColour = new Color(0.98f, 0.86f, 0.62f, 1f);
        static readonly Color RowColour = new Color(0.10f, 0.09f, 0.13f, 0.72f);

        [MenuItem("Tools/Cozy TGC/Build Dialog Scene", false, 1)]
        public static void BuildDialogScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Scenes");
            EnsureFolder(CharacterFolder);

            CharacterData player = BuildCharacter(PlayerName, 0);
            CharacterData partner = BuildCharacter(PartnerName, 10);
            DialogTree tree = BuildSampleTree(partner);
            MarkerManager markers = BuildMarkerManager();

            GameObject prefab = BuildPrefab(tree, player, markers);
            BuildScene(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

#if !ENABLE_LEGACY_INPUT_MANAGER
            Debug.LogWarning(
                "[Cozy TGC] Active Input Handling is 'Input System Package (New)'. Dialog " +
                "Builder's DialogTreeRunner reads UnityEngine.Input in Update, which throws " +
                "under that setting - the dialog still runs, but click-to-skip fills the " +
                "console instead of advancing the line. Set Project Settings > Player > " +
                "Active Input Handling to 'Both' and restart the Editor.");
#endif

            Debug.Log($"[Cozy TGC] Dialog built. Open {ScenePath} and press Play. " +
                      $"Edit the conversation at {TreePath} through Tools > DialogBuilder.");
        }

        // -------------------------------------------------------------------
        // Content - created once, never overwritten
        // -------------------------------------------------------------------
        static CharacterData BuildCharacter(string characterName, int trustThreshold)
        {
            string path = $"{CharacterFolder}/{characterName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CharacterData>(path);
            if (existing != null) return existing;

            var character = ScriptableObject.CreateInstance<CharacterData>();
            character.name = characterName;
            character.TrustThreshold = trustThreshold;
            character.BasePopularity = 0;
            character.Influenceability = 1f;
            AssetDatabase.CreateAsset(character, path);
            return character;
        }

        /// <summary>
        /// The marker asset from com.cod.audioplayer, which is where a voiced line gets
        /// its per-paragraph timings from. Assigned to the runner even though the sample
        /// conversation is silent: the runner reads the field without a null check the
        /// moment any node carries an AudioClip. Mark the paragraphs on a clip through
        /// the package's own Audio Player window.
        /// </summary>
        static MarkerManager BuildMarkerManager()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MarkerManager>(MarkerPath);
            if (existing != null) return existing;

            var markers = ScriptableObject.CreateInstance<MarkerManager>();
            markers.name = "DialogMarkers";
            markers.CharacterNames = new List<string> { PlayerName, PartnerName };
            AssetDatabase.CreateAsset(markers, MarkerPath);
            return markers;
        }

        /// <summary>
        /// A short two-level conversation: the partner opens, the player picks one of
        /// three answers, the partner reacts and the tree runs out - which is what ends
        /// the dialog and hides the HUD.
        /// </summary>
        static DialogTree BuildSampleTree(CharacterData partner)
        {
            var existing = AssetDatabase.LoadAssetAtPath<DialogTree>(TreePath);
            if (existing != null) return existing;

            var tree = ScriptableObject.CreateInstance<DialogTree>();
            tree.name = "SampleConversation";
            tree.StartNodes = new List<DialogOptionNode>();
            // The runner reads the NPC's name and trust level off this - an NPC line with
            // no CharacterData in the blackboard throws on the way to the subtitle.
            tree.Blackboard = new Blackboard { CharacterData = partner };
            AssetDatabase.CreateAsset(tree, TreePath);

            var greeting = CreateNode<NpcDialogOption>(tree,
                "Oh, du sammelst die auch?",
                "Oh, you collect these too?",
                new Vector2(0f, 0f));
            tree.StartNodes.Add(greeting);

            var proud = CreatePlayerNode(tree,
                "Seit ich klein bin. Die Sonderdrucke haben es mir angetan.",
                "Since I was little. The special prints got me hooked.",
                AnswerType.DeepTalk, new Vector2(360f, -160f));
            var casual = CreatePlayerNode(tree,
                "Nur wegen der Bilder, ehrlich gesagt.",
                "Just for the artwork, honestly.",
                AnswerType.SmallTalk, new Vector2(360f, 0f));
            var rude = CreatePlayerNode(tree,
                "Karten sind doch was fuer Kinder.",
                "Cards are for kids, though.",
                AnswerType.TrashTalk, new Vector2(360f, 160f));

            tree.AddChild(greeting, proud);
            tree.AddChild(greeting, casual);
            tree.AddChild(greeting, rude);

            tree.AddChild(proud, CreateNode<NpcDialogOption>(tree,
                "Dann zeig mir mal deine schoenste.",
                "Then show me your finest one.",
                new Vector2(760f, -160f)));
            tree.AddChild(casual, CreateNode<NpcDialogOption>(tree,
                "Auch ein guter Grund.",
                "That is a good enough reason.",
                new Vector2(760f, 0f)));
            tree.AddChild(rude, CreateNode<NpcDialogOption>(tree,
                "Na dann viel Spass beim Zuschauen.",
                "Enjoy watching, then.",
                new Vector2(760f, 160f)));

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
            return tree;
        }

        static T CreateNode<T>(DialogTree tree, string german, string english, Vector2 position)
            where T : DialogOptionNode
        {
            var node = (T)tree.CreateNode(typeof(T));
            node.DialogLine = german;
            node.DialogLineEn = english;
            node.TextPreview = Preview(german);
            node.name = Preview(german);
            node.Position = position;
            node.RequiredNodes = new List<DialogOptionNode>();
            node.BlockerNodes = new List<DialogOptionNode>();

            // The node built its paragraphs in OnEnable, while the line was still empty.
            // Without this the runner would page through nothing and skip the node.
            node.CreateParagraphs();

            EditorUtility.SetDirty(node);
            return node;
        }

        static PlayerDialogOption CreatePlayerNode(DialogTree tree, string german, string english,
                                                   AnswerType type, Vector2 position)
        {
            var node = CreateNode<PlayerDialogOption>(tree, german, english, position);

            // Both fields are private on the node; the popularity modifier is set here
            // rather than left to the node's OnValidate so the asset is correct even if
            // that never runs.
            var serialized = new SerializedObject(node);
            serialized.FindProperty("answerType").enumValueIndex = (int)type;
            serialized.FindProperty("_popularityModifier").intValue = ModifierFor(type);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return node;
        }

        static int ModifierFor(AnswerType type)
        {
            switch (type)
            {
                case AnswerType.DeepTalk: return 5;
                case AnswerType.TrashTalk: return -5;
                default: return 0;
            }
        }

        static string Preview(string line)
        {
            if (string.IsNullOrEmpty(line)) return "Line";
            return line.Length <= 24 ? line : line.Substring(0, 21) + "...";
        }

        // -------------------------------------------------------------------
        // Prefab
        // -------------------------------------------------------------------
        static GameObject BuildPrefab(DialogTree tree, CharacterData player, MarkerManager markers)
        {
            var root = new GameObject("DialogSystem");

            BuildRig(root.transform, tree, markers);
            BuildCanvas(root.transform, player);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The package side. DialogBuilderHQ finds the presenters itself at Start, so the
        /// only reference that has to be wired is its own tree runner - and that one is
        /// not optional: its OnValidate dereferences the field without a null check.
        /// </summary>
        static void BuildRig(Transform parent, DialogTree tree, MarkerManager markers)
        {
            var rig = new GameObject("DialogBuilder");
            rig.transform.SetParent(parent, false);

            var runner = rig.AddComponent<DialogTreeRunner>();
            var decisions = rig.AddComponent<DecisionHandler>();
            var session = rig.AddComponent<DialogSession>();
            var hq = rig.AddComponent<DialogBuilderHQ>();

            session.EditorBind(tree, true);

            var runnerSerialized = new SerializedObject(runner);
            runnerSerialized.FindProperty("markermanager").objectReferenceValue = markers;
            runnerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var serialized = new SerializedObject(hq);
            serialized.FindProperty("treeRunner").objectReferenceValue = runner;
            serialized.FindProperty("decisionHandler").objectReferenceValue = decisions;
            // The package's own presenters are left out - this HUD replaces them - and the
            // idle random pick stays off so an unanswered option waits for the player.
            serialized.FindProperty("showSubtitles").boolValue = false;
            serialized.FindProperty("showOptions").boolValue = false;
            serialized.FindProperty("showDebugButtons").boolValue = false;
            serialized.FindProperty("randomPick").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildCanvas(Transform parent, CharacterData player)
        {
            var canvasGO = new GameObject("DialogCanvas", typeof(RectTransform));
            canvasGO.transform.SetParent(parent, false);
            canvasGO.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            var dialog = NewRect("Dialog", canvasGO.transform);
            Stretch(dialog, 0f, 0f, 0f, 0f);
            var group = dialog.gameObject.AddComponent<CanvasGroup>();
            var hud = dialog.gameObject.AddComponent<DialogHud>();

            GameObject linePanel = BuildLine(dialog, out Text speaker, out Text line);
            BuildOptions(dialog);
            DialogPortrait left = BuildPortrait(dialog, "PortraitLeft", true);
            DialogPortrait right = BuildPortrait(dialog, "PortraitRight", false);

            hud.EditorBind(group, left, right, linePanel, speaker, line, player);
        }

        static GameObject BuildLine(Transform parent, out Text speaker, out Text line)
        {
            var panel = NewImage("Line", parent, PanelColour);
            var rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(-Margin * 2f, LineHeight);
            rect.anchoredPosition = new Vector2(0f, LineBottom);
            // Swallows the click instead of letting it reach whatever 3D scene the HUD
            // was dropped on top of.
            panel.raycastTarget = true;

            speaker = NewText("Speaker", panel.transform, 30, TextAnchor.UpperLeft, SpeakerColour);
            speaker.fontStyle = FontStyle.Bold;
            var speakerRect = speaker.rectTransform;
            speakerRect.anchorMin = new Vector2(0f, 1f);
            speakerRect.anchorMax = new Vector2(1f, 1f);
            speakerRect.pivot = new Vector2(0.5f, 1f);
            speakerRect.sizeDelta = new Vector2(-64f, 38f);
            speakerRect.anchoredPosition = new Vector2(0f, -22f);

            line = NewText("Text", panel.transform, 34, TextAnchor.UpperLeft, TextColour);
            line.lineSpacing = 1.15f;
            Stretch(line.rectTransform, 32f, 26f, 32f, 74f);

            return panel.gameObject;
        }

        /// <summary>
        /// The option stack sits between the two portraits and grows upwards from just
        /// above the line, so a long list never reaches over the portraits.
        /// </summary>
        static void BuildOptions(Transform parent)
        {
            var options = NewRect("Options", parent);
            options.anchorMin = new Vector2(0f, 0f);
            options.anchorMax = new Vector2(1f, 0f);
            options.pivot = new Vector2(0.5f, 0f);
            options.sizeDelta = new Vector2(-(Margin + PortraitWidth + Gap) * 2f, 0f);
            options.anchoredPosition = new Vector2(0f, StackBottom);

            var layout = options.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = options.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var choices = options.gameObject.AddComponent<DialogChoiceList>();
            choices.EditorBind(options, BuildOptionTemplate(options));
        }

        static DialogOptionRow BuildOptionTemplate(Transform parent)
        {
            var row = NewRect("Option (template)", parent);

            // The row's own layout group turns the wrapped label height into the row
            // height, so a long answer gets a taller row instead of spilling out of it.
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 14, 14);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 64f;

            var component = row.gameObject.AddComponent<DialogOptionRow>();

            var background = NewImage("Background", row, RowColour);
            Stretch(background.rectTransform, 0f, 0f, 0f, 0f);
            // Ignored by the row's layout group, so it can stay stretched behind the
            // padded label instead of being treated as a second column.
            background.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            background.raycastTarget = true;

            var label = NewText("Label", row, 32, TextAnchor.MiddleLeft, TextColour);

            component.EditorBind(row, background, label);
            row.gameObject.SetActive(false);
            return component;
        }

        static DialogPortrait BuildPortrait(Transform parent, string name, bool onLeft)
        {
            var root = NewRect(name, parent);
            root.anchorMin = new Vector2(onLeft ? 0f : 1f, 0f);
            root.anchorMax = root.anchorMin;
            root.pivot = new Vector2(onLeft ? 0f : 1f, 0f);
            root.sizeDelta = new Vector2(PortraitWidth, PortraitHeight);
            root.anchoredPosition = new Vector2(onLeft ? Margin : -Margin, StackBottom);

            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var portrait = root.gameObject.AddComponent<DialogPortrait>();

            var pivot = NewRect("Pivot", root);
            Stretch(pivot, 0f, 0f, 0f, 0f);

            var frame = NewImage("Frame", pivot, Color.white);
            Stretch(frame.rectTransform, 0f, 0f, 0f, 0f);

            var icon = NewImage("Icon", frame.transform, Color.white);
            icon.sprite = null;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.enabled = false;
            Stretch(icon.rectTransform, 10f, 62f, 10f, 10f);

            var initial = NewText("Initial", frame.transform, 140, TextAnchor.MiddleCenter,
                                  new Color(1f, 1f, 1f, 0.85f));
            Stretch(initial.rectTransform, 10f, 62f, 10f, 10f);

            var plate = NewImage("NamePlate", frame.transform, PlateColour);
            var plateRect = plate.rectTransform;
            plateRect.anchorMin = new Vector2(0f, 0f);
            plateRect.anchorMax = new Vector2(1f, 0f);
            plateRect.pivot = new Vector2(0.5f, 0f);
            plateRect.sizeDelta = new Vector2(0f, 52f);
            plateRect.anchoredPosition = Vector2.zero;

            var nameLabel = NewText("Name", plate.transform, 28, TextAnchor.MiddleCenter, TextColour);
            Stretch(nameLabel.rectTransform, 8f, 0f, 8f, 0f);

            portrait.EditorBind(group, pivot, frame, icon, initial, nameLabel);
            return portrait;
        }

        // -------------------------------------------------------------------
        // Scene
        // -------------------------------------------------------------------
        static void BuildScene(GameObject prefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.065f, 0.09f, 1f);
            cam.GetUniversalAdditionalCameraData();

            PrefabUtility.InstantiatePrefab(prefab);

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

        // -------------------------------------------------------------------
        // UI helpers
        // -------------------------------------------------------------------
        static Font uiFont;

        static Font UiFont
        {
            get
            {
                // The built-in font keeps the HUD free of an asset import step. TextMesh
                // Pro would need its essential resources pulled into the project first,
                // which this builder has no business doing behind the user's back.
                if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return uiFont;
            }
        }

        static Sprite panelSprite;

        static Sprite PanelSprite
        {
            get
            {
                if (panelSprite == null)
                    panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                return panelSprite;
            }
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image NewImage(string name, Transform parent, Color colour)
        {
            var rect = NewRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = PanelSprite;
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        static Text NewText(string name, Transform parent, int size, TextAnchor anchor, Color colour)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = colour;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            return text;
        }

        static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
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
