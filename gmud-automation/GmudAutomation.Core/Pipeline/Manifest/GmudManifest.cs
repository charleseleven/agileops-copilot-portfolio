using System.Text.Json.Serialization;

namespace GmudAutomation.Core.Pipeline.Manifest;

/// <summary>
/// Manifesto da GMUD (<c>gmuds/&lt;id&gt;/manifest.json</c>): fonte única para retomar o fluxo e gerar o resumo
/// da infra. Cada etapa lê e grava aqui; as chaves JSON seguem o contrato da especificação (em português).
/// </summary>
/// <remarks>Classes mutáveis de propósito: as etapas enriquecem o mesmo manifesto e o runner o persiste após cada etapa.</remarks>
public sealed class GmudManifest
{
    [JsonPropertyName("gmud")] public GmudHeader Gmud { get; set; } = new();

    [JsonPropertyName("userStories")] public List<UserStoryEntry> UserStories { get; set; } = [];

    /// <summary>Ambiente ("prd"/"gmud") → chave do repositório ("web", "api"...) → App Service.</summary>
    [JsonPropertyName("ambientes")] public Dictionary<string, Dictionary<string, AppServiceEntry>> Environments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Repositórios de código (com container): recebem branch da GMUD, merges, releases e build.</summary>
    [JsonPropertyName("repositorios")] public Dictionary<string, RepositoryEntry> Repositories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Banco de dados: para cada user story, a <c>feature/us_&lt;US&gt;</c> criada pelo desenvolvedor (com link e commits)
    /// e os scripts anexados às tasks — a automação não cria branch, merge nem release no Database.
    /// </summary>
    [JsonPropertyName("banco")] public DatabaseEntry? Database { get; set; }

    /// <summary>Número da etapa → status (<see cref="GmudStepStatus"/>).</summary>
    [JsonPropertyName("etapas")] public Dictionary<string, string> Steps { get; set; } = new();

    [JsonPropertyName("execucoes")] public List<ExecutionEntry> Executions { get; set; } = [];

    [JsonPropertyName("avisos")] public List<string> Warnings { get; set; } = [];

    /// <summary>Plano de escrita produzido no modo dry-run (o que seria feito, por repositório).</summary>
    [JsonPropertyName("plano")] public List<string> Plan { get; set; } = [];

    public void AddWarning(string warning)
    {
        if (!Warnings.Contains(warning, StringComparer.Ordinal))
        {
            Warnings.Add(warning);
        }
    }

    public string GetStepStatus(int stepNumber) =>
        Steps.TryGetValue(stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), out var status) ? status : GmudStepStatus.Pending;

    public void SetStepStatus(int stepNumber, string status) =>
        Steps[stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)] = status;

    /// <summary>Todos os itens (tasks/bugs) de todas as user stories, na ordem da Descrição.</summary>
    public IEnumerable<(UserStoryEntry UserStory, WorkItemEntry Item)> AllItems() =>
        UserStories.SelectMany(us => us.Items.Select(item => (us, item)));
}

public sealed class GmudHeader
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("titulo")] public string Title { get; set; } = string.Empty;

    /// <summary>Data normalizada DDMMAAAA.</summary>
    [JsonPropertyName("data")] public string Date { get; set; } = string.Empty;

    /// <summary>Cliente como aparece no container/branch (maiúsculas).</summary>
    [JsonPropertyName("cliente")] public string Client { get; set; } = string.Empty;

    [JsonPropertyName("clienteTitulo")] public string? ClientTitleName { get; set; }
    [JsonPropertyName("clienteAppService")] public string? ClientAppServiceName { get; set; }
    [JsonPropertyName("tipo")] public string? ChangeType { get; set; }
    [JsonPropertyName("organizacao")] public string? Organization { get; set; }
    [JsonPropertyName("projeto")] public string? Project { get; set; }
    [JsonPropertyName("estado")] public string? State { get; set; }
}

public sealed class UserStoryEntry
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("tipo")] public string Type { get; set; } = "User Story";
    [JsonPropertyName("titulo")] public string? Title { get; set; }
    [JsonPropertyName("estado")] public string? State { get; set; }
    [JsonPropertyName("branch")] public string? Branch { get; set; }
    [JsonPropertyName("itens")] public List<WorkItemEntry> Items { get; set; } = [];
}

public sealed class WorkItemEntry
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("tipo")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("titulo")] public string? Title { get; set; }
    [JsonPropertyName("estado")] public string? State { get; set; }
    [JsonPropertyName("projetos")] public List<string> Projects { get; set; } = [];

    /// <summary>De onde veio cada projeto ("texto", "link", "anexo") — explica a detecção na revisão humana.</summary>
    [JsonPropertyName("origemProjetos")] public Dictionary<string, List<string>> ProjectSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("anexosBanco")] public List<string> DatabaseAttachments { get; set; } = [];

    /// <summary>Commits vinculados ao card (links de desenvolvimento) — identificam o que pertence à user story.</summary>
    [JsonPropertyName("commits")] public List<LinkedCommit> Commits { get; set; } = [];
}

public sealed class LinkedCommit
{
    [JsonPropertyName("repositorio")] public string Repository { get; set; } = string.Empty;
    [JsonPropertyName("commitId")] public string CommitId { get; set; } = string.Empty;
}

public sealed class AppServiceEntry
{
    [JsonPropertyName("appService")] public string AppService { get; set; } = string.Empty;
    [JsonPropertyName("repositorio")] public string? Repository { get; set; }
    [JsonPropertyName("imagem")] public string? Image { get; set; }
    [JsonPropertyName("buildId")] public int? BuildId { get; set; }
    [JsonPropertyName("release")] public string? Release { get; set; }
    [JsonPropertyName("commitSha")] public string? CommitSha { get; set; }

    /// <summary>Preenchido quando o nome padrão não existe (cenário 5) — o operador escolhe um.</summary>
    [JsonPropertyName("candidatos")] public List<string>? Candidates { get; set; }
}

public sealed class RepositoryEntry
{
    [JsonPropertyName("tipo")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("branchBase")] public string? BaseBranch { get; set; }
    [JsonPropertyName("commitBase")] public string? BaseCommit { get; set; }
    [JsonPropertyName("branchGmud")] public string? GmudBranch { get; set; }
    [JsonPropertyName("commitGmud")] public string? GmudCommit { get; set; }
    [JsonPropertyName("merges")] public List<MergeEntry> Merges { get; set; } = [];

    /// <summary>Ambiente → nome da branch de release criada.</summary>
    [JsonPropertyName("releases")] public Dictionary<string, string> Releases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("builds")] public Dictionary<string, BuildEntry> Builds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivos que diferem entre a base de produção e a branch da GMUD conciliada.</summary>
    [JsonPropertyName("arquivosAlterados")] public List<string>? ChangedFiles { get; set; }

    /// <summary>Motivo pelo qual o repositório está bloqueado (ex: branch de user story ausente).</summary>
    [JsonPropertyName("bloqueio")] public string? Blocker { get; set; }
}

public sealed class DatabaseEntry
{
    [JsonPropertyName("repositorio")] public string Repository { get; set; } = string.Empty;
    [JsonPropertyName("branches")] public List<DatabaseBranchEntry> Branches { get; set; } = [];
}

/// <summary>Uma user story da GMUD × a <c>feature/us_&lt;US&gt;</c> do desenvolvedor no Database.</summary>
public sealed class DatabaseBranchEntry
{
    [JsonPropertyName("userStory")] public int UserStoryId { get; set; }
    [JsonPropertyName("branch")] public string Branch { get; set; } = string.Empty;

    /// <summary>O card cita o Database (texto/link): a branch tem de existir. Sem isso, a branch é procurada mesmo assim.</summary>
    [JsonPropertyName("citadoNoCard")] public bool Required { get; set; }

    [JsonPropertyName("temScripts")] public bool HasScripts { get; set; }

    /// <summary>Link da branch no Azure Repos (preenchido quando ela existe).</summary>
    [JsonPropertyName("link")] public string? Url { get; set; }

    [JsonPropertyName("commit")] public string? Commit { get; set; }

    /// <summary>Branch padrão do repositório contra a qual os commits da feature são contados.</summary>
    [JsonPropertyName("branchBase")] public string? BaseBranch { get; set; }

    /// <summary>Commits (sem merges) da feature que não estão na branch base.</summary>
    [JsonPropertyName("commitsAFrente")] public int? CommitsAhead { get; set; }

    /// <summary>Ver <see cref="DatabaseBranchStatus"/>.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = DatabaseBranchStatus.Pending;
}

public static class DatabaseBranchStatus
{
    public const string Pending = "pendente";

    /// <summary>A feature existe e tem commit próprio: houve alteração no Database.</summary>
    public const string Changed = "alterada";

    /// <summary>A feature existe, mas sem commit à frente da base (criada vazia ou já mesclada).</summary>
    public const string NoCommits = "sem-commits";

    /// <summary>O card cita o Database, mas a feature não existe — bloqueia.</summary>
    public const string Missing = "branch-inexistente";

    /// <summary>Sem branch, mas com script anexado: só os scripts vão para a infra.</summary>
    public const string ScriptsOnly = "somente-scripts";

    /// <summary>Sem branch, sem script e sem citação: a user story não altera o Database.</summary>
    public const string NotChanged = "sem-alteracao";

    public static bool AffectsDatabase(string status) => status is Changed or NoCommits or Missing or ScriptsOnly;
}

public sealed class MergeEntry
{
    [JsonPropertyName("origem")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("userStory")] public int UserStoryId { get; set; }

    /// <summary>Ver <see cref="MergeStatus"/>.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = MergeStatus.Pending;

    [JsonPropertyName("commitOrigem")] public string? SourceCommit { get; set; }
    [JsonPropertyName("commitsUserStory")] public List<string> UserStoryCommits { get; set; } = [];
    [JsonPropertyName("commitsForaDoEscopo")] public List<string> OutOfScopeCommits { get; set; } = [];
    [JsonPropertyName("arquivosUserStory")] public List<string> UserStoryFiles { get; set; } = [];
    [JsonPropertyName("conflitos")] public List<ConflictEntry> Conflicts { get; set; } = [];
    [JsonPropertyName("commitResultado")] public string? ResultCommit { get; set; }
    [JsonPropertyName("relatorio")] public string? ReportPath { get; set; }
}

public sealed class ConflictEntry
{
    [JsonPropertyName("arquivo")] public string Path { get; set; } = string.Empty;

    /// <summary>Ver <see cref="ConflictResolution"/>.</summary>
    [JsonPropertyName("resolucao")] public string Resolution { get; set; } = string.Empty;

    [JsonPropertyName("motivo")] public string Reason { get; set; } = string.Empty;

    /// <summary>Decisão humana para conflitos ambíguos: "feature", "release" ou "manual" (editada no manifesto pelo operador).</summary>
    [JsonPropertyName("decisao")] public string? Decision { get; set; }
}

public sealed class BuildEntry
{
    [JsonPropertyName("buildId")] public int? BuildId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "pendente";
    [JsonPropertyName("resultado")] public string? Result { get; set; }
    [JsonPropertyName("pipeline")] public string? Pipeline { get; set; }
    [JsonPropertyName("branch")] public string? Branch { get; set; }
    [JsonPropertyName("commitSha")] public string? CommitSha { get; set; }
    [JsonPropertyName("novaTag")] public string? NewTag { get; set; }
    [JsonPropertyName("imagem")] public string? Image { get; set; }
}

public sealed class ExecutionEntry
{
    [JsonPropertyName("etapa")] public int Step { get; set; }
    [JsonPropertyName("nome")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("inicio")] public DateTimeOffset StartedAt { get; set; }
    [JsonPropertyName("fim")] public DateTimeOffset FinishedAt { get; set; }
    [JsonPropertyName("duracaoMs")] public long DurationMs { get; set; }
    [JsonPropertyName("resultado")] public string Result { get; set; } = string.Empty;
    [JsonPropertyName("dryRun")] public bool DryRun { get; set; }
    [JsonPropertyName("mensagem")] public string? Message { get; set; }
}

/// <summary>Status das etapas no manifesto.</summary>
public static class GmudStepStatus
{
    public const string Pending = "pendente";
    public const string Completed = "concluida";

    /// <summary>Executada em dry-run: o plano foi calculado, nada foi escrito — a execução real ainda precisa rodá-la.</summary>
    public const string Simulated = "simulada";

    /// <summary>Parou aguardando o operador (candidatos de App Service, branch ausente, conflito ambíguo).</summary>
    public const string Blocked = "bloqueada";

    public const string Failed = "falhou";
}

/// <summary>Status de cada merge de user story.</summary>
public static class MergeStatus
{
    public const string Pending = "pendente";
    public const string Ok = "ok";
    public const string AlreadyContained = "ja-contido";
    public const string Conflict = "conflito";
    public const string NeedsReconciliation = "requer-conciliacao";
    public const string Reconciled = "conciliado";
    public const string AwaitingApproval = "aguardando-aprovacao";
    public const string MissingBranch = "branch-inexistente";

    public const string SimulatedOk = "simulado-ok";
    public const string SimulatedConflictRisk = "simulado-risco-conflito";
    public const string SimulatedReconcilable = "simulado-conciliavel";

    public static bool IsFinal(string status) =>
        status is Ok or AlreadyContained or Reconciled;
}

/// <summary>Como cada arquivo foi tratado na conciliação (etapa 9).</summary>
public static class ConflictResolution
{
    /// <summary>Arquivo alterado só pelos commits da user story e intocado na produção: entra a versão da feature.</summary>
    public const string TakeFeature = "versao-da-feature";

    /// <summary>Arquivo alterado só por commits fora do escopo: não é levado (fica a versão de produção).</summary>
    public const string Discarded = "descartado-fora-do-escopo";

    /// <summary>Não é possível decidir automaticamente — para o fluxo para aprovação humana.</summary>
    public const string Ambiguous = "ambiguo";

    /// <summary>Ambíguo aprovado pelo operador como "release": fica a versão de produção.</summary>
    public const string KeptProduction = "mantida-producao";

    /// <summary>Ambíguo resolvido pelo operador direto na branch da GMUD.</summary>
    public const string ResolvedManually = "resolvido-manualmente";
}
