using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using Vextex.Compat;

namespace Vextex.Core
{
    /// <summary>
    /// Helpers para scoring: tainted (penalidade alta, exceto Psychopath), coverage sets,
    /// distância (preferir inventory/stockpile próximo), sterile para cirurgia, heat-resistant para firefighting.
    /// </summary>
    public static class ApparelScoringHelpers
    {
        private static PropertyInfo _wornByCorpseProp;

        static ApparelScoringHelpers()
        {
            try
            {
                Type apparelType = typeof(Apparel);
                _wornByCorpseProp = apparelType.GetProperty("WornByCorpse", BindingFlags.Public | BindingFlags.Instance);
                if (_wornByCorpseProp == null)
                    _wornByCorpseProp = apparelType.GetProperty("WornByCorpseInternal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch { }
        }

        /// <summary>
        /// Retorna true se a roupa está tainted (WornByCorpse). Psychopaths ignoram penalidade.
        /// </summary>
        public static bool IsTainted(Apparel apparel)
        {
            if (apparel == null)
                return false;
            try
            {
                if (_wornByCorpseProp != null)
                {
                    object val = _wornByCorpseProp.GetValue(apparel);
                    if (val is bool b)
                        return b;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Penalidade alta para tainted apparel. Retorna 0 se pawn é Psychopath.
        /// </summary>
        public static float GetTaintedPenalty(Pawn pawn, Apparel apparel, float basePenalty = -25f)
        {
            if (pawn == null || apparel == null)
                return 0f;
            if (IsTainted(apparel) == false)
                return 0f;
            if (HasTrait(pawn, "Psychopath"))
                return 0f;
            return basePenalty;
        }

        /// <summary>
        /// Bonus para outfits que formem sets completos sem gaps de coverage (body part groups).
        /// </summary>
        public static float GetCoverageSetBonus(Pawn pawn, Apparel candidate, List<Apparel> wornApparel)
        {
            if (pawn?.RaceProps?.body?.AllParts == null || candidate?.def?.apparel?.bodyPartGroups == null)
                return 0f;
            var candidateGroups = candidate.def.apparel.bodyPartGroups;
            if (candidateGroups == null || candidateGroups.Count == 0)
                return 0f;
            int newCoverage = candidateGroups.Count;
            foreach (Apparel w in wornApparel ?? new List<Apparel>())
            {
                if (w == candidate) continue;
                if (w?.def?.apparel?.bodyPartGroups == null) continue;
                foreach (var g in w.def.apparel.bodyPartGroups)
                {
                    if (g != null && !candidateGroups.Contains(g))
                        newCoverage++;
                }
            }
            if (newCoverage >= 4)
                return 1.5f;
            if (newCoverage >= 3)
                return 0.8f;
            return 0f;
        }

        /// <summary>
        /// Penaliza apparel distante: preferir inventory do pawn ou stockpiles próximos.
        /// Retorna penalidade negativa (0 ou menor) conforme distância.
        /// </summary>
        public static float GetDistancePenalty(Pawn pawn, Apparel apparel)
        {
            if (pawn?.Map == null || apparel == null)
                return 0f;
            try
            {
                IntVec3 pawnPos = pawn.Position;
                IntVec3 apparelPos;
                if (apparel.Spawned)
                    apparelPos = apparel.Position;
                else
                {
                    var holder = apparel.ParentHolder;
                    if (holder is Pawn p && p == pawn)
                        return 0f;
                    if (holder is Thing thing && thing.Spawned)
                        apparelPos = thing.Position;
                    else
                        return -3f;
                }
                float dist = pawnPos.DistanceTo(apparelPos);
                if (dist <= 5f)
                    return 0f;
                if (dist <= 15f)
                    return -0.5f;
                if (dist <= 30f)
                    return -1.5f;
                return -3f;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Bonus para apparel sterile quando pawn é doctor e está em job de cirurgia.
        /// </summary>
        public static float GetSterileBonus(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel?.def?.apparel == null)
                return 0f;
            Job cur = pawn.CurJob;
            if (cur?.def == null)
                return 0f;
            string defName = cur.def.defName ?? "";
            if (defName.IndexOf("Surgery", StringComparison.OrdinalIgnoreCase) < 0
                && defName.IndexOf("Doctor", StringComparison.OrdinalIgnoreCase) < 0)
                return 0f;
            if (apparel.def.apparel.tags != null && apparel.def.apparel.tags.Contains("Sterile"))
                return 4f;
            return 0f;
        }

        /// <summary>
        /// Bonus para apparel heat-resistant quando pawn está em job de firefighting.
        /// </summary>
        public static float GetHeatResistBonus(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel?.def == null)
                return 0f;
            Job cur = pawn.CurJob;
            if (cur?.def == null)
                return 0f;
            string defName = cur.def.defName ?? "";
            if (defName.IndexOf("Firefighting", StringComparison.OrdinalIgnoreCase) < 0)
                return 0f;
            float heatInsul = apparel.GetStatValue(StatDefOf.Insulation_Heat);
            if (heatInsul > 5f)
                return 2f;
            return 0f;
        }

        private static bool HasTrait(Pawn pawn, string defName)
        {
            if (pawn?.story?.traits?.allTraits == null)
                return false;
            foreach (Trait t in pawn.story.traits.allTraits)
            {
                if (t?.def?.defName != null && string.Equals(t.def.defName, defName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
