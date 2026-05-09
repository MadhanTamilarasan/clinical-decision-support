namespace ClinicalDecisionSupport.UI.Models;

public class DrugInteractionModel
{
    public string Drug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Safe | Caution | Unsafe

    public List<InteractionDetail> Interactions { get; set; } = new();
    public string? AllergyConflict { get; set; }
    public List<string> Contraindications { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;
}

public class InteractionDetail
{
    public string InteractsWith { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Major | Moderate | Minor
    public string Description { get; set; } = string.Empty;
}