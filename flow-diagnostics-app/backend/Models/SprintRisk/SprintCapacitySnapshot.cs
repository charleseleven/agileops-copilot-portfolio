namespace AgilePredict.Models.SprintRisk
{
    /// <summary>
    /// Retrato da Sprint coletado do Azure Boards (PBI 8.1, AC 1): iteração, calendário do time,
    /// capacidade por membro e work items com horas. É o contrato entre a coleta (I/O) e a regra
    /// analítica (pura), o que permite testar a regra sem Azure DevOps.
    /// </summary>
    public sealed record SprintCapacitySnapshot
    {
        public required string Organization { get; init; }
        public required string Project { get; init; }
        public required string Team { get; init; }
        public required SprintIteration Iteration { get; init; }

        /// <summary>Dias da semana em que o time trabalha (configuração do time no Azure DevOps).</summary>
        public IReadOnlyList<DayOfWeek> WorkingDays { get; init; } = DefaultWorkingDays;

        /// <summary>Folgas do time inteiro na sprint (feriados, eventos).</summary>
        public IReadOnlyList<DateRange> TeamDaysOff { get; init; } = Array.Empty<DateRange>();

        public IReadOnlyList<TeamMemberCapacity> Members { get; init; } = Array.Empty<TeamMemberCapacity>();

        public IReadOnlyList<SprintWorkItem> WorkItems { get; init; } = Array.Empty<SprintWorkItem>();

        public DateTime CollectedAtUtc { get; init; } = DateTime.UtcNow;

        public static readonly IReadOnlyList<DayOfWeek> DefaultWorkingDays = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
        };
    }

    public sealed record SprintIteration(string Id, string Name, string Path, DateOnly StartDate, DateOnly FinishDate);

    /// <summary>Intervalo fechado de datas (início e fim inclusos).</summary>
    public sealed record DateRange(DateOnly Start, DateOnly End)
    {
        public bool Contains(DateOnly date) => date >= Start && date <= End;
    }

    /// <summary>
    /// Capacidade de um membro na sprint: horas por dia (soma das atividades) e folgas individuais.
    /// </summary>
    public sealed record TeamMemberCapacity(string DisplayName, double CapacityPerDay, IReadOnlyList<DateRange> DaysOff);

    public sealed record SprintWorkItem
    {
        public int Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string WorkItemType { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? AssignedTo { get; init; }

        /// <summary>Horas Estimadas (Original Estimate).</summary>
        public double? OriginalEstimate { get; init; }
        public double? RemainingWork { get; init; }
        public double? CompletedWork { get; init; }
    }
}
