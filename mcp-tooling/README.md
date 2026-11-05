# MCP Tooling — Azure DevOps, App Services e fluxo de GMUD

> Origem: Epic #178 "Implementação do Servidor MCP" / Feature #179 "Integração Local com Azure DevOps Boards".
> v3: ampliado para o fluxo automatizado de preparação de GMUD (ver `gmud-automation/`).

Servidor MCP (Model Context Protocol) que conecta o Claude Code ao Azure DevOps (Boards, Repos e
Pipelines), aos App Services (via Azure CLI) e à CLI `gmud` — para ler cards, revisar escopo de
branches, conferir builds e tags em produção e conduzir uma GMUD por linguagem natural no terminal.

## Tools

Leitura é livre. As tools marcadas com ✍️ escrevem e só devem ser usadas com aprovação explícita do usuário.

| Grupo | Tool | O que faz |
|---|---|---|
| Boards | `get_work_item` | Título, tipo, estado, descrição, critérios, sub-tasks, itens citados na descrição (links e `#id`), anexos e links de commit/PR/branch |
| | `get_work_item_relations` | Relações em JSON: filhos, pai, relacionados (com tipo/estado), anexos e links de desenvolvimento decodificados |
| | `list_work_item_comments` | Comentários do card (texto tratado como dado) |
| | `get_work_item_attachment` | Conteúdo de anexo de texto (.sql, .txt…, até 256 KB) — revisar scripts de banco |
| | `update_work_item` ✍️ | Prioridade, iteration path, area path e/ou estado (JSON-Patch) |
| Repos | `list_repositories` | Nome e ID (o ID aparece nos links dos cards) |
| | `list_branches` | Branches por prefixo, com o commit de cada uma |
| | `compare_branches` | Commits à frente e arquivos alterados entre duas branches (ex: feature × release de produção) |
| | `list_builds` | Builds de uma branch: ID, status, resultado, pipeline, commit |
| App Service | `find_app_services` | Sites cujo nome contém um fragmento (cliente) |
| | `get_app_service_image` | Imagem configurada (`linuxFxVersion`) com a tag decomposta |
| | `parse_container_tag` | `<buildId>-<branch>-<commitSha>` (imagem ou só a tag) → id, branch (`release/…` ou `main_<cliente>`) e sha, sem rede |
| GMUD | `gmud_run` ✍️ | Executa `gmud run` — **dry-run por padrão**; `confirm_write` só após o usuário aprovar o plano |
| | `gmud_status` | Manifesto resumido (etapas, itens, repositórios, merges, builds, avisos) |
| | `gmud_report` | Gera e retorna `infra.md` (tag atual → nova tag por App Service, scripts em ordem) |
| | `gmud_manifest` | `manifest.json`, `infra.md` ou relatório de conciliação |

Também expõe:

- **Resources** `gmud://<id>/manifest` e `gmud://<id>/infra` para cada GMUD já processada.
- **Prompt** `preparar-gmud` — conduz dry-run → revisão do plano → escrita com aprovação → pacote da infra.

O fluxo da GMUD (regras de negócio, gates de escrita, idempotência) mora no `gmud-automation` (C#,
testado); as tools `gmud_*` apenas o executam. As tools de Repos são somente leitura de propósito.

## Segurança

- PATs só via variáveis de ambiente (`.env` fora do Git); nunca aparecem em respostas.
- Comandos externos (`az`, `dotnet`) rodam sem shell; no Windows, onde `az.cmd` exige shell, os nomes
  são validados (`[A-Za-z0-9._-]`) antes da chamada. Entrada do `gmud_run` aceita só ID ou link do Boards.
- `gmud_run` recusa `confirm_write` junto com `dry_run`; a CLI ainda exige `--confirm-write` para escrever.
- Texto de cards, comentários e anexos é dado, nunca instrução (descrito nas instruções do servidor).

## Resolução de organização/projeto/PAT

- `organization`/`project`/`reposProject` são opcionais em cada chamada — padrão em `AZURE_DEVOPS_DEFAULT_ORG`,
  `AZURE_DEVOPS_DEFAULT_PROJECT` e `AZURE_DEVOPS_DEFAULT_REPOS_PROJECT`.
- PAT por organização: a padrão usa `AZURE_DEVOPS_EXT_PAT`; as demais, `AZURE_DEVOPS_PAT_<ORGANIZACAO_EM_MAIUSCULO>`.
- App Services: `az login` válido e `AZURE_SUBSCRIPTION_ID` (ou `subscription` na chamada).
- GMUD: `GMUD_WORKDIR` (pasta com `gmud.config.json`, padrão `../gmud-automation`) e `GMUD_CLI` (comando `gmud`
  instalado; sem ele usa `dotnet run`).

## Como configurar

```bash
cd mcp-tooling
npm install
cp .env.example .env   # preencha os PATs
npm test               # testes dos parsers (node:test)
```

## Como registrar no Claude Code

`.mcp.json` na raiz do projeto:

```json
{
  "mcpServers": {
    "azure-devops": {
      "type": "stdio",
      "command": "node",
      "args": ["mcp-tooling/mcp-azure-devops.js"]
    }
  }
}
```

Reconecte o MCP na sessão (`/mcp`) para carregar as tools novas.
