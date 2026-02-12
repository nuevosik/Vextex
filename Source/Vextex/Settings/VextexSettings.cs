using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Vextex.Compat;

namespace Vextex.Settings
{
    /// <summary>
    /// How aggressively Vextex should override or cooperate with other outfit logic.
    /// </summary>
    public enum OutfitAIMode
    {
        /// <summary>Vextex fully controls apparel scoring when safe to do so.</summary>
        Aggressive,

        /// <summary>Vextex blends its score with existing results from vanilla/other mods.</summary>
        Cooperative,

        /// <summary>Vextex does not touch outfit logic at all (maximum compatibility).</summary>
        Passive
    }

    /// <summary>
    /// Quick-start presets that configure all sliders to sensible values for a given playstyle.
    /// </summary>
    public enum BehaviorPreset
    {
        /// <summary>No preset active; user is tweaking sliders manually.</summary>
        Custom,

        /// <summary>Subtle improvements over vanilla outfit logic.</summary>
        VanillaPlus,

        /// <summary>Maximizes protection and combat readiness.</summary>
        CombatFocus,

        /// <summary>Prioritizes comfort, quality and insulation over raw armor.</summary>
        ComfortRP,

        /// <summary>Tuned for Combat Extended: high armor importance, bulk awareness.</summary>
        CEHardcore
    }

    /// <summary>
    /// Lightweight snapshot of all scoring weights, used by the calculator to evaluate
    /// apparel without mutating the global settings when per-outfit profiles are active.
    /// </summary>
    public struct ScoringWeights
    {
        public float armorWeight, insulationWeight, materialWeight, qualityWeight;
        public float movePenaltyWeight, aimPenaltyWeight, bulkPenaltyWeight;
        public float meleeMaterialBias, rangedMaterialBias, nonCombatantMaterialBias;
        public float meleeThreshold, rangedThreshold, nonCombatantCeiling;
        public float earlyGameDaysThreshold, lateGameWealthThreshold;
        public float tierSWeight, tierAWeight, tierBWeight, tierCWeight, tierDWeight;
        public bool enableMaterialEvaluation, enableSkillPriority, enableRoleDetection;
        public float ceNormalizationFactor;
    }

    /// <summary>
    /// Mod settings exposed to the player via the in-game mod settings menu.
    /// </summary>
    public class VextexSettings : ModSettings
    {
        // Active preset (Custom = manual tweaking)
        public BehaviorPreset currentPreset = BehaviorPreset.VanillaPlus;

        // Feature toggles
        public bool enableSkillPriority = true;
        public bool enableRoleDetection = true;
        public bool enableMaterialEvaluation = true;

        // Diagnostics
        public bool enableVerboseLogging = false;

        // Compatibility / behavior mode
        public OutfitAIMode outfitMode = OutfitAIMode.Aggressive;

        /// <summary>
        /// When true, Vextex will assume another mod fully controls outfit AI
        /// and will never touch the optimize apparel logic (behaves like Passive mode).
        /// </summary>
        public bool externalOutfitController = false;

        // Weight sliders (0.0 to 3.0)
        public float armorWeight = 1.5f;
        public float insulationWeight = 1.0f;
        public float materialWeight = 1.0f;
        public float qualityWeight = 1.3f;

        // Skill thresholds for role detection
        public float meleeThreshold = 8f;
        public float rangedThreshold = 6f;
        public float nonCombatantCeiling = 5f;

        // Combat Extended compatibility
        public float ceNormalizationFactor = 0f; // 0 = use defaults

        // Colony / material scaling
        public float earlyGameDaysThreshold = 30f;
        public float lateGameWealthThreshold = 100000f;

        // Per-tier material weights (multipliers on top of the base tier bonus)
        public float tierSWeight = 1.15f;
        public float tierAWeight = 1.10f;
        public float tierBWeight = 1.00f;
        public float tierCWeight = 1.00f;
        public float tierDWeight = 0.90f;

        // Per-role material bias (how much the role cares about material quality)
        public float meleeMaterialBias = 1.10f;
        public float rangedMaterialBias = 1.00f;
        public float nonCombatantMaterialBias = 0.90f;

        // Penalty weights (movement, aim, bulk) to tune how strict Vextex is
        // with heavy gear that slows or penalizes accuracy.
        public float movePenaltyWeight = 1.00f;
        public float aimPenaltyWeight = 1.00f;
        public float bulkPenaltyWeight = 1.00f;

        // Per-outfit profiles (advanced)
        public bool enablePerOutfitProfiles = false;
        public Dictionary<string, BehaviorPreset> outfitProfiles = new Dictionary<string, BehaviorPreset>();

        /// <summary>
        /// Builds a ScoringWeights snapshot from the current global settings values.
        /// </summary>
        public ScoringWeights ToWeights()
        {
            return new ScoringWeights
            {
                armorWeight = armorWeight,
                insulationWeight = insulationWeight,
                materialWeight = materialWeight,
                qualityWeight = qualityWeight,
                movePenaltyWeight = movePenaltyWeight,
                aimPenaltyWeight = aimPenaltyWeight,
                bulkPenaltyWeight = bulkPenaltyWeight,
                meleeMaterialBias = meleeMaterialBias,
                rangedMaterialBias = rangedMaterialBias,
                nonCombatantMaterialBias = nonCombatantMaterialBias,
                meleeThreshold = meleeThreshold,
                rangedThreshold = rangedThreshold,
                nonCombatantCeiling = nonCombatantCeiling,
                earlyGameDaysThreshold = earlyGameDaysThreshold,
                lateGameWealthThreshold = lateGameWealthThreshold,
                tierSWeight = tierSWeight,
                tierAWeight = tierAWeight,
                tierBWeight = tierBWeight,
                tierCWeight = tierCWeight,
                tierDWeight = tierDWeight,
                enableMaterialEvaluation = enableMaterialEvaluation,
                enableSkillPriority = enableSkillPriority,
                enableRoleDetection = enableRoleDetection,
                ceNormalizationFactor = ceNormalizationFactor
            };
        }

        /// <summary>
        /// Returns a ScoringWeights snapshot for a specific preset (using the preset's fixed values)
        /// merged with feature toggles from the global settings.
        /// </summary>
        public ScoringWeights ToWeightsForPreset(BehaviorPreset preset)
        {
            if (preset == BehaviorPreset.Custom)
                return ToWeights();

            // Temporarily apply preset to a fresh snapshot
            var w = ToWeights(); // start with global feature toggles / CE factor
            switch (preset)
            {
                case BehaviorPreset.VanillaPlus:
                    w.armorWeight = 1.5f; w.insulationWeight = 1.0f; w.materialWeight = 1.0f; w.qualityWeight = 1.3f;
                    w.movePenaltyWeight = 1.0f; w.aimPenaltyWeight = 1.0f; w.bulkPenaltyWeight = 1.0f;
                    w.meleeMaterialBias = 1.10f; w.rangedMaterialBias = 1.00f; w.nonCombatantMaterialBias = 0.90f;
                    w.meleeThreshold = 8f; w.rangedThreshold = 6f; w.nonCombatantCeiling = 5f;
                    w.earlyGameDaysThreshold = 30f; w.lateGameWealthThreshold = 100000f;
                    w.tierSWeight = 1.15f; w.tierAWeight = 1.10f; w.tierBWeight = 1.00f; w.tierCWeight = 1.00f; w.tierDWeight = 0.90f;
                    break;
                case BehaviorPreset.CombatFocus:
                    w.armorWeight = 2.2f; w.insulationWeight = 0.7f; w.materialWeight = 1.2f; w.qualityWeight = 1.5f;
                    w.movePenaltyWeight = 0.6f; w.aimPenaltyWeight = 0.7f; w.bulkPenaltyWeight = 0.7f;
                    w.meleeMaterialBias = 1.30f; w.rangedMaterialBias = 1.10f; w.nonCombatantMaterialBias = 0.80f;
                    w.meleeThreshold = 6f; w.rangedThreshold = 5f; w.nonCombatantCeiling = 3f;
                    w.earlyGameDaysThreshold = 20f; w.lateGameWealthThreshold = 80000f;
                    w.tierSWeight = 1.30f; w.tierAWeight = 1.20f; w.tierBWeight = 1.00f; w.tierCWeight = 0.90f; w.tierDWeight = 0.80f;
                    break;
                case BehaviorPreset.ComfortRP:
                    w.armorWeight = 0.8f; w.insulationWeight = 1.8f; w.materialWeight = 1.3f; w.qualityWeight = 2.0f;
                    w.movePenaltyWeight = 1.5f; w.aimPenaltyWeight = 1.3f; w.bulkPenaltyWeight = 1.3f;
                    w.meleeMaterialBias = 1.00f; w.rangedMaterialBias = 1.00f; w.nonCombatantMaterialBias = 1.10f;
                    w.meleeThreshold = 10f; w.rangedThreshold = 8f; w.nonCombatantCeiling = 6f;
                    w.earlyGameDaysThreshold = 40f; w.lateGameWealthThreshold = 120000f;
                    w.tierSWeight = 1.20f; w.tierAWeight = 1.15f; w.tierBWeight = 1.05f; w.tierCWeight = 1.00f; w.tierDWeight = 0.85f;
                    break;
                case BehaviorPreset.CEHardcore:
                    w.armorWeight = 2.5f; w.insulationWeight = 0.6f; w.materialWeight = 1.4f; w.qualityWeight = 1.6f;
                    w.movePenaltyWeight = 0.5f; w.aimPenaltyWeight = 0.5f; w.bulkPenaltyWeight = 0.6f;
                    w.meleeMaterialBias = 1.40f; w.rangedMaterialBias = 1.20f; w.nonCombatantMaterialBias = 0.70f;
                    w.meleeThreshold = 5f; w.rangedThreshold = 4f; w.nonCombatantCeiling = 2f;
                    w.earlyGameDaysThreshold = 15f; w.lateGameWealthThreshold = 60000f;
                    w.tierSWeight = 1.40f; w.tierAWeight = 1.25f; w.tierBWeight = 1.00f; w.tierCWeight = 0.85f; w.tierDWeight = 0.70f;
                    break;
            }
            return w;
        }

        /// <summary>
        /// Resolves the effective ScoringWeights for a specific pawn, considering per-outfit profiles.
        /// </summary>
        public ScoringWeights ResolveWeightsForPawn(Pawn pawn)
        {
            BehaviorPreset preset = GetEffectivePreset(pawn);
            if (preset == BehaviorPreset.Custom || preset == currentPreset)
                return ToWeights();
            return ToWeightsForPreset(preset);
        }

        /// <summary>
        /// Returns the effective BehaviorPreset for a pawn based on their current outfit assignment.
        /// Falls back to <see cref="currentPreset"/> (global) when per-outfit profiles are disabled
        /// or no profile is configured for the pawn's outfit.
        /// </summary>
        public BehaviorPreset GetEffectivePreset(Pawn pawn)
        {
            try
            {
                if (!enablePerOutfitProfiles || outfitProfiles == null || outfitProfiles.Count == 0)
                    return currentPreset;

                if (pawn?.outfits?.CurrentApparelPolicy == null)
                    return currentPreset;

                string label = pawn.outfits.CurrentApparelPolicy.label;
                if (label != null && outfitProfiles.TryGetValue(label, out BehaviorPreset preset))
                    return preset;
            }
            catch { }
            return currentPreset;
        }

        /// <summary>
        /// Applies a preset, setting all slider values to match the chosen playstyle.
        /// </summary>
        public void ApplyPreset(BehaviorPreset preset)
        {
            currentPreset = preset;

            switch (preset)
            {
                case BehaviorPreset.VanillaPlus:
                    armorWeight = 1.5f; insulationWeight = 1.0f; materialWeight = 1.0f; qualityWeight = 1.3f;
                    movePenaltyWeight = 1.0f; aimPenaltyWeight = 1.0f; bulkPenaltyWeight = 1.0f;
                    meleeMaterialBias = 1.10f; rangedMaterialBias = 1.00f; nonCombatantMaterialBias = 0.90f;
                    meleeThreshold = 8f; rangedThreshold = 6f; nonCombatantCeiling = 5f;
                    earlyGameDaysThreshold = 30f; lateGameWealthThreshold = 100000f;
                    tierSWeight = 1.15f; tierAWeight = 1.10f; tierBWeight = 1.00f; tierCWeight = 1.00f; tierDWeight = 0.90f;
                    break;

                case BehaviorPreset.CombatFocus:
                    armorWeight = 2.2f; insulationWeight = 0.7f; materialWeight = 1.2f; qualityWeight = 1.5f;
                    movePenaltyWeight = 0.6f; aimPenaltyWeight = 0.7f; bulkPenaltyWeight = 0.7f;
                    meleeMaterialBias = 1.30f; rangedMaterialBias = 1.10f; nonCombatantMaterialBias = 0.80f;
                    meleeThreshold = 6f; rangedThreshold = 5f; nonCombatantCeiling = 3f;
                    earlyGameDaysThreshold = 20f; lateGameWealthThreshold = 80000f;
                    tierSWeight = 1.30f; tierAWeight = 1.20f; tierBWeight = 1.00f; tierCWeight = 0.90f; tierDWeight = 0.80f;
                    break;

                case BehaviorPreset.ComfortRP:
                    armorWeight = 0.8f; insulationWeight = 1.8f; materialWeight = 1.3f; qualityWeight = 2.0f;
                    movePenaltyWeight = 1.5f; aimPenaltyWeight = 1.3f; bulkPenaltyWeight = 1.3f;
                    meleeMaterialBias = 1.00f; rangedMaterialBias = 1.00f; nonCombatantMaterialBias = 1.10f;
                    meleeThreshold = 10f; rangedThreshold = 8f; nonCombatantCeiling = 6f;
                    earlyGameDaysThreshold = 40f; lateGameWealthThreshold = 120000f;
                    tierSWeight = 1.20f; tierAWeight = 1.15f; tierBWeight = 1.05f; tierCWeight = 1.00f; tierDWeight = 0.85f;
                    break;

                case BehaviorPreset.CEHardcore:
                    armorWeight = 2.5f; insulationWeight = 0.6f; materialWeight = 1.4f; qualityWeight = 1.6f;
                    movePenaltyWeight = 0.5f; aimPenaltyWeight = 0.5f; bulkPenaltyWeight = 0.6f;
                    meleeMaterialBias = 1.40f; rangedMaterialBias = 1.20f; nonCombatantMaterialBias = 0.70f;
                    meleeThreshold = 5f; rangedThreshold = 4f; nonCombatantCeiling = 2f;
                    earlyGameDaysThreshold = 15f; lateGameWealthThreshold = 60000f;
                    tierSWeight = 1.40f; tierAWeight = 1.25f; tierBWeight = 1.00f; tierCWeight = 0.85f; tierDWeight = 0.70f;
                    break;

                case BehaviorPreset.Custom:
                default:
                    // Custom: don't change any values
                    break;
            }
        }

        /// <summary>
        /// Returns a short human-readable description for a preset.
        /// </summary>
        public static string GetPresetDescription(BehaviorPreset preset)
        {
            switch (preset)
            {
                case BehaviorPreset.VanillaPlus:
                    return "Balanced improvements over vanilla outfit logic. Good for most playstyles.";
                case BehaviorPreset.CombatFocus:
                    return "Maximizes protection. Colonists accept heavier gear and prioritize armor.";
                case BehaviorPreset.ComfortRP:
                    return "Prioritizes quality, comfort and insulation. Best for roleplay-focused games.";
                case BehaviorPreset.CEHardcore:
                    return "Tuned for Combat Extended. Armor is critical, penalties are tolerated.";
                case BehaviorPreset.Custom:
                default:
                    return "Manual slider configuration. Change any value freely.";
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref currentPreset, "currentPreset", BehaviorPreset.VanillaPlus);
            Scribe_Values.Look(ref enableSkillPriority, "enableSkillPriority", true);
            Scribe_Values.Look(ref enableRoleDetection, "enableRoleDetection", true);
            Scribe_Values.Look(ref enableMaterialEvaluation, "enableMaterialEvaluation", true);
            Scribe_Values.Look(ref enableVerboseLogging, "enableVerboseLogging", false);

            Scribe_Values.Look(ref outfitMode, "outfitMode", OutfitAIMode.Aggressive);
            Scribe_Values.Look(ref externalOutfitController, "externalOutfitController", false);

            Scribe_Values.Look(ref armorWeight, "armorWeight", 1.5f);
            Scribe_Values.Look(ref insulationWeight, "insulationWeight", 1.0f);
            Scribe_Values.Look(ref materialWeight, "materialWeight", 1.0f);
            Scribe_Values.Look(ref qualityWeight, "qualityWeight", 1.3f);

            Scribe_Values.Look(ref meleeThreshold, "meleeThreshold", 8f);
            Scribe_Values.Look(ref rangedThreshold, "rangedThreshold", 6f);
            Scribe_Values.Look(ref nonCombatantCeiling, "nonCombatantCeiling", 5f);

            Scribe_Values.Look(ref ceNormalizationFactor, "ceNormalizationFactor", 0f);

            Scribe_Values.Look(ref earlyGameDaysThreshold, "earlyGameDaysThreshold", 30f);
            Scribe_Values.Look(ref lateGameWealthThreshold, "lateGameWealthThreshold", 100000f);

            Scribe_Values.Look(ref tierSWeight, "tierSWeight", 1.15f);
            Scribe_Values.Look(ref tierAWeight, "tierAWeight", 1.10f);
            Scribe_Values.Look(ref tierBWeight, "tierBWeight", 1.00f);
            Scribe_Values.Look(ref tierCWeight, "tierCWeight", 1.00f);
            Scribe_Values.Look(ref tierDWeight, "tierDWeight", 0.90f);

            Scribe_Values.Look(ref meleeMaterialBias, "meleeMaterialBias", 1.10f);
            Scribe_Values.Look(ref rangedMaterialBias, "rangedMaterialBias", 1.00f);
            Scribe_Values.Look(ref nonCombatantMaterialBias, "nonCombatantMaterialBias", 0.90f);

            Scribe_Values.Look(ref movePenaltyWeight, "movePenaltyWeight", 1.00f);
            Scribe_Values.Look(ref aimPenaltyWeight, "aimPenaltyWeight", 1.00f);
            Scribe_Values.Look(ref bulkPenaltyWeight, "bulkPenaltyWeight", 1.00f);

            Scribe_Values.Look(ref enablePerOutfitProfiles, "enablePerOutfitProfiles", false);
            Scribe_Collections.Look(ref outfitProfiles, "outfitProfiles", LookMode.Value, LookMode.Value);
            if (outfitProfiles == null)
                outfitProfiles = new Dictionary<string, BehaviorPreset>();
        }

        public void DoWindowContents(UnityEngine.Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            // === Detected Mods Status ===
            listing.Label($"Vextex v{VextexMod.VERSION}");
            if (ModCompat.DetectedMods.Count > 0)
            {
                listing.Label($"Compatible mods detected: {string.Join(", ", ModCompat.DetectedMods)}");
            }
            else
            {
                listing.Label("No known compatible mods detected (vanilla mode).");
            }
            listing.GapLine();

            // === Behavior Preset ===
            listing.Label("=== Behavior Preset ===");
            listing.GapLine();
            listing.Label(GetPresetDescription(currentPreset));

            if (listing.RadioButton("Vanilla+ (balanced)", currentPreset == BehaviorPreset.VanillaPlus))
            {
                ApplyPreset(BehaviorPreset.VanillaPlus);
            }
            if (listing.RadioButton("Combat Focus (max protection)", currentPreset == BehaviorPreset.CombatFocus))
            {
                ApplyPreset(BehaviorPreset.CombatFocus);
            }
            if (listing.RadioButton("Comfort / RP (quality + insulation)", currentPreset == BehaviorPreset.ComfortRP))
            {
                ApplyPreset(BehaviorPreset.ComfortRP);
            }
            if (listing.RadioButton("CE Hardcore (Combat Extended tuned)", currentPreset == BehaviorPreset.CEHardcore))
            {
                ApplyPreset(BehaviorPreset.CEHardcore);
            }
            if (listing.RadioButton("Custom (manual sliders below)", currentPreset == BehaviorPreset.Custom))
            {
                currentPreset = BehaviorPreset.Custom;
            }

            listing.GapLine();

            // === Feature Toggles ===
            listing.Label("=== Feature Toggles ===");
            listing.GapLine();
            listing.CheckboxLabeled("Enable skill-based gear priority", ref enableSkillPriority,
                "Stronger colonists will prioritize better gear.");
            listing.CheckboxLabeled("Enable combat role detection", ref enableRoleDetection,
                "Detect melee vs ranged colonists and adjust gear preference.");
            listing.CheckboxLabeled("Enable material evaluation", ref enableMaterialEvaluation,
                "Evaluate material quality tiers (Hyperweave > Devilstrand > Cloth).");

            listing.GapLine();

            // === Diagnostics ===
            listing.Label("=== Diagnostics ===");
            listing.GapLine();
            listing.CheckboxLabeled("Enable verbose logging", ref enableVerboseLogging,
                "Log detailed scoring info to help diagnose mod conflicts. Check Player.log for output.");

            listing.GapLine();

            // === Outfit AI Mode / Compatibility ===
            listing.Label("=== Outfit AI Mode ===");
            listing.GapLine();

            listing.Label("Choose how aggressively Vextex should override other outfit logic.");

            if (listing.RadioButton("Aggressive (Vextex controls apparel scoring)", outfitMode == OutfitAIMode.Aggressive))
            {
                outfitMode = OutfitAIMode.Aggressive;
            }

            if (listing.RadioButton("Cooperative (blend with existing logic)", outfitMode == OutfitAIMode.Cooperative))
            {
                outfitMode = OutfitAIMode.Cooperative;
            }

            if (listing.RadioButton("Passive (do not touch outfit AI)", outfitMode == OutfitAIMode.Passive))
            {
                outfitMode = OutfitAIMode.Passive;
            }

            listing.CheckboxLabeled("Another mod fully controls outfit AI", ref externalOutfitController,
                "When enabled, Vextex will never modify optimize apparel behavior, regardless of the selected mode.");

            listing.GapLine();

            // === Weight Sliders ===
            listing.Label("=== Scoring Weights ===");
            listing.GapLine();

            listing.Label($"Armor Weight: {armorWeight:F1}");
            armorWeight = listing.Slider(armorWeight, 0f, 3f);

            listing.Label($"Insulation Weight: {insulationWeight:F1}");
            insulationWeight = listing.Slider(insulationWeight, 0f, 3f);

            listing.Label($"Material Weight: {materialWeight:F1}");
            materialWeight = listing.Slider(materialWeight, 0f, 3f);

            listing.Label($"Quality Weight: {qualityWeight:F1}");
            qualityWeight = listing.Slider(qualityWeight, 0f, 3f);

            listing.GapLine();

            // === Skill Thresholds ===
            listing.Label("=== Skill Thresholds ===");
            listing.GapLine();

            listing.Label($"Melee role threshold: {meleeThreshold:F0}");
            meleeThreshold = listing.Slider(meleeThreshold, 1f, 20f);

            listing.Label($"Ranged role threshold: {rangedThreshold:F0}");
            rangedThreshold = listing.Slider(rangedThreshold, 1f, 20f);

            listing.Label($"Non-combatant ceiling: {nonCombatantCeiling:F0}");
            nonCombatantCeiling = listing.Slider(nonCombatantCeiling, 1f, 20f);

            // === CE Settings (only shown when CE is active) ===
            if (CombatExtendedCompat.IsCEActive)
            {
                listing.GapLine();
                listing.Label("=== Combat Extended ===");
                listing.GapLine();

                listing.Label($"Armor normalization factor: {(ceNormalizationFactor > 0f ? ceNormalizationFactor.ToString("F1") : "Auto")}");
                ceNormalizationFactor = listing.Slider(ceNormalizationFactor, 0f, 30f);
                listing.Label("(0 = automatic, higher = treats armor as less important)");
            }

            listing.GapLine();

            // === Penalty Weights ===
            listing.Label("=== Penalty Weights ===");
            listing.GapLine();

            listing.Label($"Move speed penalty weight: {movePenaltyWeight:F2}");
            movePenaltyWeight = listing.Slider(movePenaltyWeight, 0.3f, 2.0f);

            listing.Label($"Aim penalty weight: {aimPenaltyWeight:F2}");
            aimPenaltyWeight = listing.Slider(aimPenaltyWeight, 0.3f, 2.0f);

            listing.Label($"Bulk penalty weight (CE): {bulkPenaltyWeight:F2}");
            bulkPenaltyWeight = listing.Slider(bulkPenaltyWeight, 0.3f, 2.0f);

            listing.GapLine();

            // === Material Scaling ===
            listing.Label("=== Material Scaling ===");
            listing.GapLine();

            listing.Label($"Early game days threshold: {earlyGameDaysThreshold:F0}");
            earlyGameDaysThreshold = listing.Slider(earlyGameDaysThreshold, 1f, 120f);

            listing.Label($"Late game wealth threshold: {lateGameWealthThreshold:F0}");
            lateGameWealthThreshold = listing.Slider(lateGameWealthThreshold, 20000f, 300000f);

            listing.GapLine();
            listing.Label("Tier weights (multipliers for S/A/B/C/D materials):");

            listing.Label($"Tier S weight: {tierSWeight:F2}");
            tierSWeight = listing.Slider(tierSWeight, 0.5f, 2.0f);

            listing.Label($"Tier A weight: {tierAWeight:F2}");
            tierAWeight = listing.Slider(tierAWeight, 0.5f, 2.0f);

            listing.Label($"Tier B weight: {tierBWeight:F2}");
            tierBWeight = listing.Slider(tierBWeight, 0.5f, 2.0f);

            listing.Label($"Tier C weight: {tierCWeight:F2}");
            tierCWeight = listing.Slider(tierCWeight, 0.5f, 2.0f);

            listing.Label($"Tier D weight: {tierDWeight:F2}");
            tierDWeight = listing.Slider(tierDWeight, 0.5f, 2.0f);

            listing.GapLine();
            listing.Label("Role-based material bias (how much each role cares about material):");

            listing.Label($"Melee material bias: {meleeMaterialBias:F2}");
            meleeMaterialBias = listing.Slider(meleeMaterialBias, 0.5f, 2.0f);

            listing.Label($"Ranged material bias: {rangedMaterialBias:F2}");
            rangedMaterialBias = listing.Slider(rangedMaterialBias, 0.5f, 2.0f);

            listing.Label($"Non-combatant material bias: {nonCombatantMaterialBias:F2}");
            nonCombatantMaterialBias = listing.Slider(nonCombatantMaterialBias, 0.5f, 2.0f);

            listing.GapLine();

            // === Per-Outfit Profiles (advanced) ===
            listing.Label("=== Per-Outfit Profiles (Advanced) ===");
            listing.GapLine();
            listing.CheckboxLabeled("Enable per-outfit preset profiles", ref enablePerOutfitProfiles,
                "Assign a different behavior preset to each outfit. Overrides the global preset for pawns assigned to that outfit.");

            if (enablePerOutfitProfiles && Current.Game != null)
            {
                try
                {
                    var policies = Current.Game.outfitDatabase?.AllOutfits;
                    if (policies != null)
                    {
                        foreach (var policy in policies)
                        {
                            if (policy?.label == null) continue;
                            string label = policy.label;

                            if (!outfitProfiles.ContainsKey(label))
                                outfitProfiles[label] = BehaviorPreset.Custom;

                            BehaviorPreset current = outfitProfiles[label];
                            string displayName = current == BehaviorPreset.Custom ? "Global (no override)" : current.ToString();
                            listing.Label($"  Outfit \"{label}\": {displayName}");

                            if (listing.ButtonText($"Cycle preset for \"{label}\""))
                            {
                                // Cycle through presets
                                int next = ((int)current + 1) % 5; // 5 enum values
                                outfitProfiles[label] = (BehaviorPreset)next;
                            }
                        }
                    }
                }
                catch
                {
                    listing.Label("  (Could not read outfit database — only available in-game)");
                }
            }

            listing.End();
        }
    }
}

