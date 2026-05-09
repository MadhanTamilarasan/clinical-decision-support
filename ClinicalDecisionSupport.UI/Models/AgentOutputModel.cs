namespace ClinicalDecisionSupport.UI.Models;

public class AgentOutputModel
{
    public string AgentName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending | Running | Completed
    public string ConfidenceLevel { get; set; } = string.Empty;

    public List<string> Evidence { get; set; } = new();
}
