using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Configuration;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IIntegrationBranchService"/>
public sealed class IntegrationBranchService : IIntegrationBranchService
{
    private readonly IAppServiceClient _appServiceClient;
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly IReleaseBranchNameBuilder _nameBuilder;
    private readonly GmudNamingConventions _naming;

    public IntegrationBranchService(
        IAppServiceClient appServiceClient, IAzureDevOpsClient azureDevOpsClient, IReleaseBranchNameBuilder nameBuilder,
        GmudNamingConventions? naming = null)
    {
        _appServiceClient = appServiceClient ?? throw new ArgumentNullException(nameof(appServiceClient));
        _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
        _nameBuilder = nameBuilder ?? throw new ArgumentNullException(nameof(nameBuilder));
        _naming = naming ?? new GmudNamingConventions(new NamingConfig());
    }

    public async Task<string> CreateReleaseBranchAsync(ReleaseBranchRequest request, CancellationToken cancellationToken = default)
    {
        var releaseBranchName = _nameBuilder.Build(request.Environment, request.ClientName, request.ReleaseDate);
        var currentTag = await _appServiceClient.GetCurrentTagAsync(request.SiteName, cancellationToken);

        // A tag não é o nome da branch: "<buildId>-<branch>-<commitSha>" — a branch publicada é o segmento
        // central, com "release/" na frente (ou como está, para "main_<cliente>").
        if (!GmudNamingConventions.TryParseContainerTag(currentTag, out var tag))
        {
            throw new InvalidOperationException(
                $"A tag \"{currentTag}\" do site \"{request.SiteName}\" não segue o padrão <buildId>-<branch>-<commitSha> — " +
                "não dá para saber de qual branch a release deve partir.");
        }

        var sourceBranch = _naming.SourceBranchFromRelease(tag.ReleaseName);
        await _azureDevOpsClient.CreateBranchAsync(request.RepositoryName, sourceBranch, releaseBranchName, cancellationToken);

        foreach (var featureBranch in request.FeatureBranchNames)
        {
            // Interrompe na primeira MergeConflictException — o AC exige que a execução pare para resolução manual do Tech Lead.
            await _azureDevOpsClient.MergeBranchesAsync(request.RepositoryName, featureBranch, releaseBranchName, cancellationToken);
        }

        return releaseBranchName;
    }
}
