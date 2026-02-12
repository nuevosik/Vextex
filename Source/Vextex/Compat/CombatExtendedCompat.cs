using RimWorld;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft dependency compatibility layer for Combat Extended.
    /// Detects CE at runtime without requiring a direct assembly reference.
    /// Provides normalization factors to translate CE's RHA-based armor values
    /// to the vanilla 0-1 scale used by Vextex's scoring system.
    /// </summary>
    public static class CombatExtendedCompat
    {
        /// <summary>
        /// Whether Combat Extended is currently active in the mod list.
        /// </summary>
        public static bool IsCEActive { get; private set; }

        /// <summary>
        /// Normalization divisor for ArmorRating_Sharp in CE.
        /// CE uses RHA values (typically 2-20+), divide by this to get ~0-1 scale.
        /// </summary>
        public const float DefaultSharpNormalization = 15f;

        /// <summary>
        /// Normalization divisor for ArmorRating_Blunt in CE.
        /// CE uses MPa-based values (typically 5-30+).
        /// </summary>
        public const float DefaultBluntNormalization = 20f;

        /// <summary>
        /// Normalization divisor for ArmorRating_Heat in CE.
        /// Heat armor scales similarly to vanilla in most CE setups.
        /// </summary>
        public const float DefaultHeatNormalization = 2f;

        /// <summary>
        /// Detects whether Combat Extended is loaded.
        /// Should be called once during mod initialization.
        /// </summary>
        public static void DetectCE()
        {
            // CE has used different packageIds over time, check all known variants
            IsCEActive = ModsConfig.IsActive("CETeam.CombatExtended")
                      || ModsConfig.IsActive("CombatExtended")
                      || IsAssemblyLoaded("CombatExtended");

            if (IsCEActive)
            {
                Log.Message("[Vextex] Combat Extended detected! Armor stat normalization enabled.");
            }
            else
            {
                Log.Message("[Vextex] Combat Extended not detected. Using vanilla armor scaling.");
            }
        }

        /// <summary>
        /// Normalizes a sharp armor value from CE's RHA scale to ~0-1 range.
        /// </summary>
        public static float NormalizeSharp(float ceValue, float customFactor = 0f)
        {
            float divisor = customFactor > 0f ? customFactor : DefaultSharpNormalization;
            return ceValue / divisor;
        }

        /// <summary>
        /// Normalizes a blunt armor value from CE's MPa scale to ~0-1 range.
        /// </summary>
        public static float NormalizeBlunt(float ceValue, float customFactor = 0f)
        {
            float divisor = customFactor > 0f ? customFactor : DefaultBluntNormalization;
            return ceValue / divisor;
        }

        /// <summary>
        /// Normalizes a heat armor value from CE scale to ~0-1 range.
        /// </summary>
        public static float NormalizeHeat(float ceValue, float customFactor = 0f)
        {
            float divisor = customFactor > 0f ? customFactor : DefaultHeatNormalization;
            return ceValue / divisor;
        }

        /// <summary>
        /// Gets the Bulk stat value from an apparel item if CE is active.
        /// Uses reflection to avoid hard dependency on CE assembly.
        /// Returns 0 if CE is not active or stat not found.
        /// </summary>
        public static float GetBulkValue(Thing apparel)
        {
            if (!IsCEActive)
                return 0f;

            return GetCEStatValue(apparel, "Bulk");
        }

        /// <summary>
        /// Gets the WornBulk stat value from an apparel item if CE is active.
        /// Returns 0 if CE is not active or stat not found.
        /// </summary>
        public static float GetWornBulkValue(Thing apparel)
        {
            if (!IsCEActive)
                return 0f;

            return GetCEStatValue(apparel, "WornBulk");
        }

        /// <summary>
        /// Tries to get a CE-specific stat value by defName using the game's StatDef database.
        /// This avoids referencing CE types directly.
        /// </summary>
        private static float GetCEStatValue(Thing thing, string statDefName)
        {
            try
            {
                StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
                if (stat != null)
                {
                    return thing.GetStatValue(stat);
                }
            }
            catch (System.Exception)
            {
                // Silently fail - CE stat not available
            }

            return 0f;
        }

        /// <summary>
        /// Checks if a specific assembly is loaded (fallback detection method).
        /// </summary>
        private static bool IsAssemblyLoaded(string assemblyName)
        {
            try
            {
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == assemblyName)
                        return true;
                }
            }
            catch (System.Exception)
            {
                // Ignore reflection errors
            }

            return false;
        }
    }
}
