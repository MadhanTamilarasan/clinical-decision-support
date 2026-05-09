namespace ClinicalDecisionSupport.UI.Models;

public class LabFindingModel
{
    public string Test { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Normal | Abnormal | Critical
    public string Interpretation { get; set; } = string.Empty;

    public List<string> SupportsHypotheses { get; set; } = new();
    public List<string> RefutesHypotheses { get; set; } = new();
}
