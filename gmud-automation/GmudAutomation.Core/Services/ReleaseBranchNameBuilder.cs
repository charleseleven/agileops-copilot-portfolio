namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IReleaseBranchNameBuilder"/>
public sealed class ReleaseBranchNameBuilder : IReleaseBranchNameBuilder
{
    public string Build(string environment, string clientName, DateOnly releaseDate) =>
        $"release/GMUD_{environment}_{clientName}_{releaseDate:ddMMyyyy}";
}
