using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

namespace ClinicalDecisionSupport.Agents
{
    public class SymptomsAnalyzerAgent
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;

        public SymptomsAnalyzerAgent(Kernel kernel)
        {
            _kernel = kernel;
            _chatService = kernel.GetRequiredService<IChatCompletionService>();
        }

        [KernelFunction("analyze_symptoms")]
        [Description("Analyzes patient symptoms and returns ranked differential diagnoses.")]
        public async Task<string> AnalyzeSymptomsAsync(
            [Description("Normalized patient clinical context JSON")]
            string patientContext,
            [Description("Retrieved clinical conditions from search")]
            string retrievedConditions,
            [Description("Retrieved ICD-10 codes from search")]
            string retrievedIcdCodes)
        {
            var prompt = $@"
You are a Symptoms Analyzer clinical agent.

Task:
Analyze the patient context and retrieved clinical knowledge to produce a ranked differential diagnosis (top 3).

Rules:
Use only the provided data.
Do not prescribe medications.
Do not claim certainty.
Output the result as a structured Markdown Table with the following columns:
- Rank
- Diagnosis
- Confidence (0-100%)
- Key Evidence (bullet points)

At the very end of your response, you MUST include a `<structured_data>` XML block containing a valid JSON object summarizing your conclusions, confidence level, and key findings. 
Example:
<structured_data>
{{ ""confidence"": 85, ""conclusions"": [...] }}
</structured_data>

Patient Context:
{patientContext}

Retrieved Clinical Knowledge:
{retrievedConditions}

Retrieved ICD-10 Codes:
{retrievedIcdCodes}
";

            var response = await _chatService.GetChatMessageContentAsync(prompt);
            return response.Content ?? string.Empty;
        }
    }
}