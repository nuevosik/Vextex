using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Vextex.Core
{
    /// <summary>
    /// Evaluates materials used in apparel construction.
    /// Assigns tier bonuses and calculates material-specific stat contributions.
    /// Safe for modded materials — unknown materials are classified via stat analysis.
    /// </summary>
    public static class MaterialEvaluator
    {
        /// <summary>
        /// Material tier classification from S (best) to D (worst).
        /// </summary>
        public enum MaterialTier
        {
            S,  // Hyperweave, Thrumbofur
            A,  // Devilstrand, Plastel
            B,  // Synthread, Camelhide, Bearskin
            C,  // Cloth, Plainleather, most common leathers
            D   // Human leather, Patchleather
        }

        // Tier bonus values
        private static readonly Dictionary<MaterialTier, float> TierBonuses = new Dictionary<MaterialTier, float>
        {
            { MaterialTier.S, 0.40f },
            { MaterialTier.A, 0.30f },
            { MaterialTier.B, 0.20f },
            { MaterialTier.C, 0.10f },
            { MaterialTier.D, 0.00f }
        };

        // Known material tier overrides (defName -> tier)
        // Includes vanilla + popular mod materials
        private static readonly Dictionary<string, MaterialTier> KnownMaterials = new Dictionary<string, MaterialTier>
        {
            // === Vanilla S Tier ===
            { "Hyperweave", MaterialTier.S },
            { "ThrumboFur", MaterialTier.S },

            // === Vanilla A Tier ===
            { "DevilstrandCloth", MaterialTier.A },
            { "Plasteel", MaterialTier.A },

            // === Vanilla B Tier ===
            { "Synthread", MaterialTier.B },
            { "Leather_Camel", MaterialTier.B },
            { "Leather_Bear", MaterialTier.B },
            { "Leather_Rhinoceros", MaterialTier.B },
            { "Leather_Elephant", MaterialTier.B },
            { "Leather_Heavy", MaterialTier.B },
            { "WoolMegasloth", MaterialTier.B },

            // === Vanilla C Tier ===
            { "Cloth", MaterialTier.C },
            { "Leather_Plain", MaterialTier.C },
            { "Leather_Dog", MaterialTier.C },
            { "Leather_Pig", MaterialTier.C },
            { "Leather_Light", MaterialTier.C },
            { "WoolAlpaca", MaterialTier.C },
            { "WoolMuffalo", MaterialTier.C },

            // === Vanilla D Tier ===
            { "Leather_Human", MaterialTier.D },
            { "Leather_Patch", MaterialTier.D },

            // === Vanilla Expanded / Popular Mod Materials ===
            // VFE materials (best-effort defNames, safe if mod not loaded)
            { "VFE_Finlink", MaterialTier.S },
            { "VFE_Eltex", MaterialTier.A },
            { "VFEM_Finsteel", MaterialTier.A },

            // Alpha Animals materials
            { "AA_Ironweave", MaterialTier.S },
            { "AA_Archoweave", MaterialTier.S },

            // Royalty DLC
            { "Leather_Chinchilla", MaterialTier.B },
        };

        /// <summary>
        /// Gets the material tier for a given stuff def.
        /// Falls back to analyzing stats if the material is not in the known list.
        /// Never crashes — returns C tier on any error.
        /// </summary>
        public static MaterialTier GetMaterialTier(ThingDef stuffDef)
        {
            try
            {
                if (stuffDef == null)
                    return MaterialTier.C;

                // Check known materials first
                if (KnownMaterials.TryGetValue(stuffDef.defName, out MaterialTier knownTier))
                    return knownTier;

                // Unknown material - classify based on stat factors
                return ClassifyByStats(stuffDef);
            }
            catch
            {
                return MaterialTier.C;
            }
        }

        /// <summary>
        /// Gets the tier bonus score for the given material.
        /// </summary>
        public static float GetTierBonus(ThingDef stuffDef)
        {
            try
            {
                MaterialTier tier = GetMaterialTier(stuffDef);
                return TierBonuses.TryGetValue(tier, out float bonus) ? bonus : 0.10f;
            }
            catch
            {
                return 0.10f;
            }
        }

        /// <summary>
        /// Calculates a composite material score based on the stuff's intrinsic stat factors.
        /// This captures the material's actual performance for armor and insulation.
        /// Safe for modded materials with unusual stat values.
        /// </summary>
        public static float CalculateMaterialStatScore(ThingDef stuffDef)
        {
            try
            {
                if (stuffDef?.stuffProps == null)
                    return 0f;

                float sharpArmor = GetStatSafe(stuffDef, StatDefOf.StuffPower_Armor_Sharp);
                float bluntArmor = GetStatSafe(stuffDef, StatDefOf.StuffPower_Armor_Blunt);
                float heatArmor = GetStatSafe(stuffDef, StatDefOf.StuffPower_Armor_Heat);
                float coldInsulation = GetStatSafe(stuffDef, StatDefOf.StuffPower_Insulation_Cold);
                float heatInsulation = GetStatSafe(stuffDef, StatDefOf.StuffPower_Insulation_Heat);

                // Weighted composite
                float armorScore = (sharpArmor * 1.5f) + (bluntArmor * 1.2f) + (heatArmor * 0.8f);
                float insulationScore = (coldInsulation + heatInsulation) * 0.5f;

                float result = armorScore + insulationScore;

                // Guard against NaN/Infinity
                if (float.IsNaN(result) || float.IsInfinity(result))
                    return 0f;

                return result;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Classifies an unknown material by analyzing its stat factors.
        /// </summary>
        private static MaterialTier ClassifyByStats(ThingDef stuffDef)
        {
            try
            {
                float score = CalculateMaterialStatScore(stuffDef);

                // Guard against NaN
                if (float.IsNaN(score) || float.IsInfinity(score))
                    return MaterialTier.C;

                if (score >= 2.5f)
                    return MaterialTier.S;
                if (score >= 1.8f)
                    return MaterialTier.A;
                if (score >= 1.0f)
                    return MaterialTier.B;
                if (score >= 0.5f)
                    return MaterialTier.C;

                return MaterialTier.D;
            }
            catch
            {
                return MaterialTier.C;
            }
        }

        /// <summary>
        /// Safely gets an abstract stat value from a ThingDef.
        /// Returns 0 on any error or if the stat doesn't exist.
        /// </summary>
        private static float GetStatSafe(ThingDef def, StatDef stat)
        {
            try
            {
                if (def == null || stat == null)
                    return 0f;

                float value = def.GetStatValueAbstract(stat);

                if (float.IsNaN(value) || float.IsInfinity(value))
                    return 0f;

                return value;
            }
            catch
            {
                return 0f;
            }
        }
    }
}

