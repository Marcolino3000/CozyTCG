using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// The wear equivalent of <see cref="CardPreviewRenderer"/>: four frames that
    /// between them show every channel of the map doing its job, without anyone
    /// having to enter play mode and rub a card by hand.
    ///
    /// Writes CardWearPreview.png next to the Assets folder (override with the
    /// COZY_WEAR_PREVIEW_PATH env var).
    /// </summary>
    public static class CardWearPreviewRenderer
    {
        const string ScenePath = "Assets/Scenes/CardHoloDemo.unity";
        const int FrameWidth = 1100;
        const int FrameHeight = 900;

        /// <summary>
        /// One card filling each frame rather than the whole row. Damage is authored
        /// at 73x113 and every part of it - a one texel scratch, a chipped corner,
        /// the way the foil dies over a crease - disappears at the size a five card
        /// row leaves. This is the only preview where that matters.
        /// </summary>
        const int SubjectTier = 2;      // Holo: the most foil to break
        const float Severity = 0.85f;
        const float RestoreSeconds = 30f;
        const int WearSeed = 20260816;

        [MenuItem("Tools/Cozy TGC/Render Wear Preview", false, 2)]
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
            if (cards.Length == 0)
            {
                Debug.LogError("[Cozy TGC] No cards in the demo scene. Run Build Demo Scene first.");
                return;
            }
            System.Array.Sort(cards, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

            var subject = cards[Mathf.Clamp(SubjectTier, 0, cards.Length - 1)];
            var wear = subject.GetComponent<CardWear>();
            if (wear == null)
            {
                Debug.LogError("[Cozy TGC] The card prefab has no CardWear. Run Build Demo Scene first.");
                return;
            }

            // Everything else out of shot, and the camera in close enough that a
            // 73x113 card is worth looking at.
            foreach (var card in cards) if (card != subject) card.gameObject.SetActive(false);
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(
                new Vector3(subject.transform.position.x, subject.transform.position.y, -2.35f),
                Quaternion.identity);

            var rt = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var sheet = new Texture2D(FrameWidth * 2, FrameHeight * 2, TextureFormat.RGBA32, false);
            var frame = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false);

            // The first render of a fresh batch session can land before every
            // texture is resident, same as the foil preview.
            Shoot(cam, rt, frame);

            // 0 - aged, flat on: scratches, edge ink loss, chipped corners.
            Age(wear, Severity);
            Turn(subject, 0f, 0f);
            Capture(cam, rt, frame, sheet, 0);

            // 1 - the same card turned, which is the frame that matters. Dents and
            //     creases have to break the foil, and the bow has to show in the
            //     silhouette.
            Turn(subject, 42f, -12f);
            Capture(cam, rt, frame, sheet, 1);

            // 2 - the same card, same angle, after a full pass with the tools. Read
            //     against frame 1 this is the whole restoration loop in one image.
            Age(wear, Severity);
            Conserve(wear, RestoreSeconds, back: false);
            wear.Flush();
            Capture(cam, rt, frame, sheet, 2);

            // 3 - its back. Only the front was worked, so this is still at full
            //     severity - which is the point of R and G being per face.
            Turn(subject, 180f, 0f);
            Capture(cam, rt, frame, sheet, 3);

            sheet.Apply();

            string outPath = System.Environment.GetEnvironmentVariable("COZY_WEAR_PREVIEW_PATH");
            if (string.IsNullOrEmpty(outPath))
                outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CardWearPreview.png"));
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());

            Object.DestroyImmediate(frame);
            Object.DestroyImmediate(sheet);
            rt.Release();
            Object.DestroyImmediate(rt);

            // Leave the scene as we found it. Nothing here is saved.
            cam.transform.SetPositionAndRotation(camPos, camRot);
            foreach (var card in cards)
            {
                card.gameObject.SetActive(true);
                var w = card.GetComponent<CardWear>();
                if (w != null) { w.MakePristine(); w.Flush(); }
                Turn(card, 0f, 0f);
            }

            Debug.Log($"[Cozy TGC] Wear preview written to {outPath}");
            LogGradingRamp(wear);
            wear.MakePristine();
            wear.Flush();
        }

        /// <summary>
        /// Prints what each severity actually averages, which is the only way to set
        /// the full-scale constants on <see cref="CardCondition"/> honestly. Severity
        /// 1 should land near Poor and the restored row should climb the grades - if
        /// everything reads Mint, those constants are wrong, not the damage.
        /// </summary>
        static void LogGradingRamp(CardWear wear)
        {
            var sb = new System.Text.StringBuilder("[Cozy TGC] Grading ramp\n");
            foreach (float s in new[] { 0f, 0.3f, 0.55f, 0.85f, 1f })
            {
                wear.Age(WearSeed, s);
                sb.AppendLine($"  aged {s:0.00}   {wear.Condition.Detail()}");
            }
            foreach (float work in new[] { 5f, 15f, 40f, 90f })
            {
                wear.Age(WearSeed, 0.85f);
                Conserve(wear, work, back: false);
                sb.AppendLine($"  0.85 +{work,3:0}s  {wear.Condition.Detail()}");
            }
            Debug.Log(sb.ToString());
        }

        static void Age(CardWear wear, float severity)
        {
            wear.Age(WearSeed, severity);
            wear.Flush();
        }

        static void Turn(CardView card, float yaw, float pitch)
        {
            var visual = card.transform.Find("Visual");
            if (visual != null) visual.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Works the tools over the card in the order the rates reward: flatten,
        /// roll the dents out, take the scratches off, fill, re-ink, polish. Going
        /// the other way means the pad lifts the ink the pen just laid.
        ///
        /// The fill and the pen only ever touch the border, because that is where
        /// chips and ink loss are - and because every tool charges `overworkScuff`
        /// wherever it has nothing left to do. Sweeping a pen over the whole face
        /// is the single worst thing you can do to a card, which is exactly what
        /// the rates are meant to teach.
        /// </summary>
        static void Conserve(CardWear wear, float seconds, bool back)
        {
            Sweep(wear, RestorationTools.Press, seconds * 0.15f, back);
            Sweep(wear, RestorationTools.Burnisher, seconds * 0.2f, back);
            Sweep(wear, RestorationTools.Pad, seconds * 0.3f, back);
            SweepBorder(wear, RestorationTools.Fill, seconds * 0.1f, back);
            SweepBorder(wear, RestorationTools.Pen, seconds * 0.15f, back);
            Sweep(wear, RestorationTools.Cloth, seconds * 0.1f, back);
        }

        /// <summary>Round the edge, where the damage that is not everywhere lives.</summary>
        static void SweepBorder(CardWear wear, in RestorationTool tool, float seconds, bool back)
        {
            if (seconds <= 0f) return;
            const int steps = 30;
            float per = seconds / (steps * 4);
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                wear.Rub(new Vector2(t, 0.02f), back, tool, per);
                wear.Rub(new Vector2(t, 0.98f), back, tool, per);
                wear.Rub(new Vector2(0.02f, t), back, tool, per);
                wear.Rub(new Vector2(0.98f, t), back, tool, per);
            }
        }

        static void Sweep(CardWear wear, in RestorationTool tool, float seconds, bool back)
        {
            if (seconds <= 0f) return;
            if (tool.wholeCard)
            {
                wear.Rub(new Vector2(0.5f, 0.5f), back, tool, seconds);
                return;
            }

            const int steps = 14;
            float per = seconds / (steps * steps);
            for (int y = 0; y < steps; y++)
            {
                for (int x = 0; x < steps; x++)
                    wear.Rub(new Vector2((x + 0.5f) / steps, (y + 0.5f) / steps), back, tool, per);
            }
        }

        static void Shoot(Camera cam, RenderTexture rt, Texture2D frame)
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            frame.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            frame.Apply();
            RenderTexture.active = previous;
        }

        static void Capture(Camera cam, RenderTexture rt, Texture2D frame, Texture2D sheet, int index)
        {
            Shoot(cam, rt, frame);
            int col = index % 2;
            int row = index / 2;
            sheet.SetPixels(col * FrameWidth, (1 - row) * FrameHeight, FrameWidth, FrameHeight, frame.GetPixels());
        }
    }
}
