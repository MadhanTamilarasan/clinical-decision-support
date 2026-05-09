using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

namespace ClinicalDecisionSupport.Agents
{
    public class TreatmentPlannerAgent
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;

        public TreatmentPlannerAgent(Kernel kernel)
        {
            _kernel = kernel;
            _chatService = kernel.GetRequiredService<IChatCompletionService>();
        }

        [KernelFunction("plan_treatment")]
        [Description("Recommends guideline-aligned treatment options for clinician review. Never prescribes specific medications or exact dosages.")]
        public async Task<string> PlanTreatmentAsync(
            [Description("Normalized patient clinical context JSON")]
            string patientContext,
            [Description("Ranked differential diagnoses JSON from Symptoms Analyzer")]
            string differentialDiagnoses,
            [Description("Lab interpretation JSON from Lab Interpreter")]
            string labInterpretation,
            [Description("Retrieved clinical guidelines from search")]
            string retrievedGuidelines)
        {
            var prompt = $@"
You are a Treatment Planner clinical agent.

Task:
Based on the patient context, ranked differential diagnoses, lab interpretation, and retrieved clinical guidelines, recommend treatment options for each diagnosis. You are NOT prescribing — you are presenting guideline-aligned options for clinician review.

Rules:
- Use only the provided data.
- NEVER prescribe specific medications with exact dosages.
- NEVER auto-prescribe. All recommendations require physician review.
- Phrase recommendations as: ""Guidelines suggest..."", ""Clinicians may consider..."", ""Typical management involves...""
- State that dosing should follow institutional protocols.
- Include monitoring considerations and follow-up recommendations.
- Cite the guideline source for each recommendation.
- Flag cautions based on patient allergies and current medications.
- Output the result as a structured Markdown 'Treatment Roadmap' table.

Columns for Treatment Roadmap:
- Target Diagnosis
- Treatment Strategy/Option
- Rationale & Guideline Support
- Monitoring Requirements
- Recommended Follow-up

At the very end of your response, you MUST include a `<structured_data>` XML block containing a valid JSON object summarizing your conclusions, confidence level, and key findings. 
Example:
<structured_data>
{{ ""confidence"": 85, ""conclusions"": [...] }}
</structured_data>

Patient Context:
{patientContext}

Differential Diagnoses (from Symptoms Analyzer):
{differentialDiagnoses}

Lab Interpretation (from Lab Interpreter):
{labInterpretation}

Retrieved Clinical Guidelines:
{retrievedGuidelines}
";

            var response = await _chatService.GetChatMessageContentAsync(prompt);
            return response.Content ?? string.Empty;
        }
    }
}

