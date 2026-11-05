using GmudAutomation.Core.Client;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Steps;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Core.Pipeline;

/// <summary>Registra as 11 etapas padrão e resolve a ordem configurada em <see cref="GmudConfig.Steps"/>.</summary>
public static class GmudPipelineFactory
{
    public static IReadOnlyList<IGmudStep> CreateSteps(
        GmudConfig config,
        IAzureDevOpsClient boards,
        IAzureReposClient repos,
        IAppServiceClient appServices,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IEnumerable<IGmudStep>? additionalSteps = null)
    {
        var naming = GmudNamingConventions.FromConfig(config);
        var registry = new GmudStepRegistry(new IGmudStep[]
        {
            new ResolveGmudStep(boards, config, naming, new AzureBoardsUrlParser()),
            new MapDescriptionItemsStep(boards, config, naming),
            new MapRelatedItemsStep(boards, config),
            new MapProjectsAndScriptsStep(boards, repos, config, naming),
            new StoreInformationStep(config, naming),
            new FetchCurrentTagsStep(appServices, repos, config, naming),
            new CreateGmudBranchStep(repos),
            new MergeUserStoriesStep(repos, naming),
            new ReconcileConflictsStep(repos, naming),
            new CreateReleaseBranchesStep(repos, naming),
            new TrackBuildsStep(repos, config, naming, delay)
        }.Concat(additionalSteps ?? []));

        return registry.Resolve(config.Steps);
    }
}
