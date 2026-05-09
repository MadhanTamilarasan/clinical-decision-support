namespace ClinicalDecisionSupport.Models;

public enum AgentStage
{
    Pending,
    Started,
    Completed,
    SafetyAlert,
    ConsensusCompleted,
    Failed
}

public class AgentProgressEvent
{
    public string AssessmentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public AgentStage Stage { get; set; }
    public string? Message { get; set; }
    public string? Payload { get; set; } // JSON output (optional)
}