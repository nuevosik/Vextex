using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Vextex.Core;

namespace Vextex.Patches
{
    /// <summary>
    /// Adds a Dev Mode gizmo on the pawn inspector: "Vextex: Debug outfit" to show
    /// current worn scores and best swap candidate with breakdown.
    /// </summary>
    [HarmonyPatch(typeof(Thing), "GetGizmos")]
    public static class Patch_Thing_GetGizmos
    {
        private static Command_Action _cachedDebugGizmo;
        private static Gizmo[] _oneGizmoArray;

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (!Prefs.DevMode || __result == null)
                    return;
                Pawn pawn = __instance as Pawn;
                if (pawn == null || !pawn.IsColonist || pawn.Dead)
                    return;

                if (_cachedDebugGizmo == null)
                {
                    _cachedDebugGizmo = new Command_Action
                    {
                        defaultLabel = "Vextex: Debug outfit",
                        defaultDesc = "Show current worn scores and best swap candidate (armor, insulation, quality, net gain).",
                        action = () =>
                        {
                            Pawn p = Find.Selector.SingleSelectedThing as Pawn;
                            if (p != null && p.IsColonist && !p.Dead)
                                Log.Message(ApparelDebugHelper.GetDebugOutfitSummary(p));
                            else
                                Log.Message("[Vextex] Select a living colonist.");
                        }
                    };
                    _oneGizmoArray = new Gizmo[] { _cachedDebugGizmo };
                }

                __result = __result.Concat(_oneGizmoArray);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Vextex] Gizmo patch error (non-fatal): {ex.Message}");
            }
        }
    }
}
