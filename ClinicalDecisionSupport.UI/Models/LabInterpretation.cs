namespace ClinicalDecisionSupport.UI.Models;

public class LabInterpretation
{
    public List<LabFinding> LabFindings { get; set; } = new();
    public List<string> CriticalAlerts { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class LabFinding
{
    public string Test { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Normal | Abnormal | Critical
    public string Interpretation { get; set; } = string.Empty;
}