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
        // Run this postfix after most other patches so we can cooperate instead of fight
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn, Apparel ap, List<float> wornScoresCache, ref float __result)
        {
            try
            {
                // === Read settings & compatibility mode ===
                VextexSettings settings = VextexModHandler.Settings;
                OutfitAIMode mode = settings?.outfitMode ?? OutfitAIMode.Aggressive;

                if (settings?.externalOutfitController == true || mode == OutfitAIMode.Passive)
                    return;

                // === Safety guards ===
                if (pawn == null || ap == null || ap.def?.apparel == null)
                    return;
                if (!pawn.IsColonist || pawn.IsPrisoner || pawn.Dead)
                    return;
                if (__result < -999f)
                    return;

                // === Build decision context ===
                ApparelDecisionContext ctx = ApparelScoreCalculator.BuildDecisionContext(pawn, ap);
                if (!ctx.IsValid)
                    return;

                // === Compute swap evaluation fields ===
                ComputeSwapFields(ctx, pawn, ap, settings);

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

                // === Apply according to selected mode ===
                if (mode == OutfitAIMode.Cooperative)
                {
                    if (ctx.NetGain <= 0f || ctx.NetGain <= ctx.SwapThreshold)
                        return;

                    float blendWeight = 0.5f;
                    __result = __result + (ctx.NetGain - __result) * blendWeight;
                }
                else
                {
                    if (ctx.NetGain > ctx.SwapThreshold)
                        __result = ctx.NetGain;
                    else
                        __result = -1f;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error in apparel score calculation: {ex.Message}",
                    (pawn?.thingIDNumber ?? 0) ^ 0x7E47E4);
            }
        }

        /// <summary>
        /// Fills the swap-evaluation fields of a decision context (worn score, net gain,
        /// threshold, naked-check).  Separated so the patch body stays clean.
        /// </summary>
        private static void ComputeSwapFields(ApparelDecisionContext ctx, Pawn pawn, Apparel ap, VextexSettings settings)
        {
            float removedWornScore = 0f;
            float currentTotalScore = 0f;
            bool isNaked = true;

            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel != null)
            {
                for (int i = 0; i < wornApparel.Count; i++)
                {
                    Apparel wornItem = wornApparel[i];
                    if (wornItem == null || wornItem.def == null)
                        continue;

                    // Track total current score
                    float wornScore = ApparelScoreCalculator.CalculateScore(pawn, wornItem);
                    if (!float.IsNaN(wornScore) && !float.IsInfinity(wornScore))
                        currentTotalScore += wornScore;

                    // Check conflict
                    if (HasApparelConflict(wornItem.def, ap.def))
                    {
                        // Forced check
                        if (pawn.outfits?.forcedHandler != null)
                        {
                            try
                            {
                                if (pawn.outfits.forcedHandler.IsForced(wornItem))
                                {
                                    ctx.NetGain = -1000f;
                                    ctx.SwapThreshold = 0f;
                                    ctx.IsValid = false;
                                    return;
                                }
                            }
                            catch { }
                        }

                        if (!float.IsNaN(wornScore) && !float.IsInfinity(wornScore))
                            removedWornScore += wornScore;
                    }

                    // Check if pawn already covers candidate body groups
                    if (isNaked)
                    {
                        try
                        {
                            var candidateGroups = ap.def.apparel.bodyPartGroups;
                            var wornGroups = wornItem.def?.apparel?.bodyPartGroups;
                            if (candidateGroups != null && wornGroups != null)
                            {
                                for (int g = 0; g < candidateGroups.Count && isNaked; g++)
                                {
                                    if (wornGroups.Contains(candidateGroups[g]))
                                        isNaked = false;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            ctx.RemovedWornScore = removedWornScore;
            ctx.CurrentTotalScore = currentTotalScore;
            ctx.NetGain = ctx.VextexScore - removedWornScore;
            ctx.IsNakedOnCoveredGroups = isNaked;

            // Base threshold: 5% of current total, min 0.05
            float swapThreshold = currentTotalScore * 0.05f;
            if (swapThreshold < 0.05f)
                swapThreshold = 0.05f;

            // Naked on covered groups → halve threshold
            if (isNaked)
                swapThreshold *= 0.5f;

            // Power-level scaling: top 25% get easier threshold, bottom 25% get stricter
            if (ctx.PowerPercentile >= 0.75f)
                swapThreshold *= 0.7f;
            else if (ctx.PowerPercentile < 0.25f)
                swapThreshold *= 1.2f;

            ctx.SwapThreshold = swapThreshold;
        }

        /// <summary>
        /// Checks if two apparel defs conflict (cannot be worn together).
        /// Two apparel items conflict if they share the same apparel layer AND
        /// overlap on at least one body part group.
        /// </summary>
        private static bool HasApparelConflict(ThingDef a, ThingDef b)
        {
            try
            {
                if (a?.apparel == null || b?.apparel == null)
                    return false;

                var layersA = a.apparel.layers;
                var layersB = b.apparel.layers;
                var groupsA = a.apparel.bodyPartGroups;
                var groupsB = b.apparel.bodyPartGroups;

                if (layersA == null || layersB == null || groupsA == null || groupsB == null)
                    return false;

                // Check for shared layers
                for (int la = 0; la < layersA.Count; la++)
                {
                    for (int lb = 0; lb < layersB.Count; lb++)
                    {
                        if (layersA[la] == layersB[lb])
                        {
                            // Shared layer found - check for body part group overlap
                            for (int ga = 0; ga < groupsA.Count; ga++)
                            {
                                for (int gb = 0; gb < groupsB.Count; gb++)
                                {
                                    if (groupsA[ga] == groupsB[gb])
                                    {
                                        return true; // Conflict: same layer + same body part
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If we can't determine conflict, assume no conflict (safe default)
            }

            return false;
        }
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
