using System.ComponentModel.DataAnnotations;

namespace AgilePredict.Models.Configuration
{
    /// <summary>
    /// Configurações para integração com API LLM
    /// </summary>
    public class LlmConfiguration
    {
        /// <summary>
        /// Seção no appsettings.json
        /// </summary>
        public const string SectionName = "LlmSettings";

        /// <summary>
        /// URL base da API da LLM
        /// </summary>
        [Required(ErrorMessage = "A URL da API é obrigatória")]
        [Url(ErrorMessage = "A URL deve ser válida")]
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>
        /// Chave de API para autenticação (NÃO deve ser hardcoded)
        /// Deve vir do Secret Manager (dev) ou Environment Variables (prod)
        /// </summary>
        [Required(ErrorMessage = "A API Key é obrigatória")]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Modelo padrão da LLM a ser utilizado
        /// Groq: meta-llama/llama-4-scout-17b-16e-instruct (gratuito)
        /// </summary>
        public string DefaultModel { get; set; } = "meta-llama/llama-4-scout-17b-16e-instruct";

        /// <summary>
        /// Timeout para requisições HTTP (em segundos)
        /// </summary>
        [Range(5, 300, ErrorMessage = "O timeout deve estar entre 5 e 300 segundos")]
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Número máximo de tentativas em caso de falha
        /// </summary>
        [Range(1, 5, ErrorMessage = "MaxRetries deve estar entre 1 e 5")]
        public int MaxRetries { get; set; } = 3;
    }
}
