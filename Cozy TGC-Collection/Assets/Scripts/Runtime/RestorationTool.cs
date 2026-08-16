using System;
using UnityEngine;

namespace CozyTGC
{
    /// <summary>
    /// One thing you can rub over a card, as a set of rates. A stroke is only ever
    /// a subtraction from the wear map, so a tool is fully described by what it
    /// takes off, how far down it can reach, and what it puts back on in exchange.
    ///
    /// The exchange is the point. A tool that only ever improved the card would
    /// reduce restoring one to holding the button down; every tool here damages
    /// something else while it works, which is what puts the steps in an order.
    /// Flatten before you abrade, abrade before you re-ink, and polish last -
    /// go round the other way and the pad takes off the ink the pen just laid.
    /// </summary>
    [Serializable]
    public struct RestorationTool
    {
        public string name;
        public string blurb;

        [Tooltip("Brush radius in world units, matched against the card's own size.")]
        public float radius;

        [Tooltip("Rubs the whole card at once instead of a spot under the pointer. " +
                 "For the press, which is weight rather than a stroke.")]
        public bool wholeCard;

        [Tooltip("Scuff taken off per second at the centre of the brush.")]
        public float scuffRate;
        [Tooltip("How deep this tool reaches. A cloth cannot polish a gouge out, so " +
                 "it stops here and the scratch stays until something coarser comes.")]
        public float scuffFloor;

        [Tooltip("Ink loss taken off per second - putting colour back, not taking it.")]
        public float inkRate;
        public float inkFloor;

        [Tooltip("How fast dents and creases are pressed back towards flat.")]
        public float flattenRate;

        [Tooltip("How fast a chipped edge is filled back in.")]
        public float fillRate;

        [Tooltip("Bow and folded corners pulled back towards flat per second.")]
        public float bendRate;

        [Tooltip("Scuff this leaves behind while it works. Abrading is itself abrasion.")]
        public float scuffCost;
        [Tooltip("Ink this takes off while it works. A pad lifts print along with the " +
                 "scratch; a paper fill arrives blank.")]
        public float inkCost;

        [Tooltip("Scuff added once there is nothing left here for the tool to do. " +
                 "Every tool has it: this is what makes overworking a spot a mistake " +
                 "rather than free, and why a good restoration is a light touch.")]
        public float overworkScuff;
    }

    /// <summary>
    /// The tools themselves. Held in code rather than as assets for the same reason
    /// <see cref="CardFinishOdds"/> is one curve instead of a table per card: these
    /// are the rules of the minigame, not authored content, and every card in the
    /// game is restored by the same five things.
    /// </summary>
    public static class RestorationTools
    {
        public static readonly RestorationTool Cloth = new RestorationTool
        {
            name = "Soft Cloth",
            blurb = "Lifts haze and dust. Will not reach a real scratch.",
            radius = 0.11f,
            scuffRate = 0.55f,
            scuffFloor = 0.4f,
            overworkScuff = 0.02f,
        };

        public static readonly RestorationTool Pad = new RestorationTool
        {
            name = "Abrasive Pad",
            blurb = "Takes scratches all the way out, and some of the print with them.",
            radius = 0.08f,
            scuffRate = 0.9f,
            scuffFloor = 0f,
            // A cost, not a ratchet. Above about 0.15 the pad takes off more print
            // than the pen can lay back on, and no order of tools improves a card -
            // which reads to a player as the game being broken rather than as a
            // trade-off they are getting wrong.
            inkCost = 0.12f,
            overworkScuff = 0.1f,
        };

        public static readonly RestorationTool Burnisher = new RestorationTool
        {
            name = "Burnishing Bone",
            blurb = "Rolls dents and creases flat. Leaves its own polish marks.",
            radius = 0.13f,
            flattenRate = 0.8f,
            scuffCost = 0.1f,
            overworkScuff = 0.03f,
        };

        public static readonly RestorationTool Press = new RestorationTool
        {
            name = "Press",
            blurb = "Weight, not a stroke. Hold it and the whole card comes back flat.",
            wholeCard = true,
            bendRate = 0.35f,
            flattenRate = 0.15f,
        };

        public static readonly RestorationTool Fill = new RestorationTool
        {
            name = "Paper Fill",
            blurb = "Puts a chipped edge back. Arrives blank - the print is your problem.",
            radius = 0.05f,
            fillRate = 0.5f,
            inkCost = 0.8f,
            overworkScuff = 0.04f,
        };

        public static readonly RestorationTool Pen = new RestorationTool
        {
            name = "Touch-Up Pen",
            blurb = "Lays colour back in. Keep going past done and it beads up.",
            radius = 0.045f,
            inkRate = 1.1f,
            inkFloor = 0f,
            overworkScuff = 0.3f,
        };

        /// <summary>
        /// In the order they are meant to be reached for. Nothing enforces it -
        /// the rates do, by punishing any other order.
        /// </summary>
        public static readonly RestorationTool[] All = { Press, Burnisher, Pad, Fill, Pen, Cloth };
    }
}
