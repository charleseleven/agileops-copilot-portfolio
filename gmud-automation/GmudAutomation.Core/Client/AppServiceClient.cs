using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GmudAutomation.Core.Client;

/// <summary>
/// Consulta a tag da imagem em execução em um Azure App Service (containerizado) via Azure Resource Manager,
/// lendo o campo linuxFxVersion (formato "DOCKER|repositorio/imagem:tag") da configuração do site (Task 197).
/// </summary>
public sealed partial class AppServiceClient : IAppServiceClient
{
    private const string ManagementResource = "https://management.azure.com/";
    private const string ResourcesApiVersion = "2021-04-01";

    [GeneratedRegex(@":(?<tag>[^:|]+)$")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"/resourceGroups/(?<rg>[^/]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceGroupRegex();

    private readonly HttpClient _httpClient;
    private readonly AppServiceClientOptions _options;
    private readonly IAzureAccessTokenProvider _tokenProvider;

    public AppServiceClient(HttpClient httpClient, AppServiceClientOptions options, IAzureAccessTokenProvider tokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

        _httpClient.BaseAddress ??= new Uri(ManagementResource);
    }

    public async Task<string> GetCurrentTagAsync(string siteName, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(ManagementResource, cancellationToken);
        var resourceGroup = await ResolveResourceGroupAsync(siteName, accessToken, cancellationToken)
            ?? throw new InvalidOperationException($"App Service \"{siteName}\" não encontrado na subscription configurada.");

        var linuxFxVersion = await GetLinuxFxVersionAsync(siteName, resourceGroup, accessToken, cancellationToken);
        var match = TagRegex().Match(linuxFxVersion);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Não foi possível extrair a tag da configuração \"{linuxFxVersion}\" do site \"{siteName}\".");
        }

        return match.Groups["tag"].Value;
    }

    public async Task<string?> GetContainerImageAsync(string siteName, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(ManagementResource, cancellationToken);
        var resourceGroup = await ResolveResourceGroupAsync(siteName, accessToken, cancellationToken);
        if (resourceGroup is null)
        {
            return null;
        }

        var linuxFxVersion = await GetLinuxFxVersionAsync(siteName, resourceGroup, accessToken, cancellationToken);
        var separator = linuxFxVersion.IndexOf('|');
        return separator >= 0 ? linuxFxVersion[(separator + 1)..] : linuxFxVersion;
    }

    public async Task<IReadOnlyList<string>> FindSitesAsync(string nameFragment, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(ManagementResource, cancellationToken);

        // O filtro do ARM só aceita substringof em "name"; a comparação sem maiúsculas é refeita aqui
        // porque nomes de App Service são sempre minúsculos, mas o fragmento pode vir do título da GMUD.
        var fragment = nameFragment.ToLowerInvariant().Replace("'", "''", StringComparison.Ordinal);
        var filter = Uri.EscapeDataString($"resourceType eq 'Microsoft.Web/sites' and substringof('{fragment}', name)");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"subscriptions/{_options.SubscriptionId}/resources?$filter={filter}&api-version={ResourcesApiVersion}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.GetProperty("value").EnumerateArray()
            .Select(v => v.GetProperty("name").GetString() ?? string.Empty)
            .Where(n => n.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string> GetLinuxFxVersionAsync(string siteName, string resourceGroup, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"subscriptions/{_options.SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{siteName}/config/web?api-version={_options.ApiVersion}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.GetProperty("properties").GetProperty("linuxFxVersion").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Descobre o Resource Group de um App Service pelo nome, via Azure Resource Manager — evita que o
    /// usuário precise informar manualmente o RG de cada site (cenário comum: um App Service por cliente
    /// final por ambiente, com RGs diferentes entre si). Retorna null quando o site não existe.
    /// </summary>
    private async Task<string?> ResolveResourceGroupAsync(string siteName, string accessToken, CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"resourceType eq 'Microsoft.Web/sites' and name eq '{siteName}'");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"subscriptions/{_options.SubscriptionId}/resources?$filter={filter}&api-version={ResourcesApiVersion}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var values = document.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
        {
            return null;
        }

        var resourceId = values[0].GetProperty("id").GetString() ?? string.Empty;
        var match = ResourceGroupRegex().Match(resourceId);

        if (!match.Success)
        {
            throw new InvalidOperationException($"Não foi possível extrair o resource group de \"{resourceId}\".");
        }

        return match.Groups["rg"].Value;
    }
}
