using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Vextex.Core;
using Vextex.Settings;

namespace Vextex.Patches
{
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "ApparelScoreGain")]
    public static class Patch_ApparelScoreGain
    {
        /// <summary>Minimum absolute net gain required to recommend a swap. Prevents infinite equip/unequip
        /// when two items have nearly identical scores. Hysteresis % is from Mod Settings (e.g. 25%).</summary>
        private const float MinAbsoluteNetGain = 0.18f;

        /// <summary>Throttle: colônias com mais que este número de pawns têm JobGiver espaçado.</summary>
        private const int ThrottleColonyPawnCount = 15;

        /// <summary>Quando throttle ativo, só permite optimize a cada N ticks por pawn.</summary>
        private const int ThrottleTicksPerPawn = 1200;

        /// <summary>After recommending a swap for a pawn, ignore further recommendations for this pawn for this many ticks.
        /// Must be longer than the time to complete an optimize-apparel job (walk to item + equip), otherwise the pawn
        /// can get a new swap recommendation before finishing, causing infinite equip/unequip loops.</summary>
        private const int SwapCooldownTicks = 600; // ~10 seconds at 1x speed

        private static int _cooldownCacheTick = -1;
        private static readonly Dictionary<int, int> _lastRecommendTickByPawn = new Dictionary<int, int>(32);

        /// <summary>Returns true if this pawn is still in cooldown after a recent swap recommendation.</summary>
        private static bool IsInCooldown(Pawn pawn)
        {
            if (pawn == null) return false;
            int tick = Find.TickManager?.TicksGame ?? 0;
            PruneCooldownCache(tick);
            if (!_lastRecommendTickByPawn.TryGetValue(pawn.thingIDNumber, out int lastTick))
                return false;
            return (tick - lastTick) < SwapCooldownTicks;
        }

        /// <summary>Throttle: quando colônia tem mais de 15 pawns, só permite optimize a cada ThrottleTicksPerPawn por pawn.</summary>
        private static bool IsThrottled(Pawn pawn)
        {
            if (pawn?.Map == null) return false;
            try
            {
                int colonistCount = pawn.Map.mapPawns?.FreeColonists?.Count ?? 0;
                if (colonistCount <= ThrottleColonyPawnCount) return false;
                int tick = Find.TickManager?.TicksGame ?? 0;
                int bucket = (tick / ThrottleTicksPerPawn) % Math.Max(1, colonistCount);
                int pawnBucket = pawn.thingIDNumber % Math.Max(1, colonistCount);
                return bucket != pawnBucket;
            }
            catch { return false; }
        }

        /// <summary>Remove cooldown entries that have expired to avoid unbounded growth.</summary>
        private static void PruneCooldownCache(int currentTick)
        {
            if (currentTick == _cooldownCacheTick) return;
            _cooldownCacheTick = currentTick;
            var toRemove = new List<int>();
            foreach (var kv in _lastRecommendTickByPawn)
            {
                if ((currentTick - kv.Value) >= SwapCooldownTicks)
                    toRemove.Add(kv.Key);
            }
            foreach (int key in toRemove)
                _lastRecommendTickByPawn.Remove(key);
        }

        /// <summary>Marks that we recommended a swap for this pawn this tick.</summary>
        private static void SetCooldown(Pawn pawn)
        {
            if (pawn == null) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            _lastRecommendTickByPawn[pawn.thingIDNumber] = tick;
        }

        // Run this postfix after most other patches so we can cooperate instead of fight
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn, Apparel ap, List<float> wornScoresCache, ref float __result)
        {
            try
            {
                // === Read settings: when another mod controls outfit AI, do not touch __result (100% vanilla behavior) ===
                VextexSettings settings = VextexModHandler.Settings;
                if (settings?.externalOutfitController == true)
                    return;

                // === Safety guards ===
                if (pawn == null || ap == null || ap.def?.apparel == null)
                    return;
                if (!pawn.IsColonist || pawn.IsPrisoner || pawn.Dead)
                    return;
                // Drafted pawns must not swap apparel mid-combat (mods may force job anyway)
                if (pawn.Drafted)
                {
                    __result = -1f;
                    return;
                }
                if (__result < -999f)
                    return;
                // Never recommend swapping to something the pawn is already wearing (avoids loops from other mods or edge cases)
                if (pawn.apparel?.WornApparel != null && pawn.apparel.WornApparel.Contains(ap))
                {
                    __result = -1f;
                    return;
                }

                // === Build decision context ===
                ApparelDecisionContext ctx = ApparelScoreCalculator.BuildDecisionContext(pawn, ap);
                if (!ctx.IsValid)
                    return;

                // === Compute swap evaluation fields ===
                ApparelScoreCalculator.ComputeSwapFields(ctx, pawn, ap);
                if (!ctx.IsValid)
                    return;

                // === Verbose logging ===
                if (settings != null && settings.enableVerboseLogging && Prefs.DevMode)
                {
                    Log.Message($"[Vextex Verbose] {pawn.LabelShort} vs {ap.def.defName}: " +
                                $"Armor={ctx.ArmorScoreRaw:F2}, Insul={ctx.InsulationScoreRaw:F2}, " +
                                $"Mat={ctx.MaterialScoreRaw:F2}, Qual={ctx.QualityScoreRaw:F2}, " +
                                $"Pen={ctx.PenaltyScoreRaw:F2}, Score={ctx.VextexScore:F2}, " +
                                $"NetGain={ctx.NetGain:F2}, Thresh={ctx.SwapThreshold:F3}, " +
                                $"Naked={ctx.IsNakedOnCoveredGroups}, PwrPct={ctx.PowerPercentile:F2}");
                }

                // === Single behavior: recommend swap only when gain is clear (hysteresis from settings, e.g. 25% better) and not in cooldown ===
                bool recommendSwap = ctx.NetGain > 0f
                    && ctx.NetGain >= ctx.SwapThreshold
                    && ctx.NetGain >= MinAbsoluteNetGain
                    && !IsInCooldown(pawn)
                    && !IsThrottled(pawn);

                // === Debug logging (DevMode): pawn, role, score change, threshold, breakdown ===
                if (settings != null && settings.debugLogging && Prefs.DevMode)
                {
                    PawnRoleDetector.PawnRole role = PawnRoleDetector.DetectRole(pawn);
                    float oldScore = ctx.CurrentTotalScore;
                    float newScore = oldScore + ctx.NetGain;
                    float threshPct = ctx.CurrentTotalScore > 0f ? ctx.SwapThreshold / ctx.CurrentTotalScore : 0f;
                    Log.Message($"[Vextex] {pawn.LabelShort} ({role}) | Score: {oldScore:F2} → {newScore:F2} (+{ctx.NetGain:F2}) | Threshold: {threshPct:P0} | Swap: {recommendSwap} | Breakdown: Thermal={ctx.InsulationScoreRaw:F2} Armor={ctx.ArmorScoreRaw:F2} Quality={ctx.QualityScoreRaw:F2} Penalties={ctx.PenaltyScoreRaw:F2}");
                }

                if (recommendSwap)
                {
                    SetCooldown(pawn);
                    __result = ctx.NetGain;
                }
                else
                    __result = -1f;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error in apparel score calculation: {ex.Message}",
                    (pawn?.thingIDNumber ?? 0) ^ 0x7E47E4);
            }
        }

        /// <summary>
        /// Conflict check delegated to ApparelScoreCalculator for reuse by debug tools.
        /// </summary>
        private static bool HasApparelConflict(ThingDef a, ThingDef b) => ApparelScoreCalculator.HasApparelConflict(a, b);
    }

    /// <summary>
    /// Optional: Patch to log Vextex decisions for debugging.
    /// Only active when dev mode is enabled.
    /// Fully wrapped in try-catch to never crash the game.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob")]
    public static class Patch_TryGiveJob_Debug
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Verse.AI.Job __result)
        {
            try
            {
                if (!Prefs.DevMode || __result == null)
                    return;

                if (pawn == null || !pawn.IsColonist || pawn.Dead)
                    return;

                ColonistRoleDetector.CombatRole role = ColonistRoleDetector.DetectRole(pawn);
                float power = ColonistRoleDetector.CalculatePowerLevel(pawn);
                float percentile = ColonistRoleDetector.GetPowerPercentile(pawn);

                VextexSettings settings = VextexModHandler.Settings;
                string preset = settings?.currentPreset.ToString() ?? "?";

                Log.Message($"[Vextex Debug] {pawn.LabelShort}: Role={role}, Power={power:F1}, " +
                           $"Percentile={percentile:P0}, Preset={preset}, Changing apparel via job.");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Debug logging error: {ex.Message}", 0x7E47E6);
            }
        }
    }
}
