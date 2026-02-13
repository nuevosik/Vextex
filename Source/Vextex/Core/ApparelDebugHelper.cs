using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Vextex.Settings;

namespace Vextex.Core
{
    /// <summary>
    /// Dev-only helper: builds a human-readable summary of current outfit scores and best swap candidate.
    /// Used by the Vextex debug gizmo on the pawn inspector.
    /// </summary>
    public static class ApparelDebugHelper
    {
        private const int MaxCandidates = 35;

        /// <summary>
        /// Returns a multi-line string: worn apparel with scores, then best candidate (if any) with breakdown.
        /// Safe to call from any thread; may be expensive (iterates apparel on map).
        /// </summary>
        public static string GetDebugOutfitSummary(Pawn pawn)
        {
            if (pawn == null || !pawn.IsColonist || pawn.Dead)
                return "[Vextex] Select a living colonist.";
            var sb = new StringBuilder();
            sb.AppendLine($"[Vextex] === Outfit debug: {pawn.LabelShort} ===");
            sb.AppendLine($"Role: {ColonistRoleDetector.DetectRole(pawn)}. Preset: {VextexModHandler.Settings?.currentPreset ?? BehaviorPreset.VanillaPlus}");

            // Current worn + scores
            var worn = pawn.apparel?.WornApparel;
            float currentTotal = 0f;
            if (worn != null)
            {
                sb.AppendLine("--- Currently worn ---");
                foreach (Apparel ap in worn)
                {
                    if (ap?.def == null) continue;
                    float s = ApparelScoreCalculator.CalculateScore(pawn, ap);
                    currentTotal += s;
                    sb.AppendLine($"  {ap.def.label}: Score {s:F2}");
                }
                sb.AppendLine($"  Total worn score: {currentTotal:F2}");
            }
            else
            {
                sb.AppendLine("--- No apparel worn ---");
            }

            // Best candidate from map
            Map map = pawn.Map;
            if (map == null)
            {
                sb.AppendLine("--- No map (best candidate N/A) ---");
                return sb.ToString();
            }

            var lister = map.listerThings;
            if (lister == null)
            {
                sb.AppendLine("--- No lister (best candidate N/A) ---");
                return sb.ToString();
            }

            var allApparel = lister.ThingsInGroup(ThingRequestGroup.Apparel).OfType<Apparel>().ToList();
            Apparel bestCandidate = null;
            ApparelDecisionContext bestCtx = null;
            float bestNetGain = float.NegativeInfinity;

            int checkedCount = 0;
            foreach (Apparel ap in allApparel)
            {
                if (checkedCount >= MaxCandidates) break;
                if (ap == null || ap.def?.apparel == null) continue;
                if (ap.Spawned == false) continue;
                if (pawn.apparel != null && pawn.apparel.WornApparel != null && pawn.apparel.WornApparel.Contains(ap))
                    continue;

                checkedCount++;
                var ctx = ApparelScoreCalculator.BuildDecisionContext(pawn, ap);
                if (!ctx.IsValid) continue;
                ApparelScoreCalculator.ComputeSwapFields(ctx, pawn, ap);
                if (!ctx.IsValid) continue;
                if (ctx.NetGain > bestNetGain)
                {
                    bestNetGain = ctx.NetGain;
                    bestCandidate = ap;
                    bestCtx = ctx;
                }
            }

            sb.AppendLine("--- Best swap candidate ---");
            if (bestCandidate == null || bestCtx == null)
            {
                sb.AppendLine("  None (no better apparel found in first " + MaxCandidates + " candidates).");
                return sb.ToString();
            }

            sb.AppendLine($"  {bestCandidate.def.label}: Score {bestCtx.VextexScore:F2}. NetGain {bestCtx.NetGain:F2} (threshold {bestCtx.SwapThreshold:F3}).");
            float psychic = ApparelScoreCalculator.GetPsychicApparelScore(pawn, bestCandidate);
            if (psychic > 0f)
                sb.AppendLine($"  Why? Armor={bestCtx.ArmorScoreRaw:F2}, Insul={bestCtx.InsulationScoreRaw:F2}, Mat={bestCtx.MaterialScoreRaw:F2}, Qual={bestCtx.QualityScoreRaw:F2}, Pen={bestCtx.PenaltyScoreRaw:F2}, Psychic=+{psychic:F2} (psycaster gear).");
            else
                sb.AppendLine($"  Why? Armor={bestCtx.ArmorScoreRaw:F2}, Insul={bestCtx.InsulationScoreRaw:F2}, Mat={bestCtx.MaterialScoreRaw:F2}, Qual={bestCtx.QualityScoreRaw:F2}, Pen={bestCtx.PenaltyScoreRaw:F2}.");
            return sb.ToString();
        }
    }
}
