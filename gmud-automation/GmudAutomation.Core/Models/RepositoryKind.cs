namespace GmudAutomation.Core.Models;

/// <summary>Categoria do repositório em uma GMUD, usada para decidir se a PR deve ser completada automaticamente.</summary>
public enum RepositoryKind
{
    Api,
    Web,
    Database,

    /// <summary>Serviços de backend sem interface própria (ex: Contoso.Integracao.GerenciadorJobs). PR completada automaticamente, como Api/Web.</summary>
    Backend
}
