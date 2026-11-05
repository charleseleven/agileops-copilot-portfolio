# GMUD Automation — Orquestração de Release End-to-End

> Origem: Epic #185 "Automação e Orquestração do Processo de GMUD".

Automação de ponta a ponta do processo de GMUD (Gestão de Mudança) integrando Azure Boards e
Azure Repos: varre a User Story principal, extrai o escopo (Work Items filhos), identifica
impacto técnico (quais repositórios/branches foram alterados), orquestra a criação da branch de
release com merge das features, e abre as Pull Requests (completando automaticamente API/WEB,
mantendo Database em aprovação manual do DBA).

CLI .NET — sem interface web; roda via terminal, com um gate `--confirm-write` em todo comando que
altera algo no Azure DevOps.

## Fluxo automatizado de preparação de GMUD (`gmud run`)

A partir do link de uma GMUD, executa em 11 etapas todo o preparo de branches e builds e entrega à
infra a tag correta de cada App Service. Cada etapa lê e grava `gmuds/<id>/manifest.json`, é
idempotente e pode ser reexecutada isoladamente.

```bash
gmud run <link|id> --dry-run        # etapas 1 a 9 sem escrever no Azure; mostra o plano
gmud run <link|id> --confirm-write  # executa de verdade (pede "s" no terminal interativo antes de 7 e de 10)
gmud run <id> --from-step 6         # reexecuta a partir de uma etapa
gmud status <id>                    # manifesto resumido
gmud report <id>                    # gera gmuds/<id>/infra.md
```

| # | Etapa (chave) | Escreve? | O que faz |
|---|---|---|---|
| 1 | `resolver-gmud` | — | Valida o item na organização e extrai data (DD-MM-AAAA → DDMMAAAA) e cliente do título |
| 2 | `mapear-itens-descricao` | — | Links `_workitems/edit/<id>` e `#<id>` da Descrição → user stories (e Bugs) |
| 3 | `mapear-tasks-bugs` | — | Tasks/Bugs ligados por Child ou Related |
| 4 | `mapear-projetos-scripts` | — | Projetos por texto/alias, links de commit/PR/branch; baixa scripts `.sql`/`.txt` |
| 5 | `armazenar-informacoes` | — | Consolida os repositórios afetados no manifesto |
| 6 | `buscar-tag-vigente` | — | Imagem dos App Services prd/gmud → buildId, release, commitSha; lista candidatos se o nome não existir |
| 7 | `criar-branch-gmud` | ✍️ | `feature/us_<GMUD>` a partir do commit em produção, só nos repositórios afetados |
| 8 | `merge-user-stories` | ✍️ | Merge de `feature/us_<US>` na ordem da Descrição (repositórios de código; o Database usa só a branch do desenvolvedor) |
| 9 | `corrigir-conflitos` | ✍️ | Conciliação: entram só os arquivos dos commits da US; ambíguos param para aprovação |
| 10 | `criar-releases` | ✍️ | `release/GMUD_PROD_<CLIENTE>_<DATA>` e `release/GMUD_GMUD_...` (nunca sobrescreve) |
| 11 | `acompanhar-build` | — | Espera o build de cada release, grava buildId e a nova tag, gera `infra.md` |

Códigos de saída: `0` ok · `1` falha · `2` aguardando decisão do operador · `3` confirmação de escrita necessária.

**Conciliação (etapa 9).** A base é a release de produção. Um commit pertence à user story quando está
vinculado a um card dela ou cita a US/item na mensagem (`#20611`, `us_20611`, `Task 20612`). Arquivos
tocados só por commits da US e intocados na produção entram com a versão da feature; arquivos só de
commits fora do escopo não são levados; o resto é ambíguo e gera relatório em
`gmuds/<id>/conciliacao/`. O operador decide editando `"decisao"` (`feature` | `release` | `manual`)
no conflito do `manifest.json` e roda `--from-step 9`. Ao final, o diff contra a produção é conferido:
só pode conter arquivos das user stories.

**Configuração.** Copie `gmud.config.example.json` para `gmud.config.json` (ignorado pelo Git) ou aponte
`--config`/`GMUD_CONFIG_PATH`. Repositórios, aliases, padrões de App Service, clientes (nome no
título → container → App Service, com `appServiceOverrides` para sites fora do padrão), padrões de nome
e a ordem das etapas ficam lá. Credenciais: `AZURE_DEVOPS_PAT_<ORG>` (ou `AZURE_DEVOPS_EXT_PAT`) com
escopo Work Items leitura, Code leitura/escrita e Build leitura; `AZURE_SUBSCRIPTION_ID` + `az login`
para leitura dos App Services.

**Nova etapa.** Implemente `IGmudStep` (`executar(manifesto) → manifesto`), registre em
`GmudPipelineFactory` e inclua a chave em `steps` do `gmud.config.json` — as demais etapas não mudam.

**Instalar como ferramenta (`gmud`).**

```bash
dotnet pack GmudAutomation.Cli -c Release
dotnet tool install --global --add-source GmudAutomation.Cli/nupkg GmudAutomation.Cli
```

O MCP do `mcp-tooling/` expõe o fluxo como as tools `gmud_run` (dry-run por padrão), `gmud_status`,
`gmud_report` e `gmud_manifest`.

## Comandos das Features 1 a 3

| Comando | Feature/US | O que faz |
|---|---|---|
| `hierarchy` | 1.1 | Mapeia a hierarquia da GMUD (US principal → Tasks/Bugs filhos) — somente leitura |
| `impact` | 1.2 | Identifica impacto técnico (repositórios/branches afetados) via comentários — somente leitura |
| `publish-impact` | 1.2 | Publica o relatório de impacto como comentário no card |
| `run-impact` | Feature 1 completa | Hierarquia + impacto, ponta a ponta (era `run` antes do fluxo automatizado) |
| `release-branch` | 2.1 | Cria a branch de release a partir da tag em produção e faz merge sequencial das features; interrompe com log legível se houver conflito |
| `pull-requests` | 3.1 | Cria/orquestra as PRs: API/WEB completadas automaticamente (se as políticas do Azure permitirem); Database fica em Draft/Ativa para aprovação do DBA |

## Capacidades prontas mas ainda não ligadas a um comando (Feature 3 / US 3.2)

- `AttachmentRenamingService` — baixa, renomeia (`US<US>_SCRIPT_TASK<Task>`) e reanexa os arquivos da GMUD.
- `ReleaseReportFormatter` / `ReleaseReportPublisher` — gera e publica o relatório consolidado de
  release (branches, PRs, tags) como comentário na Task principal da GMUD.

Ambos têm cobertura de testes completa em `GmudAutomation.Tests`; faltam apenas o(s) comando(s) de
CLI que os exponham — ver `GmudAutomation.Cli/Program.cs` para o padrão usado pelos demais comandos.

## Configuração

O catálogo de repositórios (`repository-catalog.example.json`, fictício) descreve, por repositório:
nome, tipo (`Api`/`Web`/`Backend`/`Database`), padrão do nome do site no App Service e a tag usada
para reconhecer menções informais nos comentários (`(WEB)`, `API:`, etc.). Para uso real, aponte a
variável de ambiente `GMUD_REPOSITORY_CATALOG_PATH` para o seu próprio catálogo (fora do controle
de versão) e configure `GMUD_KNOWN_ORGANIZATION`, `GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT` e
`AZURE_SUBSCRIPTION_ID` (mesmo `.env` usado pelo `mcp-tooling/`, se os dois forem usados juntos).

## Rodar

```bash
cd gmud-automation
dotnet test        # 217 testes (xUnit + Moq + FluentAssertions), incluindo os cenários BDD 1 a 10 do fluxo
dotnet run --project GmudAutomation.Cli -- hierarchy --work-item <id>
```
