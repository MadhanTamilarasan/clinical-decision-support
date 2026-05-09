using ClinicalDecisionSupport.UI.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClinicalDecisionSupport.UI.Services;

public class ClinicalAssessmentSignalRService
{
    private HubConnection? _connection;

    public event Action<AgentProgressEvent>? OnProgress;

    // ── Shared assessment state ──────────────────────────────────────────────
    // Written by AssessmentRun when ConsensusCompleted fires.
    // Read by AssessmentResult on OnInitialized (snapshot approach).

    /// <summary>The most recently completed consensus assessment, or null if not yet run.</summary>
    public FinalAssessmentModel? CurrentAssessment { get; private set; }

    /// <summary>Stores the parsed consensus model before navigating to the result page.</summary>
    public void StoreAssessment(FinalAssessmentModel model)
        => CurrentAssessment = model;

    // ── SignalR connection ───────────────────────────────────────────────────

    public async Task ConnectAsync(string baseUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/clinicalAssessmentHub")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<AgentProgressEvent>("AgentProgress", evt =>
        {
            OnProgress?.Invoke(evt);
        });

        await _connection.StartAsync();
    }

    public async Task JoinAssessmentAsync(string assessmentId)
    {
        if (_connection == null)
            throw new InvalidOperationException("SignalR not connected.");

        await _connection.InvokeAsync("JoinAssessment", assessmentId);
    }
}