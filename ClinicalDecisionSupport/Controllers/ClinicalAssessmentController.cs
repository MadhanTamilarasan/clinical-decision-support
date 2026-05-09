using ClinicalDecisionSupport.Orchestration;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/clinical-assessment")]
public class ClinicalAssessmentController : ControllerBase
{
    private readonly ClinicalOrchestrator _orchestrator;

    public ClinicalAssessmentController(ClinicalOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("run/{patientId}")]
    public async Task<IActionResult> RunAssessment(
        string patientId, 
        [FromQuery] string assessmentId,
        [FromQuery] string clinicianName = "Unknown",
        [FromQuery] string clinicianRole = "Unknown")
    {
        if (string.IsNullOrEmpty(assessmentId)) assessmentId = Guid.NewGuid().ToString();

        // Start assessment in the background
        _ = _orchestrator.RunClinicalAssessmentAsync(patientId, assessmentId, clinicianName, clinicianRole);

        return Ok(new { assessmentId });
    }

    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatient(string patientId)
    {
        try
        {
            var patientContext = await _orchestrator.GetPatientContextAsync(patientId);
            return Content(patientContext, "application/json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to fetch patient context: {ex.Message}");
            return StatusCode(500, new { error = "Failed to fetch patient context from FHIR." });
        }
    }

    [HttpGet("history/{assessmentId}")]
    public IActionResult GetHistory(string assessmentId)
    {
        var history = _orchestrator.GetAssessmentHistory(assessmentId);
        if (history == null || history.Count == 0) return NotFound();
        return Ok(history);
    }

    [HttpGet("result/{patientId}")]
    public async Task<IActionResult> GetResult(string patientId)
    {
        // For the demo, we'll try to find the latest assessment file for this patient
        try
        {
            var storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assessments");
            if (!Directory.Exists(storagePath)) return NotFound("No assessments found.");

            var files = Directory.GetFiles(storagePath, $"assessment_*.json")
                .Select(f => new { Path = f, Time = System.IO.File.GetCreationTime(f) })
                .OrderByDescending(f => f.Time)
                .ToList();

            foreach (var file in files)
            {
                var content = await System.IO.File.ReadAllTextAsync(file.Path);
                var doc = System.Text.Json.JsonDocument.Parse(content);
                if (doc.RootElement.GetProperty("PatientId").GetString() == patientId)
                {
                    return Content(content, "application/json");
                }
            }

            return NotFound($"No assessments found for patient {patientId}.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("intake")]
    public async Task<IActionResult> Intake([FromBody] ClinicalDecisionSupport.Models.PatientIntakeRequest request)
    {
        try
        {
            string customId = $"cdss-patient-{DateTime.Now.Ticks % 10000}";
            string patientId;

            try {
                patientId = await _orchestrator.CreatePatientAsync(
                    request.FirstName, 
                    request.LastName, 
                    request.Gender, 
                    request.BirthDate.ToString("yyyy-MM-dd"),
                    customId);
            } catch (Exception ex) {
                return StatusCode(500, new { error = $"Failed to create FHIR Patient record: {ex.Message}" });
            }

            // Vitals
            try {
                if (!string.IsNullOrEmpty(request.BloodPressure))
                    await _orchestrator.CreateObservationAsync(patientId, "85354-9", "Blood Pressure", 0, request.BloodPressure, "vital-signs");

                if (request.HeartRate > 0)
                    await _orchestrator.CreateObservationAsync(patientId, "8867-4", "Heart rate", request.HeartRate, "bpm", "vital-signs");

                if (request.Temperature > 0)
                    await _orchestrator.CreateObservationAsync(patientId, "8310-5", "Body temperature", request.Temperature, "degF", "vital-signs");
            } catch (Exception ex) {
                Console.WriteLine($"[Warning] Vitals save failed: {ex.Message}");
            }

            // Complaint & Symptoms
            try {
                if (!string.IsNullOrEmpty(request.ChiefComplaint))
                    await _orchestrator.CreateObservationAsync(patientId, "10154-3", "Chief complaint", 0, request.ChiefComplaint, "exam");

                if (!string.IsNullOrEmpty(request.Symptoms))
                    await _orchestrator.CreateObservationAsync(patientId, "75325-1", "Symptoms", 0, request.Symptoms, "exam");
            } catch (Exception ex) {
                Console.WriteLine($"[Warning] Complaint/Symptoms save failed: {ex.Message}");
            }

            // Allergies
            try {
                foreach (var allergy in request.Allergies.Where(a => !string.IsNullOrEmpty(a)))
                {
                    await _orchestrator.CreateAllergyIntoleranceAsync(patientId, allergy);
                }
            } catch (Exception ex) {
                Console.WriteLine($"[Warning] Allergies save failed: {ex.Message}");
            }

            // Labs
            try {
                foreach (var lab in request.Labs)
                {
                    await _orchestrator.CreateObservationAsync(patientId, "75325-1", lab.TestName, lab.Value, lab.Unit, "laboratory");
                }
            } catch (Exception ex) {
                Console.WriteLine($"[Warning] Labs save failed: {ex.Message}");
            }

            // Medications
            try {
                foreach (var med in request.Medications)
                {
                    var dosageString = $"{med.Dosage} {med.Frequency}";
                    await _orchestrator.CreateMedicationStatementAsync(patientId, med.Name, dosageString);
                }
            } catch (Exception ex) {
                Console.WriteLine($"[Warning] Medications save failed: {ex.Message}");
            }

            return Ok(new { patientId, customId, message = $"Patient {customId} successfully indexed in FHIR." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Critical] Intake total failure: {ex.Message}");
            return StatusCode(500, new { error = "Internal server error during clinical intake." });
        }
    }
}