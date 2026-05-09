using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.AspNetCore.SignalR;
using ClinicalDecisionSupport.Hubs;
using ClinicalDecisionSupport.Models;
using ClinicalDecisionSupport.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;


namespace ClinicalDecisionSupport.Orchestration
{
    public class ClinicalOrchestrator
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatService;
        private readonly IHubContext<ClinicalAssessmentHub> _hubContext;
        private readonly ConcurrentDictionary<string, List<AgentProgressEvent>> _history = new();
        private readonly ConcurrentDictionary<string, string> _finalResults = new();
        private readonly string _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assessments");


        private readonly CosmosSessionService _cosmosService;
        private readonly GuardrailService _guardrailService;

        public ClinicalOrchestrator(
            Kernel kernel,
            IHubContext<ClinicalAssessmentHub> hubContext,
            CosmosSessionService cosmosService,
            GuardrailService guardrailService)
        {
            _kernel = kernel;
            _chatService = kernel.GetRequiredService<IChatCompletionService>();
            _hubContext = hubContext;
            _cosmosService = cosmosService;
            _guardrailService = guardrailService;

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }


        public async Task<string> RunClinicalAssessmentAsync(string patientId, string assessmentId, string clinicianName = "Unknown", string clinicianRole = "Unknown")
        {
            try
            {
            await Task.Delay(1000);
            Console.WriteLine($"[Orchestrator] Starting clinical assessment for patient: {patientId}");
            Console.WriteLine(new string('=', 60));

            await _cosmosService.CreateSessionAsync(assessmentId, patientId, clinicianName, clinicianRole);

            // ── Fetch Patient Context (FHIR) ──
            Console.WriteLine("[Orchestrator] Fetching patient context from FHIR...");
            var patientContext = await GetPatientContextAsync(patientId);

            // ══════════════════════════════════════════════════════
            // PHASE 1: Symptoms Analyzer + Lab Interpreter (parallel)
            // ══════════════════════════════════════════════════════
            Console.WriteLine("\n[Orchestrator] Phase 1: Running Symptoms Analyzer + Lab Interpreter in parallel...");

            await NotifyAsync(assessmentId, "Symptoms Analyzer", AgentStage.Started);
            await NotifyAsync(assessmentId, "Lab Interpreter", AgentStage.Started);


            var symptomsTask = RunSymptomsAnalyzerAsync(patientContext);
            var labTask = RunLabInterpreterAsync(patientContext);

            await Task.WhenAll(symptomsTask, labTask);

            var symptomsAnalyzerOutput = symptomsTask.Result;
            var labInterpreterOutput = labTask.Result;

            symptomsAnalyzerOutput = await ProcessAgentOutputAsync("Symptoms Analyzer", symptomsAnalyzerOutput, assessmentId);
            labInterpreterOutput = await ProcessAgentOutputAsync("Lab Interpreter", labInterpreterOutput, assessmentId);

            await NotifyAsync(
                assessmentId,
                "Symptoms Analyzer",
                AgentStage.Completed,
                payload: symptomsAnalyzerOutput);

            await NotifyAsync(
                assessmentId,
                "Lab Interpreter",
                AgentStage.Completed,
                payload: labInterpreterOutput);

            Console.WriteLine("\n===== Symptoms Analyzer Output =====");
            Console.WriteLine(symptomsAnalyzerOutput);
            Console.WriteLine("\n===== Lab Interpreter Output =====");
            Console.WriteLine(labInterpreterOutput);

            // ══════════════════════════════════════════════════════
            // PHASE 2: Treatment Planner
            // ══════════════════════════════════════════════════════
            Console.WriteLine("\n[Orchestrator] Phase 2: Running Treatment Planner...");


            await NotifyAsync(
                assessmentId,
                "Treatment Planner",
                AgentStage.Started);


            var treatmentPlannerOutput = await RunTreatmentPlannerAsync(
                patientContext, symptomsAnalyzerOutput, labInterpreterOutput);

            treatmentPlannerOutput = await ProcessAgentOutputAsync("Treatment Planner", treatmentPlannerOutput, assessmentId);

            await NotifyAsync(
                assessmentId,
                "Treatment Planner",
                AgentStage.Completed,
                payload: treatmentPlannerOutput);

            Console.WriteLine("\n===== Treatment Planner Output =====");
            Console.WriteLine(treatmentPlannerOutput);

            // ══════════════════════════════════════════════════════
            // PHASE 3: Drug Interaction Checker
            // ══════════════════════════════════════════════════════
            Console.WriteLine("\n[Orchestrator] Phase 3: Running Drug Interaction Checker...");

            await NotifyAsync(
                assessmentId,
                "Drug Interaction Checker",
                AgentStage.Started);


            var drugCheckerOutput = await RunDrugInteractionCheckerAsync(
                patientContext, treatmentPlannerOutput);

            drugCheckerOutput = await ProcessAgentOutputAsync("Drug Interaction Checker", drugCheckerOutput, assessmentId);

            Console.WriteLine("\n===== Drug Interaction Checker Output =====");
            Console.WriteLine(drugCheckerOutput);

            await NotifyAsync(
                assessmentId,
                "Drug Interaction Checker",
                AgentStage.Completed,
                payload: drugCheckerOutput);

            // Real Azure Function Guardrail Evaluation
            var guardrailResult = await _guardrailService.EvaluateSafetyAsync(patientContext, treatmentPlannerOutput);
            await _cosmosService.LogGuardrailSampleAsync(assessmentId, "AIGuardrailCheck", guardrailResult.Status, guardrailResult.Reason);
            
            // Broadcast the safety result to the UI
            await NotifyAsync(
                assessmentId, 
                "Safety Guardrail", 
                AgentStage.Completed, 
                message: $"Safety evaluation: {guardrailResult.Status}", 
                payload: JsonSerializer.Serialize(guardrailResult));


            // ══════════════════════════════════════════════════════
            // PHASE 4: Consensus Synthesis
            // ══════════════════════════════════════════════════════
            Console.WriteLine("\n[Orchestrator] Phase 4: Building consensus output...");

            var finalOutput = await BuildConsensusAsync(
                patientId, patientContext,
                symptomsAnalyzerOutput, labInterpreterOutput,
                treatmentPlannerOutput, drugCheckerOutput);

            // Clean the final report as well to ensure no raw data leaks
            finalOutput = await ProcessAgentOutputAsync("Consensus Builder", finalOutput, assessmentId);

            _finalResults[assessmentId] = finalOutput;
            await SaveResultAsync(assessmentId, patientId, finalOutput);
            
            await NotifyAsync(
                assessmentId,
                "Consensus Builder",
                AgentStage.ConsensusCompleted,
                message: "Final Clinical Executive Summary compiled.",
                payload: finalOutput);

            return finalOutput;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Orchestrator] ERROR during assessment: {ex}");
                await NotifyAsync(assessmentId, "Orchestrator Error", AgentStage.Failed, payload: ex.Message);
                return "Failed";
            }
        }

        private async Task<string> ProcessAgentOutputAsync(string agentName, string rawOutput, string assessmentId)
        {
            var extractedMarkdown = rawOutput;
            
            try
            {
                // 1. Extract and log structured data if present
                var match = Regex.Match(rawOutput, @"<structured_data>(.*?)</structured_data>", RegexOptions.Singleline);
                if (match.Success)
                {
                    var jsonStr = match.Groups[1].Value.Trim();
                    // Strip the XML block out
                    extractedMarkdown = rawOutput.Replace(match.Value, "").Trim();
                    
                    await _cosmosService.LogAgentAssessmentAsync(assessmentId, agentName, jsonStr);
                }

                // 2. Clean up common AI "chatter" and technical artifacts
                // Matches any header (# to ######) followed by technical keywords
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"^#+\s*(XML Structured Data|Structured Data|Structured Data Block|Structured Data Output|Summary XML Block|XML Block|Analysis|Rationale|Thinking|Summary).*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                
                // Also catch them if they are just bold or plain text lines at the end
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"\n(XML Structured Data|Structured Data|Structured Data Block|Summary XML Block|XML Block|Summary):?.*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

                extractedMarkdown = Regex.Replace(extractedMarkdown, @"```json.*?```", "", RegexOptions.Singleline);
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"```xml.*?```", "", RegexOptions.Singleline);
                
                // Aggressively remove any remaining raw JSON-like blocks { ... } or XML-like blocks <...> 
                // that appear at the end of the text (common for AI "thinking" or "data" blocks)
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"\n\s*\{.*\}\s*$", "", RegexOptions.Singleline);
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"\n\s*<.*>\s*$", "", RegexOptions.Singleline);
                
                // Remove specific artifacts like "Summary XML Block:" even without header
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"Summary XML Block:.*", "", RegexOptions.IgnoreCase);
                extractedMarkdown = Regex.Replace(extractedMarkdown, @"Structured Data:.*", "", RegexOptions.IgnoreCase);
                
                // 3. Remove trailing filler sentences if the agent had no data
                if (extractedMarkdown.Contains("None identified as no lab results were provided", StringComparison.OrdinalIgnoreCase))
                {
                    extractedMarkdown = extractedMarkdown.Replace("None identified as no lab results were provided. However:", "").Trim();
                }

                extractedMarkdown = extractedMarkdown.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Orchestrator] Failed to clean output for {agentName}: {ex.Message}");
            }
            
            return extractedMarkdown;
        }

        public async Task<string> GetPatientContextAsync(string patientId)
        {
            var patientContextResult = await _kernel.InvokeAsync(
                "FHIR",
                "get_patient_context",
                new KernelArguments { ["patientId"] = patientId }
            );
            return patientContextResult.ToString();
        }

        public async Task<string> CreatePatientAsync(string firstName, string lastName, string gender, string birthDate, string? customId = null)
        {
            var result = await _kernel.InvokeAsync(
                "FHIR",
                "create_patient",
                new KernelArguments
                {
                    ["firstName"] = firstName,
                    ["lastName"] = lastName,
                    ["gender"] = gender,
                    ["birthDate"] = birthDate,
                    ["customId"] = customId
                }
            );
            return result.ToString() ?? "";
        }

        public async Task<string> CreateObservationAsync(string patientId, string code, string display, double value, string unit, string category = "vital-signs")
        {
            var result = await _kernel.InvokeAsync(
                "FHIR",
                "create_observation",
                new KernelArguments
                {
                    ["patientId"] = patientId,
                    ["code"] = code,
                    ["display"] = display,
                    ["value"] = value,
                    ["unit"] = unit,
                    ["category"] = category
                }
            );
            return result.ToString() ?? "";
        }

        public async Task<string> CreateMedicationStatementAsync(string patientId, string medicationName, string dosage)
        {
            var result = await _kernel.InvokeAsync(
                "FHIR",
                "create_medication_statement",
                new KernelArguments
                {
                    ["patientId"] = patientId,
                    ["medicationName"] = medicationName,
                    ["dosage"] = dosage
                }
            );
            return result.ToString() ?? "";
        }

        public async Task<string> CreateAllergyIntoleranceAsync(string patientId, string allergyName)
        {
            var result = await _kernel.InvokeAsync(
                "FHIR",
                "create_allergy",
                new KernelArguments
                {
                    ["patientId"] = patientId,
                    ["allergyName"] = allergyName
                }
            );
            return result.ToString() ?? "";
        }

        private async Task<string> RunSymptomsAnalyzerAsync(string patientContext)
        {
            var query = patientContext.Length > 200 ? patientContext.Substring(0, 200) : patientContext;

            var retrievedConditions = (await _kernel.InvokeAsync(
                "ClinicalSearch",
                "search_conditions",
                new KernelArguments { ["query"] = query, ["top"] = 5 }
            )).ToString();

            var retrievedIcdCodes = (await _kernel.InvokeAsync(
                "ICDSearch",
                "search_icd10",
                new KernelArguments { ["query"] = query, ["top"] = 5 }
            )).ToString();

            var result = await _kernel.InvokeAsync(
                "SymptomsAgent",
                "analyze_symptoms",
                new KernelArguments
                {
                    ["patientContext"] = patientContext,
                    ["retrievedConditions"] = retrievedConditions,
                    ["retrievedIcdCodes"] = retrievedIcdCodes
                }
            );

            return result.ToString();
        }

        private async Task<string> RunLabInterpreterAsync(string patientContext)
        {
            var query = patientContext.Length > 200 ? patientContext.Substring(0, 200) : patientContext;

            var retrievedLabGuidelines = (await _kernel.InvokeAsync(
                "LabSearch",
                "search_lab_references",
                new KernelArguments { ["query"] = query, ["top"] = 5 }
            )).ToString();

            var result = await _kernel.InvokeAsync(
                "LabAgent",
                "interpret_labs",
                new KernelArguments
                {
                    ["patientContext"] = patientContext,
                    ["retrievedLabGuidelines"] = retrievedLabGuidelines
                }
            );

            return result.ToString();
        }


        private async Task<string> RunTreatmentPlannerAsync(
            string patientContext, string symptomsAnalyzerOutput, string labInterpreterOutput)
        {
            var query = patientContext.Length > 200 ? patientContext.Substring(0, 200) : patientContext;
            var guidelineQuery = "evidence based treatment guidelines for the following patient conditions: " + query;

            var retrievedGuidelines = (await _kernel.InvokeAsync(
                "GuidelineSearch",
                "search_guidelines",
                new KernelArguments { ["diagnosis"] = guidelineQuery, ["top"] = 5 }
            )).ToString();

            var result = await _kernel.InvokeAsync(
                "TreatmentAgent",
                "plan_treatment",
                new KernelArguments
                {
                    ["patientContext"] = patientContext,
                    ["differentialDiagnoses"] = symptomsAnalyzerOutput,
                    ["labInterpretation"] = labInterpreterOutput,
                    ["retrievedGuidelines"] = retrievedGuidelines
                }
            );

            return result.ToString();
        }

        private async Task<string> RunDrugInteractionCheckerAsync(
            string patientContext, string treatmentPlannerOutput)
        {
            var query = patientContext.Length > 200 ? patientContext.Substring(0, 200) : patientContext;
            var drugQuery = "drug interactions contraindications and allergies related to proposed treatment plans for patient with: " + query;

            var retrievedDrugData = (await _kernel.InvokeAsync(
                "DrugSearch",
                "search_drug_interactions",
                new KernelArguments { ["query"] = drugQuery, ["top"] = 5 }
            )).ToString();

            var result = await _kernel.InvokeAsync(
                "DrugAgent",
                "check_drug_interactions",
                new KernelArguments
                {
                    ["patientContext"] = patientContext,
                    ["proposedTreatments"] = treatmentPlannerOutput,
                    ["retrievedDrugData"] = retrievedDrugData
                }
            );

            return result.ToString();
        }


        private async Task<string> BuildConsensusAsync(
            string patientId, string patientContext,
            string symptomsAnalyzerOutput, string labInterpreterOutput,
            string treatmentPlannerOutput, string drugCheckerOutput)

        {

            var query = patientContext.Length > 200 ? patientContext.Substring(0, 200) : patientContext;

            var literatureResult = await _kernel.InvokeAsync(
                    "LiteratureSearch",
                    "search_medical_literature",
                    new KernelArguments
                    {
                        ["query"] = $"medical literature and evidence based management guidelines for: {query}",
                        ["top"] = 3
                    }
                );

            var medicalLiterature = literatureResult.ToString();

            var prompt = $@"
You are the Clinical Orchestrator — a consensus synthesizer for a multi-agent clinical decision support system.

Task:
Synthesize the outputs of four specialized clinical agents into a single, unified clinical executive summary. You are NOT a medical expert — you are a coordinator that identifies agreement, disagreement, and uncertainty across agents.

Rules:
- Do NOT override any agent's output.
- Highlight areas where agents agree strongly.
- Highlight areas where agents disagree and flag for clinician resolution.
- Elevate ALL safety alerts from the Drug Interaction Checker.
- Output the result as a professional Markdown Clinical Executive Summary.
- CRITICAL: Do NOT include raw JSON, technical tags (like <structured_data>), XML blocks, or headers like ""Structured Data Output"" or ""XML Structured Data"".
- CRITICAL: The report must be human-readable and purely clinical. Do NOT include any ""thinking"" or ""rationale"" or ""data"" sections at the end.
- Include sections for: 
  - Summary of Presentation
  - Consensus Diagnoses & Confidence
  - Lab Correlations
  - Proposed Management Roadmap
  - Critical Safety Alerts & Interactions

Patient ID: {patientId}

Patient Context:
{patientContext}

Symptoms Analyzer Output:
{symptomsAnalyzerOutput}

Lab Interpreter Output:
{labInterpreterOutput}

Treatment Planner Output:
{treatmentPlannerOutput}

Drug Interaction Checker Output:
{drugCheckerOutput}

Supporting Medical Literature (for citation and rationale only):
{medicalLiterature}
";

            var response = await _chatService.GetChatMessageContentAsync(prompt);
            return response.Content ?? string.Empty;
        }

        private async Task NotifyAsync(
        string assessmentId,
        string agentName,
        AgentStage stage,
        string? message = null,
        string? payload = null)
        {
            if (_hubContext?.Clients == null)
                return;

            Console.WriteLine($"[SignalR] Broadcasting {agentName} {stage} to group {assessmentId}");
            // Update history
            if (!_history.ContainsKey(assessmentId))
                _history[assessmentId] = new List<AgentProgressEvent>();
            
            var evt = new AgentProgressEvent
            {
                AssessmentId = assessmentId,
                AgentName = agentName,
                Stage = stage,
                Message = message,
                Payload = payload
            };
            
            _history[assessmentId].Add(evt);

            await _hubContext.Clients
                .Group(assessmentId)
                .SendAsync("AgentProgress", evt);
        }

        private async Task SaveResultAsync(string assessmentId, string patientId, string finalJson)
        {
            try
            {
                var filePath = Path.Combine(_storagePath, $"assessment_{assessmentId}.json");
                var record = new
                {
                    AssessmentId = assessmentId,
                    PatientId = patientId,
                    Timestamp = DateTime.UtcNow,
                    History = _history.GetValueOrDefault(assessmentId),
                    FinalResult = finalJson
                };

                await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(record, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"[Storage] Assessment saved to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Storage Error] Failed to save assessment: {ex.Message}");
            }
        }

        public List<AgentProgressEvent> GetAssessmentHistory(string assessmentId)
        {
            return _history.GetValueOrDefault(assessmentId) ?? new List<AgentProgressEvent>();
        }

    }
}
