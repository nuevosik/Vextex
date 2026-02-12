using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Vextex.Compat;
using Vextex.Settings;

namespace Vextex.Core
{
    /// <summary>
    /// Central scoring engine for apparel evaluation.
    /// Combines protection, insulation, material quality, item quality,
    /// combat role multipliers, and skill-based prioritization.
    /// All calculations are guarded against NaN/Infinity from modded stats.
    /// Includes a lightweight per-tick cache to avoid recalculating the same
    /// (pawn, apparel) pair multiple times within the same game tick.
    /// </summary>
    public static class ApparelScoreCalculator
    {
        internal enum ColonyStage
        {
            Early,
            Mid,
            Late
        }

        // ====================================================================
        // Lightweight per-tick score cache
        // ====================================================================
        private static int _cacheTick = -1;
        private static readonly Dictionary<long, float> _scoreCache = new Dictionary<long, float>(256);

        /// <summary>Maximum number of cache entries before forced eviction.</summary>
        private const int MaxCacheEntries = 2048;

        /// <summary>
        /// Returns a combined key for a (pawn, apparel) pair.
        /// </summary>
        private static long CacheKey(int pawnId, int apparelId)
        {
            return ((long)pawnId << 32) | (uint)apparelId;
        }

        /// <summary>
        /// Ensures the cache is valid for the current tick. Clears it when the tick changes.
        /// </summary>
        private static void ValidateCache()
        {
            try
            {
                int tick = Find.TickManager?.TicksGame ?? -1;
                if (tick != _cacheTick || _scoreCache.Count > MaxCacheEntries)
                {
                    _scoreCache.Clear();
                    _cacheTick = tick;
                }
            }
            catch
            {
                _scoreCache.Clear();
                _cacheTick = -1;
            }
        }

        /// <summary>
        /// Calculates a comprehensive score for how suitable an apparel item is
        /// for a specific pawn, considering all Vextex factors.
        /// Returns 0 on any error — never crashes.
        /// </summary>
        public static float CalculateScore(Pawn pawn, Apparel apparel)
        {
            try
            {
                if (pawn == null || apparel == null || apparel.def == null)
                    return 0f;

                // Check tick cache first
                ValidateCache();
                long key = CacheKey(pawn.thingIDNumber, apparel.thingIDNumber);
                if (_scoreCache.TryGetValue(key, out float cached))
                    return cached;

                VextexSettings settings = VextexModHandler.Settings;
                ScoringWeights w = settings?.ResolveWeightsForPawn(pawn) ?? DefaultWeights();

                ColonistRoleDetector.CombatRole role = ColonistRoleDetector.DetectRole(pawn);
                ColonistRoleDetector.RoleMultipliers roleMult = ColonistRoleDetector.GetMultipliers(role);
                ColonyStage stage = GetColonyStage(pawn, w);

                float armorScore = CalculateArmorScore(apparel, roleMult, w);
                float insulationScore = CalculateInsulationScore(pawn, apparel, roleMult);
                float materialScore = CalculateMaterialScore(pawn, apparel, role, stage, w);
                float qualityScore = CalculateQualityScore(apparel);
                float durabilityFactor = CalculateDurabilityFactor(apparel);
                float penaltyScore = CalculatePenalties(apparel, roleMult, w);

                // Anomaly detection: warn about extreme component values
                DetectAnomalies(apparel, armorScore, insulationScore, materialScore, penaltyScore);

                float result = ComposeScore(armorScore, insulationScore, materialScore,
                    qualityScore, durabilityFactor, penaltyScore, pawn, w);

                _scoreCache[key] = result;
                return result;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error calculating score for {apparel?.def?.defName ?? "unknown"}: {ex.Message}",
                    (apparel?.thingIDNumber ?? 0) ^ 0x7E47EA);
                return 0f;
            }
        }

        /// <summary>
        /// Warns once per apparel def if any score component is unreasonably large,
        /// which usually indicates misconfigured modded XML.
        /// </summary>
        private static void DetectAnomalies(Apparel apparel, float armor, float insulation, float material, float penalty)
        {
            const float Limit = 100f;
            try
            {
                if (Math.Abs(armor) > Limit || Math.Abs(insulation) > Limit ||
                    Math.Abs(material) > Limit || Math.Abs(penalty) > Limit)
                {
                    Log.WarningOnce(
                        $"[Vextex] Anomalous score component for {apparel.def.defName}: " +
                        $"Armor={armor:F1}, Insul={insulation:F1}, Mat={material:F1}, Pen={penalty:F1}. " +
                        "This may indicate a modded XML misconfiguration.",
                        apparel.def.shortHash ^ 0x7E47EC);
                }
            }
            catch { }
        }

        private static ScoringWeights DefaultWeights()
        {
            return new ScoringWeights
            {
                armorWeight = 1.5f, insulationWeight = 1.0f, materialWeight = 1.0f, qualityWeight = 1.3f,
                movePenaltyWeight = 1.0f, aimPenaltyWeight = 1.0f, bulkPenaltyWeight = 1.0f,
                meleeMaterialBias = 1.10f, rangedMaterialBias = 1.00f, nonCombatantMaterialBias = 0.90f,
                meleeThreshold = 8f, rangedThreshold = 6f, nonCombatantCeiling = 5f,
                earlyGameDaysThreshold = 30f, lateGameWealthThreshold = 100000f,
                tierSWeight = 1.15f, tierAWeight = 1.10f, tierBWeight = 1.00f, tierCWeight = 1.00f, tierDWeight = 0.90f,
                enableMaterialEvaluation = true, enableSkillPriority = true, enableRoleDetection = true,
                ceNormalizationFactor = 0f
            };
        }

        /// <summary>
        /// Builds a full decision context for a (pawn, candidate) pair.
        /// Used by the patch for decision-making, by debug tools for breakdown display,
        /// and by the public API.  Never throws.
        /// </summary>
        public static ApparelDecisionContext BuildDecisionContext(Pawn pawn, Apparel candidate)
        {
            var ctx = new ApparelDecisionContext
            {
                Pawn = pawn,
                Candidate = candidate,
                IsValid = false
            };

            try
            {
                if (pawn == null || candidate == null || candidate.def == null)
                    return ctx;

                VextexSettings settings = VextexModHandler.Settings;
                ScoringWeights w = settings?.ResolveWeightsForPawn(pawn) ?? DefaultWeights();

                ctx.Role = ColonistRoleDetector.DetectRole(pawn);
                ctx.RoleMult = ColonistRoleDetector.GetMultipliers(ctx.Role);

                ColonyStage stage = GetColonyStage(pawn, w);

                ctx.ArmorScoreRaw = CalculateArmorScore(candidate, ctx.RoleMult, w);
                ctx.InsulationScoreRaw = CalculateInsulationScore(pawn, candidate, ctx.RoleMult);
                ctx.MaterialScoreRaw = CalculateMaterialScore(pawn, candidate, ctx.Role, stage, w);
                ctx.QualityScoreRaw = CalculateQualityScore(candidate);
                ctx.DurabilityFactor = CalculateDurabilityFactor(candidate);
                ctx.PenaltyScoreRaw = CalculatePenalties(candidate, ctx.RoleMult, w);

                ctx.VextexScore = ComposeScore(
                    ctx.ArmorScoreRaw, ctx.InsulationScoreRaw, ctx.MaterialScoreRaw,
                    ctx.QualityScoreRaw, ctx.DurabilityFactor, ctx.PenaltyScoreRaw,
                    pawn, w);

                if (float.IsNaN(ctx.VextexScore) || float.IsInfinity(ctx.VextexScore))
                    return ctx;

                ctx.PowerPercentile = ColonistRoleDetector.GetPowerPercentile(pawn);
                ctx.IsValid = true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error building decision context: {ex.Message}",
                    (candidate?.thingIDNumber ?? 0) ^ 0x7E47EB);
            }

            return ctx;
        }

        /// <summary>
        /// Public, compatibility-friendly entry point for other mods.
        /// Never throws, and returns false only if scoring is not possible for this case.
        /// </summary>
        public static bool TryCalculateScore(Pawn pawn, Apparel apparel, out float score)
        {
            score = 0f;

            if (pawn == null || apparel == null)
                return false;

            float result = CalculateScore(pawn, apparel);

            if (float.IsNaN(result) || float.IsInfinity(result))
                return false;

            score = result;
            return true;
        }

        /// <summary>
        /// Public entry point that returns a full score breakdown for other mods or debug tools.
        /// </summary>
        public static bool GetScoreBreakdown(Pawn pawn, Apparel apparel, out ApparelDecisionContext ctx)
        {
            ctx = BuildDecisionContext(pawn, apparel);
            return ctx.IsValid;
        }

        /// <summary>
        /// Combines individual component scores into the final weighted composite score.
        /// </summary>
        internal static float ComposeScore(
            float armorScore, float insulationScore, float materialScore,
            float qualityScore, float durabilityFactor, float penaltyScore,
            Pawn pawn, ScoringWeights w)
        {
            float compositeScore =
                (SanitizeFloat(armorScore) * w.armorWeight) +
                (SanitizeFloat(insulationScore) * w.insulationWeight) +
                (SanitizeFloat(materialScore) * w.materialWeight) +
                (SanitizeFloat(qualityScore) * w.qualityWeight) +
                SanitizeFloat(penaltyScore);

            compositeScore *= SanitizeFloat(durabilityFactor, 1.0f);

            if (w.enableSkillPriority)
            {
                float skillMultiplier = ColonistRoleDetector.GetSkillPriorityMultiplier(pawn);
                compositeScore *= SanitizeFloat(skillMultiplier, 1.0f);
            }

            return SanitizeFloat(compositeScore);
        }

        /// <summary>
        /// Calculates armor contribution to the score, weighted by combat role.
        /// </summary>
        private static float CalculateArmorScore(Apparel apparel, ColonistRoleDetector.RoleMultipliers roleMult, ScoringWeights w)
        {
            try
            {
                float sharpArmor = GetStatValueSafe(apparel, StatDefOf.ArmorRating_Sharp);
                float bluntArmor = GetStatValueSafe(apparel, StatDefOf.ArmorRating_Blunt);
                float heatArmor = GetStatValueSafe(apparel, StatDefOf.ArmorRating_Heat);

                if (CombatExtendedCompat.IsCEActive)
                {
                    float customFactor = w.ceNormalizationFactor;
                    sharpArmor = CombatExtendedCompat.NormalizeSharp(sharpArmor, customFactor);
                    bluntArmor = CombatExtendedCompat.NormalizeBlunt(bluntArmor, customFactor);
                    heatArmor = CombatExtendedCompat.NormalizeHeat(heatArmor, customFactor);
                }

                float score =
                    (sharpArmor * roleMult.armorSharp) +
                    (bluntArmor * roleMult.armorBlunt) +
                    (heatArmor * roleMult.armorHeat);

                return SanitizeFloat(score);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Calculates insulation score, dynamically weighted based on the current biome temperature.
        /// </summary>
        private static float CalculateInsulationScore(Pawn pawn, Apparel apparel,
            ColonistRoleDetector.RoleMultipliers roleMult)
        {
            try
            {
                float coldInsulation = GetStatValueSafe(apparel, StatDefOf.Insulation_Cold);
                float heatInsulation = GetStatValueSafe(apparel, StatDefOf.Insulation_Heat);

                // Get biome temperature to decide which insulation matters more
                float biomeTemp = GetBiomeAverageTemperature(pawn);

                float coldWeight, heatWeight;

                if (biomeTemp < 0f)
                {
                    // Cold biome: cold insulation is critical
                    coldWeight = 2.0f + Math.Abs(biomeTemp) * 0.02f; // scales with how cold
                    heatWeight = 0.3f;
                }
                else if (biomeTemp > 30f)
                {
                    // Hot biome: heat insulation is critical
                    coldWeight = 0.3f;
                    heatWeight = 2.0f + (biomeTemp - 30f) * 0.02f; // scales with how hot
                }
                else
                {
                    // Temperate: balanced
                    coldWeight = 1.0f;
                    heatWeight = 1.0f;
                }

                // Cold insulation is stored as negative in RimWorld (e.g., -40 means 40 degrees of cold protection)
                // We use the absolute value for scoring
                float score = (Math.Abs(coldInsulation) * coldWeight + heatInsulation * heatWeight) * roleMult.insulation;

                // Normalize to reasonable range, clamp to prevent extreme values from modded gear
                float normalized = score * 0.01f;
                return Math.Max(-5f, Math.Min(5f, SanitizeFloat(normalized)));
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Calculates the material quality bonus if material evaluation is enabled.
        /// Takes into account colony stage (early/mid/late) and pawn combat role.
        /// </summary>
        private static float CalculateMaterialScore(
            Pawn pawn,
            Apparel apparel,
            ColonistRoleDetector.CombatRole role,
            ColonyStage stage,
            ScoringWeights w)
        {
            try
            {
                if (!w.enableMaterialEvaluation)
                    return 0f;

                if (apparel.def == null || !apparel.def.MadeFromStuff)
                    return 0.15f;

                ThingDef stuff = apparel.Stuff;
                if (stuff == null)
                    return 0.15f;

                MaterialEvaluator.MaterialTier tier = MaterialEvaluator.GetMaterialTier(stuff);
                float tierBonus = MaterialEvaluator.GetTierBonus(stuff);

                float roleBias = 1f;
                switch (role)
                {
                    case ColonistRoleDetector.CombatRole.Melee:    roleBias = w.meleeMaterialBias; break;
                    case ColonistRoleDetector.CombatRole.Ranged:   roleBias = w.rangedMaterialBias; break;
                    default:                                        roleBias = w.nonCombatantMaterialBias; break;
                }

                float tierWeight = 1f;
                switch (tier)
                {
                    case MaterialEvaluator.MaterialTier.S: tierWeight = w.tierSWeight; break;
                    case MaterialEvaluator.MaterialTier.A: tierWeight = w.tierAWeight; break;
                    case MaterialEvaluator.MaterialTier.B: tierWeight = w.tierBWeight; break;
                    case MaterialEvaluator.MaterialTier.C: tierWeight = w.tierCWeight; break;
                    default:                               tierWeight = w.tierDWeight; break;
                }

                float stageMultiplier = 1f;
                switch (stage)
                {
                    case ColonyStage.Early:
                        if (tier == MaterialEvaluator.MaterialTier.S || tier == MaterialEvaluator.MaterialTier.A)
                            stageMultiplier = 0.9f;
                        else if (tier == MaterialEvaluator.MaterialTier.B || tier == MaterialEvaluator.MaterialTier.C)
                            stageMultiplier = 1.1f;
                        break;
                    case ColonyStage.Late:
                        if (tier == MaterialEvaluator.MaterialTier.S || tier == MaterialEvaluator.MaterialTier.A)
                            stageMultiplier = 1.15f;
                        else if (tier == MaterialEvaluator.MaterialTier.D)
                            stageMultiplier = 0.8f;
                        break;
                }

                float scaledTierBonus = tierBonus * tierWeight * roleBias * stageMultiplier;
                float statScore = MaterialEvaluator.CalculateMaterialStatScore(stuff) * 0.1f * roleBias;

                return SanitizeFloat(scaledTierBonus + statScore);
            }
            catch
            {
                return 0.15f;
            }
        }

        /// <summary>
        /// Calculates quality bonus. Higher quality items get significantly more score.
        /// </summary>
        private static float CalculateQualityScore(Apparel apparel)
        {
            try
            {
                QualityCategory quality;
                if (!apparel.TryGetQuality(out quality))
                    return 0.5f; // items without quality get baseline

                switch (quality)
                {
                    case QualityCategory.Awful:       return 0.3f;
                    case QualityCategory.Poor:        return 0.5f;
                    case QualityCategory.Normal:      return 1.0f;
                    case QualityCategory.Good:        return 1.3f;
                    case QualityCategory.Excellent:   return 1.7f;
                    case QualityCategory.Masterwork:  return 2.2f;
                    case QualityCategory.Legendary:   return 2.8f;
                    default:                          return 1.0f;
                }
            }
            catch
            {
                return 0.5f;
            }
        }

        /// <summary>
        /// Durability factor: items closer to breaking are less desirable.
        /// Returns a value from 0.5 (nearly broken) to 1.0 (perfect condition).
        /// </summary>
        private static float CalculateDurabilityFactor(Apparel apparel)
        {
            try
            {
                if (apparel.MaxHitPoints <= 0)
                    return 0.5f;

                float hpPercent = (float)apparel.HitPoints / apparel.MaxHitPoints;
                // Map from [0,1] to [0.5, 1.0] so even badly damaged gear isn't zero
                return 0.5f + (SanitizeFloat(hpPercent) * 0.5f);
            }
            catch
            {
                return 0.5f;
            }
        }

        /// <summary>
        /// Calculates negative score contributions from movement speed and aim penalties.
        /// </summary>
        private static float CalculatePenalties(Apparel apparel, ColonistRoleDetector.RoleMultipliers roleMult, ScoringWeights w)
        {
            float score = 0f;

            try
            {
                float movePenaltyW = w.movePenaltyWeight;
                float aimPenaltyW = w.aimPenaltyWeight;
                float bulkPenaltyW = w.bulkPenaltyWeight;

                // Check for move speed offset (negative = penalty)
                if (StatDefOf.MoveSpeed != null)
                {
                    float moveSpeedOffset = GetEquippedStatOffset(apparel, StatDefOf.MoveSpeed);
                    if (moveSpeedOffset < 0f)
                    {
                        score += moveSpeedOffset * roleMult.moveSpeedPenalty * movePenaltyW;
                    }
                }

                // Check for shooting accuracy and aiming time penalties
                if (roleMult.aimPenalty != 0f)
                {
                    // Check for ShootingAccuracyPawn offset if it exists
                    StatDef shootingAccuracy = DefDatabase<StatDef>.GetNamedSilentFail("ShootingAccuracyPawn");
                    if (shootingAccuracy != null)
                    {
                        float aimOffset = GetEquippedStatOffset(apparel, shootingAccuracy);
                        if (aimOffset < 0f)
                        {
                            score += aimOffset * roleMult.aimPenalty * aimPenaltyW;
                        }
                    }

                    // Also check AimingDelayFactor - higher values are worse for ranged
                    StatDef aimingDelay = DefDatabase<StatDef>.GetNamedSilentFail("AimingDelayFactor");
                    if (aimingDelay != null)
                    {
                        float delayFactor = GetEquippedStatOffset(apparel, aimingDelay);
                        if (delayFactor > 0f)
                        {
                            score += delayFactor * roleMult.aimPenalty * 0.5f * aimPenaltyW;
                        }
                    }
                }

                // CE-specific: Bulk and WornBulk penalties
                if (CombatExtendedCompat.IsCEActive)
                {
                    float wornBulk = CombatExtendedCompat.GetWornBulkValue(apparel);
                    if (wornBulk > 0f)
                    {
                        float bulkPenalty = wornBulk * 0.02f;
                        if (roleMult.aimPenalty != 0f)
                        {
                            bulkPenalty *= 1.5f; // Ranged cares more about bulk
                        }
                        score -= bulkPenalty * bulkPenaltyW;
                    }
                }
            }
            catch
            {
                // Penalty calculation failed — return what we have so far
            }

            // Clamp extreme penalties so a single heavy piece cannot be considered
            // infinitely worse than being naked. We loosely tie this to armor so
            // very protective gear can still be penalized more, but within bounds.
            try
            {
                float armorScore = CalculateArmorScore(apparel, roleMult, w);
                float maxPenaltyMagnitude = Math.Max(5f, Math.Abs(armorScore) * 2f);
                if (score < -maxPenaltyMagnitude)
                    score = -maxPenaltyMagnitude;
            }
            catch
            {
                // If armor calculation fails, fall back to a sane clamp.
                if (score < -5f)
                    score = -5f;
            }

            return SanitizeFloat(score);
        }

        /// <summary>
        /// Gets a stat offset that an apparel item applies when equipped.
        /// Safe for modded apparel with missing or null stat lists.
        /// </summary>
        private static float GetEquippedStatOffset(Apparel apparel, StatDef stat)
        {
            try
            {
                if (apparel?.def?.equippedStatOffsets == null || stat == null)
                    return 0f;

                foreach (StatModifier modifier in apparel.def.equippedStatOffsets)
                {
                    if (modifier?.stat == stat)
                        return SanitizeFloat(modifier.value);
                }
            }
            catch
            {
                // Modded stat offset error — ignore
            }

            return 0f;
        }

        /// <summary>
        /// Gets the average outdoor temperature for the pawn's current map biome.
        /// </summary>
        private static float GetBiomeAverageTemperature(Pawn pawn)
        {
            try
            {
                if (pawn?.Map?.mapTemperature == null)
                    return 20f; // default temperate

                float temp = pawn.Map.mapTemperature.OutdoorTemp;
                return SanitizeFloat(temp, 20f);
            }
            catch
            {
                return 20f;
            }
        }

        /// <summary>
        /// Roughly classifies the colony as Early/Mid/Late game based on days passed and map wealth.
        /// Uses settings thresholds but is always safe for maps without wealth trackers.
        /// </summary>
        private static ColonyStage GetColonyStage(Pawn pawn, ScoringWeights w)
        {
            try
            {
                float earlyDays = w.earlyGameDaysThreshold;
                float lateWealth = w.lateGameWealthThreshold;

                float daysPassed = GenDate.DaysPassedFloat;

                float wealth = 0f;
                try
                {
                    if (pawn?.Map?.wealthWatcher != null)
                    {
                        wealth = pawn.Map.wealthWatcher.WealthTotal;
                    }
                }
                catch
                {
                    wealth = 0f;
                }

                // Early game: few days and low wealth
                if (daysPassed <= earlyDays && wealth < lateWealth * 0.5f)
                    return ColonyStage.Early;

                // Late game: high wealth regardless of days
                if (wealth >= lateWealth)
                    return ColonyStage.Late;

                return ColonyStage.Mid;
            }
            catch
            {
                return ColonyStage.Mid;
            }
        }

        /// <summary>
        /// Safely gets a stat value from a Thing. Returns 0 on any error.
        /// Guards against NaN/Infinity from modded XML or stat overrides.
        /// </summary>
        private static float GetStatValueSafe(Thing thing, StatDef stat)
        {
            try
            {
                if (thing == null || stat == null)
                    return 0f;

                float value = thing.GetStatValue(stat);
                return SanitizeFloat(value);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Sanitizes a float value, replacing NaN or Infinity with a safe default.
        /// </summary>
        private static float SanitizeFloat(float value, float fallback = 0f)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value;
        }
    }
}

