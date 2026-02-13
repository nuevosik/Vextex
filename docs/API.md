# API Vextex — Integração para outros mods

O Vextex expõe uma API estável para que outros mods possam integrar-se **sem precisar fazer patch** no mesmo método que o Vextex. Use esta API para adicionar critérios de score ou consultar o score calculado.

---

## 1. Contribuidor de score (`IApparelScoreContributor`)

Outros mods podem **somar** um valor ao score de roupa que o Vextex já calcula (armadura, isolamento, material, qualidade, penalidades). O contribuidor é chamado para cada par (pawn, apparel) avaliado.

### Interface

```csharp
namespace Vextex.Core
{
    public interface IApparelScoreContributor
    {
        /// <summary>
        /// Contribuição aditiva ao score (pode ser 0 ou negativa para penalidades).
        /// Somada ao score base antes dos multiplicadores de durabilidade e skill.
        /// </summary>
        float GetScore(ScoreContributorContext ctx);
    }

    public struct ScoreContributorContext
    {
        public Pawn Pawn;
        public Apparel Apparel;
        public ScoringWeights Weights;
        public ColonistRoleDetector.CombatRole Role;
        public ColonistRoleDetector.RoleMultipliers RoleMult;
        public ColonyStageContributor Stage;  // Early, Mid, Late
    }
}
```

### Registo

- **Registrar:** `ScoreContributorRegistry.Register(meuContributor);`
- **Desregistrar:** `ScoreContributorRegistry.Unregister(meuContributor);`
- Faça o registo no arranque do seu mod (por exemplo em `[StaticConstructorOnStartup]`), para garantir ordem consistente.

### Boas práticas

- **Não lance exceções** em `GetScore`. Em caso de erro, retorne `0f`.
- O contexto é **read-only**; não altere `Pawn` ou `Apparel`.
- Valores muito grandes podem dominar o score; prefira contribuições na ordem de -2 a +2 para equilibrar com os outros fatores.

---

## 2. Exemplo mínimo: contribuidor que penaliza roupa quebrada

```csharp
using Vextex.Core;
using Verse;

namespace MeuMod.Compat
{
    public static class VextexContributorRegistration
    {
        [StaticConstructorOnStartup]
        public static void Register()
        {
            ScoreContributorRegistry.Register(new PenalizeDamagedApparelContributor());
        }
    }

    public class PenalizeDamagedApparelContributor : IApparelScoreContributor
    {
        public float GetScore(ScoreContributorContext ctx)
        {
            try
            {
                if (ctx.Apparel == null || ctx.Apparel.def == null)
                    return 0f;
                if (ctx.Apparel.MaxHitPoints <= 0)
                    return 0f;

                float hpPercent = (float)ctx.Apparel.HitPoints / ctx.Apparel.MaxHitPoints;
                // Penalidade suave quando abaixo de 50% HP
                if (hpPercent >= 0.5f)
                    return 0f;
                return (hpPercent - 0.5f) * 2f; // até -1 quando 0% HP
            }
            catch
            {
                return 0f;
            }
        }
    }
}
```

---

## 3. Consultar score sem alterar a AI

Se o seu mod só precisa de **ler** o score que o Vextex atribuiria (por exemplo para UI ou decisões próprias), use:

### Calcular score

```csharp
using Vextex.Core;

// Retorna true e o score em out score; false se não for possível calcular
bool ok = ApparelScoreCalculator.TryCalculateScore(pawn, apparel, out float score);
```

### Obter breakdown completo

```csharp
bool ok = ApparelScoreCalculator.GetScoreBreakdown(pawn, apparel, out ApparelDecisionContext ctx);
if (ok && ctx.IsValid)
{
    // ctx.VextexScore, ctx.ArmorScoreRaw, ctx.InsulationScoreRaw, ctx.NetGain, etc.
}
```

---

## 4. Desativar a lógica do Vextex (outro mod controla outfit)

Se o **seu mod** controla totalmente a AI de outfit e não quer que o Vextex altere o resultado de `ApparelScoreGain`:

- O jogador pode ativar nas opções do Vextex: **"Another mod fully controls outfit AI"** (`externalOutfitController`).
- Com isso, o Vextex não modifica o retorno de `ApparelScoreGain`; o comportamento fica 100% no controlo do jogo/outro mod.

Não é possível desativar o Vextex por código de forma estável; a opção é via configuração do utilizador.

---

## 5. Resumo

| Objetivo | Método |
|----------|--------|
| Adicionar critério ao score de roupa | Implementar `IApparelScoreContributor` e `ScoreContributorRegistry.Register(...)` |
| Só ler o score que o Vextex daria | `ApparelScoreCalculator.TryCalculateScore(pawn, apparel, out score)` |
| Ver detalhe do score (armadura, isolamento, etc.) | `ApparelScoreCalculator.GetScoreBreakdown(pawn, apparel, out ctx)` |
| Evitar conflito quando outro mod controla outfit | Utilizador ativa "Another mod fully controls outfit AI" nas definições do Vextex |

Referência de tipos: assembly **Vextex**, namespaces `Vextex.Core`, `Vextex.Settings`. Sem referências diretas a assemblies de outros mods; compatibilidade via API e deteção por `ModsConfig`/Defs.
