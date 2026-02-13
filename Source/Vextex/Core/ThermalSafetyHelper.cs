using System;
using System.Reflection;
using RimWorld;
using Verse;
using Vextex.Compat;

namespace Vextex.Core
{
    /// <summary>
    /// Thermal safety robusta: previsão de temperatura 24–48h, buffer de conforto ±5°C,
    /// multiplicadores por xenotype (ex.: genies toleram mais calor) e bonus para
    /// combinações de layers que cobrem faixa ampla sem over-insulation.
    /// </summary>
    public static class ThermalSafetyHelper
    {
        private const float ComfortBufferC = 5f;
        private const int ForecastTicks = 60000; // ~24h em 1x

        private static MethodInfo _outdoorTempAt;

        static ThermalSafetyHelper()
        {
            try
            {
                Type genTemp = typeof(GenTemperature);
                _outdoorTempAt = genTemp.GetMethod("GetOutdoorTemperature", BindingFlags.Public | BindingFlags.Static);
                if (_outdoorTempAt == null)
                    _outdoorTempAt = genTemp.GetMethod("OutdoorTempAt", BindingFlags.Public | BindingFlags.Static);
            }
            catch { }
        }

        /// <summary>
        /// Retorna temperatura efetiva prevista para as próximas 24–48h (média simples ou atual se API não existir).
        /// </summary>
        public static float GetForecastTemperature(Map map)
        {
            if (map?.mapTemperature == null)
                return 20f;
            try
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                float current = map.mapTemperature.OutdoorTemp;
                if (_outdoorTempAt != null)
                {
                    try
                    {
                        int futureTick = tick + ForecastTicks;
                        object result = _outdoorTempAt.Invoke(null, new object[] { map.Tile, futureTick });
                        if (result is float f)
                            return (current + f) * 0.5f;
                    }
                    catch { }
                }
                return current;
            }
            catch
            {
                return map.mapTemperature.OutdoorTemp;
            }
        }

        /// <summary>
        /// Zona de conforto do pawn com buffer ±5°C. Usa ComfyTemperatureMin/Max se disponível.
        /// </summary>
        public static void GetComfortZone(Pawn pawn, out float min, out float max)
        {
            min = 15f;
            max = 30f;
            if (pawn == null)
                return;
            try
            {
                if (OptionalStatsResolver.ComfyTemperatureMin != null && OptionalStatsResolver.ComfyTemperatureMax != null)
                {
                    float rawMin = pawn.GetStatValue(OptionalStatsResolver.ComfyTemperatureMin);
                    float rawMax = pawn.GetStatValue(OptionalStatsResolver.ComfyTemperatureMax);
                    min = rawMin - ComfortBufferC;
                    max = rawMax + ComfortBufferC;
                }
            }
            catch { }
        }

        private static PropertyInfo _genesProp;

        /// <summary>
        /// Multiplicador de tolerância térmica por xenotype (Biotech). Ex.: genies toleram mais calor.
        /// Retorna (heatMult, coldMult); &gt;1 = mais tolerante. Usa reflection para evitar dependência de Biotech.
        /// </summary>
        public static void GetXenotypeThermalMultipliers(Pawn pawn, out float heatMult, out float coldMult)
        {
            heatMult = 1f;
            coldMult = 1f;
            if (pawn == null) return;
            try
            {
                if (_genesProp == null)
                    _genesProp = typeof(Pawn).GetProperty("Genes", BindingFlags.Public | BindingFlags.Instance);
                if (_genesProp == null) return;
                object genes = _genesProp.GetValue(pawn);
                if (genes == null) return;
                Type genesType = genes.GetType();
                var listProp = genesType.GetProperty("GenesListForReading", BindingFlags.Public | BindingFlags.Instance)
                    ?? genesType.GetProperty("GeneDefs", BindingFlags.Public | BindingFlags.Instance);
                if (listProp == null) return;
                var list = listProp.GetValue(genes) as System.Collections.IList;
                if (list == null) return;
                foreach (object gene in list)
                {
                    if (gene == null) continue;
                    string defName = gene.GetType().GetProperty("Def", BindingFlags.Public | BindingFlags.Instance)?.GetValue(gene)?.ToString()
                        ?? gene.GetType().GetProperty("def", BindingFlags.Public | BindingFlags.Instance)?.GetValue(gene)?.ToString();
                    if (string.IsNullOrEmpty(defName)) continue;
                    if (defName.IndexOf("Heat", StringComparison.OrdinalIgnoreCase) >= 0
                        || defName.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0)
                        heatMult *= 1.15f;
                    if (defName.IndexOf("Cold", StringComparison.OrdinalIgnoreCase) >= 0
                        || defName.IndexOf("Frost", StringComparison.OrdinalIgnoreCase) >= 0)
                        coldMult *= 1.15f;
                }
            }
            catch { }
        }

        /// <summary>
        /// Penalidade térmica quando vestir esta peça deixaria o pawn fora da zona de conforto (com buffer ±5°C e xenotype).
        /// </summary>
        public static float GetThermalSafetyPenalty(Pawn pawn, Apparel apparel, float forecastTemp)
        {
            if (pawn?.Map == null || apparel?.def?.apparel == null)
                return 0f;
            GetComfortZone(pawn, out float min, out float max);
            GetXenotypeThermalMultipliers(pawn, out float heatMult, out float coldMult);
            float heatInsul = apparel.GetStatValue(StatDefOf.Insulation_Heat);
            float coldInsul = Math.Abs(apparel.GetStatValue(StatDefOf.Insulation_Cold));
            float effectiveTemp = forecastTemp + (heatInsul * 0.3f) - (coldInsul * 0.3f);
            if (effectiveTemp > max)
            {
                float excess = (effectiveTemp - max) / heatMult;
                return -Math.Min(15f, excess * 0.5f);
            }
            if (effectiveTemp < min)
            {
                float deficit = (min - effectiveTemp) / coldMult;
                return -Math.Min(15f, deficit * 0.5f);
            }
            return 0f;
        }

        /// <summary>
        /// Bonus para combinações de layers que cobrem faixa ampla de temperatura sem over-insulation.
        /// Retorna bonus pequeno (0..2) quando cold+heat insulation estão balanceados e não exagerados.
        /// </summary>
        public static float GetWideRangeLayerBonus(Pawn pawn, Apparel apparel, System.Collections.Generic.List<Apparel> wornApparel)
        {
            if (apparel?.def?.apparel == null)
                return 0f;
            float heatInsul = apparel.GetStatValue(StatDefOf.Insulation_Heat);
            float coldInsul = Math.Abs(apparel.GetStatValue(StatDefOf.Insulation_Cold));
            float totalHeat = heatInsul;
            float totalCold = coldInsul;
            if (wornApparel != null)
            {
                foreach (Apparel w in wornApparel)
                {
                    if (w == apparel) continue;
                    totalHeat += w.GetStatValue(StatDefOf.Insulation_Heat);
                    totalCold += Math.Abs(w.GetStatValue(StatDefOf.Insulation_Cold));
                }
            }
            const float overInsulationThreshold = 80f;
            if (totalHeat > overInsulationThreshold || totalCold > overInsulationThreshold)
                return 0f;
            if (totalHeat > 5f && totalCold > 5f)
                return 0.5f;
            return 0f;
        }
    }
}
