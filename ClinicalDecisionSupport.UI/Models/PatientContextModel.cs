using System.Text.Json.Serialization;

namespace ClinicalDecisionSupport.UI.Models;

public class PatientContextModel
{
    [JsonPropertyName("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [JsonPropertyName("demographics")]
    public DemographicsModel Demographics { get; set; } = new();

    [JsonPropertyName("conditions")]
    public List<string> Conditions { get; set; } = new();

    [JsonPropertyName("observations")]
    public Dictionary<string, string> Observations { get; set; } = new();

    [JsonPropertyName("labs")]
    public List<PatientLabResult> Labs { get; set; } = new();

    [JsonPropertyName("medications")]
    public List<string> Medications { get; set; } = new();

    [JsonPropertyName("allergies")]
    public List<string> Allergies { get; set; } = new();
}

public class PatientLabResult
{
    [JsonPropertyName("testName")]
    public string TestName { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = string.Empty;
}

public class DemographicsModel
{
    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("birthDate")]
    public string BirthDate { get; set; } = string.Empty;
}
