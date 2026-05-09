namespace ClinicalDecisionSupport.UI.Models;

public class DrugInteractionResult
{
    public string Drug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Safe | Unsafe | Caution
    public List<DrugInteraction> Interactions { get; set; } = new();
    public string? AllergyConflict { get; set; }
    public List<string> Contraindications { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;
}

public class DrugInteraction
{
    public string InteractsWith { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Major | Moderate | Minor
    public string Description { get; set; } = string.Empty;
}