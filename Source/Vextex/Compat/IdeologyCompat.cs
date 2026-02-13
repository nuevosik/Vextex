using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft compatibility for Ideology DLC: bonus for apparel that satisfies precepts (avoids -mood).
    /// No hard reference to Ideology assembly; uses reflection when available.
    /// </summary>
    public static class IdeologyCompat
    {
        private static bool _resolved;
        private static MethodInfo _getPreferredApparel;
        private static PropertyInfo _ideoProp;

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
    }
}
