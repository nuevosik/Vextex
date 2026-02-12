using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Vextex.Compat;
using Vextex.Settings;

namespace Vextex
{
    /// <summary>
    /// Main entry point for the Vextex mod.
    /// Initializes mod detection and Harmony patches on game startup.
    /// All initialization is wrapped in safety guards to prevent crashes.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class VextexMod
    {
        public const string VERSION = "1.2.0";

        static VextexMod()
        {
            Log.Message($"[Vextex] Initializing Smart Outfit AI v{VERSION}...");

            try
            {
                // Detect all known mods before applying patches
                ModCompat.DetectAll();
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] Mod detection encountered an error (non-fatal): {ex.Message}");
            }

            try
            {
                var harmony = new Harmony("com.vextex.smartoutfit");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[Vextex] All Harmony patches applied successfully.");

                // Optional diagnostics: in dev mode, log if other mods also patch optimize apparel.
                try
                {
                    if (Prefs.DevMode)
                    {
                        var method = AccessTools.Method(typeof(JobGiver_OptimizeApparel), "ApparelScoreGain");
                        if (method != null)
                        {
                            var patchInfo = Harmony.GetPatchInfo(method);
                            if (patchInfo != null && patchInfo.Owners != null)
                            {
                                // If there are other owners besides Vextex, inform the player once.
                                var otherOwners = new System.Collections.Generic.List<string>();
                                foreach (string owner in patchInfo.Owners)
                                {
                                    if (!string.Equals(owner, "com.vextex.smartoutfit", StringComparison.OrdinalIgnoreCase))
                                    {
                                        otherOwners.Add(owner);
                                    }
                                }

                                if (otherOwners.Count > 0)
                                {
                                    Log.Message("[Vextex] Other mods are also patching JobGiver_OptimizeApparel.ApparelScoreGain: " +
                                                string.Join(", ", otherOwners) +
                                                ". If you experience outfit conflicts, consider using Cooperative or Passive mode in Vextex settings.");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Vextex] Failed to inspect optimize apparel patches (non-fatal): {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Vextex] CRITICAL: Failed to apply Harmony patches! Error: {ex}");
                Log.Error("[Vextex] The mod will not function correctly. Please report this with your mod list.");
            }
        }
    }

    /// <summary>
    /// Mod class for handling settings UI.
    /// </summary>
    public class VextexModHandler : Mod
    {
        public static VextexSettings Settings { get; private set; }

        public VextexModHandler(ModContentPack content) : base(content)
        {
            try
            {
                Settings = GetSettings<VextexSettings>();
                Log.Message("[Vextex] Settings loaded.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Vextex] Failed to load settings (using defaults): {ex.Message}");
                Settings = new VextexSettings();
            }
        }

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            try
            {
                Settings.DoWindowContents(inRect);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Error rendering settings UI: {ex.Message}", 0x7E47E5);
            }
        }

        public override string SettingsCategory()
        {
            return "Vextex - Smart Outfit AI";
        }
    }
}
