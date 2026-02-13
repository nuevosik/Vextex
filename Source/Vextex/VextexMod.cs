using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Vextex.Compat;
using Vextex.Comps;
using Vextex.Settings;

namespace Vextex
{
    /// <summary>
    /// Main entry point for the Vextex mod.
    /// Initializes mod detection and Harmony patches on game startup.
    /// All initialization is wrapped in safety guards to prevent crashes.
    /// </summary>
    /// <summary>
    /// Startup runs in StaticConstructorOnStartup (single run, minimal work: mod detection,
    /// optional StatDef resolution, Harmony patches). No LongEvent needed for current scope.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class VextexMod
    {
        public const string VERSION = "1.1.0";

        static VextexMod()
        {
            Log.Message($"[Vextex] Initializing Smart Outfit AI v{VERSION}...");

            try
            {
                // Detect known mods and resolve optional StatDefs once (fast; no UI block)
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

                // Detect other mods that patch ApparelScoreGain (for UI warning and compatibility)
                try
                {
                    var method = AccessTools.Method(typeof(JobGiver_OptimizeApparel), "ApparelScoreGain");
                    if (method != null && ModCompat.OtherApparelScorePatchOwners != null)
                    {
                        ModCompat.OtherApparelScorePatchOwners.Clear();
                        var patchInfo = Harmony.GetPatchInfo(method);
                        if (patchInfo?.Owners != null)
                        {
                            foreach (string owner in patchInfo.Owners)
                            {
                                if (!string.Equals(owner, "com.vextex.smartoutfit", StringComparison.OrdinalIgnoreCase))
                                    ModCompat.OtherApparelScorePatchOwners.Add(owner);
                            }
                            if (ModCompat.OtherApparelScorePatchOwners.Count > 0 && Prefs.DevMode)
                            {
                                Log.Message("[Vextex] Other mods also patch ApparelScoreGain: " +
                                            string.Join(", ", ModCompat.OtherApparelScorePatchOwners) +
                                            ". If you see conflicts, enable 'Another mod fully controls outfit AI' in Vextex settings.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Vextex] Failed to inspect optimize apparel patches (non-fatal): {ex.Message}");
                }

                InjectApparelMemoryComp();
            }
            catch (Exception ex)
            {
                Log.Error($"[Vextex] CRITICAL: Failed to apply Harmony patches! Error: {ex}");
                Log.Error("[Vextex] The mod will not function correctly. Please report this with your mod list.");
            }
        }

        /// <summary>Adiciona CompPawnApparelMemory a todas as raças humanlike para memória adaptativa.</summary>
        private static void InjectApparelMemoryComp()
        {
            try
            {
                int added = 0;
                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def?.race == null || !def.race.Humanlike) continue;
                    if (def.comps == null)
                        def.comps = new List<CompProperties>();
                    bool hasAlready = false;
                    for (int i = 0; i < def.comps.Count; i++)
                    {
                        if (def.comps[i] is CompProperties_PawnApparelMemory)
                        { hasAlready = true; break; }
                    }
                    if (!hasAlready)
                    {
                        def.comps.Add(new CompProperties_PawnApparelMemory());
                        added++;
                    }
                }
                if (added > 0)
                    Log.Message($"[Vextex] Apparel memory comp injected into {added} humanlike race(s).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] Failed to inject apparel memory comp (non-fatal): " + ex.Message);
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
            if (Settings == null)
                Settings = new VextexSettings();
        }

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            if (Settings == null)
                return;
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
