using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Vextex.Compat;
using Vextex.Settings;

namespace Vextex.Core
{
    /// <summary>
    /// Papel granular do pawn para scoring de apparel: combina combate (melee/ranged),
    /// trabalho (hauler, cleaner, crafter, doctor, hunter) e contexto atual (job, arma).
    /// Reavaliado a cada chamada de otimização para decisões dinâmicas.
    /// </summary>
    public static class PawnRoleDetector
    {
        /// <summary>Papéis de trabalho/combate que afetam prioridade de stats (move speed, armor por parte do corpo, etc.).</summary>
        public enum PawnRole
        {
            None,
            Melee,
            Ranged,
            NonCombatant,
            Hauler,
            Cleaner,
            Crafter,
            Doctor,
            Hunter
        }

        /// <summary>Pesos por parte do corpo para armor: melee prioriza torso/limbs, ranged prioriza head/neck.</summary>
        public struct BodyPartArmorWeights
        {
            public float Torso;
            public float Head;
            public float Neck;
            public float Limbs;

            public static readonly BodyPartArmorWeights Melee = new BodyPartArmorWeights
            {
                Torso = 1.8f,
                Head = 0.8f,
                Neck = 0.9f,
                Limbs = 1.5f
            };

            public static readonly BodyPartArmorWeights Ranged = new BodyPartArmorWeights
            {
                Torso = 1.0f,
                Head = 1.6f,
                Neck = 1.5f,
                Limbs = 0.9f
            };

            public static readonly BodyPartArmorWeights Default = new BodyPartArmorWeights
            {
                Torso = 1.0f,
                Head = 1.0f,
                Neck = 1.0f,
                Limbs = 1.0f
            };
        }

        /// <summary>
        /// Detecta o papel atual do pawn usando: arma equipada, prioridades de workSettings,
        /// job atual e traits. Retorna o papel predominante para esta avaliação.
        /// </summary>
        public static PawnRole DetectRole(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return PawnRole.NonCombatant;

            VextexSettings settings = VextexModHandler.Settings;
            if (settings != null && !settings.enableRoleDetection)
                return PawnRole.NonCombatant;

            // 1) Job atual tem prioridade para contexto imediato (médico em cirurgia, bombeiro, etc.)
            PawnRole jobRole = GetRoleFromCurrentJob(pawn);
            if (jobRole != PawnRole.None)
                return jobRole;

            // 2) Arma equipada define melee vs ranged
            PawnRole weaponRole = GetRoleFromWeapon(pawn);
            if (weaponRole != PawnRole.None)
                return weaponRole;

            // 3) Work priorities: hauling, cleaning, crafting, doctor, hunting
            PawnRole workRole = GetRoleFromWorkPriorities(pawn);
            if (workRole != PawnRole.None)
                return workRole;

            // 4) Traits (Brawler -> melee, etc.)
            PawnRole traitRole = GetRoleFromTraits(pawn);
            if (traitRole != PawnRole.None)
                return traitRole;

            // 5) Skills como fallback (já coberto por ColonistRoleDetector para Melee/Ranged/NonCombatant)
            return GetRoleFromSkills(pawn);
        }

        /// <summary>Retorna pesos de armor por parte do corpo conforme o papel.</summary>
        public static BodyPartArmorWeights GetBodyPartWeights(PawnRole role)
        {
            switch (role)
            {
                case PawnRole.Melee: return BodyPartArmorWeights.Melee;
                case PawnRole.Ranged: return BodyPartArmorWeights.Ranged;
                default: return BodyPartArmorWeights.Default;
            }
        }

        /// <summary>True se o papel deve priorizar move speed (haulers, cleaners, crafters).</summary>
        public static bool RolePrioritizesMoveSpeed(PawnRole role)
        {
            return role == PawnRole.Hauler || role == PawnRole.Cleaner || role == PawnRole.Crafter;
        }

        /// <summary>True se o papel deve priorizar manipulation e sterile (doctors em cirurgia).</summary>
        public static bool RolePrioritizesManipulationAndSterile(PawnRole role)
        {
            return role == PawnRole.Doctor;
        }

        /// <summary>True se o papel deve priorizar shooting accuracy (hunters).</summary>
        public static bool RolePrioritizesShootingAccuracy(PawnRole role)
        {
            return role == PawnRole.Hunter || role == PawnRole.Ranged;
        }

        private static PawnRole GetRoleFromCurrentJob(Pawn pawn)
        {
            try
            {
                Job cur = pawn.CurJob;
                if (cur == null || cur.def == null)
                    return PawnRole.None;

                string defName = cur.def.defName ?? "";
                // Doctor em cirurgia
                if (defName.IndexOf("Surgery", StringComparison.OrdinalIgnoreCase) >= 0
                    || defName.IndexOf("Doctor", StringComparison.OrdinalIgnoreCase) >= 0
                    || cur.def.reportString == "performing surgery")
                    return PawnRole.Doctor;
                // Firefighting tratado em ThermalSafetyHelper
                if (defName.IndexOf("Firefighting", StringComparison.OrdinalIgnoreCase) >= 0)
                    return PawnRole.None; // mantém papel de combate/trabalho, heat resist é aplicado em outro lugar
                return PawnRole.None;
            }
            catch
            {
                return PawnRole.None;
            }
        }

        private static PawnRole GetRoleFromWeapon(Pawn pawn)
        {
            try
            {
                if (pawn.equipment?.Primary == null)
                    return PawnRole.None;
                ThingDef def = pawn.equipment.Primary.def;
                if (def == null) return PawnRole.None;
                if (def.IsMeleeWeapon) return PawnRole.Melee;
                if (def.IsRangedWeapon) return PawnRole.Ranged;
                return PawnRole.None;
            }
            catch
            {
                return PawnRole.None;
            }
        }

        private static PawnRole GetRoleFromWorkPriorities(Pawn pawn)
        {
            try
            {
                Pawn_WorkSettings ws = pawn.workSettings;
                if (ws == null || !ws.EverWork)
                    return PawnRole.None;

                // Prioridade 1 = mais alta no RimWorld
                const int highPriority = 1;
                if (WorkPriority(pawn, WorkTypeDefOf.Hauling) >= highPriority)
                    return PawnRole.Hauler;
                if (WorkPriority(pawn, WorkTypeDefOf.Cleaning) >= highPriority)
                    return PawnRole.Cleaner;
                if (WorkPriority(pawn, WorkTypeDefOf.Crafting) >= highPriority)
                    return PawnRole.Crafter;
                if (WorkPriority(pawn, WorkTypeDefOf.Doctor) >= highPriority)
                    return PawnRole.Doctor;
                if (WorkPriority(pawn, WorkTypeDefOf.Hunting) >= highPriority)
                    return PawnRole.Hunter;

                return PawnRole.None;
            }
            catch
            {
                return PawnRole.None;
            }
        }

        private static int WorkPriority(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
                return 0;
            try
            {
                return pawn.workSettings.GetPriority(workType);
            }
            catch
            {
                return 0;
            }
        }

        private static PawnRole GetRoleFromTraits(Pawn pawn)
        {
            if (pawn?.story?.traits?.allTraits == null)
                return PawnRole.None;
            foreach (Trait t in pawn.story.traits.allTraits)
            {
                if (t?.def?.defName == null) continue;
                if (string.Equals(t.def.defName, "Brawler", StringComparison.OrdinalIgnoreCase))
                    return PawnRole.Melee;
            }
            return PawnRole.None;
        }

        private static PawnRole GetRoleFromSkills(Pawn pawn)
        {
            ColonistRoleDetector.CombatRole combat = ColonistRoleDetector.DetectRole(pawn);
            switch (combat)
            {
                case ColonistRoleDetector.CombatRole.Melee: return PawnRole.Melee;
                case ColonistRoleDetector.CombatRole.Ranged: return PawnRole.Ranged;
                default: return PawnRole.NonCombatant;
            }
        }
    }
}
