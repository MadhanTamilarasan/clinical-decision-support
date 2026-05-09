namespace ClinicalDecisionSupport.Models;

public class PatientIntakeRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateOnly BirthDate { get; set; }
    public string BloodPressure { get; set; } = string.Empty;
    public double HeartRate { get; set; }
    public double Temperature { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public List<string> Allergies { get; set; } = new();
    public List<LabEntry> Labs { get; set; } = new();
    public List<MedicationEntry> Medications { get; set; } = new();
}

public class LabEntry
{
    public string TestName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class MedicationEntry
{
    public string Name { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
}
