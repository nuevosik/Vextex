using System;
using RimWorld;
using Verse;
using Vextex.Settings;

namespace Vextex.Core
{
    /// <summary>
    /// Bônus de score para pawns em low mood: prioriza Beauty, SocialImpact e apparel
    /// "confortável" (tribalwear, cloth, tuques) para evitar breakdowns.
    /// Só aplicado quando o papel NÃO é combat-focused (Melee/Ranged/Hunter) durante threat-active.
    /// Traits Depressive, Pessimist, Nervous, Gloomy recebem multiplicador maior.
    /// </summary>
    public static class MoodComfortHelper
    {
        /// <summary>Tags comuns de apparel considerado confortável (cloth, tribal, tuques, etc.).</summary>
        private static readonly string[] ComfortableApparelTags = { "Comfortable", "Cloth", "Tribal", "Tribalwear", "Tuque", "Basic", "Simple" };

        /// <summary>
        /// Retorna bônus de score quando o pawn está em low mood (CurLevelPercentage &lt; moodThreshold).
        /// Considera Beauty, SocialImpact e bonus para apparel confortável; multiplicador para traits sensíveis.
        /// Retorna 0 se mood acima do threshold, ou se papel for combat-focused durante ameaça.
        /// </summary>
        public static float GetMoodComfortBonus(Pawn pawn, Apparel apparel, float moodThreshold, float weight,
            bool isCombatFocusedDuringThreat)
        {
            if (pawn == null || apparel?.def == null || weight <= 0f)
                return 0f;
            if (moodThreshold <= 0f)
                return 0f;
            if (isCombatFocusedDuringThreat)
                return 0f;

            try
            {
                Need_Mood mood = pawn.needs?.mood;
                if (mood == null)
                    return 0f;
                float moodPct = mood.CurLevelPercentage;
                if (moodPct >= moodThreshold)
                    return 0f;

                float beauty = 0f;
                if (StatDefOf.Beauty != null)
                    beauty = apparel.GetStatValue(StatDefOf.Beauty);
                float social = 0f;
                StatDef socialImpact = DefDatabase<StatDef>.GetNamedSilentFail("SocialImpact");
                if (socialImpact != null)
                    social = apparel.GetStatValue(socialImpact);

                float comfortableBonus = IsComfortableApparel(apparel) ? 1.5f : 0f;
                float raw = (Math.Max(0f, beauty) * 2f) + (Math.Max(0f, social) * 1.5f) + comfortableBonus;
                if (raw <= 0f)
                    return 0f;

                float severity = 1f - (moodPct / moodThreshold);
                severity = Math.Min(1f, Math.Max(0f, severity));
                float traitMult = GetMoodSensitiveTraitMultiplier(pawn);
                float result = raw * severity * traitMult * weight;
                return result;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>True se o apparel tem tag ou defName considerado confortável.</summary>
        private static bool IsComfortableApparel(Apparel apparel)
        {
            if (apparel?.def?.apparel?.tags == null)
                return false;
            for (int i = 0; i < ComfortableApparelTags.Length; i++)
            {
                if (apparel.def.apparel.tags.Contains(ComfortableApparelTags[i]))
                    return true;
            }
            string defName = apparel.def.defName ?? "";
            if (defName.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Tribal", StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Tuque", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>Multiplicador &gt; 1 para traits Depressive, Pessimist, Nervous, Gloomy.</summary>
        private static float GetMoodSensitiveTraitMultiplier(Pawn pawn)
        {
            if (pawn?.story?.traits?.allTraits == null)
                return 1f;
            float mult = 1f;
            foreach (Trait t in pawn.story.traits.allTraits)
            {
                if (t?.def?.defName == null) continue;
                string dn = t.def.defName;
                if (string.Equals(dn, "Depressive", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dn, "Pessimist", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dn, "Nervous", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dn, "Gloomy", StringComparison.OrdinalIgnoreCase))
                    mult += 0.5f;
            }
            return mult;
        }
    }
}
