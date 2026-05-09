namespace ClinicalDecisionSupport.UI.Models
{
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
