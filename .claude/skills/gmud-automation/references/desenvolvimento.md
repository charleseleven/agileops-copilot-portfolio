# Guia de Desenvolvimento — gmud-automation

## 1. Arquitetura

```
GmudAutomation.Cli/Program.cs     → composição (new manual, sem DI container), parse de opções, gate de escrita, saída no console
GmudAutomation.Core/Client/       → acesso externo: AzureDevOpsClient (REST + PAT), AppServiceClient (ARM + token do az CLI)
GmudAutomation.Core/Models/       → records imutáveis (WorkItemInfo, TechnicalImpact, ReleaseReportData, ...)
GmudAutomation.Core/Services/     → regras de negócio, uma interface I<Nome> por serviço
GmudAutomation.Tests/             → espelha Client/ e Services/; xUnit + Moq + FluentAssertions
```

Fluxo automatizado (`run/status/report`):

```
GmudAutomation.Cli/GmudPipelineCommands.cs   → parse, credenciais, composição, saída, confirmação interativa
GmudAutomation.Core/Pipeline/
  IGmudStep.cs, GmudPipelineRunner.cs        → contrato executar(manifesto) → manifesto; retomada, dry-run, gates, log
  GmudStepRegistry.cs, GmudPipelineFactory.cs → chaves → etapas; ordem vem de gmud.config.json "steps"
  Configuration/ (GmudConfig, GmudNamingConventions) → config + ÚNICO módulo de nomes
  Manifest/ (GmudManifest, GmudManifestStore) → contrato JSON da especificação (chaves em português)
  Analysis/                                   → parsers puros (descrição, links vstfs, projetos, commits, conciliação)
  Steps/                                      → 11 etapas, uma classe cada
  Reporting/InfraReportFormatter.cs           → infra.md
GmudAutomation.Core/Client/AzureReposClient.cs → refs, commitsbatch, diffs, merges (assíncrono), pushes, builds
GmudAutomation.Tests/Pipeline/Fakes/          → Boards/Repos/App Services em memória (grafo de commits + merge 3 vias)
```

Nova etapa: classe `IGmudStep` em `Steps/` (declare `WritesRemote`/`SupportsDryRun`; se escreve, implemente
`DescribePendingWrites`), registre em `GmudPipelineFactory`, adicione a chave em `GmudStepKeys`/config e cubra
com teste de aceitação usando os fakes.

Princípio: **regra de negócio mora no Core**, testada isoladamente. `Program.cs` só orquestra, valida opções e imprime. Se uma regra estiver crescendo dentro de `Program.cs` (ex.: `CollectRepositoryBranchesAsync`, `TryGetRecentGmudCatchUpBranchAsync`), considere extraí-la para um serviço testável.

## 2. Padrões de código (siga o que já existe)

- Classes `public sealed class X : IX`; interface com `/// <summary>` em português citando US/Task (ex.: `US 3.2 (Task 206)`); implementação com `/// <inheritdoc cref="IX"/>`.
- Dependências via construtor com `?? throw new ArgumentNullException(nameof(...))`.
- Campos `private readonly _camelCase`.
- Métodos assíncronos com sufixo `Async` e `CancellationToken cancellationToken = default` como último parâmetro.
- Regex com `[GeneratedRegex]` em classe `partial`; `RegexOptions.IgnoreCase`.
- Coleções de retorno `IReadOnlyList<T>`; vazio com `Array.Empty<T>()`; comparações de nomes `StringComparer.OrdinalIgnoreCase`.
- Models como `sealed record` posicionais.
- Comentários explicam o **porquê** de negócio (ver os de `Program.cs`), não o óbvio.
- Mensagens ao usuário em português, prefixadas por `[<repositório>]` quando por repositório.
- Nada de valores reais de cliente no código — use variáveis de ambiente.

## 3. Padrões de teste

```csharp
public class XServiceTests
{
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly XService _sut;

    public XServiceTests() => _sut = new XService(_clientMock.Object);

    [Fact]
    public async Task Metodo_Cenario_ResultadoEsperado()
    {
        _clientMock.Setup(c => c.GetWorkItemAsync(123, It.IsAny<CancellationToken>())).ReturnsAsync(...);

        var result = await _sut.MetodoAsync(123);

        result.Should()....;
        _clientMock.Verify(c => c.AddWorkItemCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- Clients HTTP são testados com `HttpMessageHandler` falso (ver `Client/AzureDevOpsClientTests.cs`).
- `[Theory]` + `[InlineData]` para variações de formato (tags, URLs, nomes).
- Para defeitos vindos de dados reais: copie o trecho (HTML/comentário) **anonimizado** para o teste.

## 4. Receita: ligar um serviço pronto a um novo comando

Exemplo-alvo atual: **US 3.2** (`AttachmentRenamingService` + `ReleaseReportPublisher`).

1. Defina o contrato do comando (nome, opções, se escreve) e os critérios de aceite em Gherkin.
2. Se a orquestração tiver lógica (ex.: decidir quais anexos de quais Tasks), crie um serviço `I<Nome>OrchestrationService` no Core com testes — não coloque essa lógica no `switch`.
3. Em `Program.cs`:
   - instancie o serviço junto aos demais;
   - adicione `case "<comando>":` com `if (!options.ConfirmedWrite) return RequireConfirmWrite();` se escrever;
   - novas opções: propriedade em `CliOptions` + `case "--opcao" when i + 1 < args.Length:` no `Parse`;
   - documente em `PrintUsage()`.
4. Atualize `README.md` do módulo (tabela de comandos; remova da seção "prontas mas não ligadas").
5. Atualize o backlog em `references/processo-gmud.md`.
6. `dotnet build` e `dotnet test` verdes.

## 5. Definition of Done

- [ ] Critérios de aceite escritos e cobertos por testes
- [ ] Suíte completa verde (`dotnet test`), número de testes informado
- [ ] Gate `--confirm-write` em todo caminho que escreve, com teste/verificação de que nada é escrito sem ele
- [ ] Mensagens de erro acionáveis (dizem o que fazer)
- [ ] Sem segredos ou dados reais de cliente no diff (`git diff` revisado)
- [ ] README, `PrintUsage()` e referências da skill atualizados
- [ ] Commit no padrão do repo: `feat(gmud-automation): ...`, `fix(gmud-automation): ...`, `test(...)`, `docs(...)` — só commitar quando o usuário pedir

## 6. Checklist de revisão de código

| Área | Verificar |
|---|---|
| Segurança | PAT/subscription nunca logados; dados reais fora do Git; escrita protegida |
| Correção | Case-insensitive em nomes; deduplicação; ordem de merge determinística |
| Resiliência | `HttpRequestException` tratada; falha em um repositório não deixa estado ambíguo sem relatar |
| Idempotência | Reexecutar não duplica comentários/branches/PRs sem aviso (gap conhecido — avaliar) |
| Testabilidade | Lógica nova no Core, atrás de interface, com testes |
| Coerência | Comportamento = README = `PrintUsage()` = `processo-gmud.md` |

## 7. Dívidas/oportunidades conhecidas

- US 3.2 não ligada à CLI (anexos + relatório de release).
- Lógica de coleta de branches e catch-up em `Program.cs` (fora do Core, sem teste direto).
- ~~Sem comando único ponta a ponta com checkpoints; sem `--dry-run`~~ → resolvido pelo fluxo automatizado (`run`).
- ~~`release-branch` (US 2.1) usa a tag do App Service inteira como nome de branch de origem~~ → resolvido em 2026-11-05: decompõe a tag com `GmudNamingConventions.TryParseContainerTag` + `SourceBranchFromRelease` (inclui `main_<cliente>` e o catch-up).
- `MergeBranchesAsync` (US 2.1) envia nomes de branch como `parents` e não move a ref após o merge; o fluxo automatizado usa `AzureReposClient.MergeCommitsAsync` + `UpdateBranchAsync` (SHAs). Verificar com o usuário e corrigir a US 2.1.
- Reexecução: verificar comportamento quando branch de release/PR já existe.
- `pull-requests` usa `featureBranches[0]` para Database — múltiplas features em Database não são tratadas.
