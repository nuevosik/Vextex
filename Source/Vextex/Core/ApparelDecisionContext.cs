using RimWorld;
using Verse;

namespace Vextex.Core
{
    /// <summary>
    /// Centralised structure that holds every component of a Vextex apparel-swap decision.
    /// Built once per (pawn, candidate) evaluation and reused for decision-making, logging
    /// and the public API.  All values are pre-sanitised (no NaN/Infinity).
    /// </summary>
    public class ApparelDecisionContext
    {
        // === References ===
        public Pawn Pawn;
        public Apparel Candidate;

        // === Role / colony metadata ===
        public ColonistRoleDetector.CombatRole Role;
        public ColonistRoleDetector.RoleMultipliers RoleMult;
        public float PowerPercentile;

        // === Individual score components (before settings weights) ===
        public float ArmorScoreRaw;
        public float InsulationScoreRaw;
        public float MaterialScoreRaw;
        public float QualityScoreRaw;
        public float DurabilityFactor;
        public float PenaltyScoreRaw;

        // === Final weighted score for the candidate ===
        public float VextexScore;

        // === Swap evaluation ===
        public float RemovedWornScore;
        public float CurrentTotalScore;
        public float NetGain;
        public float SwapThreshold;
        public bool IsNakedOnCoveredGroups;

        // === Convenience ===

        /// <summary>Whether this context was built successfully (false = fallback to vanilla).</summary>
        public bool IsValid;
    }
}
