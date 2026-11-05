using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GmudAutomation.Core.Pipeline.Configuration;

/// <summary>
/// Ponto único das regras de nome do fluxo de GMUD (data e cliente do título, branches, releases, App
/// Services e mensagens de commit). Toda etapa monta nomes por aqui — mudar uma convenção é mudar o
/// <see cref="NamingConfig"/>, sem tocar no código do fluxo.
/// </summary>
public sealed partial class GmudNamingConventions
{
    // Data no título no formato DD-MM-AAAA (ex: "GMUD 22-10-2026 - Cliente - Implantação").
    [GeneratedRegex(@"(?<!\d)(?<dia>\d{2})-(?<mes>\d{2})-(?<ano>\d{4})(?!\d)")]
    private static partial Regex TitleDateRegex();

    // Separador de segmentos do título: hífen cercado de espaços, para não quebrar nomes compostos ("Sao-Jose").
    [GeneratedRegex(@"\s+-\s+")]
    private static partial Regex SegmentSeparatorRegex();

    [GeneratedRegex(@"^(?<build>\d+)-(?<release>.+)-(?<sha>[0-9a-fA-F]{40})$")]
    private static partial Regex ContainerTagRegex();

    private readonly NamingConfig _naming;
    private readonly IReadOnlyList<ClientConfig> _clients;

    public GmudNamingConventions(NamingConfig naming, IReadOnlyList<ClientConfig>? clients = null)
    {
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _clients = clients ?? Array.Empty<ClientConfig>();
    }

    public static GmudNamingConventions FromConfig(GmudConfig config) => new(config.Naming, config.Clients);

    public string ProductionEnvironment => _naming.ProductionEnvironment;

    public string GmudEnvironment => _naming.GmudEnvironment;

    /// <summary>
    /// Extrai data e cliente do título. Lança <see cref="GmudValidationException"/> indicando o campo inválido —
    /// cenário 2: título fora do padrão interrompe o fluxo na etapa 1.
    /// </summary>
    public GmudTitle ParseTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GmudValidationException("titulo", "O título da GMUD está vazio.");
        }

        var dateMatch = TitleDateRegex().Match(title);
        if (!dateMatch.Success)
        {
            throw new GmudValidationException("data",
                $"O título \"{title}\" não contém a data da GMUD no formato DD-MM-AAAA (ex: \"GMUD 22-10-2026 - Cliente - Implantação\").");
        }

        var rawDate = $"{dateMatch.Groups["dia"].Value}-{dateMatch.Groups["mes"].Value}-{dateMatch.Groups["ano"].Value}";
        if (!DateOnly.TryParseExact(rawDate, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new GmudValidationException("data", $"A data \"{rawDate}\" do título \"{title}\" não é uma data válida.");
        }

        var afterDate = title[(dateMatch.Index + dateMatch.Length)..];
        var segments = SegmentSeparatorRegex().Split(" " + afterDate.Trim() + " ")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            throw new GmudValidationException("cliente",
                $"O título \"{title}\" não informa o cliente depois da data (formato esperado: \"GMUD DD-MM-AAAA - Cliente - Tipo\").");
        }

        return new GmudTitle(date, segments[0], segments.Count > 1 ? segments[1] : null);
    }

    /// <summary>Data normalizada usada em branches e releases (DDMMAAAA).</summary>
    public static string FormatDate(DateOnly date) => date.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

    /// <summary>Resolve os nomes do cliente (container e App Service) a partir do nome que aparece no título.</summary>
    public GmudClient ResolveClient(string titleName)
    {
        var configured = _clients.FirstOrDefault(c => string.Equals(Normalize(c.TitleName), Normalize(titleName), StringComparison.OrdinalIgnoreCase));
        var normalized = Normalize(titleName);

        return new GmudClient(
            TitleName: titleName,
            ContainerName: configured?.ContainerName ?? normalized.ToUpperInvariant(),
            AppServiceName: configured?.AppServiceName ?? normalized.ToLowerInvariant(),
            IsConfigured: configured is not null);
    }

    /// <summary>Nome exato configurado para um App Service fora do padrão, se houver.</summary>
    public string? GetAppServiceOverride(string clientTitleName, string repositoryName, string environment)
    {
        var configured = _clients.FirstOrDefault(c => string.Equals(Normalize(c.TitleName), Normalize(clientTitleName), StringComparison.OrdinalIgnoreCase));
        if (configured?.AppServiceOverrides is null)
        {
            return null;
        }

        var byEnvironment = configured.AppServiceOverrides
            .FirstOrDefault(o => string.Equals(o.Key, repositoryName, StringComparison.OrdinalIgnoreCase)).Value;
        return byEnvironment?.FirstOrDefault(e => string.Equals(e.Key, environment, StringComparison.OrdinalIgnoreCase)).Value;
    }

    public string AppServiceName(RepositoryConfig repository, string environment, GmudClient client) =>
        repository.AppService is null
            ? throw new InvalidOperationException($"O repositório \"{repository.Name}\" não tem App Service configurado.")
            : repository.AppService.Replace("{ambiente}", environment).Replace("{cliente}", client.AppServiceName);

    public string GmudBranch(int gmudId) => _naming.GmudBranch.Replace("{id}", gmudId.ToString(CultureInfo.InvariantCulture));

    public string ItemBranch(int workItemId) => _naming.ItemBranch.Replace("{id}", workItemId.ToString(CultureInfo.InvariantCulture));

    public string ProductionRelease(string containerClient, DateOnly date) => FillClientAndDate(_naming.ProductionRelease, containerClient, date);

    public string GmudRelease(string containerClient, DateOnly date) => FillClientAndDate(_naming.GmudRelease, containerClient, date);

    /// <summary>Nome esperado do script de banco anexado (sem extensão): "US20611_SCRIPT_TASK20612".</summary>
    public string DatabaseScriptName(int userStoryId, int itemId) => _naming.DatabaseScriptName
        .Replace("{userStory}", userStoryId.ToString(CultureInfo.InvariantCulture))
        .Replace("{item}", itemId.ToString(CultureInfo.InvariantCulture));

    /// <summary>O anexo segue a nomenclatura da user story e da task/bug? Aceita sufixo (ex: "_2") para mais de um script.</summary>
    public bool IsDatabaseScriptName(string fileName, int userStoryId, int itemId)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var expected = DatabaseScriptName(userStoryId, itemId);
        return stem.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
            && (stem.Length == expected.Length || !char.IsDigit(stem[expected.Length]));
    }

    /// <summary>
    /// Branch de origem a partir do segmento central da tag: "GMUD_PROD_CLI1_22102026" → "release/GMUD_PROD_CLI1_22102026";
    /// branches listadas em <see cref="NamingConfig.SourceBranchesWithoutPrefix"/> (ex: "main_cli1") ficam como estão.
    /// </summary>
    public string SourceBranchFromRelease(string releaseName) =>
        IsBranchWithoutPrefix(releaseName) ? releaseName : _naming.SourceBranchPrefix + releaseName;

    /// <summary>Remove o prefixo de branch (o nome da release como aparece na tag do container).</summary>
    public string ReleaseNameFromBranch(string branchName) =>
        branchName.StartsWith(_naming.SourceBranchPrefix, StringComparison.OrdinalIgnoreCase) ? branchName[_naming.SourceBranchPrefix.Length..] : branchName;

    private bool IsBranchWithoutPrefix(string releaseName) =>
        _naming.SourceBranchesWithoutPrefix.Any(pattern => pattern.EndsWith('*')
            ? releaseName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(releaseName, pattern, StringComparison.OrdinalIgnoreCase));

    public string MergeCommitMessage(int gmudId, string branch) => FillGmudAndBranch(_naming.MergeCommitMessage, gmudId, branch);

    public string ReconciliationCommitMessage(int gmudId, string branch) => FillGmudAndBranch(_naming.ReconciliationCommitMessage, gmudId, branch);

    /// <summary>Tag do container: <c>&lt;buildId&gt;-&lt;nomeDaRelease&gt;-&lt;commitSha&gt;</c>.</summary>
    public static string ContainerTag(int buildId, string releaseName, string commitSha) => $"{buildId}-{releaseName}-{commitSha}";

    /// <summary>
    /// Decompõe a imagem do container (<c>registro/projeto:buildId-release-commitSha</c>, com ou sem o prefixo
    /// "DOCKER|"): buildId antes do primeiro hífen, commitSha (40 hex) no fim e a release no meio.
    /// </summary>
    public static bool TryParseContainerImage(string? image, out ContainerImage parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(image))
        {
            return false;
        }

        var value = image.Trim();
        var pipe = value.IndexOf('|');
        if (pipe >= 0)
        {
            value = value[(pipe + 1)..];
        }

        // O ":" da tag é o último depois da última "/" (o registro pode ter porta, ex: "registro:5000/projeto:tag").
        var lastSlash = value.LastIndexOf('/');
        var colon = value.IndexOf(':', lastSlash + 1);
        if (colon < 0)
        {
            return false;
        }

        if (!TryParseContainerTag(value[(colon + 1)..], out var tag))
        {
            return false;
        }

        parsed = new ContainerImage(value, value[..colon], tag.Tag, tag.BuildId, tag.ReleaseName, tag.CommitSha);
        return true;
    }

    /// <summary>
    /// Decompõe só a tag (<c>buildId-release-commitSha</c>), ex: "31229-GMUD_PROD_CLI1_29102026-&lt;sha&gt;" →
    /// build 31229, release "GMUD_PROD_CLI1_29102026", commit &lt;sha&gt;. A release pode conter hífens.
    /// </summary>
    public static bool TryParseContainerTag(string? tag, out ContainerTagParts parsed)
    {
        parsed = null!;
        var match = ContainerTagRegex().Match(tag?.Trim() ?? string.Empty);
        if (!match.Success || !int.TryParse(match.Groups["build"].Value, out var buildId))
        {
            return false;
        }

        parsed = new ContainerTagParts(match.Value, buildId, match.Groups["release"].Value, match.Groups["sha"].Value.ToLowerInvariant());
        return true;
    }

    /// <summary>Remove acentos, espaços e símbolos: "São José" → "SaoJose".</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string FillClientAndDate(string template, string containerClient, DateOnly date) =>
        template.Replace("{cliente}", containerClient).Replace("{data}", FormatDate(date));

    private static string FillGmudAndBranch(string template, int gmudId, string branch) =>
        template.Replace("{gmud}", gmudId.ToString(CultureInfo.InvariantCulture)).Replace("{branch}", branch).Trim();
}

/// <summary>Partes do título da GMUD.</summary>
public sealed record GmudTitle(DateOnly Date, string ClientTitleName, string? ChangeType);

/// <summary>Nomes do cliente nos três lugares em que ele aparece.</summary>
public sealed record GmudClient(string TitleName, string ContainerName, string AppServiceName, bool IsConfigured);

/// <summary>Imagem do container decomposta conforme a convenção de tag.</summary>
public sealed record ContainerImage(string Image, string Repository, string Tag, int BuildId, string ReleaseName, string CommitSha);

public sealed record ContainerTagParts(string Tag, int BuildId, string ReleaseName, string CommitSha);
