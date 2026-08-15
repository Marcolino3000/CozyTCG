using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// Loads a folder of card artwork out of Resources: every texture becomes a
    /// face except the one named like the deck's back.
    ///
    /// The pack scene reads its cards off <see cref="CardDeckData"/> assets rather
    /// than out of here - a card has a price and a pull weight on it now, which a
    /// folder of textures has nowhere to put. This is still what decides the *order*
    /// of a deck: the asset builder loads a folder through here and lays the cards out
    /// in that order, and the shader test bench swaps whole decks with it.
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
}
