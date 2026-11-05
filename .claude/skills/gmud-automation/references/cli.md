# CLI — Comandos, Configuração e Erros Conhecidos

Todos os comandos rodam a partir de `gmud-automation/`:

```powershell
dotnet run --project GmudAutomation.Cli -- <comando> --url "<url-do-work-item>" [opções]
```

## 0. Fluxo automatizado (`run`/`status`/`report`)

Configurado por `gmud.config.json` (ou `--config`/`GMUD_CONFIG_PATH`), independente das opções da seção 1.

```powershell
dotnet run --project GmudAutomation.Cli -- run <link|id> --dry-run         # 1 a 9, sem escrita, mostra o plano
dotnet run --project GmudAutomation.Cli -- run <link|id> --confirm-write   # após o "ok" do usuário
dotnet run --project GmudAutomation.Cli -- run <id> --from-step 6
dotnet run --project GmudAutomation.Cli -- status <id>
dotnet run --project GmudAutomation.Cli -- report <id>                     # gmuds/<id>/infra.md
```

Via MCP: `gmud_run` (dry-run por padrão), `gmud_status`, `gmud_report`, `gmud_manifest`.

| Saída | Significado | Ação |
|---|---|---|
| `0` | Concluído / dry-run concluído | Apresentar plano ou `infra.md` |
| `1` | Falha (título fora do padrão, HTTP, release recusada, build falhou) | Ler a mensagem da etapa; diagnosticar |
| `2` | Aguardando decisão do operador | App Service candidato → `appServiceOverrides` + `--from-step 6`; conflito ambíguo → `"decisao"` no manifesto + `--from-step 9`; branch de US ausente → dev cria + `--from-step 8`; build pendente → `--from-step 11` |
| `3` | Confirmação de escrita necessária | Esperado sem `--confirm-write`; pedir o "ok" do usuário |

Credenciais: `AZURE_DEVOPS_PAT_<ORG>` (fallback `AZURE_DEVOPS_EXT_PAT`), `AZURE_SUBSCRIPTION_ID`, `az login`.

## 1. Comandos das Features 1 a 3

| Comando | Escreve? | Opções obrigatórias além da URL | Uso |
|---|---|---|---|
| `hierarchy` | Não | — | Lista US principal e filhos |
| `impact` | Não | — | Repositórios × branches × existência da branch |
| `publish-impact` | Sim | `--confirm-write` | Publica o relatório de impacto como comentário |
| `run-impact` | Sim | `--confirm-write` | Feature 1 completa (hierarquia + impacto publicado) — era `run` |
| `release-branch` | Sim | `--confirm-write --ambiente <gmud\|prd> --cliente <sufixo>` (+ subscription) | Cria `release/GMUD_...` e mescla features |
| `pull-requests` | Sim | `--confirm-write --ambiente <gmud\|prd> --cliente <sufixo>` | Cria/completa PRs |
| `add-comment` | Sim | `--confirm-write --text "..."` | Apenas dados de teste |

Opções gerais: `--work-item <id> --org <org> --project <proj>` (alternativa à URL), `--repos-project <proj>`, `--target <branch>` (padrão `main`), `--subscription-id <id>`.

## 2. Sequência padrão de uma GMUD

```powershell
$url = "<url>"
# Fase 1 — leitura (livre)
dotnet run --project GmudAutomation.Cli -- hierarchy --url $url
dotnet run --project GmudAutomation.Cli -- impact    --url $url
# Fase 2 — escrita (somente após confirmação do usuário)
dotnet run --project GmudAutomation.Cli -- publish-impact --url $url --confirm-write
dotnet run --project GmudAutomation.Cli -- release-branch --url $url --ambiente gmud --cliente <sufixo> --confirm-write
dotnet run --project GmudAutomation.Cli -- pull-requests  --url $url --ambiente gmud --cliente <sufixo> --confirm-write
```

Gatilho do usuário → ambiente: `GMUD GMUD:` → `--ambiente gmud`; `GMUD PROD:` → `--ambiente prd`.

## 3. Variáveis de ambiente

| Variável | Para quê | Observação |
|---|---|---|
| `AZURE_DEVOPS_EXT_PAT` | PAT da organização sandbox padrão (`CliOptions.DefaultOrganization`) | |
| `AZURE_DEVOPS_PAT_<ORG>` | PAT de qualquer outra organização | `<ORG>` = nome da org em maiúsculas, não alfanuméricos → `_` |
| `GMUD_KNOWN_ORGANIZATION` | Organização "conhecida" do processo real | Ativa o `--repos-project` padrão abaixo |
| `GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT` | Team Project de Repos/Pipelines quando difere do de Boards | |
| `GMUD_REPOSITORY_CATALOG_PATH` | Caminho do catálogo real (fora do Git) | Sem ela, usa o catálogo fictício de exemplo |
| `AZURE_SUBSCRIPTION_ID` | Subscription dos App Services | Necessária para `release-branch` |

Carregamento: costumam estar no `.env` local (mesmo do `mcp-tooling/`). Para carregar numa sessão PowerShell **sem exibir valores**:

```powershell
Get-Content <caminho>\.env | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
    $k, $v = $_ -split '=', 2; [Environment]::SetEnvironmentVariable($k.Trim(), $v.Trim().Trim('"'), 'Process') }
```

Também é preciso `az login` válido (o token do App Service vem de `AzureCliAccessTokenProvider`).

## 4. Formato do catálogo

```json
[
  { "name": "Org.Produto.API", "kind": "Api", "siteNameTemplate": "app-produto-api-{ambiente}-{cliente}", "tag": "API" },
  { "name": "Org.Jobs",        "kind": "Backend", "fixedSiteName": "app-jobs-prd-01" },
  { "name": "Org.Database",    "kind": "Database", "tag": "Database" }
]
```

## 5. Erros conhecidos

| Sintoma (saída) | Causa provável | Ação |
|---|---|---|
| `PAT não configurado. Defina a variável de ambiente X` | Variável do PAT ausente para aquela org | Carregar `.env`; conferir nome sanitizado da org |
| `Uso: dotnet run -- ...` | URL inválida ou sem ID | Conferir formato `https://dev.azure.com/<org>/<proj>/_workitems/edit/<id>` |
| `Esse comando ESCREVE no Azure Boards...` | Faltou `--confirm-write` | Esperado — só adicionar após confirmação |
| `Work items filhos (0)` | Descrição da US sem links `/_workitems/edit/` | Problema de dado no board; orientar o usuário |
| `Nenhum projeto/branch identificado...` | Comentários sem tags reconhecíveis ou catálogo de exemplo em uso | Conferir `GMUD_REPOSITORY_CATALOG_PATH` e o texto dos comentários |
| Impacto com branch "não existe" | `feature/us_<id>` não criada ou com outro nome | Confirmar com o dev; se o padrão de nome mudou → Corrigir Automação |
| `Subscription do Azure não configurada` | `AZURE_SUBSCRIPTION_ID` ausente | Definir ou passar `--subscription-id` |
| `Sem Azure App Service correspondente` | Repositório `Database` ou sem template | Esperado para Database; para outros, ajustar catálogo |
| `Repositório não catalogado — pulei a criação de PR` | Nome do repo não está no catálogo | Adicionar ao catálogo local |
| `Configuração não encontrada em "gmud.config.json"` | Fluxo automatizado sem config | Copiar `gmud.config.example.json` e preencher (fora do Git) |
| `não contém a data da GMUD no formato DD-MM-AAAA` | Título da GMUD fora do padrão | Dado do board: corrigir o título |
| `Nenhum repositório foi identificado nas tasks/bugs` | Cards sem nome/alias/link/anexo reconhecível | Conferir `aliases` no config ou o texto dos cards |
| `App Service "..." (prd) não encontrado. Candidatos: ...` | Nome fora do padrão | Operador escolhe; `appServiceOverrides` |
| `informa alteração no banco, mas feature/us_<US> não existe` | Card cita o Database (procedure) e o dev não criou a branch | Dev cria a branch; `--from-step 6` |
| `o anexo "..." não segue a nomenclatura "US<US>_SCRIPT_TASK<item>.txt"` | Script anexado com outro nome (ou .txt que não é script) | Conferir o anexo; renomear no card se for script |
| `não tem "appService" no gmud.config.json` | Repositório de código sem App Service (só o Database pode) | Configurar `appService` do repositório |
| `traz N commit(s) fora da user story` | Feature saiu de develop/outra base | Esperado: etapa 9 concilia; revisar relatório |
| `... já existe em X, diferente de ... — não é sobrescrita` | Release com esse nome já publicada | Confirmar com o usuário (GMUD já implantada? data errada no título?) |
| `A etapa N ... ainda não foi executada de verdade` | `--from-step` pulando etapa só simulada | Rodar a partir da etapa indicada |
| `CONFLITO DE MERGE — resolução manual necessária` | Features conflitantes | PARAR; reportar arquivos/branches; usuário decide |
| `Falha ao comunicar com o Azure DevOps: 401/403` | PAT expirado ou sem escopo (Work Items, Code R/W, Build R) | Renovar PAT/escopos |
| `... 404` | Projeto de repos errado | Conferir `--repos-project` / `GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT` |
| Falha ao ler tag do App Service | `az login` expirado ou subscription errada | `az login`; `az account show` |
| Build falha com arquivo bloqueado (MSB3027) | Processo `GmudAutomation.Cli` ainda rodando | Encerrar o processo |

Novo erro descoberto? Adicione uma linha aqui.
