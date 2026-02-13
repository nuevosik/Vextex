using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft compatibility for Royalty DLC: detect psycasters, score psychic apparel (Eltex, etc.),
    /// and give a large bonus to apparel required by the pawn's royal title (so nobles keep Royal Vest, etc.).
    /// No hard reference to Royalty assembly.
    /// </summary>
    public static class RoyaltyCompat
    {
        private static bool _resolved;
        private static PropertyInfo _psychicEntropyProp;
        private static PropertyInfo _psylinkLevelProp;

        // Royal title required apparel (Baron, Count, Duke, etc.)
        private static PropertyInfo _royaltyProp;
        private static MethodInfo _highestTitleWithBedroomReqs;
        private static PropertyInfo _titleDefProp;
        private static FieldInfo _requiredApparelField;
        private static PropertyInfo _requiredApparelProp;

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

                // Royalty tracker and title requirements (requiredApparel: Royal Vest, Corset, Robe, etc.)
                _royaltyProp = pawnType.GetProperty("Royalty", BindingFlags.Public | BindingFlags.Instance)
                    ?? pawnType.GetProperty("royalty", BindingFlags.Public | BindingFlags.Instance);
                if (_royaltyProp == null) return;
                Type royaltyType = _royaltyProp.PropertyType;
                _highestTitleWithBedroomReqs = royaltyType.GetMethod("HighestTitleWithBedroomRequirements", Type.EmptyTypes)
                    ?? royaltyType.GetMethod("GetHighestTitleWithBedroomRequirements", Type.EmptyTypes);
                if (_highestTitleWithBedroomReqs == null) return;
                Type titleType = _highestTitleWithBedroomReqs.ReturnType;
                if (titleType == typeof(void) || !titleType.IsClass) return;
                _titleDefProp = titleType.GetProperty("Def", BindingFlags.Public | BindingFlags.Instance)
                    ?? titleType.GetProperty("def", BindingFlags.Public | BindingFlags.Instance);
                if (_titleDefProp == null) return;
                Type titleDefType = _titleDefProp.PropertyType;
                _requiredApparelProp = titleDefType.GetProperty("RequiredApparel", BindingFlags.Public | BindingFlags.Instance)
                    ?? titleDefType.GetProperty("requiredApparel", BindingFlags.Public | BindingFlags.Instance);
                if (_requiredApparelProp == null)
                    _requiredApparelField = titleDefType.GetField("requiredApparel", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Royalty resolve failed (non-fatal): {ex.Message}", 0x7E47EC);
                _psychicEntropyProp = null;
                _psylinkLevelProp = null;
                _royaltyProp = null;
                _highestTitleWithBedroomReqs = null;
                _titleDefProp = null;
                _requiredApparelProp = null;
                _requiredApparelField = null;
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

        /// <summary>
        /// Returns a score bonus if the apparel is in the pawn's royal title required apparel list
        /// (e.g. Royal Vest, Corset, Robe). Prevents nobles from swapping to Marine Armor and losing permissions.
        /// Vanilla: Prestige variants (Prestige Recon Armor, etc.) are typically in requiredApparel and work as-is.
        /// Mod-added variants of required apparel may need to be added to the title def to be recognized.
        /// Returns 0 if Royalty not present, pawn has no title with requirements, or apparel not required.
        /// </summary>
        public static float GetRequiredApparelBonus(Pawn pawn, ThingDef apparelDef)
        {
            if (pawn == null || apparelDef == null) return 0f;
            TryResolve();
            if (_royaltyProp == null || _highestTitleWithBedroomReqs == null || _titleDefProp == null)
                return 0f;
            if (_requiredApparelProp == null && _requiredApparelField == null)
                return 0f;
            try
            {
                object royalty = _royaltyProp.GetValue(pawn);
                if (royalty == null) return 0f;
                object title = _highestTitleWithBedroomReqs.Invoke(royalty, null);
                if (title == null) return 0f;
                object titleDef = _titleDefProp.GetValue(title);
                if (titleDef == null) return 0f;
                IList<ThingDef> required = null;
                if (_requiredApparelProp != null)
                    required = _requiredApparelProp.GetValue(titleDef) as IList<ThingDef>;
                if (required == null && _requiredApparelField != null)
                    required = _requiredApparelField.GetValue(titleDef) as IList<ThingDef>;
                if (required == null || required.Count == 0) return 0f;
                for (int i = 0; i < required.Count; i++)
                {
                    if (required[i] == apparelDef)
                        return 40f; // Large bonus so required royal apparel beats high-armor alternatives
                }
                return 0f;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
