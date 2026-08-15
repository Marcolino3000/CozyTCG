using UnityEditor;
using UnityEngine;

namespace CozyTGC.EditorTools
{
    /// <summary>
    /// Draws a want as the one question it is actually asking. A line that names a card
    /// has said which deck and which suit by saying which card, so those two are not
    /// shown next to it - they only come out for the loose lines that need them.
    ///
    /// Fields that do nothing are worse than clutter: they read as settings that are
    /// being ignored, which is exactly what a reader then goes looking for in the code.
    /// </summary>
    [CustomPropertyDrawer(typeof(CardWant))]
    public class CardWantDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect area, SerializedProperty want, GUIContent label)
        {
            EditorGUI.BeginProperty(area, label, want);

            var card = want.FindPropertyRelative("card");
            var rect = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight);

            Line(ref rect, card);
            if (card.objectReferenceValue == null)
            {
                Line(ref rect, want.FindPropertyRelative("deck"));
                Line(ref rect, want.FindPropertyRelative("suit"));
            }

            Line(ref rect, want.FindPropertyRelative("finish"));
            Line(ref rect, want.FindPropertyRelative("count"));

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty want, GUIContent label)
        {
            int lines = want.FindPropertyRelative("card").objectReferenceValue == null ? 5 : 3;
            return lines * EditorGUIUtility.singleLineHeight +
                   (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        static void Line(ref Rect rect, SerializedProperty property)
        {
            EditorGUI.PropertyField(rect, property);
            rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
        }
    }

    /// <summary>
    /// The same for a row on the buy shelf: the card and the finish it is printed in
    /// are only read when the row is selling a named card, so that is the only time
    /// they are drawn.
    /// </summary>
    [CustomPropertyDrawer(typeof(ShopOffer))]
    public class ShopOfferDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect area, SerializedProperty offer, GUIContent label)
        {
            EditorGUI.BeginProperty(area, label, offer);

            var goods = offer.FindPropertyRelative("goods");
            var rect = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight);

            Field(ref rect, offer, "title");
            Field(ref rect, offer, "detail");
            Field(ref rect, offer, "price");
            Line(ref rect, goods);
            Field(ref rect, offer, "count");

            if (goods.enumValueIndex == (int)ShopGoods.NamedCard)
            {
                Field(ref rect, offer, "card");
                var finish = offer.FindPropertyRelative("finish");
                Line(ref rect, finish);

                // Any is a real setting and stays selectable, but a shelf that names its
                // card is meant to name the print with it - so a row left on Any says so
                // rather than looking like the finish was simply not thought about.
                if (finish.enumValueIndex == AnyFinish)
                {
                    rect.height = NoteHeight;
                    EditorGUI.HelpBox(rect, "Any rolls the finish per copy. Name a print " +
                                            "for a row that sells a known card.", MessageType.Info);
                    rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                    rect.height = EditorGUIUtility.singleLineHeight;
                }
            }

            EditorGUI.EndProperty();
        }

        /// <summary>Any is the first entry of CardFinish, being -1.</summary>
        const int AnyFinish = 0;
        static float NoteHeight => EditorGUIUtility.singleLineHeight * 1.6f;

        public override float GetPropertyHeight(SerializedProperty offer, GUIContent label)
        {
            float height = Height(offer, "title") + Height(offer, "detail") + Height(offer, "price") +
                           EditorGUIUtility.singleLineHeight + Height(offer, "count");
            int gaps = 4;

            if (offer.FindPropertyRelative("goods").enumValueIndex == (int)ShopGoods.NamedCard)
            {
                height += Height(offer, "card") + EditorGUIUtility.singleLineHeight;
                gaps += 2;

                if (offer.FindPropertyRelative("finish").enumValueIndex == AnyFinish)
                {
                    height += NoteHeight;
                    gaps++;
                }
            }
            return height + gaps * EditorGUIUtility.standardVerticalSpacing;
        }

        /// <summary>A detail box is a TextArea and taller than a line, so each field is
        /// drawn at the height it asks for rather than at one shared row height.</summary>
        static void Field(ref Rect rect, SerializedProperty offer, string name)
        {
            var property = offer.FindPropertyRelative(name);
            rect.height = EditorGUI.GetPropertyHeight(property, true);
            EditorGUI.PropertyField(rect, property, true);
            rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
            rect.height = EditorGUIUtility.singleLineHeight;
        }

        static float Height(SerializedProperty offer, string name)
            => EditorGUI.GetPropertyHeight(offer.FindPropertyRelative(name), true);

        static void Line(ref Rect rect, SerializedProperty property)
        {
            EditorGUI.PropertyField(rect, property);
            rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
