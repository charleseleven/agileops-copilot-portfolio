using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IGmudAutomationOrchestrator"/>
public sealed class GmudAutomationOrchestrator : IGmudAutomationOrchestrator
{
    private readonly IGmudHierarchyService _hierarchyService;
    private readonly IGmudTechnicalImpactPublisher _impactPublisher;

    public GmudAutomationOrchestrator(IGmudHierarchyService hierarchyService, IGmudTechnicalImpactPublisher impactPublisher)
    {
        _hierarchyService = hierarchyService ?? throw new ArgumentNullException(nameof(hierarchyService));
        _impactPublisher = impactPublisher ?? throw new ArgumentNullException(nameof(impactPublisher));
    }

    public async Task<GmudRequest> RunAsync(int gmudWorkItemId, CancellationToken cancellationToken = default)
    {
        var hierarchy = await _hierarchyService.BuildHierarchyAsync(gmudWorkItemId, cancellationToken);

        foreach (var child in hierarchy.ChildWorkItems)
        {
            await _impactPublisher.AnalyzeAndPublishAsync(child.Id, cancellationToken);
        }

        return hierarchy;
    }
}
