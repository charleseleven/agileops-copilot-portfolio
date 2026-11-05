using System.Text.Encodings.Web;
using System.Text.Json;

namespace GmudAutomation.Core.Pipeline;

/// <summary>Log estruturado do fluxo (rastreabilidade por etapa). Nunca recebe tokens ou credenciais.</summary>
public interface IGmudLogger
{
    void Info(string message, int? step = null);

    void Warn(string message, int? step = null);

    void Error(string message, int? step = null);

    void StepFinished(int step, string name, string result, TimeSpan duration, string? message);
}

/// <summary>Escreve no console (legível) e em <c>execucao.log.jsonl</c> (uma linha JSON por evento).</summary>
public sealed class ConsoleJsonLinesGmudLogger : IGmudLogger, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly TextWriter _console;
    private readonly TextWriter _errorConsole;
    private readonly StreamWriter? _file;
    private readonly TimeProvider _clock;

    public ConsoleJsonLinesGmudLogger(TextWriter console, TextWriter errorConsole, string? logFilePath, TimeProvider? clock = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _errorConsole = errorConsole ?? throw new ArgumentNullException(nameof(errorConsole));
        _clock = clock ?? TimeProvider.System;
        if (logFilePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFilePath))!);
            _file = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
        }
    }

    public void Info(string message, int? step = null) => Write("info", message, step, _console);

    public void Warn(string message, int? step = null) => Write("aviso", message, step, _console);

    public void Error(string message, int? step = null) => Write("erro", message, step, _errorConsole);

    public void StepFinished(int step, string name, string result, TimeSpan duration, string? message)
    {
        _console.WriteLine($"[etapa {step}] {name}: {result} ({duration.TotalSeconds:0.0}s){(message is null ? string.Empty : " — " + message)}");
        _file?.WriteLine(JsonSerializer.Serialize(new
        {
            momento = _clock.GetUtcNow(),
            nivel = "etapa",
            etapa = step,
            nome = name,
            resultado = result,
            duracaoMs = (long)duration.TotalMilliseconds,
            mensagem = message
        }, JsonOptions));
    }

    private void Write(string level, string message, int? step, TextWriter target)
    {
        var prefix = step is null ? string.Empty : $"[etapa {step}] ";
        target.WriteLine(level == "info" ? prefix + message : $"{prefix}{level.ToUpperInvariant()}: {message}");
        _file?.WriteLine(JsonSerializer.Serialize(new { momento = _clock.GetUtcNow(), nivel = level, etapa = step, mensagem = message }, JsonOptions));
    }

    public void Dispose() => _file?.Dispose();
}
