using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Vextex.Compat;
using Vextex.Settings;

namespace Vextex.Core
{
    /// <summary>
    /// Detects whether a colonist should be classified as Melee, Ranged, or NonCombatant
    /// based on their skills, traits, and current weapon.
    /// Safe for modded pawns (androids, mechs, custom races) that may lack
    /// standard skills, traits, or story elements.
    /// </summary>
    public static class ColonistRoleDetector
    {
        public enum CombatRole
        {
            Melee,
            Ranged,
            NonCombatant
        }

        /// <summary>
        /// Role-specific multipliers for apparel stat scoring.
        /// </summary>
        public struct RoleMultipliers
        {
            public float armorSharp;
            public float armorBlunt;
            public float armorHeat;
            public float moveSpeedPenalty;
            public float aimPenalty;
            public float insulation;

            public static readonly RoleMultipliers MeleeMultipliers = new RoleMultipliers
            {
                armorSharp = 2.0f,
                armorBlunt = 1.8f,
                armorHeat = 1.0f,
                moveSpeedPenalty = -2.0f,
                aimPenalty = 0f,       // melee doesn't care about aim
                insulation = 0.8f
            };

            public static readonly RoleMultipliers RangedMultipliers = new RoleMultipliers
            {
                armorSharp = 1.5f,
                armorBlunt = 1.0f,
                armorHeat = 0.8f,
                moveSpeedPenalty = -1.0f,
                aimPenalty = -3.0f,    // heavily penalize aim-reducing gear
                insulation = 1.0f
            };

            public static readonly RoleMultipliers NonCombatantMultipliers = new RoleMultipliers
            {
                armorSharp = 0.5f,
                armorBlunt = 0.5f,
                armorHeat = 0.3f,
                moveSpeedPenalty = -0.3f,
                aimPenalty = 0f,
                insulation = 2.0f     // prioritize comfort
            };
        }

        /// <summary>
        /// Determines the combat role of a pawn based on skills, traits, and equipped weapon.
        /// Safe for non-human pawns and modded races — returns NonCombatant as fallback.
        /// </summary>
        public static CombatRole DetectRole(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                    return CombatRole.NonCombatant;

                // Non-human pawns (animals, mechs, modded races without story) default to NonCombatant
                if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return CombatRole.NonCombatant;

                if (pawn.skills == null)
                    return CombatRole.NonCombatant;

                VextexSettings settings = VextexModHandler.Settings;
                if (settings != null && !settings.enableRoleDetection)
                    return CombatRole.NonCombatant;

                // Safely get skill levels — modded pawns may have null skill records
                float meleeSkill = GetSkillLevelSafe(pawn, SkillDefOf.Melee);
                float shootingSkill = GetSkillLevelSafe(pawn, SkillDefOf.Shooting);

                float meleeThreshold = settings?.meleeThreshold ?? 8f;
                float rangedThreshold = settings?.rangedThreshold ?? 6f;
                float nonCombatantCeiling = settings?.nonCombatantCeiling ?? 5f;

                // Check traits first - they override skill-based detection
                if (HasTrait(pawn, "Brawler"))
                    return CombatRole.Melee;

                // Check if pawn has an incapable-of work tag that prevents a role
                try
                {
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                        return CombatRole.NonCombatant;
                }
                catch
                {
                    // Some modded pawns crash on WorkTagIsDisabled — ignore
                }

                // Check currently equipped weapon as a strong hint
                try
                {
                    if (pawn.equipment?.Primary != null)
                    {
                        ThingDef weaponDef = pawn.equipment.Primary.def;
                        if (weaponDef != null)
                        {
                            if (weaponDef.IsMeleeWeapon)
                                return CombatRole.Melee;
                            if (weaponDef.IsRangedWeapon)
                                return CombatRole.Ranged;
                        }
                    }
                }
                catch
                {
                    // Weapon check failed — continue with skill-based detection
                }

                // === Skill-based detection ===
                // Design philosophy:
                // - Only true pacifists / totally unskilled colonists are NonCombatant.
                // - In a small early colony, somebody MUST fight, even with low skills.
                // - Thresholds are used to decide a *strong* preference, but we always
                //   fall back to "best available" (relative) role.

                bool hasAnyCombatSkill = meleeSkill > 0f || shootingSkill > 0f;

                // If pawn has literally no combat skill and no weapon, treat as NonCombatant
                if (!hasAnyCombatSkill && pawn.equipment?.Primary == null)
                    return CombatRole.NonCombatant;

                // Strong melee preference when clearly above threshold and higher than shooting
                if (meleeSkill >= meleeThreshold && meleeSkill > shootingSkill * 1.3f)
                    return CombatRole.Melee;

                // Strong ranged preference when clearly above threshold and higher than melee
                if (shootingSkill >= rangedThreshold && shootingSkill > meleeSkill * 1.2f)
                    return CombatRole.Ranged;

                // Below thresholds or close together:
                // fall back to the better of the two skills (relative role).
                if (shootingSkill >= meleeSkill)
                    return CombatRole.Ranged;

                return CombatRole.Melee;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error detecting role for {pawn?.LabelShort ?? "unknown"}: {ex.Message}",
                    (pawn?.thingIDNumber ?? 0) ^ 0x7E47E7);
                return CombatRole.NonCombatant;
            }
        }

        /// <summary>
        /// Gets the role-specific multipliers for scoring.
        /// </summary>
        public static RoleMultipliers GetMultipliers(CombatRole role)
        {
            // CE makes armor much more critical (penetration = instant death)
            // Use boosted multipliers when CE is active
            if (CombatExtendedCompat.IsCEActive)
            {
                return GetCEMultipliers(role);
            }

            switch (role)
            {
                case CombatRole.Melee:
                    return RoleMultipliers.MeleeMultipliers;
                case CombatRole.Ranged:
                    return RoleMultipliers.RangedMultipliers;
                case CombatRole.NonCombatant:
                default:
                    return RoleMultipliers.NonCombatantMultipliers;
            }
        }

        /// <summary>
        /// CE-adjusted multipliers. Armor is life-or-death in CE,
        /// so combat roles weight it even higher.
        /// </summary>
        private static RoleMultipliers GetCEMultipliers(CombatRole role)
        {
            switch (role)
            {
                case CombatRole.Melee:
                    return new RoleMultipliers
                    {
                        armorSharp = 2.5f,   // CE melee needs max sharp armor
                        armorBlunt = 2.2f,   // Blunt also critical in CE
                        armorHeat = 1.0f,
                        moveSpeedPenalty = -1.5f,  // Slightly less penalty than vanilla (heavy armor is expected)
                        aimPenalty = 0f,
                        insulation = 0.6f
                    };
                case CombatRole.Ranged:
                    return new RoleMultipliers
                    {
                        armorSharp = 1.8f,   // Ranged still needs armor in CE
                        armorBlunt = 1.2f,
                        armorHeat = 0.8f,
                        moveSpeedPenalty = -1.5f,
                        aimPenalty = -3.5f,  // Even more important in CE
                        insulation = 0.8f
                    };
                case CombatRole.NonCombatant:
                default:
                    return new RoleMultipliers
                    {
                        armorSharp = 0.8f,   // Even non-combatants need some armor in CE
                        armorBlunt = 0.6f,
                        armorHeat = 0.3f,
                        moveSpeedPenalty = -0.3f,
                        aimPenalty = 0f,
                        insulation = 1.8f
                    };
            }
        }

        /// <summary>
        /// Calculates a "power level" for the pawn used in skill-based prioritization.
        /// Higher power = pawn should get better gear first.
        /// Safe for modded pawns with missing or disabled skills.
        /// </summary>
        public static float CalculatePowerLevel(Pawn pawn)
        {
            try
            {
                if (pawn?.skills == null)
                    return 0f;

                float meleeSkill = GetSkillLevelSafe(pawn, SkillDefOf.Melee);
                float shootingSkill = GetSkillLevelSafe(pawn, SkillDefOf.Shooting);
                float medicalSkill = GetSkillLevelSafe(pawn, SkillDefOf.Medicine);

                // Combat skills are heavily weighted
                float combatPower = Math.Max(meleeSkill, shootingSkill) * 2f;
                float secondaryCombat = Math.Min(meleeSkill, shootingSkill) * 0.5f;
                float medicalValue = medicalSkill * 0.3f;

                // Average of all skills as a general competence factor
                float totalSkill = 0f;
                int skillCount = 0;

                if (pawn.skills.skills != null)
                {
                    foreach (SkillRecord skill in pawn.skills.skills)
                    {
                        if (skill != null && skill.def != null && !skill.TotallyDisabled)
                        {
                            totalSkill += skill.Level;
                            skillCount++;
                        }
                    }
                }

                float avgSkill = skillCount > 0 ? totalSkill / skillCount : 0f;
                float generalCompetence = avgSkill * 0.1f;

                return combatPower + secondaryCombat + medicalValue + generalCompetence;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error calculating power level: {ex.Message}", 
                    (pawn?.thingIDNumber ?? 0) ^ 0x7E47E8);
                return 0f;
            }
        }

        // ====================================================================
        // Scalability: per-tick, per-map cache for power level and percentile
        // so we don't recompute for every apparel candidate (O(colonists) per call).
        // ====================================================================
        private static int _powerCacheTick = -1;
        private static int _powerCacheMapId = -1;
        private static readonly Dictionary<int, float> _cachedPowerLevels = new Dictionary<int, float>(64);

        /// <summary>
        /// Ensures the power cache is valid for the current tick and map; rebuilds if needed.
        /// </summary>
        private static void EnsurePowerCacheForMap(Map map)
        {
            if (map == null) return;
            int tick = Find.TickManager?.TicksGame ?? -1;
            int mapId = map.uniqueID;
            if (tick == _powerCacheTick && mapId == _powerCacheMapId && _cachedPowerLevels.Count > 0)
                return;
            _powerCacheTick = tick;
            _powerCacheMapId = mapId;
            _cachedPowerLevels.Clear();
            try
            {
                foreach (Pawn p in map.mapPawns.FreeColonists)
                {
                    if (p != null && !p.Dead)
                        _cachedPowerLevels[p.thingIDNumber] = CalculatePowerLevel(p);
                }
            }
            catch
            {
                _cachedPowerLevels.Clear();
            }
        }

        /// <summary>
        /// Calculates the pawn's power percentile (0.0–1.0) among all free colonists on the same map.
        /// 1.0 = strongest, 0.0 = weakest. Uses per-tick per-map cache for scalability.
        /// </summary>
        public static float GetPowerPercentile(Pawn pawn)
        {
            try
            {
                if (pawn?.Map == null)
                    return 0.5f;

                EnsurePowerCacheForMap(pawn.Map);
                if (_cachedPowerLevels.Count <= 1)
                    return 1.0f;
                if (!_cachedPowerLevels.TryGetValue(pawn.thingIDNumber, out float myPower))
                    return 0.5f;

                int betterThanMe = 0;
                foreach (float otherPower in _cachedPowerLevels.Values)
                {
                    if (otherPower > myPower)
                        betterThanMe++;
                }
                return 1f - ((float)betterThanMe / _cachedPowerLevels.Count);
            }
            catch
            {
                return 0.5f;
            }
        }

        /// <summary>
        /// Gets the skill priority multiplier based on pawn's rank among all colonists.
        /// Top 25% -> 1.5x, Middle 50% -> 1.0x, Bottom 25% -> 0.7x
        /// </summary>
        public static float GetSkillPriorityMultiplier(Pawn pawn)
        {
            try
            {
                VextexSettings settings = VextexModHandler.Settings;
                if (settings != null && !settings.enableSkillPriority)
                    return 1.0f;

                float percentile = GetPowerPercentile(pawn);

                if (percentile >= 0.75f)
                    return 1.5f;  // Top 25%
                else if (percentile >= 0.25f)
                    return 1.0f;  // Middle 50%
                else
                    return 0.7f;  // Bottom 25%
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error calculating skill priority: {ex.Message}",
                    (pawn?.thingIDNumber ?? 0) ^ 0x7E47E9);
                return 1.0f;
            }
        }

        /// <summary>
        /// Safely gets a skill level for a pawn. Returns 0 if the skill doesn't exist or is disabled.
        /// </summary>
        private static float GetSkillLevelSafe(Pawn pawn, SkillDef skillDef)
        {
            try
            {
                if (pawn?.skills == null || skillDef == null)
                    return 0f;

                SkillRecord record = pawn.skills.GetSkill(skillDef);
                if (record == null || record.TotallyDisabled)
                    return 0f;

                return record.Level;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Checks if a pawn has a specific trait by defName.
        /// Safe for pawns without story/traits (modded races, androids).
        /// </summary>
        private static bool HasTrait(Pawn pawn, string traitDefName)
        {
            try
            {
                if (pawn?.story?.traits?.allTraits == null)
                    return false;

                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    if (trait?.def?.defName == traitDefName)
                        return true;
                }
            }
            catch
            {
                // Modded trait system error — treat as no trait
            }

            return false;
        }
    }
}

