using System;
using RimWorld;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft dependency compatibility layer for Combat Extended.
    /// Detects CE at runtime without requiring a direct assembly reference.
    /// Provides normalization factors to translate CE's RHA-based armor values
    /// to the vanilla 0-1 scale used by Vextex's scoring system.
    /// Caches CE stat lookups and supports separate tuning for Sharp/Blunt/Heat.
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

        // Cached CE stat defs (resolved once after detection; null = use DefDatabase each time or not found)
        private static StatDef _cachedBulkStat;
        private static StatDef _cachedWornBulkStat;
        private static bool _ceStatsResolved;
        private static bool _warnedMissingWornBulk;

        /// <summary>
        /// Detects whether Combat Extended is loaded and resolves CE-specific stats.
        /// Should be called once during mod initialization.
        /// </summary>
        public static void DetectCE()
        {
            IsCEActive = ModsConfig.IsActive("CETeam.CombatExtended")
                      || ModsConfig.IsActive("CombatExtended")
                      || IsAssemblyLoaded("CombatExtended");

            _ceStatsResolved = false;
            _cachedBulkStat = null;
            _cachedWornBulkStat = null;
            _warnedMissingWornBulk = false;

            if (IsCEActive)
            {
                ResolveCEStats();
                Log.Message("[Vextex] Combat Extended detected! Armor normalization and bulk penalties enabled.");
            }
            else
            {
                Log.Message("[Vextex] Combat Extended not detected. Using vanilla armor scaling.");
            }
        }

        /// <summary>
        /// Resolves and caches CE stat defs (Bulk, WornBulk). Tries known defNames.
        /// </summary>
        private static void ResolveCEStats()
        {
            if (_ceStatsResolved)
                return;

            try
            {
                // CE uses "Bulk" and "WornBulk" in DefDatabase; some forks may use a prefix
                string[] bulkNames = { "Bulk", "CE_Bulk" };
                string[] wornBulkNames = { "WornBulk", "CE_WornBulk" };

                foreach (string name in bulkNames)
                {
                    _cachedBulkStat = DefDatabase<StatDef>.GetNamedSilentFail(name);
                    if (_cachedBulkStat != null)
                        break;
                }

                foreach (string name in wornBulkNames)
                {
                    _cachedWornBulkStat = DefDatabase<StatDef>.GetNamedSilentFail(name);
                    if (_cachedWornBulkStat != null)
                        break;
                }

                _ceStatsResolved = true;

                if (_cachedWornBulkStat == null && !_warnedMissingWornBulk)
                {
                    _warnedMissingWornBulk = true;
                    Log.Warning("[Vextex] Combat Extended is active but WornBulk stat was not found. Bulk penalties will be disabled. Check CE version or report to Vextex.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] Error resolving CE stats (non-fatal): {ex.Message}");
                _ceStatsResolved = true;
            }
        }

        /// <summary>
        /// Normalizes a sharp armor value from CE's RHA scale to ~0-1 range.
        /// divisorOrZero: use this divisor; if 0 or negative, uses DefaultSharpNormalization.
        /// </summary>
        public static float NormalizeSharp(float ceValue, float divisorOrZero = 0f)
        {
            float divisor = divisorOrZero > 0f ? divisorOrZero : DefaultSharpNormalization;
            return SanitizeFloat(ceValue / divisor, 0f);
        }

        /// <summary>
        /// Normalizes a blunt armor value from CE's MPa scale to ~0-1 range.
        /// divisorOrZero: use this divisor; if 0 or negative, uses DefaultBluntNormalization.
        /// </summary>
        public static float NormalizeBlunt(float ceValue, float divisorOrZero = 0f)
        {
            float divisor = divisorOrZero > 0f ? divisorOrZero : DefaultBluntNormalization;
            return SanitizeFloat(ceValue / divisor, 0f);
        }

        /// <summary>
        /// Normalizes a heat armor value from CE scale to ~0-1 range.
        /// divisorOrZero: use this divisor; if 0 or negative, uses DefaultHeatNormalization.
        /// </summary>
        public static float NormalizeHeat(float ceValue, float divisorOrZero = 0f)
        {
            float divisor = divisorOrZero > 0f ? divisorOrZero : DefaultHeatNormalization;
            return SanitizeFloat(ceValue / divisor, 0f);
        }

        /// <summary>
        /// Resolves the divisor to use for Sharp armor when CE is active.
        /// globalOverride &gt; 0: use for all three types; else use perStat if &gt; 0, else default.
        /// </summary>
        public static float ResolveSharpDivisor(float globalOverride, float perStat)
        {
            if (globalOverride > 0f)
                return globalOverride;
            return perStat > 0f ? perStat : 0f; // 0 = caller uses default
        }

        /// <summary>
        /// Resolves the divisor to use for Blunt armor when CE is active.
        /// </summary>
        public static float ResolveBluntDivisor(float globalOverride, float perStat)
        {
            if (globalOverride > 0f)
                return globalOverride;
            return perStat > 0f ? perStat : 0f;
        }

        /// <summary>
        /// Resolves the divisor to use for Heat armor when CE is active.
        /// </summary>
        public static float ResolveHeatDivisor(float globalOverride, float perStat)
        {
            if (globalOverride > 0f)
                return globalOverride;
            return perStat > 0f ? perStat : 0f;
        }

        /// <summary>
        /// Gets the Bulk stat value from an apparel item if CE is active.
        /// Returns 0 if CE is not active or stat not found. Result is sanitized (no NaN/Infinity).
        /// </summary>
        public static float GetBulkValue(Thing apparel)
        {
            if (!IsCEActive)
                return 0f;
            return GetCEStatValue(apparel, _cachedBulkStat, "Bulk", "CE_Bulk");
        }

        /// <summary>
        /// Gets the WornBulk stat value from an apparel item if CE is active.
        /// Returns 0 if CE is not active or stat not found. Result is sanitized.
        /// </summary>
        public static float GetWornBulkValue(Thing apparel)
        {
            if (!IsCEActive)
                return 0f;
            return GetCEStatValue(apparel, _cachedWornBulkStat, "WornBulk", "CE_WornBulk");
        }

        /// <summary>
        /// Tries to get a CE-specific stat value. Uses cached stat if available, else resolves by defName.
        /// Tries fallback defNames. Returns sanitized value (no NaN/Infinity).
        /// </summary>
        private static float GetCEStatValue(Thing thing, StatDef cachedStat, string primaryDefName, string fallbackDefName)
        {
            if (thing == null)
                return 0f;
            try
            {
                StatDef stat = cachedStat;
                if (stat == null)
                {
                    stat = DefDatabase<StatDef>.GetNamedSilentFail(primaryDefName)
                        ?? DefDatabase<StatDef>.GetNamedSilentFail(fallbackDefName);
                }
                if (stat != null)
                {
                    float value = thing.GetStatValue(stat);
                    return SanitizeFloat(value, 0f);
                }
            }
            catch (Exception)
            {
                // Silently fail - CE stat not available or GetStatValue failed
            }
            return 0f;
        }

        private static float SanitizeFloat(float value, float fallback = 0f)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value;
        }

        private static bool IsAssemblyLoaded(string assemblyName)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == assemblyName)
                        return true;
                }
            }
            catch (Exception)
            {
                // Ignore reflection errors
            }
            return false;
        }
    }
}
