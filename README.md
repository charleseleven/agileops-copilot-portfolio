# 🤖 AgileOps Copilot

> Copiloto de IA para as operações ágeis do dia a dia de **Scrum Master**, **Product Owner** e
> **Systems Analyst**. Cobre a geração de backlog a partir de documentos, a gestão de Work Items no
> Azure DevOps, a orquestração de releases (GMUD) e o diagnóstico preditivo de gargalos de fluxo.

Extraído e reorganizado a partir do portfólio [`applied-ai-engineering`](../applied-ai-engineering).
Reúne, sob uma identidade própria, capacidades já desenvolvidas e validadas.

## As capacidades

| | Origem (Azure DevOps) | O que é | Como rodar |
|---|---|---|---|
| 🧱 [`backlog-automation/`](./backlog-automation) | — | CLI .NET que transforma um documento (`.md`, `.docx`, `.pdf`, `.txt`) ou um prompt em backlog **Epic → Feature → User Story → Task/Bug**, com as estimativas do documento (story points, horas, dias...). Cria os cards no **Azure DevOps, Jira ou Trello**. | `cd backlog-automation && dotnet test` — ver [README](./backlog-automation/README.md) |
| 🔌 [`mcp-tooling/`](./mcp-tooling) | Epic #178 | Servidor MCP que conecta um assistente de IA (Claude Code) ao Azure DevOps para ler e atualizar Work Items por linguagem natural no terminal. | `cd mcp-tooling && npm install` — ver [README](./mcp-tooling/README.md) |
| 🔀 [`gmud-automation/`](./gmud-automation) | Epic #185 | CLI .NET que automatiza o processo de GMUD ponta a ponta: escopo, impacto técnico, branch de release, merge e Pull Requests. | `cd gmud-automation && dotnet test` — ver [README](./gmud-automation/README.md) |
| 📊 [`flow-diagnostics-app/`](./flow-diagnostics-app) | Epic #209 | Web app (API .NET + Nuxt/Vue, 100% Tailwind) com diagnóstico preditivo de Cycle Time, gargalos e insights de IA. | Ver seção abaixo |

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) para `backlog-automation/`, `gmud-automation/` e o
  backend do `flow-diagnostics-app/`
- Node.js 20+ para o `mcp-tooling/`, e Node.js 20+ com [pnpm](https://pnpm.io) para o frontend do
  `flow-diagnostics-app/`
- Um PAT do Azure DevOps (escopo Work Items) e, para os recursos de IA, uma chave de API compatível com
  OpenAI (padrão: [Groq](https://console.groq.com))

## Do documento ao backlog em 3 passos

```bash
cd backlog-automation

# 1. Prévia: gera o backlog e valida as boas práticas (somente leitura)
dotnet run --project BacklogAutomation.Cli -- plan --file requisitos.docx --out backlog.json

# 2. Revise/ajuste o backlog.json

# 3. Cria os cards já na hierarquia, com story points e horas
dotnet run --project BacklogAutomation.Cli -- create --plan backlog.json \
  --url "https://dev.azure.com/sua-org/Seu Projeto" --confirm-write
```

Documentos já estruturados (`Epic:`, `Feature:`, `User Story:`...) são lidos sem IA. Texto livre e
prompts passam pela LLM, que segue um prompt de boas práticas: INVEST, "Como/Quero/Para que" e critérios
em Gherkin. Detalhes no [README do módulo](./backlog-automation/README.md).

## Rodando o Flow Diagnostics (o único com interface web)

```bash
# Backend
cd flow-diagnostics-app/backend
dotnet user-secrets set "LlmSettings:ApiKey" "<sua-chave-groq>"
dotnet user-secrets set "AzureDevOpsSettings:PersonalAccessToken" "<seu-pat>"
dotnet user-secrets set "AzureDevOpsSettings:Organization" "<sua-organizacao>"
dotnet user-secrets set "AzureDevOpsSettings:Project" "<seu-projeto>"
dotnet run    # http://localhost:5250 (Scalar em /scalar/v1)

# Frontend (outro terminal)
cd flow-diagnostics-app/frontend
pnpm install
pnpm run dev  # http://localhost:3000
```

Abra `http://localhost:3000` para ver a landing e clique em "Experimentar agora" para abrir o Flow
Diagnostics funcional.

## Por que isso importa

O projeto cobre, de ponta a ponta, as frentes do trabalho de gestão ágil aumentada por IA:

- **Refinamento de backlog** (Backlog Automation)
- **Automação de ferramentas** (MCP)
- **Automação de processo** (GMUD/release management)
- **Inteligência preditiva** (Flow Diagnostics)

Todas aplicam ao dia a dia de Scrum Master, Product Owner e Systems Analyst o mesmo rigor de engenharia
de software: testes automatizados, isolamento de dependências e configuração via secrets, nunca
hardcoded.

## Nota sobre segredos

Nenhum PAT, chave de API ou dado de cliente real está neste repositório. Cada subprojeto usa seu
próprio mecanismo de configuração local, descrito no README de cada um:

- `.env` no `mcp-tooling/` e no `backlog-automation/`
- `dotnet user-secrets` no `flow-diagnostics-app/backend/`
- catálogo de repositórios via variável de ambiente no `gmud-automation/`
