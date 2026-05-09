using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

namespace ClinicalDecisionSupport.Agents
{
    public class DrugInteractionCheckerAgent
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;

        public DrugInteractionCheckerAgent(Kernel kernel)
        {
            _kernel = kernel;
            _chatService = kernel.GetRequiredService<IChatCompletionService>();
        }

        [KernelFunction("check_drug_interactions")]
        [Description("Checks proposed treatments for drug interactions, allergy conflicts, and contraindications.")]
        public async Task<string> CheckDrugInteractionsAsync(
            [Description("Normalized patient clinical context JSON")]
            string patientContext,
            [Description("Proposed treatment plans JSON from Treatment Planner")]
            string proposedTreatments,
            [Description("Retrieved drug interaction data from search")]
            string retrievedDrugData)
        {
            var prompt = $@"
You are a Drug Interaction Checker clinical agent.

Task:
Review the proposed treatment plans against the patient's current medications, allergies, and conditions. Check for drug interactions, allergy conflicts, and contraindications using the retrieved drug data.

Rules:
- Use only the provided data.
- Flag ALL known interactions with severity level.
- Hard-flag dangerous combinations.
- Check patient allergies against proposed drugs.
- Do not auto-approve or auto-reject. Flag risks for clinician decision.
- Output the result as a structured Markdown 'Drug Safety Analysis' table.

Columns for Drug Safety Analysis:
- Proposed Drug
- Safety Status (Safe | Unsafe | Caution)
- Severity (Major | Moderate | Minor | N/A)
- Conflict/Interaction Details
- Clinical Recommendation

At the very end of your response, you MUST include a `<structured_data>` XML block containing a valid JSON object summarizing your conclusions, confidence level, and key findings. 
Example:
<structured_data>
{{ ""confidence"": 85, ""conclusions"": [...] }}
</structured_data>

Patient Context:
{patientContext}

Proposed Treatment Plans (from Treatment Planner):
{proposedTreatments}

Retrieved Drug Interaction Data:
{retrievedDrugData}

Disclaimer: This is a clinical decision support tool. All recommendations require physician review and approval.
";

            var response = await _chatService.GetChatMessageContentAsync(prompt);
            return response.Content ?? string.Empty;
        }
    }
}

