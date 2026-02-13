using System;
using RimWorld;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Resolves optional StatDefs (vanilla or modded) once at startup (ModCompat.DetectAll).
    /// Use these cached refs instead of DefDatabase.GetNamedSilentFail in hot paths.
    /// CE-specific stats (Bulk, WornBulk) are resolved in CombatExtendedCompat.DetectCE.
    /// All lookups are safe; missing stats are null and callers must check.
    /// </summary>
    public static class OptionalStatsResolver
    {
        private static bool _resolved;

        /// <summary>ShootingAccuracyPawn — may be added by combat mods. Null if not present.</summary>
        public static StatDef ShootingAccuracyPawn { get; private set; }

        /// <summary>AimingDelayFactor — may be added by combat mods. Null if not present.</summary>
        public static StatDef AimingDelayFactor { get; private set; }

        /// <summary>ComfyTemperatureMin — vanilla; zone de conforto térmico mínima do pawn.</summary>
        public static StatDef ComfyTemperatureMin { get; private set; }

        /// <summary>ComfyTemperatureMax — vanilla; zone de conforto térmico máxima do pawn.</summary>
        public static StatDef ComfyTemperatureMax { get; private set; }

        /// <summary>ToxicResistance — Biotech; resistência a toxinas em mapa poluído.</summary>
        public static StatDef ToxicResistance { get; private set; }

        /// <summary>PsychicSensitivity — Royalty; Eltex gear increases psycast effectiveness.</summary>
        public static StatDef PsychicSensitivity { get; private set; }

        /// <summary>PsychicEntropyRecoveryRate — Royalty; neural heat recovery from apparel.</summary>
        public static StatDef PsychicEntropyRecoveryRate { get; private set; }

        /// <summary>
        /// Resolves all optional stats. Call once during mod initialization (after Defs loaded).
        /// </summary>
        public static void ResolveAll()
        {
            if (_resolved)
                return;

            try
            {
                ShootingAccuracyPawn = DefDatabase<StatDef>.GetNamedSilentFail("ShootingAccuracyPawn");
                AimingDelayFactor = DefDatabase<StatDef>.GetNamedSilentFail("AimingDelayFactor");
                ComfyTemperatureMin = DefDatabase<StatDef>.GetNamedSilentFail("ComfyTemperatureMin");
                ComfyTemperatureMax = DefDatabase<StatDef>.GetNamedSilentFail("ComfyTemperatureMax");
                ToxicResistance = DefDatabase<StatDef>.GetNamedSilentFail("ToxicResistance");
                PsychicSensitivity = DefDatabase<StatDef>.GetNamedSilentFail("PsychicSensitivity");
                PsychicEntropyRecoveryRate = DefDatabase<StatDef>.GetNamedSilentFail("PsychicEntropyRecoveryRate");
                _resolved = true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] OptionalStatsResolver failed (non-fatal): {ex.Message}");
                _resolved = true;
            }
        }
    }
}
