using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Constrói a hierarquia completa de uma GMUD a partir da User Story principal (US 1.1).</summary>
public interface IGmudHierarchyService
{
    Task<GmudRequest> BuildHierarchyAsync(int gmudWorkItemId, CancellationToken cancellationToken = default);
}
