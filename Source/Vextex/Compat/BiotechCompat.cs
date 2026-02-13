using System;
using System.Reflection;
using Verse;

namespace Vextex.Compat
{
    /// <summary>
    /// Soft compatibility for Biotech DLC: read map pollution level without hard reference.
    /// </summary>
    public static class BiotechCompat
    {
        private static bool _resolved;
        private static PropertyInfo _pollutionLevelProp;

        private static bool _stateResolved;
        private static PropertyInfo _pollutionStateProp;
        private static PropertyInfo _levelOnStateProp;

        private static void TryResolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type mapType = typeof(Map);
                _pollutionLevelProp = mapType.GetProperty("PollutionLevel", BindingFlags.Public | BindingFlags.Instance);
                if (_pollutionLevelProp != null) return;
                var pollutionState = mapType.GetProperty("PollutionState", BindingFlags.Public | BindingFlags.Instance);
                if (pollutionState != null)
                {
                    _pollutionStateProp = pollutionState;
                    Type stateType = pollutionState.PropertyType;
                    _levelOnStateProp = stateType.GetProperty("PollutionLevel", BindingFlags.Public | BindingFlags.Instance);
                    _stateResolved = true;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Biotech resolve failed (non-fatal): {ex.Message}", 0x7E47EA);
                _pollutionLevelProp = null;
                _pollutionStateProp = null;
                _levelOnStateProp = null;
            }
        }

        /// <summary>
        /// Returns the map's pollution level (0 = none). Returns 0 if Biotech not present or not polluted.
        /// </summary>
        public static float GetMapPollutionLevel(Map map)
        {
            if (map == null) return 0f;
            TryResolve();
            try
            {
                if (_pollutionLevelProp != null)
                {
                    object val = _pollutionLevelProp.GetValue(map);
                    if (val is float f) return f;
                    if (val is int i) return i;
                    return 0f;
                }
                if (_stateResolved && _pollutionStateProp != null && _levelOnStateProp != null)
                {
                    object state = _pollutionStateProp.GetValue(map);
                    if (state == null) return 0f;
                    object val = _levelOnStateProp.GetValue(state);
                    if (val is float f) return f;
                    if (val is int i) return i;
                    return 0f;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Vextex] Biotech pollution read failed (non-fatal): {ex.Message}", 0x7E47EB);
            }
            return 0f;
        }
    }
}
