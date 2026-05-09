namespace ClinicalDecisionSupport.UI.Models;

public class SymptomDiagnosis
{
    public string Diagnosis { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public List<string> Evidence { get; set; } = new();
}