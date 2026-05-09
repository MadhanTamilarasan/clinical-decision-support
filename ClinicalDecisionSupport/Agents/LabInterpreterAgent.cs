using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

namespace ClinicalDecisionSupport.Agents
{
    public class LabInterpreterAgent
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;

        public LabInterpreterAgent(Kernel kernel)
        {
            _kernel = kernel;
            _chatService = kernel.GetRequiredService<IChatCompletionService>();
        }

        [KernelFunction("interpret_labs")]
        [Description("Analyzes lab results in context of differential diagnoses to confirm or refute hypotheses and flag critical values.")]
        public async Task<string> InterpretLabsAsync(
            [Description("Normalized patient clinical context JSON")]
            string patientContext,
            [Description("Retrieved lab reference guidelines from search")]
            string retrievedLabGuidelines)
        {
            var prompt = $@"
You are a Lab Interpreter clinical agent.

Task:
Analyze the patient's lab results in context of their clinical presentation. For each lab value:
1. State whether it is normal, abnormal, or critical.
2. Indicate which differential diagnoses it supports or refutes.
3. Flag any critical values that require immediate attention.

Rules:
- Use only the provided data.
- Do not prescribe medications.
- Do not claim certainty.
- Output the result as a structured Markdown Table for 'Lab Findings' and a separate list for 'Critical Alerts'.

Columns for Lab Findings Table:
- Test Name
- Value
- Status (Normal | Abnormal | Critical)
- Interpretation
- Clinical Correlation (Supports/Refutes)

At the very end of your response, you MUST include a `<structured_data>` XML block containing a valid JSON object summarizing your conclusions, confidence level, and key findings. 
Example:
<structured_data>
{{ ""confidence"": 85, ""conclusions"": [...] }}
</structured_data>

Patient Context:
{patientContext}

Retrieved Lab Guidelines:
{retrievedLabGuidelines}
";

            var response = await _chatService.GetChatMessageContentAsync(prompt);
            return response.Content ?? string.Empty;
        }
    }
}

