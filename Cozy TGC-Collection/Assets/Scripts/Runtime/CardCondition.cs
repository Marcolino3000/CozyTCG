using UnityEngine;

namespace CozyTGC
{
    /// <summary>What a grader would call the card, worst to best.</summary>
    public enum CardGrade
    {
        Poor,
        Played,
        Good,
        Excellent,
        NearMint,
        Mint,
    }

    /// <summary>
    /// The wear map read back as one number and a label. What the shop prices
    /// against: <see cref="CardData.price"/> is what an Excellent copy fetches and
    /// <see cref="PriceMultiplier"/> moves it from there.
    ///
    /// The raw figures are means over every texel of the card, and **they are tiny**.
    /// Damage concentrates - a chipped corner is sixty texels out of eight thousand,
    /// a scratch is thirty. Scored against 1 every card in the game grades Mint, so
    /// each channel is divided by what a ruined card actually averages first. Those
    /// full-scale constants are the tuning knob for the whole grading curve; the
    /// weights below only decide which damage a collector minds most.
    /// </summary>
    public readonly struct CardCondition
    {
        /// <summary>
        /// Mean of each channel on a card aged at severity 1, measured off the
        /// generator rather than guessed - `Render Wear Preview` prints the ramp
        /// these came from. Change how ageing works and they have to be re-measured,
        /// or the whole game grades Mint again.
        /// </summary>
        public const float ScuffFull = 0.065f;
        public const float InkFull = 0.090f;
        public const float DentFull = 0.038f;
        public const float MissingFull = 0.005f;

        /// <summary>Means over the card, before any of that scaling.</summary>
        public readonly float RawScuff;
        public readonly float RawInk;
        public readonly float RawDent;
        public readonly float RawMissing;
        public readonly float RawBend;

        public CardCondition(float scuff, float ink, float dent, float missing, float bend)
        {
            RawScuff = scuff;
            RawInk = ink;
            RawDent = dent;
            RawMissing = missing;
            RawBend = bend;
        }

        public static CardCondition Pristine => new CardCondition(0f, 0f, 0f, 0f, 0f);

        public float Scuff => Mathf.Clamp01(RawScuff / ScuffFull);
        public float InkLoss => Mathf.Clamp01(RawInk / InkFull);
        public float Dent => Mathf.Clamp01(RawDent / DentFull);
        /// <summary>
        /// Missing material is small in area and huge in the eye - a corner off the
        /// card is the first thing anyone sees - which is what the tightest full
        /// scale of the five is for.
        /// </summary>
        public float Missing => Mathf.Clamp01(RawMissing / MissingFull);
        public float Bend => Mathf.Clamp01(RawBend);

        /// <summary>1 is untouched, 0 is a ruin.</summary>
        public float Score => Mathf.Clamp01(1f - (Scuff * 0.28f
                                               + InkLoss * 0.24f
                                               + Missing * 0.22f
                                               + Dent * 0.16f
                                               + Bend * 0.10f));

        public CardGrade Grade
        {
            get
            {
                float s = Score;
                if (s >= 0.97f) return CardGrade.Mint;
                if (s >= 0.88f) return CardGrade.NearMint;
                if (s >= 0.72f) return CardGrade.Excellent;
                if (s >= 0.52f) return CardGrade.Good;
                if (s >= 0.28f) return CardGrade.Played;
                return CardGrade.Poor;
            }
        }

        /// <summary>
        /// What the grade does to the price. Stepped rather than continuous on
        /// purpose: a collector pays for the label, so one more rub that tips
        /// Excellent into Near Mint has to be worth something visible.
        /// </summary>
        public float PriceMultiplier => Grade switch
        {
            CardGrade.Mint => 1.6f,
            CardGrade.NearMint => 1.25f,
            CardGrade.Excellent => 1f,
            CardGrade.Good => 0.7f,
            CardGrade.Played => 0.45f,
            _ => 0.2f,
        };

        public static string Label(CardGrade grade) => grade switch
        {
            CardGrade.NearMint => "Near Mint",
            _ => grade.ToString(),
        };

        public override string ToString() => $"{Label(Grade)} ({Score:0.00})";

        /// <summary>Both halves of the numbers, for tuning the full scales above.</summary>
        public string Detail() =>
            $"{Label(Grade),-10} score {Score:0.00}  " +
            $"scuff {Scuff:0.00} ({RawScuff:0.0000})  ink {InkLoss:0.00} ({RawInk:0.0000})  " +
            $"dent {Dent:0.00} ({RawDent:0.0000})  chip {Missing:0.00} ({RawMissing:0.0000})  " +
            $"bend {Bend:0.00}";
    }
}
