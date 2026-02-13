using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Vextex.Comps;
using Vextex.Settings;

namespace Vextex.Patches
{
    /// <summary>
    /// Registra trauma térmico (hypo/hyperthermia quase fatal) e trauma em parte do corpo (injury crítica)
    /// para memória adaptativa de apparel.
    /// </summary>
    [HarmonyPatch(typeof(HediffSet), "AddDirect")]
    public static class Patch_ApparelMemoryHediff
    {
        private static HediffDef _hypothermiaDef;
        private static HediffDef _heatstrokeDef;

        static Patch_ApparelMemoryHediff()
        {
            _hypothermiaDef = DefDatabase<HediffDef>.GetNamedSilentFail("Hypothermia");
            _heatstrokeDef = DefDatabase<HediffDef>.GetNamedSilentFail("Heatstroke");
        }

        [HarmonyPostfix]
        static void Postfix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (hediff == null || __instance?.pawn == null) return;
                Pawn pawn = __instance.pawn;
                if (!pawn.IsColonist || pawn.Dead) return;
                VextexSettings settings = VextexModHandler.Settings;
                if (settings?.adaptiveMemoryEnabled != true) return;

                var comp = pawn.GetComp<CompPawnApparelMemory>();
                if (comp == null) return;

                if (hediff.def == _hypothermiaDef || hediff.def == _heatstrokeDef)
                {
                    if (hediff.Severity >= 0.9f)
                        comp.RecordThermalTrauma();
                    return;
                }

                if (hediff.Part != null && (hediff.def?.isBad == true || (hediff.def?.defName != null &&
                    (hediff.def.defName.IndexOf("Injury", StringComparison.OrdinalIgnoreCase) >= 0
                    || hediff.def.defName.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0
                    || hediff.def.defName.IndexOf("Brain", StringComparison.OrdinalIgnoreCase) >= 0))))
                    comp.RecordBodyPartTrauma(hediff.Part);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] Apparel memory patch error: {ex.Message}");
            }
        }
    }
}
