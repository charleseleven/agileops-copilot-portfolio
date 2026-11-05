using System.Diagnostics;
using GmudAutomation.Core.Pipeline.Manifest;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Core.Pipeline;

/// <summary>Parâmetros de <c>gmud run</c>.</summary>
public sealed record GmudRunRequest(string Input, int GmudId, bool DryRun, int? FromStep, bool ConfirmWrite);

public enum GmudRunOutcome
{
    Completed,
    DryRunCompleted,

    /// <summary>Chegou a uma etapa que escreve sem <c>--confirm-write</c>: nada foi escrito.</summary>
    ConfirmationRequired,

    /// <summary>O operador recusou a confirmação interativa.</summary>
    Cancelled,

    Blocked,
    Failed
}

public sealed record GmudRunResult(GmudRunOutcome Outcome, GmudManifest Manifest, int? StoppedAtStep, string? Message)
{
    /// <summary>0 = ok, 1 = falha, 2 = aguardando decisão do operador, 3 = confirmação de escrita necessária.</summary>
    public int ExitCode => Outcome switch
    {
        GmudRunOutcome.Completed or GmudRunOutcome.DryRunCompleted => 0,
        GmudRunOutcome.Blocked => 2,
        GmudRunOutcome.ConfirmationRequired or GmudRunOutcome.Cancelled => 3,
        _ => 1
    };
}

/// <summary>Confirmação explícita do operador antes de escrever (mostrando o resumo por repositório).</summary>
public interface IOperatorConfirmation
{
    Task<bool> ConfirmAsync(int stepNumber, string stepName, string summary, CancellationToken cancellationToken = default);
}

/// <summary>Usada quando não há terminal interativo: a confirmação é o próprio <c>--confirm-write</c>.</summary>
public sealed class FlagOnlyOperatorConfirmation : IOperatorConfirmation
{
    public Task<bool> ConfirmAsync(int stepNumber, string stepName, string summary, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>
/// Executa as etapas configuradas sobre o manifesto da GMUD: retoma da primeira etapa não concluída (ou de
/// <c>--from-step</c>), grava o manifesto após cada etapa e para em falha, bloqueio ou falta de confirmação.
/// </summary>
public sealed class GmudPipelineRunner
{
    private readonly IReadOnlyList<IGmudStep> _steps;
    private readonly IGmudManifestStore _store;
    private readonly IOperatorConfirmation _confirmation;
    private readonly string _workspaceDisplayRoot;
    private readonly TimeProvider _clock;

    public GmudPipelineRunner(
        IReadOnlyList<IGmudStep> steps, IGmudManifestStore store, IOperatorConfirmation confirmation, string workspaceDisplayRoot, TimeProvider? clock = null)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _workspaceDisplayRoot = workspaceDisplayRoot.Replace('\\', '/').TrimEnd('/');
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<IGmudStep> Steps => _steps;

    /// <summary>Extrai o ID da GMUD de um link do Azure Boards ou de um número.</summary>
    public static bool TryParseInput(string input, IAzureBoardsUrlParser urlParser, out int gmudId, out AzureBoardsWorkItemReference? reference)
    {
        reference = null;
        var trimmed = input.Trim().TrimStart('#');
        if (int.TryParse(trimmed, out gmudId) && gmudId > 0)
        {
            return true;
        }

        if (urlParser.TryParse(input.Trim(), out var parsed))
        {
            reference = parsed;
            gmudId = parsed.WorkItemId;
            return true;
        }

        gmudId = 0;
        return false;
    }

    public async Task<GmudRunResult> RunAsync(GmudRunRequest request, IGmudLogger logger, CancellationToken cancellationToken = default)
    {
        var manifest = _store.Load(request.GmudId) ?? new GmudManifest { Gmud = { Id = request.GmudId } };

        if (request.FromStep is { } from && (from < 1 || from > _steps.Count))
        {
            return new GmudRunResult(GmudRunOutcome.Failed, manifest, null, $"--from-step deve estar entre 1 e {_steps.Count}.");
        }

        var startIndex = request.FromStep is { } fromStep
            ? fromStep - 1
            : Enumerable.Range(0, _steps.Count).FirstOrDefault(i => manifest.GetStepStatus(i + 1) != GmudStepStatus.Completed, -1);

        if (startIndex < 0)
        {
            return new GmudRunResult(GmudRunOutcome.Completed, manifest, null,
                "Todas as etapas já estão concluídas. Use --from-step N para reexecutar uma etapa.");
        }

        if (request.DryRun)
        {
            manifest.Plan.Clear();
        }

        logger.Info(startIndex > 0
            ? $"Retomando a GMUD #{request.GmudId} a partir da etapa {startIndex + 1} ({_steps[startIndex].Name})."
            : $"Iniciando a GMUD #{request.GmudId}{(request.DryRun ? " em modo dry-run (nada será escrito no Azure)" : string.Empty)}.");

        var askedConfirmationInThisRun = false;
        var gmudDirectory = _store.GetGmudDirectory(request.GmudId);

        for (var index = startIndex; index < _steps.Count; index++)
        {
            var step = _steps[index];
            var number = index + 1;

            if (request.DryRun && !step.SupportsDryRun)
            {
                logger.Info($"Etapa {number} ({step.Name}) não roda em dry-run — será executada só na execução real.", number);
                continue;
            }

            if (!request.DryRun && step.WritesRemote)
            {
                var pendingWriteStep = Enumerable.Range(0, index)
                    .FirstOrDefault(i => _steps[i].WritesRemote && manifest.GetStepStatus(i + 1) != GmudStepStatus.Completed, -1);
                if (pendingWriteStep >= 0)
                {
                    return Stop(GmudRunOutcome.Failed, manifest, number,
                        $"A etapa {pendingWriteStep + 1} ({_steps[pendingWriteStep].Name}) ainda não foi executada de verdade (status: " +
                        $"{manifest.GetStepStatus(pendingWriteStep + 1)}). Rode com --from-step {pendingWriteStep + 1}.");
                }

                if (!request.ConfirmWrite)
                {
                    return Stop(GmudRunOutcome.ConfirmationRequired, manifest, number,
                        $"A etapa {number} ({step.Name}) escreve no Azure DevOps. Revise o plano com --dry-run e rode novamente com --confirm-write.");
                }

                if (!askedConfirmationInThisRun || step is IGmudConfirmationCheckpoint)
                {
                    askedConfirmationInThisRun = true;
                    if (!await _confirmation.ConfirmAsync(number, step.Name, step.DescribePendingWrites(manifest), cancellationToken))
                    {
                        return Stop(GmudRunOutcome.Cancelled, manifest, number, "Escrita cancelada pelo operador — nada foi alterado nesta etapa.");
                    }
                }
            }

            var context = new GmudStepContext
            {
                Input = request.Input,
                DryRun = request.DryRun,
                StepNumber = number,
                GmudDirectory = gmudDirectory,
                GmudDirectoryDisplay = $"{_workspaceDisplayRoot}/{request.GmudId}",
                Logger = logger,
                Clock = _clock
            };

            var startedAt = _clock.GetUtcNow();
            var stopwatch = Stopwatch.StartNew();
            string status;
            string? message = null;
            GmudRunOutcome? stopOutcome = null;

            try
            {
                manifest = await step.ExecuteAsync(manifest, context, cancellationToken);
                status = request.DryRun && step.WritesRemote ? GmudStepStatus.Simulated : GmudStepStatus.Completed;
            }
            catch (GmudBlockedException ex)
            {
                status = GmudStepStatus.Blocked;
                message = ex.Message;
                stopOutcome = GmudRunOutcome.Blocked;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Validação, HTTP, recusa do Azure Repos etc.: registra e para — nunca segue com estado ambíguo.
                status = GmudStepStatus.Failed;
                message = ex is HttpRequestException http
                    ? $"Falha ao comunicar com o Azure ({(http.StatusCode is null ? "sem status" : (int)http.StatusCode)}): {http.Message}"
                    : ex.Message;
                stopOutcome = GmudRunOutcome.Failed;
            }

            stopwatch.Stop();
            manifest.SetStepStatus(number, status);
            manifest.Executions.Add(new ExecutionEntry
            {
                Step = number,
                Name = step.Name,
                StartedAt = startedAt,
                FinishedAt = _clock.GetUtcNow(),
                DurationMs = stopwatch.ElapsedMilliseconds,
                Result = status,
                DryRun = request.DryRun,
                Message = message
            });
            _store.Save(manifest);
            logger.StepFinished(number, step.Name, status, stopwatch.Elapsed, message);

            if (stopOutcome is { } outcome)
            {
                return new GmudRunResult(outcome, manifest, number, message);
            }
        }

        return new GmudRunResult(request.DryRun ? GmudRunOutcome.DryRunCompleted : GmudRunOutcome.Completed, manifest, null, null);

        GmudRunResult Stop(GmudRunOutcome outcome, GmudManifest current, int stepNumber, string reason)
        {
            _store.Save(current);
            return new GmudRunResult(outcome, current, stepNumber, reason);
        }
    }
}

/// <summary>Marca uma etapa que pede nova confirmação mesmo que outra já tenha sido confirmada na mesma execução (ex: criar releases).</summary>
public interface IGmudConfirmationCheckpoint
{
}
