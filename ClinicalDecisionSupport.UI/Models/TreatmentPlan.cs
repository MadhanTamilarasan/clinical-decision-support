namespace ClinicalDecisionSupport.UI.Models;

public class TreatmentPlan
{
    public string Diagnosis { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<TreatmentOption> TreatmentOptions { get; set; } = new();
    public string GuidelineSource { get; set; } = string.Empty;
    public List<string> Cautions { get; set; } = new();
    public string Disclaimer { get; set; } = string.Empty;
}

public class TreatmentOption
{
    public string Option { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string Monitoring { get; set; } = string.Empty;
    public string FollowUp { get; set; } = string.Empty;
}