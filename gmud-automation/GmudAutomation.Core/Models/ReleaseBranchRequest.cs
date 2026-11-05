namespace GmudAutomation.Core.Models;

/// <summary>
/// Parâmetros para a criação da branch de release e o merge sequencial das features (US 2.1).
/// <paramref name="Environment"/> é o rótulo usado no NOME da branch (ex: "PROD", "GMUD"), enquanto
/// <paramref name="SiteName"/> é o nome real do Azure App Service consultado para a tag em execução —
/// em geral eles não são a mesma coisa (o site segue um padrão configurável, ex: "cliente-portal-{api|web}-{ambiente}-{cliente}",
/// veja <see cref="Services.RepositoryCatalog"/>).
/// </summary>
public sealed record ReleaseBranchRequest(
    string Environment,
    string ClientName,
    DateOnly ReleaseDate,
    string RepositoryName,
    string SiteName,
    IReadOnlyList<string> FeatureBranchNames);
