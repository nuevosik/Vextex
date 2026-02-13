using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Vextex.Comps
{
    /// <summary>
    /// Memória adaptativa por pawn: conta traumas térmicos (quase fatal hypo/hyperthermia)
    /// e traumas por parte do corpo (dano crítico, partes perdidas).
    /// Usado no scoring para aumentar thermal safety e proteção em partes traumatizadas.
    /// </summary>
    public class CompPawnApparelMemory : ThingComp
    {
        public int thermalTraumaCount;
        /// <summary>Chave: BodyPartDef.defName ou group defName para persistência estável.</summary>
        public Dictionary<string, int> bodyPartTrauma = new Dictionary<string, int>();

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref thermalTraumaCount, "thermalTraumaCount", 0);
            Scribe_Collections.Look(ref bodyPartTrauma, "bodyPartTrauma", LookMode.Value, LookMode.Value);
            if (bodyPartTrauma == null)
                bodyPartTrauma = new Dictionary<string, int>();
        }

        /// <summary>Incrementa contador de trauma térmico (chamado por patch quando quase fatal).</summary>
        public void RecordThermalTrauma()
        {
            thermalTraumaCount++;
        }

        /// <summary>Registra trauma em uma parte do corpo (chamado por patch em dano crítico).</summary>
        public void RecordBodyPartTrauma(BodyPartRecord part)
        {
            if (part?.def == null) return;
            string key = part.def.defName ?? part.def.label ?? "Unknown";
            if (!bodyPartTrauma.ContainsKey(key))
                bodyPartTrauma[key] = 0;
            bodyPartTrauma[key]++;
        }

        /// <summary>Retorna número de traumas para um body part group (ex.: Head, Torso). Compara por defName para compatibilidade.</summary>
        public int GetTraumaCountForPart(BodyPartGroupDef group)
        {
            if (group == null || bodyPartTrauma == null) return 0;
            string groupName = group.defName ?? "";
            if (string.IsNullOrEmpty(groupName)) return 0;
            int count = 0;
            foreach (var kv in bodyPartTrauma)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (kv.Key.IndexOf(groupName, StringComparison.OrdinalIgnoreCase) >= 0)
                    count += kv.Value;
            }
            return count;
        }
    }

    public class CompProperties_PawnApparelMemory : CompProperties
    {
        public CompProperties_PawnApparelMemory()
        {
            compClass = typeof(CompPawnApparelMemory);
        }
    }
}
