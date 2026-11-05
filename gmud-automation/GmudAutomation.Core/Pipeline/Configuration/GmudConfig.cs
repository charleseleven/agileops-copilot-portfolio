using System.Text.Json;
using System.Text.Json.Serialization;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Pipeline.Configuration;

/// <summary>
/// Configuração do fluxo automatizado de GMUD (<c>gmud.config.json</c>): organização, projetos, repositórios,
/// clientes, padrões de nome e a ordem das etapas. O arquivo real fica fora do Git (dados de cliente);
/// o formato está em <c>gmud.config.example.json</c>.
/// </summary>
public sealed class GmudConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public required string Organization { get; init; }

    /// <summary>Team Project dos Work Items (Boards).</summary>
    public required string BoardsProject { get; init; }

    /// <summary>Team Project dos repositórios e pipelines; se omitido, usa <see cref="BoardsProject"/>.</summary>
    public string? ReposProject { get; init; }

    /// <summary>Subscription dos App Services; se omitida, vem de AZURE_SUBSCRIPTION_ID (nunca versionar o valor real).</summary>
    public string? SubscriptionId { get; init; }

    /// <summary>Pasta onde cada GMUD ganha <c>&lt;id&gt;/manifest.json</c>, scripts e relatórios.</summary>
    public string WorkspaceDirectory { get; init; } = "gmuds";

    public NamingConfig Naming { get; init; } = new();

    public IReadOnlyList<RepositoryConfig> Repositories { get; init; } = Array.Empty<RepositoryConfig>();

    public IReadOnlyList<ClientConfig> Clients { get; init; } = Array.Empty<ClientConfig>();

    /// <summary>Tipos aceitos na Descrição da GMUD (premissa 5: Bug também entra, com branch própria).</summary>
    public IReadOnlyList<string> DescriptionItemTypes { get; init; } = ["User Story", "Bug"];

    /// <summary>Tipos coletados nas relações das user stories (etapa 3).</summary>
    public IReadOnlyList<string> RelatedItemTypes { get; init; } = ["Task", "Bug"];

    /// <summary>Extensões de anexo das tasks/bugs tratadas como script de banco (tabelas e, às vezes, procedures).</summary>
    public IReadOnlyList<string> DatabaseScriptExtensions { get; init; } = [".txt"];

    public BuildConfig Build { get; init; } = new();

    /// <summary>
    /// Ordem das etapas (chaves registradas em <see cref="GmudStepRegistry"/>). Incluir, remover ou reordenar
    /// etapas é feito aqui, sem alterar o código das demais.
    /// </summary>
    public IReadOnlyList<string> Steps { get; init; } = GmudStepKeys.Default;

    public string EffectiveReposProject => ReposProject ?? BoardsProject;

    /// <summary>Link da branch no Azure Repos (ex: para o operador conferir a feature do desenvolvedor no Database).</summary>
    public string BranchUrl(string repositoryName, string branchName) =>
        $"https://dev.azure.com/{Uri.EscapeDataString(Organization)}/{Uri.EscapeDataString(EffectiveReposProject)}/_git/" +
        $"{Uri.EscapeDataString(repositoryName)}?version=GB{Uri.EscapeDataString(branchName)}";

    public static GmudConfig LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<GmudConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Configuração inválida em \"{path}\".");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Organization)) errors.Add("\"organization\" é obrigatório");
        if (string.IsNullOrWhiteSpace(BoardsProject)) errors.Add("\"boardsProject\" é obrigatório");
        if (Repositories.Count == 0) errors.Add("\"repositories\" precisa de ao menos um repositório");

        foreach (var duplicated in Repositories.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"repositório \"{duplicated.Key}\" declarado mais de uma vez");
        }

        foreach (var duplicated in Repositories.GroupBy(r => r.EffectiveKey, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"chave de App Service \"{duplicated.Key}\" usada por mais de um repositório (defina \"key\")");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("gmud.config.json inválido: " + string.Join("; ", errors) + ".");
        }
    }
}

/// <summary>Padrões de nome — o "módulo único" de nomenclatura; consumidos só por <see cref="GmudNamingConventions"/>.</summary>
public sealed class NamingConfig
{
    public string GmudBranch { get; init; } = "feature/us_{id}";
    public string ItemBranch { get; init; } = "feature/us_{id}";
    public string ProductionRelease { get; init; } = "release/GMUD_PROD_{cliente}_{data}";
    public string GmudRelease { get; init; } = "release/GMUD_GMUD_{cliente}_{data}";

    /// <summary>Prefixo somado ao nome da release extraído da tag do container para chegar à branch de origem.</summary>
    public string SourceBranchPrefix { get; init; } = "release/";

    /// <summary>
    /// Nomes de branch que aparecem na tag do container já como nome real, sem <see cref="SourceBranchPrefix"/>
    /// (ex: "main_&lt;cliente&gt;"). "*" no fim casa qualquer sufixo.
    /// </summary>
    public IReadOnlyList<string> SourceBranchesWithoutPrefix { get; init; } = ["main", "main_*"];

    public string MergeCommitMessage { get; init; } = "GMUD {gmud}: merge {branch}";
    public string ReconciliationCommitMessage { get; init; } = "GMUD {gmud}: conciliação {branch}";

    /// <summary>Nome (sem extensão) do script de banco anexado à task/bug; sufixos depois dele são aceitos.</summary>
    public string DatabaseScriptName { get; init; } = "US{userStory}_SCRIPT_TASK{item}";

    public string ProductionEnvironment { get; init; } = "prd";
    public string GmudEnvironment { get; init; } = "gmud";
}

/// <summary>Repositório que pode ser afetado por uma GMUD.</summary>
/// <param name="Name">Nome exato no Azure Repos.</param>
/// <param name="Kind">Categoria (Database não roda em container).</param>
/// <param name="Aliases">Outras formas de citar o projeto nos cards (ex: "WEB", "Portal Web").</param>
/// <param name="AppService">Padrão do App Service com "{ambiente}" e "{cliente}"; nulo para repositórios sem container.</param>
/// <param name="Key">Chave curta usada no manifesto (ex: "web"); padrão: o tipo em minúsculas.</param>
/// <param name="HasBuild">Se o push da release dispara pipeline (premissa 6); padrão: verdadeiro quando há App Service.</param>
public sealed record RepositoryConfig(
    string Name,
    RepositoryKind Kind,
    IReadOnlyList<string>? Aliases = null,
    string? AppService = null,
    string? Key = null,
    bool? HasBuild = null)
{
    public string EffectiveKey => Key ?? Kind.ToString().ToLowerInvariant();

    public bool EffectiveHasBuild => HasBuild ?? AppService is not null;
}

/// <summary>
/// Cliente: como aparece no título da GMUD, no container/branch (maiúsculas) e no nome do App Service.
/// Clientes não listados usam o nome do título normalizado (sem acento/espaço).
/// </summary>
/// <param name="AppServiceOverrides">Nome exato do App Service por repositório e ambiente, para sites fora do padrão (cenário 5).</param>
public sealed record ClientConfig(
    string TitleName,
    string? ContainerName = null,
    string? AppServiceName = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? AppServiceOverrides = null);

public sealed class BuildConfig
{
    public int TimeoutMinutes { get; init; } = 30;
    public int PollIntervalSeconds { get; init; } = 30;
}
