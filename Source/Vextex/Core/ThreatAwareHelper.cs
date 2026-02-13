using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace Vextex.Core
{
    /// <summary>
    /// Threat-aware: ao detectar raid, mechanoids ou inimigos próximos,
    /// aumenta temporariamente o peso de armor (multiplicador aplicado no calculator).
    /// </summary>
    public static class ThreatAwareHelper
    {
        private static PropertyInfo _threatBigProp;

        static ThreatAwareHelper()
        {
            try
            {
                Type storyWatcher = typeof(StoryWatcher);
                _threatBigProp = storyWatcher.GetProperty("Adaptation", BindingFlags.Public | BindingFlags.Instance);
                if (_threatBigProp == null)
                    _threatBigProp = typeof(StoryWatcher).GetProperty("ThreatBig", BindingFlags.Public | BindingFlags.Instance);
            }
            catch { }
        }

        /// <summary>
        /// Retorna true se há ameaça ativa: raid, mechanoids ou inimigos hostis no mapa.
        /// Usado para aumentar peso de armor temporariamente.
        /// </summary>
        public static bool IsThreatActive(Map map)
        {
            if (map == null)
                return false;
            try
            {
                // attackTargetsCache: inimigos hostis ao mapa
                var cache = map.attackTargetsCache;
                if (cache != null)
                {
                    try
                    {
                        var targets = cache?.TargetsHostileToColony;
                        if (targets != null && targets.Count > 0)
                            return true;
                    }
                    catch { }
                }

                // StoryWatcher: ThreatBig ou Adaptation
                var watcher = Find.StoryWatcher;
                if (watcher != null && _threatBigProp != null)
                {
                    try
                    {
                        object val = _threatBigProp.GetValue(watcher);
                        if (val != null)
                        {
                            var adapt = val.GetType().GetProperty("ThreatPoints", BindingFlags.Public | BindingFlags.Instance);
                            if (adapt != null)
                            {
                                object points = adapt.GetValue(val);
                                if (points is float f && f > 0f)
                                    return true;
                            }
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Multiplicador de peso de armor quando threat-aware está ativo (ex.: 1.5x).
        /// </summary>
        public const float ThreatArmorMultiplier = 1.5f;
    }
}
