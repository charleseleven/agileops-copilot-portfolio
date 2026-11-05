# Templates de Entrega

## 1. Plano de escrita (antes de pedir confirmação)

```markdown
## Plano de escrita — GMUD #<id> (<GMUD|PROD>, cliente <sufixo>)

**Escopo:** <n> work items filhos — #<id> <título>, ...

| Repositório | Tipo | Features a mesclar | Catch-up GMUD | Branch de release | PR → <target> |
|---|---|---|---|---|---|
| X.API | Api | feature/us_123 | — | release/GMUD_GMUD_cli1_22102026 | criar + completar |
| X.Database | Database | feature/us_123 | n/a | n/a | criar como Draft (DBA) |

**Alertas:** <branch inexistente, repo fora do catálogo, filho não concluído...>

Etapas que vão escrever: publish-impact → release-branch → pull-requests.
**Confirma a escrita?** (responda "pode publicar" para seguir)
```

## 2. Relatório de execução

```markdown
## GMUD #<id> — <título> · <ambiente> · <data>

**Resultado:** ✅ Concluída | ⚠️ Parcial | ⛔ Interrompida em <etapa>

| Etapa | Status | Detalhe |
|---|---|---|
| Hierarquia | ✅ | <n> filhos |
| Impacto publicado | ✅ | <n> repositórios |
| Branch de release | ✅ | release/... (<n> merges) |
| PRs | ⚠️ | API #<n> completada (build <id>); Database #<n> aguardando DBA |
| Anexos/relatório (US 3.2) | ⏸ | Não automatizado ainda — manual |

**Pendências e responsáveis:** ...
**Próximos passos:** ...
```

## 3. Relatório de mudança no código

```markdown
## Mudança: <resumo em uma frase>
**Rastreio:** Epic #185 → <Feature/US> · **Tipo:** feat | fix | refactor | test | docs

**O que mudou**
- [arquivo](caminho) — <por quê>

**Testes:** `dotnet test` → <n> aprovados, <n> falhas · novos: <Classe.Metodo>
**Impacto:** comandos afetados, comportamento em produção, configuração nova
**Riscos / pendências:** ...
```

## 4. User Story de evolução

```markdown
### US <id>: <título>
**Como** <analista de release / DBA / dev>, **quero** <capacidade>, **para que** <benefício mensurável>.

**Regras:** RN-GMUD-xxx ...
**Fora de escopo:** ...

```gherkin
Funcionalidade: <nome>
  Cenário: Caminho feliz
    Dado ...
    Quando executo "<comando> --url <url> --confirm-write"
    Então ...
  Cenário: Sem --confirm-write
    Quando executo "<comando> --url <url>"
    Então nada é escrito no Azure DevOps
    E a saída orienta a usar --confirm-write
  Cenário: Dados vazios
    ...
```
**Teste de aceite:** GmudAutomation.Tests/<Classe>.<Metodo>
```

## 5. Checklist go/no-go (GMUD PROD)

- [ ] Todos os filhos em estado concluído/aprovado
- [ ] Toda `feature/us_<n>` existe
- [ ] GMUD (homologação) desta release executada e validada
- [ ] Nenhum conflito pendente na branch de release
- [ ] PRs de Database com DBA ciente
- [ ] Tag atual de PRD registrada (rollback)
- [ ] Janela de mudança aprovada
