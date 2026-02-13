using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft compatibility for Ideology DLC: bonus for apparel that satisfies precepts (avoids -mood),
    /// and penalty for apparel that covers torso/legs when pawn is nudist or ideology requires nudity.
    /// No hard reference to Ideology assembly; uses reflection when available.
    /// </summary>
    public static class IdeologyCompat
    {
        private static bool _resolved;
        private static MethodInfo _getPreferredApparel;
        private static PropertyInfo _ideoProp;

        // Nudity: trait Nudist + precept Male/Female Nudity: Required
        private static TraitDef _nudistTraitDef;
        private static bool _nudityResolved;

        /// <summary>Body part groups that nudists dislike being covered (Torso, Legs). Hats and belts are OK.</summary>
        private static BodyPartGroupDef _torsoGroup;
        private static BodyPartGroupDef _legsGroup;

        private static void TryResolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type pawnType = typeof(Pawn);
                _ideoProp = pawnType.GetProperty("Ideo", BindingFlags.Public | BindingFlags.Instance);
                if (_ideoProp == null) return;

                Type ideoType = _ideoProp.PropertyType;
                _getPreferredApparel = ideoType.GetMethod("GetPreferredApparel", BindingFlags.Public | BindingFlags.Instance);
                if (_getPreferredApparel == null)
                    _getPreferredApparel = ideoType.GetMethod("PreferredApparel", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Ideology resolve failed (non-fatal): {ex.Message}", 0x7E47E9);
                _ideoProp = null;
                _getPreferredApparel = null;
            }
        }

        private static void TryResolveNudity()
        {
            if (_nudityResolved) return;
            _nudityResolved = true;
            try
            {
                _nudistTraitDef = DefDatabase<TraitDef>.GetNamedSilentFail("Nudist");
                _torsoGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Torso");
                _legsGroup = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail("Legs");
            }
            catch
            {
                _nudistTraitDef = null;
                _torsoGroup = null;
                _legsGroup = null;
            }
        }

        /// <summary>
        /// Returns a score bonus if the apparel is preferred/required by the pawn's ideology (Precept_Apparel).
        /// Returns 0 if Ideology not present or no match.
        /// </summary>
        public static float GetApparelPreceptBonus(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel?.def == null) return 0f;
            TryResolve();
            if (_ideoProp == null || _getPreferredApparel == null) return 0f;
            try
            {
                object ideo = _ideoProp.GetValue(pawn);
                if (ideo == null) return 0f;
                object preferred = _getPreferredApparel.Invoke(ideo, null);
                if (preferred == null) return 0f;
                var list = preferred as System.Collections.IList;
                if (list != null && list.Contains(apparel.def))
                    return 5f; // decisive so preferred beats slightly better non-preferred (avoids -mood)
                var en = preferred as System.Collections.IEnumerable;
                if (en != null)
                    foreach (object o in en)
                        if (o == apparel.def) return 5f;
                return 0f;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Ideology reflection failed (non-fatal): {ex.Message}", 0x7E47E8);
                return 0f;
            }
        }

        /// <summary>
        /// Returns a negative score (e.g. -50) if the pawn is nudist or ideology requires nudity
        /// and the apparel covers Torso or Legs (causing "Trapped in clothes" debuff).
        /// Hats and belts (no Torso/Legs) get no penalty. Returns 0 if not applicable.
        /// </summary>
        public static float GetNudityApparelPenalty(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel?.def?.apparel == null) return 0f;
            TryResolveNudity();
            if (_torsoGroup == null && _legsGroup == null) return 0f;

            bool wantsNudity = PawnWantsNudity(pawn);
            if (!wantsNudity) return 0f;

            var groups = apparel.def.apparel.bodyPartGroups;
            if (groups == null || groups.Count == 0) return 0f;

            bool coversTorsoOrLegs = false;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g == null) continue;
                if (g == _torsoGroup || g == _legsGroup)
                {
                    coversTorsoOrLegs = true;
                    break;
                }
            }
            if (!coversTorsoOrLegs) return 0f;

            return -50f; // Strong penalty so nudists don't get forced into shirts/pants
        }

        /// <summary>True if pawn has Nudist trait or ideology requires nudity.</summary>
        private static bool PawnWantsNudity(Pawn pawn)
        {
            try
            {
                if (_nudistTraitDef != null && pawn.story?.traits != null && pawn.story.traits.HasTrait(_nudistTraitDef))
                    return true;
                if (IdeologyRequiresNudity(pawn))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool IdeologyRequiresNudity(Pawn pawn)
        {
            if (_ideoProp == null) return false;
            try
            {
                object ideo = _ideoProp.GetValue(pawn);
                if (ideo == null) return false;
                Type ideoType = ideo.GetType();
                var preceptsProp = ideoType.GetProperty("PreceptsListForReading", BindingFlags.Public | BindingFlags.Instance)
                    ?? ideoType.GetProperty("Precepts", BindingFlags.Public | BindingFlags.Instance);
                if (preceptsProp == null) return false;
                var precepts = preceptsProp.GetValue(ideo) as System.Collections.IList;
                if (precepts == null) return false;
                for (int i = 0; i < precepts.Count; i++)
                {
                    object precept = precepts[i];
                    if (precept == null) continue;
                    var preceptDef = precept.GetType().GetProperty("Def", BindingFlags.Public | BindingFlags.Instance)?.GetValue(precept) as Def;
                    if (preceptDef?.defName == null) continue;
                    string dn = preceptDef.defName;
                    if (dn.IndexOf("Nudity", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // Only Mandatory/Required: massive mood debuff. Disapproved gives a small debuff
                    // that is often worth trading for life-saving armor; do not penalize here.
                    if (dn.IndexOf("Required", StringComparison.OrdinalIgnoreCase) >= 0
                        || dn.IndexOf("Mandatory", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
