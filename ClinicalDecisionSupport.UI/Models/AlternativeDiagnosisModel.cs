namespace ClinicalDecisionSupport.UI.Models;

/// <summary>
/// Maps to the <c>alternativeDiagnoses[]</c> array in the backend consensus JSON schema.
/// </summary>
public class AlternativeDiagnosisModel
{
    public string Diagnosis { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
}
