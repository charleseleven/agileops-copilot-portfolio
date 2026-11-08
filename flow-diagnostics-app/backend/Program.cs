using AgilePredict.Models.Configuration;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Polly;
using Polly.Extensions.Http;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = true;

        // Serializa enums como string (ex.: "Critical") em vez do índice numérico.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// ===== LLM (GROQ) =====
builder.Services.Configure<LlmConfiguration>(
    builder.Configuration.GetSection(LlmConfiguration.SectionName));

builder.Services.AddOptions<LlmConfiguration>()
    .Bind(builder.Configuration.GetSection(LlmConfiguration.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<ILlmIntegrationService, LlmIntegrationService>((serviceProvider, client) =>
    {
        var config = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmConfiguration>>()
            .Value;

        client.BaseAddress = new Uri(config.ApiUrl);
        client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
        client.DefaultRequestHeaders.Add("User-Agent", "AgileOpsCopilot-FlowDiagnostics/1.0");
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy())
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

// ===== DIAGNÓSTICO PREDITIVO DE FLUXO (Epic #209 original) =====
builder.Services.Configure<AzureDevOpsConfiguration>(
    builder.Configuration.GetSection(AzureDevOpsConfiguration.SectionName));

// Organization/Project/PersonalAccessToken não são [Required]: o usuário pode analisar qualquer
// organização à qual tenha acesso, informada por requisição (URL do Azure Boards colada, ou campos
// explícitos). Os valores aqui são apenas um fallback opcional de conveniência.
builder.Services.AddOptions<AzureDevOpsConfiguration>()
    .Bind(builder.Configuration.GetSection(AzureDevOpsConfiguration.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<FlowAnalyticsConfiguration>(
    builder.Configuration.GetSection(FlowAnalyticsConfiguration.SectionName));

builder.Services.AddOptions<FlowAnalyticsConfiguration>()
    .Bind(builder.Configuration.GetSection(FlowAnalyticsConfiguration.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Cliente HTTP nomeado (sem BaseAddress/Authorization fixos): organização, projeto e PAT variam
// por requisição, então cada chamada monta sua própria URL absoluta e seu próprio header
// Authorization (ver AzureDevOpsWorkItemFlowDataSource), evitando vazar o PAT de uma organização
// para requisições concorrentes de outra.
builder.Services.AddHttpClient("AzureDevOps", (serviceProvider, client) =>
    {
        var config = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureDevOpsConfiguration>>()
            .Value;

        client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddScoped<IWorkItemFlowDataSource, AzureDevOpsWorkItemFlowDataSource>();
builder.Services.AddScoped<IAzureDevOpsBoardsUrlParser, AzureDevOpsBoardsUrlParser>();
builder.Services.AddScoped<IFlowMetricsCalculator, FlowMetricsCalculator>();
builder.Services.AddScoped<IFlowInsightGenerator, FlowInsightGenerator>();
builder.Services.AddScoped<IFlowDiagnosticsOrchestrator, FlowDiagnosticsOrchestrator>();

// ===== SPRINT RISK BOT (Feature 8 / PBI 8.1 e 8.2) =====
builder.Services.AddOptions<SprintRiskConfiguration>()
    .Bind(builder.Configuration.GetSection(SprintRiskConfiguration.SectionName))
    .ValidateDataAnnotations()
    .Validate(c => c.CriticalUtilizationThreshold >= c.WarningUtilizationThreshold,
        "CriticalUtilizationThreshold deve ser maior ou igual a WarningUtilizationThreshold")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAzureDevOpsConnectionResolver, AzureDevOpsConnectionResolver>();
builder.Services.AddScoped<ISprintCapacityDataSource, AzureDevOpsSprintCapacityDataSource>();
builder.Services.AddSingleton<ISprintRiskAnalyzer, SprintRiskAnalyzer>();
builder.Services.AddSingleton<ISprintRiskPromptBuilder, SprintRiskPromptBuilder>();
builder.Services.AddScoped<ISprintRiskInsightGenerator, SprintRiskInsightGenerator>();
builder.Services.AddScoped<ISprintRiskBot, SprintRiskBot>();

// ===== CORS (dev only) =====
// Libera o Nuxt dev server (frontend) para consumir a API em desenvolvimento. Qualquer porta em
// localhost/127.0.0.1 é aceita porque o Nuxt troca de porta automaticamente quando a padrão
// já está em uso.
const string DevCorsPolicy = "NuxtDevClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                  return uri.Scheme == "http" && (uri.Host == "localhost" || uri.Host == "127.0.0.1");
              })
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new()
        {
            Title = "AgileOps Copilot — Flow Diagnostics API",
            Version = "v1",
            Description = "Diagnóstico preditivo de gargalos de fluxo via API do Azure DevOps + IA."
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("AgileOps Copilot — Flow Diagnostics API")
            .WithTheme(ScalarTheme.Purple)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

app.UseAuthorization();
app.MapControllers();

app.Run();

// ===== POLLY RESILIENCE POLICIES =====

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30));
}
