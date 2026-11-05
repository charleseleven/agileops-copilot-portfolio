# Backlog Automation — Documento/Prompt → Cards no Azure DevOps, Jira ou Trello

> **Como** Scrum Master / Product Owner, **quero** um sistema que crie os itens de trabalho (Epics,
> Features, User Stories e Tasks/Bugs) seguindo as boas práticas de mercado, a partir de um documento
> anexado ou de uma solicitação via prompt, **para que** eu automatize meu trabalho e repasse os itens
> ao time de desenvolvimento.

CLI .NET que lê um documento (`.md`, `.txt`, `.docx`, `.pdf`, `.json`) ou um prompt, monta o backlog
na hierarquia **Epic → Feature → User Story → Task/Bug**, valida as boas práticas e cria os cards na
ferramenta de destino, já vinculados ao pai e com as **estimativas informadas no documento** (story
points, horas, dias, tamanho camiseta ou qualquer outra medida).

Como no `gmud-automation/`, nada é escrito sem `--confirm-write`. O fluxo recomendado é:
prévia → revisão → criação.

```
documento / prompt ──► plan (prévia + validação) ──► backlog.json (revisão do PO) ──► create --confirm-write
```

## Como a entrada vira backlog

| Entrada | Estratégia (`--parser auto`) | Usa IA? |
|---|---|---|
| `.json` (saída de `plan --out`) | Lido direto | Não |
| `.md`/`.docx`/`.txt`/prompt **estruturado** (`Epic:`, `Feature:`, `User Story:`...) | Parser determinístico | Não |
| Texto livre (ata, e-mail, documento de requisitos, `.pdf`, prompt) | LLM com prompt de boas práticas | Sim |

Documentos Word são convertidos para Markdown com os títulos preservados ("Título 1" → `#`, inclusive no
Word em português) e as listas viram `- `. Assim, um documento de requisitos estruturado em Word não
precisa de IA.

### Formato estruturado (sem IA)

O tipo do item vem da palavra-chave, e não do nível do título. Palavras aceitas em pt-BR e en:
`Epic`/`Épico`, `Feature`/`Funcionalidade`, `User Story`/`US`/`História`/`PBI`, `Task`/`Tarefa`,
`Bug`/`Defeito`. O modelo completo sai em `dotnet run --project BacklogAutomation.Cli -- template`, e há
um exemplo em [`samples/backlog-estruturado.md`](./samples/backlog-estruturado.md).

```markdown
# Epic: Gestão de Pedidos Online
Tags: mvp, ecommerce
Prioridade: Alta
Valor de negócio: 80

## Feature: Carrinho de compras

### User Story: Adicionar item ao carrinho (5 SP)
Como cliente, quero adicionar produtos ao carrinho, para que eu possa comprá-los em um único pedido.

**Critérios de aceite:**
- Dado um produto com estoque, quando clico em "Adicionar", então o item aparece no carrinho

**Estimativas:**
- Pontos de função: 4

- Task: Criar endpoint POST /api/cart/items (6h)
- Task: Criar componente de carrinho no frontend (1 dia)

#### Bug: Total não atualiza ao remover item (2h)
Passos para reproduzir:
1. Adicionar dois produtos
2. Remover um deles
Resultado esperado: o total é recalculado
Resultado atual: o total continua somando o item removido
```

### Estimativas reconhecidas

| Escrito no documento | Vai para |
|---|---|
| `5 SP`, `8 pontos`, `13 story points`, `Story Points: 5`, `Esforço: 8` | Story Points |
| `4h`, `6 horas`, `2 horas e 30 min`, `Horas: 7` | Horas estimadas (Original Estimate) |
| `1 dia`, `1,5 dias` | Horas (× `--hours-per-day`, padrão 8) |
| `Restante: 3h` / `Concluído: 2h` | Remaining Work / Completed Work |
| `Tamanho: M` (PP/P/M/G/GG, XS…XL) | Tamanho camiseta |
| Qualquer outro `Rótulo: valor` (pontos de função, complexidade, WSJF...) | Medida livre: campo mapeado com `--field-map` |

No Azure DevOps, as estimativas vão **somente para campos do card**, nunca para a descrição. As horas
(de Epic, Feature, User Story, Task ou Bug) vão para o primeiro campo que existir no tipo: Original
Estimate; senão Effort (Epic, Feature, PBI e Bug no Scrum); numa Task sem Original Estimate, Remaining
Work. Uma medida sem campo correspondente gera um aviso na CLI, para ser mapeada com `--field-map` ou
adicionada ao processo.

Tasks e Bugs só são criados quando o documento ou o prompt os informa. Horas por disciplina dentro de uma
história (ex: "BE 11 · FE 6") são a estimativa da própria história e não viram Tasks.

### Geração com IA

O system prompt ([`AiBacklogGenerator`](./BacklogAutomation.Core/Ai/AiBacklogGenerator.cs)) segue estas
regras:

- User Stories seguem INVEST, usam a narrativa "Como/Quero/Para que" e têm critérios de aceite em Gherkin.
- Tasks e Bugs só são criados quando o documento os lista explicitamente; a IA nunca decompõe histórias
  por conta própria.
- Escopo não é inventado: lacunas viram "Pendência: ..." na descrição.
- As estimativas do documento são copiadas fielmente e nunca inventadas. Com `--suggest-estimates`, a IA
  pode sugerir as que faltam. Essas sugestões são marcadas no plano e recebem a tag `estimativa-sugerida-ia`.

## Como os cards são criados em cada ferramenta

| Nível | Azure DevOps | Jira Cloud | Trello |
|---|---|---|---|
| Epic | Epic | Tipo de nível 2 (ex: Initiative); sem ele, label `epic-<nome>` nos filhos | Card (label "Epic") |
| Feature | Feature | Epic (nível 1) | Card + anexo-link para o Epic |
| User Story | User Story / Product Backlog Item / Requirement / Issue (conforme o processo) | Story (nível 0) | Card `(5) Título` + anexo-link para a Feature |
| Task | Task | Sub-task (nível -1) | Item do checklist "Tasks" da US |
| Bug | Bug (Basic: Task com tag "Bug") | Sub-task com label `bug` | Item do checklist "Bugs" da US |
| Pontos | Story Points (Agile), Effort (Scrum/Basic), Size (CMMI) | `JIRA_STORY_POINTS_FIELD` | Prefixo `(n)` no nome do card |
| Horas | Original Estimate; sem ele, Effort (Epic/Feature/PBI/Bug) ou Remaining Work (Task) | Time tracking (`1h 30m`) | No nome do item de checklist |

- **Azure DevOps**: o processo (Agile, Scrum, CMMI ou Basic, inclusive herdados) é detectado pelos tipos
  de item do projeto, ou informado com `--process`. Antes de usar um campo, a CLI confere se ele existe no
  tipo. Critérios de aceite vão para o campo Acceptance Criteria, e passos de reprodução para Repro Steps.
  O vínculo pai/filho usa Hierarchy-Reverse. `Valor de negócio`/`Business Value` e `Criticidade`/`Time
  Criticality` usam os campos nativos.
- **Jira**: os tipos são escolhidos pelo `hierarchyLevel` do projeto, e não por nome fixo. Por isso
  funciona com Jira em português ("História", "Subtarefa"). Os campos disponíveis vêm do `createmeta` do
  tipo.
- Todo card criado recebe a tag `backlog-automation`, para filtrar ou auditar o lote.

## Comandos

| Comando | O que faz |
|---|---|
| `plan` | Gera o backlog, valida e mostra a prévia em árvore com totais. Somente leitura. Com `--out backlog.json`, salva o plano para revisão. |
| `create` | Faz o mesmo que `plan` e cria os cards no `--target` (exige `--confirm-write`). É bloqueado se a validação tiver erros. |
| `template` | Imprime o modelo de documento estruturado. |

A validação aponta **erros**, que bloqueiam o `create`: backlog vazio, título ausente ou com mais de 255
caracteres, estimativa negativa, prioridade inválida. Também aponta **avisos**, que não bloqueiam: história
fora do formato "Como/Quero/Para que" ou sem critérios de aceite; pontos fora de Fibonacci;
task com mais de 2 dias; item sem estimativa enquanto os irmãos têm.

Se uma criação falhar no meio do lote, a CLI para e lista o que já foi criado. Para retomar, remova esses
itens ou ajuste o `backlog.json` para conter só o que falta.

## Configuração

Copie [`.env.example`](./.env.example) para `.env` e preencha só o que for usar. Nenhum segredo fica no
código. O PAT do Azure DevOps segue o mesmo padrão do `mcp-tooling/`: `AZURE_DEVOPS_PAT_<ORG>` ou
`AZURE_DEVOPS_EXT_PAT`.

## Rodar

```bash
cd backlog-automation
dotnet test                                    # 90 testes (xUnit + Moq + FluentAssertions)

# 1. Prévia a partir de um documento estruturado (sem IA)
dotnet run --project BacklogAutomation.Cli -- plan --file samples/backlog-estruturado.md --out backlog.json

# 2. Prévia a partir de texto livre ou de um prompt (com IA)
dotnet run --project BacklogAutomation.Cli -- plan --file samples/requisitos-texto-livre.txt --out backlog.json
dotnet run --project BacklogAutomation.Cli -- plan --prompt "App de delivery: cadastro (5 pts), carrinho (8 pts), pagamento com Pix (13 pts)" --out backlog.json

# 3. Depois de revisar o backlog.json, criar os cards
dotnet run --project BacklogAutomation.Cli -- create --plan backlog.json --url "https://dev.azure.com/sua-org/Seu Projeto" \
  --iteration-path "Seu Projeto\Sprint 12" --confirm-write
dotnet run --project BacklogAutomation.Cli -- create --plan backlog.json --target jira --project LOJA --confirm-write
dotnet run --project BacklogAutomation.Cli -- create --plan backlog.json --target trello --board AbCd1234 --confirm-write

# Medida livre do documento → campo customizado
dotnet run --project BacklogAutomation.Cli -- create --plan backlog.json --target azure --org sua-org --project "Seu Projeto" \
  --field-map "Pontos de função=Custom.PontosDeFuncao" --confirm-write
```

Rode `dotnet run --project BacklogAutomation.Cli` sem argumentos para ver todas as opções.

## Estrutura

```
BacklogAutomation.Core/
  Models/          BacklogPlan (Epic → Feature → US → Task/Bug), Estimate, WorkItemCreationRequest
  Documents/       Extração de texto de .md/.txt/.json/.docx/.pdf (Word → Markdown com títulos)
  Parsing/         MarkdownBacklogParser (determinístico) e EstimateParser (pt-BR/en)
  Ai/              AiBacklogGenerator (prompt de boas práticas) + cliente OpenAI-compatível (Groq)
  Validation/      BacklogPlanValidator (erros bloqueantes e avisos de boas práticas)
  Services/        BacklogPlanBuilder (escolhe a estratégia) e BacklogCreationService (cria em profundidade)
  Providers/       IWorkItemTrackerProvider + AzureDevOps/, Jira/, Trello/
BacklogAutomation.Cli/    plan | create | template
BacklogAutomation.Tests/  Providers testados contra um HttpMessageHandler que grava cada requisição
```

Para adicionar outra ferramenta (ex: GitHub Projects, Linear, ClickUp), implemente
`IWorkItemTrackerProvider` e registre-a em `CreateProvider` no `Program.cs`.
