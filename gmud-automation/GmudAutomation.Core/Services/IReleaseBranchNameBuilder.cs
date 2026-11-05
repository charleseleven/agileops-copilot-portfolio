namespace GmudAutomation.Core.Services;

/// <summary>Constrói o nome da branch de release seguindo o padrão release/GMUD_&lt;AMBIENTE&gt;_&lt;NOMECLIENTE&gt;_&lt;DDMMAAAA&gt; (US 2.1).</summary>
public interface IReleaseBranchNameBuilder
{
    string Build(string environment, string clientName, DateOnly releaseDate);
}
