using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Loads a folder of card artwork out of Resources: every texture becomes a
    /// face except the one named like the deck's back.
    /// </summary>
    public static class CardArtLibrary
    {
        /// <summary>
        /// Folder inside Assets/Resources that the card art lives in, trailing slash
        /// included. Resources paths are relative to a Resources folder, so nesting
        /// the art one level deeper shifts every path at once and silently loads
        /// nothing - hence one constant here rather than the prefix spread around.
        /// </summary>
        public const string Root = "Cards/";

        public static Texture2D[] Load(string resourceFolder, string backName, out Texture2D back)
        {
            back = null;
            if (string.IsNullOrEmpty(resourceFolder)) return Array.Empty<Texture2D>();

            var all = Resources.LoadAll<Texture2D>(resourceFolder);
            var faces = new List<Texture2D>(all.Length);
            foreach (var tex in all)
            {
                if (!string.IsNullOrEmpty(backName) &&
                    string.Equals(tex.name, backName, StringComparison.OrdinalIgnoreCase)) back = tex;
                else faces.Add(tex);
            }

            // Numbered decks want numeric order - "10" after "9", not after "1".
            faces.Sort((a, b) =>
            {
                bool na = int.TryParse(a.name, out int ia);
                bool nb = int.TryParse(b.name, out int ib);
                if (na && nb) return ia.CompareTo(ib);
                return string.CompareOrdinal(a.name, b.name);
            });
            return faces.ToArray();
        }
    }

    /// <summary>
    /// One deck of artwork, as wired up in the inspector, plus what its cards are
    /// called. The names are here rather than in the shop because they are a
    /// property of the folder that was loaded: a deck dealt in suits is named by
    /// suit and rank, a deck of singles by a list of titles, and the shop's wanted
    /// ads have to be able to ask for either without knowing which it got.
    /// </summary>
    [Serializable]
    public class CardArtSet
    {
        public string resourceFolder;
        public string backName = "back";

        [Tooltip("What the deck is called on a shop shelf. Falls back to the folder name.")]
        public string displayName;
        [Tooltip("How often a card comes off this deck, against the other decks' weights. " +
                 "A pack rolls its deck per card, so this is the mix inside one pack, not " +
                 "how often a pack is 'a deck of this kind'.")]
        public float packWeight = 1f;
        [Tooltip("Cards per suit. 0 for a deck of named singles, like the tarot majors, " +
                 "where the faces are a run rather than a grid.")]
        public int suitSize;
        [Tooltip("One per suit, in face order.")]
        public string[] suitNames;
        [Tooltip("One per position within a suit - a Spanish deck runs 1-7 and then " +
                 "three court cards, so the numbering is not the position.")]
        public string[] rankNames;
        [Tooltip("One per face, for a deck with no suits.")]
        public string[] cardNames;

        [NonSerialized] public Texture2D[] Faces;
        [NonSerialized] public Texture2D Back;

        public int FaceCount => Faces?.Length ?? 0;

        /// <summary>How many suits the loaded faces make up, or 0 for a deck of singles.</summary>
        public int Suits => suitSize > 0 ? FaceCount / suitSize : 0;

        public string Name => string.IsNullOrEmpty(displayName) ? FolderLeaf : displayName;

        string FolderLeaf
        {
            get
            {
                if (string.IsNullOrEmpty(resourceFolder)) return "Deck";
                int slash = resourceFolder.LastIndexOf('/');
                return slash >= 0 ? resourceFolder.Substring(slash + 1) : resourceFolder;
            }
        }

        public void Load()
        {
            Faces = CardArtLibrary.Load(resourceFolder, backName, out Texture2D back);
            Back = back;
        }

        public Texture2D FaceAt(int face) => face >= 0 && face < FaceCount ? Faces[face] : null;

        public int SuitOf(int face) => suitSize > 0 && face >= 0 ? face / suitSize : -1;
        public int RankOf(int face) => suitSize > 0 && face >= 0 ? face % suitSize : -1;

        /// <summary>Face index of one card of one suit, or -1 if the deck is not dealt that way.</summary>
        public int FaceOf(int suit, int rank)
        {
            if (suitSize <= 0 || suit < 0 || rank < 0 || rank >= suitSize) return -1;
            int face = suit * suitSize + rank;
            return face < FaceCount ? face : -1;
        }

        public string SuitName(int suit)
            => suitNames != null && suit >= 0 && suit < suitNames.Length ? suitNames[suit] : $"suit {suit + 1}";

        public string RankName(int rank)
            => rankNames != null && rank >= 0 && rank < rankNames.Length ? rankNames[rank] : (rank + 1).ToString();

        /// <summary>"4 of Copas" or "Judgement", and a bare number for a deck with neither.</summary>
        public string NameOf(int face)
        {
            if (face < 0) return "Card";
            if (suitSize > 0) return $"{RankName(RankOf(face))} of {SuitName(SuitOf(face))}";
            if (cardNames != null && face < cardNames.Length) return cardNames[face];
            return $"#{face}";
        }
    }
}
