using Microsoft.AspNetCore.SignalR;
using ClinicalDecisionSupport.Orchestration;

namespace ClinicalDecisionSupport.Hubs;

public class ClinicalAssessmentHub : Hub
{
    private readonly ClinicalOrchestrator _orchestrator;

    public ClinicalAssessmentHub(ClinicalOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task JoinAssessmentGroup(string patientId, string clinicianName = "Unknown", string clinicianRole = "Unknown")
    {
        // Use patientId as the group name/assessment context for this simplified demo
        var assessmentId = patientId; 
        await Groups.AddToGroupAsync(Context.ConnectionId, assessmentId);
        
        // Trigger the orchestrator in the background so the hub call returns immediately
        _ = _orchestrator.RunClinicalAssessmentAsync(patientId, assessmentId, clinicianName, clinicianRole);
    }
}
