using System;
using System.Collections.Generic;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Centralized mod detection framework.
    /// Detects popular mods at runtime using ModsConfig and assembly scanning.
    /// All detection is done via soft dependencies — no hard references required.
    /// </summary>
    public static class ModCompat
    {
        // === Detection Results ===

        /// <summary>Whether Combat Extended is active (delegated to CombatExtendedCompat).</summary>
        public static bool IsCEActive => CombatExtendedCompat.IsCEActive;

        /// <summary>Whether Vanilla Expanded Framework is active.</summary>
        public static bool IsVEFrameworkActive { get; private set; }

        /// <summary>Whether Dubs Bad Hygiene is active.</summary>
        public static bool IsDubsBadHygieneActive { get; private set; }

        /// <summary>Whether Alpha Animals is active.</summary>
        public static bool IsAlphaAnimalsActive { get; private set; }

        /// <summary>Whether Save Our Ship 2 is active.</summary>
        public static bool IsSOS2Active { get; private set; }

        /// <summary>Whether Dubs Apparel Tweaks is active.</summary>
        public static bool IsDubsApparelTweaksActive { get; private set; }

        /// <summary>Whether Vanilla Factions Expanded - Insectoids is active (adds custom apparel).</summary>
        public static bool IsVFEInsectoidsActive { get; private set; }

        /// <summary>Whether Rimworld of Magic is active (custom pawn types).</summary>
        public static bool IsRimOfMagicActive { get; private set; }

        /// <summary>Whether Android Tiers is active (non-human pawns with apparel).</summary>
        public static bool IsAndroidTiersActive { get; private set; }

        /// <summary>Whether Infused/Infused2 is active (modifies apparel stats dynamically).</summary>
        public static bool IsInfusedActive { get; private set; }

        /// <summary>Set of all detected mod names for logging/UI display.</summary>
        public static List<string> DetectedMods { get; private set; } = new List<string>();

        /// <summary>
        /// Detects all known mods. Should be called once during mod initialization
        /// inside [StaticConstructorOnStartup].
        /// </summary>
        public static void DetectAll()
        {
            try
            {
                // Combat Extended — delegated to its own compat class
                CombatExtendedCompat.DetectCE();

                // Vanilla Expanded ecosystem
                IsVEFrameworkActive = IsModActive("OskarPotocki.VanillaFactionsExpanded.Core")
                                   || IsModActive("OskarPotocki.VFE.Core");
                IsVFEInsectoidsActive = IsModActive("OskarPotocki.VFE.Insectoids")
                                     || IsModActive("OskarPotocki.VFE.Insectoids2");

                // Dubs mods
                IsDubsBadHygieneActive = IsModActive("Dubwise.DubsBadHygiene");
                IsDubsApparelTweaksActive = IsModActive("Dubwise.DubsApparelTweaks")
                                         || IsModActive("Dubwise.ApparelTweaks");

                // Content mods that add custom pawns/materials
                IsAlphaAnimalsActive = IsModActive("sarg.alphaanimals");
                IsSOS2Active = IsModActive("kentington.saveourship2");
                IsRimOfMagicActive = IsModActive("Torann.ARimworldOfMagic");
                IsAndroidTiersActive = IsModActive("Atlas.AndroidTiers")
                                    || IsModActive("Atlas.AndroidTiersReforged");
                IsInfusedActive = IsModActive("latta.infused")
                               || IsModActive("latta.infused2");

                // Build detected mods list
                BuildDetectedModsList();
                LogDetectedMods();
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] Error during mod detection (non-fatal): {ex.Message}");
            }
        }

        /// <summary>
        /// Safely checks if a mod is active by packageId.
        /// Never throws — returns false on any error.
        /// </summary>
        private static bool IsModActive(string packageId)
        {
            try
            {
                return ModsConfig.IsActive(packageId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds the list of human-readable detected mod names for settings UI.
        /// </summary>
        private static void BuildDetectedModsList()
        {
            DetectedMods.Clear();

            if (IsCEActive) DetectedMods.Add("Combat Extended");
            if (IsVEFrameworkActive) DetectedMods.Add("Vanilla Expanded Framework");
            if (IsVFEInsectoidsActive) DetectedMods.Add("VFE - Insectoids");
            if (IsDubsBadHygieneActive) DetectedMods.Add("Dubs Bad Hygiene");
            if (IsDubsApparelTweaksActive) DetectedMods.Add("Dubs Apparel Tweaks");
            if (IsAlphaAnimalsActive) DetectedMods.Add("Alpha Animals");
            if (IsSOS2Active) DetectedMods.Add("Save Our Ship 2");
            if (IsRimOfMagicActive) DetectedMods.Add("A Rimworld of Magic");
            if (IsAndroidTiersActive) DetectedMods.Add("Android Tiers");
            if (IsInfusedActive) DetectedMods.Add("Infused");
        }

        /// <summary>
        /// Logs a summary of all detected mods to the RimWorld log.
        /// </summary>
        private static void LogDetectedMods()
        {
            if (DetectedMods.Count > 0)
            {
                Log.Message($"[Vextex] Detected {DetectedMods.Count} compatible mod(s): {string.Join(", ", DetectedMods)}");
            }
            else
            {
                Log.Message("[Vextex] No known compatible mods detected. Running in vanilla mode.");
            }
        }
    }
}
