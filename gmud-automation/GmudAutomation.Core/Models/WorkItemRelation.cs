namespace GmudAutomation.Core.Models;

/// <summary>
/// Relação de um Work Item (campo "relations" da API): filhos (<c>System.LinkTypes.Hierarchy-Forward</c>),
/// relacionados (<c>System.LinkTypes.Related</c>), anexos (<c>AttachedFile</c>) e links de desenvolvimento
/// (<c>ArtifactLink</c> — commits, PRs e branches vinculados ao card).
/// </summary>
/// <param name="Rel">Tipo da relação, exatamente como a API retorna.</param>
/// <param name="Url">URL do item relacionado, do anexo ou URI <c>vstfs:///</c> do artefato.</param>
/// <param name="Name">Nome do anexo ou do tipo de link de artefato (<c>attributes.name</c>).</param>
public sealed record WorkItemRelation(string Rel, string Url, string? Name);
