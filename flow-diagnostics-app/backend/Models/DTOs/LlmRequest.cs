using System.ComponentModel.DataAnnotations;

namespace AgilePredict.Models.DTOs
{
    /// <summary>
    /// Request para envio de prompt à API LLM
    /// </summary>
    public class LlmRequest
    {
        /// <summary>
        /// Prompt/texto a ser enviado para a IA
        /// </summary>
        [Required(ErrorMessage = "O prompt é obrigatório")]
        [StringLength(4000, MinimumLength = 1, ErrorMessage = "O prompt deve ter entre 1 e 4000 caracteres")]
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Temperatura para controle de criatividade (0.0 a 1.0)
        /// </summary>
        [Range(0.0, 1.0, ErrorMessage = "A temperatura deve estar entre 0.0 e 1.0")]
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Número máximo de tokens na resposta
        /// </summary>
        [Range(1, 4000, ErrorMessage = "MaxTokens deve estar entre 1 e 4000")]
        public int MaxTokens { get; set; } = 500;

        /// <summary>
        /// Modelo da LLM a ser utilizado
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Instrução de sistema (System Prompt), enviada como mensagem "system" antes do prompt do usuário.
        /// Usado, por exemplo, para restringir a IA a responder apenas com base em um contexto injetado (RAG).
        /// </summary>
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Definições de tools (function calling) que a LLM pode decidir invocar em vez de responder em texto.
        /// Ver <see cref="AgilePredict.Services.ToolDefinitions"/>.
        /// </summary>
        public IReadOnlyList<object>? Tools { get; set; }
    }
}
