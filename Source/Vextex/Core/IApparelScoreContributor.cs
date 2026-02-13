using System.Collections.Generic;
using RimWorld;
using Verse;
using Vextex.Settings;

namespace Vextex.Core
{
    /// <summary>
    /// Colony stage for use in score contributor context (mirrors internal staging).
    /// </summary>
    public enum ColonyStageContributor
    {
        Early,
        Mid,
        Late
    }

    /// <summary>
    /// Context passed to apparel score contributors. Read-only snapshot for one (pawn, apparel) evaluation.
    /// </summary>
    public struct ScoreContributorContext
    {
        public Pawn Pawn;
        public Apparel Apparel;
        public ScoringWeights Weights;
        public ColonistRoleDetector.CombatRole Role;
        public ColonistRoleDetector.RoleMultipliers RoleMult;
        public ColonyStageContributor Stage;
    }

    /// <summary>
    /// Optional contributor to the apparel score. Allows mods or future features to add
    /// score components without editing the core calculator. Contributors are summed
    /// on top of the built-in score (armor, insulation, material, quality, penalties).
    /// Implementations should be defensive (no throws) and return 0 on error.
    /// </summary>
    public interface IApparelScoreContributor
    {
        /// <summary>
        /// Returns an additive score contribution (can be 0 or negative for penalties).
        /// This is added to the built-in weighted sum before durability and skill multipliers.
        /// </summary>
        float GetScore(ScoreContributorContext ctx);
    }

    /// <summary>
    /// Registry of optional score contributors. Used by ApparelScoreCalculator when
    /// computing the final score. Thread-safe for single-threaded RimWorld use.
    /// </summary>
    public static class ScoreContributorRegistry
    {
        private static readonly List<IApparelScoreContributor> Contributors = new List<IApparelScoreContributor>(4);

        /// <summary>
        /// Registers a contributor. Call from mod startup (e.g. [StaticConstructorOnStartup]).
        /// </summary>
        public static void Register(IApparelScoreContributor contributor)
        {
            if (contributor == null) return;
            lock (Contributors)
            {
                if (!Contributors.Contains(contributor))
                    Contributors.Add(contributor);
            }
        }

        /// <summary>
        /// Unregisters a contributor (e.g. when a mod is disabled).
        /// </summary>
        public static void Unregister(IApparelScoreContributor contributor)
        {
            if (contributor == null) return;
            lock (Contributors)
            {
                Contributors.Remove(contributor);
            }
        }

        /// <summary>
        /// Returns a snapshot of registered contributors for iteration.
        /// </summary>
        internal static IReadOnlyList<IApparelScoreContributor> GetContributors()
        {
            lock (Contributors)
            {
                return Contributors.Count == 0 ? null : new List<IApparelScoreContributor>(Contributors);
            }
        }
    }
}
