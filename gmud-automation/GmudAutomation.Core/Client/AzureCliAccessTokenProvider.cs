using System.Diagnostics;
using System.Text.Json;

namespace GmudAutomation.Core.Client;

/// <summary>
/// Obtém tokens de acesso reaproveitando a sessão já autenticada do Azure CLI (`az login`) do usuário —
/// dispensa gerar e colar um token manualmente a cada execução. O token é cacheado em memória até ~2
/// minutos antes de expirar, quando é renovado automaticamente via novo `az account get-access-token`.
/// </summary>
public sealed class AzureCliAccessTokenProvider : IAzureAccessTokenProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresOn = DateTimeOffset.MinValue;

    public async Task<string> GetAccessTokenAsync(string resource, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresOn.AddMinutes(-2))
            {
                return _cachedToken;
            }

            var (token, expiresOn) = await RunAzCliAsync(resource, cancellationToken);
            _cachedToken = token;
            _expiresOn = expiresOn;
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<(string Token, DateTimeOffset ExpiresOn)> RunAzCliAsync(string resource, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "az.cmd" : "az",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("account");
        startInfo.ArgumentList.Add("get-access-token");
        startInfo.ArgumentList.Add("--resource");
        startInfo.ArgumentList.Add(resource);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("json");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o Azure CLI (\"az\"). Verifique se está instalado e no PATH.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Falha ao obter token via Azure CLI. Rode \"az login\" com a conta que tem acesso ao recurso e tente novamente. Detalhe: {stderr}");
        }

        using var document = JsonDocument.Parse(stdout);
        var token = document.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("O Azure CLI não retornou um accessToken.");

        var expiresOn = document.RootElement.TryGetProperty("expiresOn", out var expiresOnElement)
            && DateTimeOffset.TryParse(expiresOnElement.GetString(), out var parsedExpiresOn)
                ? parsedExpiresOn
                : DateTimeOffset.UtcNow.AddMinutes(50);

        return (token, expiresOn);
    }
}
