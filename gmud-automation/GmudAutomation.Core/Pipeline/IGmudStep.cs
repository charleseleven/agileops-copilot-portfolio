using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline;

/// <summary>
/// Uma etapa do fluxo automatizado de GMUD. Todas seguem o mesmo contrato <c>executar(manifesto) → manifesto</c>,
/// são idempotentes e podem ser reexecutadas isoladamente (<c>--from-step N</c>).
/// </summary>
public interface IGmudStep
{
    /// <summary>Chave usada em <see cref="Configuration.GmudConfig.Steps"/> para registrar/reordenar a etapa.</summary>
    string Key { get; }

    string Name { get; }

    /// <summary>A etapa cria ou altera algo no Azure (branches, commits). Exige confirmação fora do dry-run.</summary>
    bool WritesRemote { get; }

    /// <summary>
    /// A etapa sabe rodar em dry-run (calcula o plano sem escrever). Etapas de escrita que não suportam
    /// dry-run — criar releases, acompanhar build — são puladas nesse modo.
    /// </summary>
    bool SupportsDryRun { get; }

    /// <summary>Resumo do que a etapa vai escrever, mostrado ao operador antes da confirmação.</summary>
    string DescribePendingWrites(GmudManifest manifest) => string.Empty;

    Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default);
}

/// <summary>Dados de execução compartilhados pelas etapas.</summary>
public sealed class GmudStepContext
{
    /// <summary>Link ou ID informado em <c>gmud run</c>.</summary>
    public required string Input { get; init; }

    public required bool DryRun { get; init; }

    /// <summary>Posição da etapa no pipeline configurado (1-based) — usada no log e no manifesto.</summary>
    public int StepNumber { get; init; }

    /// <summary>Pasta da GMUD (<c>gmuds/&lt;id&gt;</c>) — anexos e relatórios são gravados aqui.</summary>
    public required string GmudDirectory { get; init; }

    /// <summary>Caminho da pasta da GMUD como deve aparecer no manifesto (relativo, com "/").</summary>
    public required string GmudDirectoryDisplay { get; init; }

    public required IGmudLogger Logger { get; init; }

    public TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>Título/entrada fora do padrão: o fluxo falha com a indicação do campo inválido.</summary>
public sealed class GmudValidationException : Exception
{
    public string Field { get; }

    public GmudValidationException(string field, string message) : base(message)
    {
        Field = field;
    }
}

/// <summary>
/// O fluxo precisa de uma decisão do operador (App Service fora do padrão, branch ausente, conflito ambíguo).
/// Regra: em dúvida, parar e perguntar — nunca adivinhar.
/// </summary>
public sealed class GmudBlockedException : Exception
{
    public GmudBlockedException(string message) : base(message)
    {
    }
}
