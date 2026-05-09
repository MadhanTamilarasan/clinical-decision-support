using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ClinicalDecisionSupport.Services
{
    public class GuardrailService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GuardrailService> _logger;

        public GuardrailService(HttpClient httpClient, ILogger<GuardrailService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<GuardrailResponse> EvaluateSafetyAsync(string patientContext, string proposedTreatment)
        {
            try
            {
                var payload = new
                {
                    PatientContext = patientContext,
                    ProposedTreatment = proposedTreatment
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                _logger.LogInformation($"Sending request to Guardrail. Payload length: {jsonPayload.Length}");
                
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                
                // Use a relative path without a leading slash to avoid double-slash issues with BaseAddress
                var response = await _httpClient.PostAsync("api/EvaluateSafety", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Guardrail Function returned {response.StatusCode}: {errorContent}");
                }

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<GuardrailResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                return result ?? new GuardrailResponse { Status = "WARN", Reason = "Failed to deserialize Guardrail response." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call Guardrail Azure Function.");
                return new GuardrailResponse { Status = "WARN", Reason = $"Guardrail service unavailable: {ex.Message}" };
            }
        }
    }

    public class GuardrailResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("reason")]
        [System.Text.Json.Serialization.JsonInclude]
        public string Reason { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("reasoning")]
        public string Reasoning { set => Reason = value; }

        [System.Text.Json.Serialization.JsonPropertyName("rationale")]
        public string Rationale { set => Reason = value; }
    }
}
