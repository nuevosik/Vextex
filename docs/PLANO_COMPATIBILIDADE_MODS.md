# Plano de Implementação: Compatibilidade com Múltiplos Mods (Vextex)

Este documento descreve um plano sistemático para tornar o Vextex compatível com **diversos mods** do RimWorld, minimizando bugs de compatibilidade sem exigir referências diretas (hard dependencies).

---

## 1. Princípios de Design

| Princípio | Descrição | Status atual |
|-----------|-----------|--------------|
| **Soft dependency** | Nunca referenciar assemblies de outros mods; detectar via `ModsConfig.IsActive` ou `AppDomain` / `DefDatabase`. | ✅ Em uso (CE, ModCompat) |
| **Fail-safe** | Qualquer acesso a Def/Stat/Pawn/Apparel deve estar em try-catch ou checagem null; retorno seguro (ex: 0, fallback). | ✅ Parcial (reforçar em pontos críticos) |
| **Fallback para vanilla** | Se algo falhar ou mod conflitante detectado, comportamento deve degradar para vanilla ou “não recomendar troca”. | ✅ `externalOutfitController`, __result = -1f |
| **Sem mutação global agressiva** | Evitar alterar estado global que outros mods dependam; preferir decisões locais (por pawn, por avaliação). | ✅ Cooldown por pawn, cache por tick |
| **API para terceiros** | Oferecer ponto de extensão estável (ex: `IApparelScoreContributor`) para outros mods integrarem sem patch. | ✅ `ScoreContributorRegistry` |

---

## 2. Camadas de Compatibilidade

### 2.1 Harmony (Patches)

**Riscos:** Outros mods podem patchar os mesmos métodos (`JobGiver_OptimizeApparel.ApparelScoreGain`, `TryGiveJob`). Ordem de aplicação e prioridade definem quem “vence”.

| Ação | Implementação |
|------|----------------|
| **Prioridade do postfix** | Manter `[HarmonyPriority(Priority.Last)]` no postfix de `ApparelScoreGain` para rodar por último e ter a palavra final quando desejado. |
| **Detecção de conflito** | Já existe: em DevMode, listar `Harmony.GetPatchInfo(method).Owners` e avisar se há outros owners. Manter e documentar na UI. |
| **Opção “outro mod controla”** | Manter `externalOutfitController`: quando ativo, o postfix retorna imediatamente sem alterar `__result`. |
| **Evitar Prefix que cancele** | Não usar prefix que retorne `false` (cancelar original) a menos que seja explícito (ex. “desabilitar Vextex”). Preferir postfix que sobrescreve resultado. |

**Tarefas:**
- [ ] Garantir que, se `externalOutfitController == true`, nenhum outro patch do Vextex altere comportamento de outfit (revisar todos os patches).
- [ ] Opcional: na tela de configurações, exibir “Outros mods que patcham Optimize Apparel: …” quando detectado (já logado; pode ser mostrado na UI).

---

### 2.2 Stats e Defs (Modded Content)

**Riscos:** Mods adicionam/alteram `StatDef`, `ThingDef`, materiais; `GetStatValue` pode lançar ou retornar NaN/Infinity.

| Ação | Implementação |
|------|----------------|
| **Stats vanilla** | Usar `StatDefOf.X` quando existir; para stats opcionais (ex: CE), usar `DefDatabase<StatDef>.GetNamedSilentFail(name)`. |
| **Stats modded** | Sempre `GetNamedSilentFail` ou equivalente; nunca assumir que o stat existe. |
| **Valores numéricos** | Sanitizar todos os valores com `SanitizeFloat` (NaN/Infinity → fallback). Já existe em vários pontos; garantir em todo retorno de score. |
| **Apparel.def / apparel.bodyPartGroups** | Sempre null-check em `def`, `def.apparel`, `layers`, `bodyPartGroups` antes de iterar (já feito em `HasApparelConflict`). |
| **TryGetQuality** | Alguns mods podem não ter qualidade; `TryGetQuality` já é seguro. Manter fallback (ex: 0.5). |
| **MaterialEvaluator** | Materiais desconhecidos já caem em `ClassifyByStats`; garantir que `GetStatSafe` e stats abstratos não lancem. |

**Tarefas:**
- [ ] Lista centralizada de `StatDef` opcionais (por mod): ex. “ShootingAccuracyPawn”, “AimingDelayFactor”, “Bulk”, “WornBulk”. Resolver uma vez no startup (como CE) e cachear; usar 0 se não existir.
- [ ] Revisar todos os `GetStatValue` / `GetStatValueAbstract` no projeto: garantir try-catch ou método `*Safe` e sanitização.

---

### 2.3 Pawns e Raças

**Riscos:** Androids, animais, raças customizadas sem `Humanlike`, sem `skills`, ou com traits/weapons que quebram expectativas.

| Ação | Implementação |
|------|----------------|
| **Humanlike** | Já: `!pawn.RaceProps.Humanlike` → NonCombatant. Manter e não assumir skills/traits. |
| **Skills null** | Já: `pawn.skills == null` → NonCombatant. `GetSkillLevelSafe` deve retornar 0 se skill null. |
| **WorkTagIsDisabled** | Já em try-catch (mods podem lançar). Manter. |
| **Equipment.Primary** | Já em try-catch. Manter. |
| **Filtro no patch** | Já: apenas `IsColonist`, não prisioneiro, não morto. Considerar: se raça não humanlike, não aplicar lógica de role (já indiretamente via NonCombatant). |
| **Android Tiers / mods de raça** | Detecção já existe (`IsAndroidTiersActive`). Não alterar comportamento por padrão; apenas garantir que null/skills/weapons não quebrem. Opcional: preset ou peso específico por “raça” no futuro. |

**Tarefas:**
- [ ] Documentar em comentário que `DetectRole` e `GetSkillLevelSafe` são o contrato para “qualquer pawn”; manter fallback NonCombatant e 0 em erros.
- [ ] Se algum mod expuser “este pawn não deve usar lógica de outfit”, considerar um contribuidor que retorna “skip” (fora do escopo atual do IApparelScoreContributor; pode ser extensão futura).

---

### 2.4 Apparel e Outfit (Mods de Roupa / AI)

**Riscos:** Mods que substituem ou complementam a AI de outfit; múltiplos patchando `ApparelScoreGain`; itens forçados; slots/layers customizados.

| Ação | Implementação |
|------|----------------|
| **Forced apparel** | Já: se item conflitante está em `forcedHandler`, não recomendar troca (NetGain = -1000, IsValid = false). |
| **Candidato já vestido** | Já: se `WornApparel.Contains(ap)`, retornar -1. Evita loop. |
| **Cooldown e histerese** | Já aumentados (cooldown 600 ticks, MinAbsoluteNetGain 0.18, hysteresis 1.25). Reduz oscilação com qualquer mod. |
| **externalOutfitController** | Opção para o jogador quando outro mod controla outfit. Documentar na descrição do mod (Steam/README). |
| **Layers/bodyPartGroups** | `HasApparelConflict` usa apenas defs; mods que adicionam layers/groups são compatíveis se as defs estiverem corretas. Não assumir tamanho fixo de listas. |

**Tarefas:**
- [ ] Testar com um mod popular de outfit (ex: “Outfitted”, “Best Apparel”) com `externalOutfitController` ativado; garantir que Vextex não altera nada.
- [ ] Opcional: detecção automática de mods conhecidos que substituem outfit AI (por packageId) e sugerir na UI “Parece que você usa X; ative ‘Outro mod controla outfit’?”.

---

### 2.5 Combat Extended (e Mods de Combate)

**Status:** Já existe `CombatExtendedCompat`: detecção, normalização Sharp/Blunt/Heat, Bulk/WornBulk, divisores configuráveis.

| Ação | Implementação |
|------|----------------|
| **Manter compat dedicada** | Manter CE em classe separada; não misturar com lógica vanilla. |
| **Outros mods de combate** | Se surgirem stats alternativos (ex: “ArmorRating_Pierce”), considerar lista de StatDef opcionais resolvidos no startup e somados ao score com peso configurável (fase 2). |

**Tarefas:**
- [ ] Nenhuma obrigatória para CE; apenas manter e testar com CE ativo.

---

### 2.5.1 DLCs: Royalty (Psycaster), Ideology, Biotech

| DLC | Implementação |
|-----|----------------|
| **Royalty** | `RoyaltyCompat.HasPsylink(pawn)` por reflexão; bónus de score para roupa com PsychicSensitivity / PsychicEntropyRecoveryRate (Eltex) para que Conde Psíquico não troque Eltex Robe por Duster de couro. |
| **Ideology** | `IdeologyCompat.GetApparelPreceptBonus`: bónus +5 para roupa preferida (evita -mood). |
| **Biotech** | `BiotechCompat.GetMapPollutionLevel`; score extra para ToxicResistance em mapa poluído. |

**Temperatura (Safety Check):** A fórmula `effectiveTempApprox = ambientTemp + (heatInsulation * 0.3f) + (coldInsulation * 0.3f)` é heurística. No vanilla o isolamento costuma ser ~1:1. O 0.3f evita trocas marginais excessivas; se houver feedback do tipo "colonista morreu de frio com parka no inventário", considerar subir o fator (ex.: 0.5f–1.0f). Ver comentário em `GetThermalSafetyPenalty`.

---

### 2.6 Expansão do Sistema de Detecção (ModCompat)

**Objetivo:** Ter uma lista única de mods “conhecidos” para log, UI e futuras regras (ex. presets ou desativar features).

| Mod (exemplos) | PackageId / detecção | Uso sugerido |
|-----------------|----------------------|--------------|
| Combat Extended | Já | Normalização armadura, bulk. |
| Vanilla Expanded (vários) | Já (Framework, Insectoids) | Só log/UI; materiais em MaterialEvaluator. |
| Dubs Bad Hygiene | Já | Só log; futuramente peso para “roupa adequada ao clima”. |
| Dubs Apparel Tweaks | Já | Pode alterar stats de roupa; garantir que GetStatValueSafe cobre. |
| Android Tiers | Já | Garantir que pawns não-humanlike não quebrem. |
| Rimworld of Magic | Já | Pawns custom; skills/traits seguros. |
| Save Our Ship 2 | Já | Só log. |
| Infused / Infused2 | Já | Stats dinâmicos; sanitização já ajuda. |
| **Outfitted** | Pesquisar packageId | Se patchar ApparelScoreGain, sugerir externalOutfitController. |
| **Best Apparel** | Pesquisar packageId | Idem. |
| **RimThreaded** | Assembly/ModsConfig | Evitar estado estático não thread-safe se RT ativo (avançado). |

**Tarefas:**
- [ ] Adicionar à lista `DetectedMods` e `BuildDetectedModsList` apenas mods já detectados; manter comentário no código com link para este plano.
- [ ] Opcional: ficheiro de configuração (XML/JSON) ou lista estática com packageIds e “sugestão” (ex. “suggestExternalOutfit”) para mostrar aviso na UI uma vez.

---

### 2.7 API Pública para Outros Mods

**Já existente:**
- `ScoreContributorRegistry.Register(IApparelScoreContributor)` / `Unregister`
- `ApparelScoreCalculator.TryCalculateScore(pawn, apparel, out score)`
- `ApparelScoreCalculator.GetScoreBreakdown(pawn, apparel, out ctx)`
- `VextexModHandler.Settings` (e `externalOutfitController`)

**Boas práticas para contribuidores:**
- Implementações de `IApparelScoreContributor` não devem lançar exceções; retornar 0 em erro.
- Registrar no startup (StaticConstructorOnStartup) para garantir ordem.

**Tarefas:**
- [ ] Documentar a API no README ou em `docs/API.md`: como registrar um contribuidor, como usar TryCalculateScore/GetScoreBreakdown.
- [ ] Opcional: exemplo mínimo (snippet ou mod de exemplo) de um mod que apenas registra um contribuidor.

---

### 2.8 Cache e Estado Global

**Riscos:** Cache por tick/pawn pode ficar desatualizado se outro mod alterar apparel/pawn no mesmo tick; ou conflitos com mods que alteram tick.

| Ação | Implementação |
|------|----------------|
| **Cache de score** | Por (tick, pawn, apparel). Tick diferente → limpar. Já feito. Manter `MaxCacheEntries` e limpeza por tick. |
| **Cooldown por pawn** | Dicionário por thingIDNumber; limpeza de entradas expiradas. Manter. |
| **Não cachear** resultados que dependem de estado mutável externo (ex. temperatura já é lida por avaliação). | OK atual. |

**Tarefas:**
- [ ] Nenhuma crítica; apenas não introduzir caches que assumam que “ninguém mais altera pawn/apparel”.

---

## 3. Plano de Tarefas por Prioridade

### Fase 1 – Estabilidade (evitar bugs com qualquer mod)
1. Revisar todos os acessos a Stat/Def/Pawn/Apparel e garantir try-catch ou método `*Safe` + sanitização.
2. Garantir que `externalOutfitController` desativa 100% da lógica de alteração de score no patch.
3. Manter e, se possível, exibir na UI o aviso quando outros mods patcham `ApparelScoreGain`.

### Fase 2 – Compatibilidade explícita
4. Centralizar resolução de StatDef opcionais (um lugar no startup) e usar em ApparelScoreCalculator/MaterialEvaluator.
5. Adicionar à detecção 1–2 mods de outfit (Outfitted, Best Apparel) e sugerir `externalOutfitController` na primeira execução ou nas configurações.
6. Documentar API (README ou docs/API.md) e exemplo de IApparelScoreContributor.

### Fase 3 – Melhorias opcionais
7. Lista configurável (ou estática) de “mods conhecidos” com sugestões (ex. “suggestExternalOutfit”) e aviso único na UI.
8. Testes manuais com load order pesado (50+ mods) e lista de “testados” no README.

---

## 4. Checklist de Validação por Release

Antes de cada release, verificar:

- [ ] Mod inicia sem erro com nenhum outro mod.
- [ ] Mod inicia sem erro com CE ativo; scores de armadura fazem sentido.
- [ ] Com “Outro mod controla outfit” ativado, nenhuma mudança de comportamento em relação ao vanilla.
- [ ] Colonos humanlike com skills normais: role Melee/Ranged/NonCombatant detectado; sem loop equipar/desequipar.
- [ ] Pawn não-humanlike (ou sem skills): não crasha; trata como NonCombatant.
- [ ] Apparel com stats NaN/Infinity ou Def faltando: score 0 ou fallback; sem crash.
- [ ] Log sem exceções não tratadas do Vextex; avisos apenas quando esperado (ex. CE sem WornBulk).

---

## 5. Resumo

O mod já segue boas práticas (soft dependency, fail-safe, cooldown, histerese, API de contribuidores). O plano reforça:

1. **Patches:** prioridade, opção “outro mod controla”, detecção de conflito.
2. **Stats/Defs:** sanitização e resolução segura em um só lugar.
3. **Pawns:** manter contratos defensivos em role/skills.
4. **Apparel/outfit:** forced, já vestido, cooldown; opção de desativar.
5. **Compat:** expandir detecção só para log/UI e sugestões; CE já dedicado.
6. **API:** documentar e, opcionalmente, exemplar.
7. **Testes:** checklist mínima por release.

Implementando as tarefas da Fase 1 e 2, o Vextex fica robusto para uso com **diversos mods** e com caminho claro para ajustes futuros (Fase 3).
