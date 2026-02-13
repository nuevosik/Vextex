using System;
using System.Reflection;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft compatibility for Royalty DLC: detect psycasters and score psychic apparel (Eltex, etc.).
    /// No hard reference to Royalty assembly.
    /// </summary>
    public static class RoyaltyCompat
    {
        private static bool _resolved;
        private static PropertyInfo _psychicEntropyProp;
        private static PropertyInfo _psylinkLevelProp;

        private static void TryResolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type pawnType = typeof(Pawn);
                _psychicEntropyProp = pawnType.GetProperty("psychicEntropy", BindingFlags.Public | BindingFlags.Instance);
                if (_psychicEntropyProp == null) return;
                Type peType = _psychicEntropyProp.PropertyType;
                _psylinkLevelProp = peType.GetProperty("PsylinkLevel", BindingFlags.Public | BindingFlags.Instance);
                if (_psylinkLevelProp == null)
                    _psylinkLevelProp = peType.GetProperty("CurrentPsylinkLevel", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Royalty resolve failed (non-fatal): {ex.Message}", 0x7E47EC);
                _psychicEntropyProp = null;
                _psylinkLevelProp = null;
            }
        }

        /// <summary>
        /// Returns true if the pawn has at least one psylink level (is a psycaster).
        /// </summary>
        public static bool HasPsylink(Pawn pawn)
        {
            if (pawn == null) return false;
            TryResolve();
            if (_psychicEntropyProp == null || _psylinkLevelProp == null) return false;
            try
            {
                object pe = _psychicEntropyProp.GetValue(pawn);
                if (pe == null) return false;
                object level = _psylinkLevelProp.GetValue(pe);
                if (level is int i) return i > 0;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
