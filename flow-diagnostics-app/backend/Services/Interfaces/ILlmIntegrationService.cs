using AgilePredict.Models.DTOs;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Interface para integração com API de LLM (Large Language Model)
    /// </summary>
    public interface ILlmIntegrationService
    {
        /// <summary>
        /// Envia um prompt para a API da LLM e retorna a resposta processada
        /// </summary>
        /// <param name="prompt">Texto do prompt a ser enviado para a IA</param>
        /// <param name="cancellationToken">Token de cancelamento para operações assíncronas</param>
        /// <returns>Resposta processada da LLM</returns>
        Task<LlmResponse> SendPromptAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Valida se a conexão com a API da LLM está funcional
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento</param>
        /// <returns>True se a conexão está OK, False caso contrário</returns>
        Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Envia um prompt com parâmetros avançados de configuração
        /// </summary>
        /// <param name="request">Objeto com prompt e configurações adicionais</param>
        /// <param name="cancellationToken">Token de cancelamento</param>
        /// <returns>Resposta detalhada da LLM</returns>
        Task<LlmResponse> SendPromptWithOptionsAsync(LlmRequest request, CancellationToken cancellationToken = default);
    }
}
