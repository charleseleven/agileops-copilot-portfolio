using AgilePredict.Models.SprintRisk;

namespace AgilePredict.Tests.Services.SprintRisk
{
    /// <summary>Relógio fixo para testar a regra analítica sem depender da data real.</summary>
    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal static class SprintRiskTestData
    {
        /// <summary>Segunda, 09/11/2026, 12h em São Paulo (15h UTC).</summary>
        public static readonly DateTimeOffset Monday9Nov = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Sprint de 02/11 (seg) a 13/11/2026 (sex): 10 dias úteis; em 09/11, 5 decorridos e 5 restantes.
        /// </summary>
        public static SprintCapacitySnapshot Snapshot(
            IReadOnlyList<TeamMemberCapacity>? members = null,
            IReadOnlyList<SprintWorkItem>? items = null,
            IReadOnlyList<DateRange>? teamDaysOff = null,
            DateOnly? start = null,
            DateOnly? finish = null) => new()
        {
            Organization = "contoso",
            Project = "Projeto",
            Team = "Projeto Team",
            Iteration = new SprintIteration("it-1", "Sprint 8", "Projeto\\Sprint 8",
                start ?? new DateOnly(2026, 11, 2), finish ?? new DateOnly(2026, 11, 13)),
            Members = members ?? new[] { Member("Ana", 6) },
            WorkItems = items ?? Array.Empty<SprintWorkItem>(),
            TeamDaysOff = teamDaysOff ?? Array.Empty<DateRange>()
        };

        public static TeamMemberCapacity Member(string name, double perDay, params DateRange[] daysOff) =>
            new(name, perDay, daysOff);

        public static SprintWorkItem Task(int id, string state, string? assignedTo, double? estimate, double? remaining = null,
            double? completed = null, string type = "Task") => new()
        {
            Id = id,
            Title = $"Item {id}",
            WorkItemType = type,
            State = state,
            AssignedTo = assignedTo,
            OriginalEstimate = estimate,
            RemainingWork = remaining,
            CompletedWork = completed
        };
    }
}
