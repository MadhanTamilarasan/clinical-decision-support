using ClinicalDecisionSupport.Agents;
using ClinicalDecisionSupport.Hubs;
using ClinicalDecisionSupport.Orchestration;
using ClinicalDecisionSupport.Plugins;
using ClinicalDecisionSupport.Services;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

//
// ───────────────────────────────────────────────
// 1. ASP.NET Core services
// ───────────────────────────────────────────────
//
builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowUI", policy =>
    {
        policy.WithOrigins("http://localhost:5034")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

//
// ───────────────────────────────────────────────
// 2. Load configuration (keys, endpoints, indexes)
// ───────────────────────────────────────────────
//
var config = builder.Configuration;

//
// ───────────────────────────────────────────────
// 3. Register Semantic Kernel + Plugins (UNCHANGED LOGIC)
// ───────────────────────────────────────────────
//
builder.Services.AddSingleton<Kernel>(_ =>
{
    var kernelBuilder = Kernel.CreateBuilder();

    // Configuration Logging
    var openAiEndpoint = config["AzureOpenAI:Endpoint"]?.Trim();
    var openAiKey = config["AzureOpenAI:ApiKey"]?.Trim();
    var chatDeployment = config["AzureOpenAI:ChatDeployment"];
    var embeddingDeployment = config["AzureOpenAI:EmbeddingDeployment"];

    Console.WriteLine($"[Config] OpenAI Endpoint: {openAiEndpoint}");
    Console.WriteLine($"[Config] OpenAI Key (last 4): ...{openAiKey?.Substring(Math.Max(0, (openAiKey?.Length ?? 0) - 4))}");
    Console.WriteLine($"[Config] Chat Deployment: {chatDeployment}");
    Console.WriteLine($"[Config] Embedding Deployment: {embeddingDeployment}");

    // Azure OpenAI - Chat
    kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: chatDeployment ?? "gpt-4o",
        endpoint: openAiEndpoint ?? "",
        apiKey: openAiKey ?? "");

#pragma warning disable SKEXP0010
    // Azure OpenAI - Embeddings
    kernelBuilder.AddAzureOpenAIEmbeddingGenerator(
        deploymentName: embeddingDeployment ?? "text-embedding-3-small",
        endpoint: openAiEndpoint ?? "",
        apiKey: openAiKey ?? "");
#pragma warning restore SKEXP0010

    var kernel = kernelBuilder.Build();

    // Shared config values
    var searchEndpoint = config["AzureAISearch:Endpoint"]?.Trim() ?? "";
    var searchApiKey = config["AzureAISearch:ApiKey"]?.Trim() ?? "";

    //
    // FHIR Plugin
    //
    kernel.ImportPluginFromObject(
        new FhirPatientDataPlugin(
            config["FHIR:BaseUrl"] ?? "",
            config["FHIR:AccessToken"] ?? ""),
        "FHIR");

    //
    // Azure AI Search Plugins
    //
    kernel.ImportPluginFromObject(
        new ClinicalKnowledgeSearchPlugin(
            kernel,
            searchEndpoint,
            config["AzureAISearch:Indexes:Conditions"] ?? "conditions-index",
            searchApiKey),
        "ClinicalSearch");

    kernel.ImportPluginFromObject(
        new TreatmentGuidelineSearchPlugin(
            kernel,
            searchEndpoint,
            config["AzureAISearch:Indexes:Guidelines"] ?? "clinical-guidelines-index",
            searchApiKey),
        "GuidelineSearch");

    kernel.ImportPluginFromObject(
        new DrugInteractionSearchPlugin(
            kernel,
            searchEndpoint,
            config["AzureAISearch:Indexes:Drugs"] ?? "drugs-index",
            searchApiKey),
        "DrugSearch");

    kernel.ImportPluginFromObject(
        new ICD10SearchPlugin(
            kernel,
            searchEndpoint,
            config["AzureAISearch:Indexes:ICD10"] ?? "icd10-codes-index",
            searchApiKey),
        "ICDSearch");

    kernel.ImportPluginFromObject(
        new LabReferenceSearchPlugin(
            kernel,
            searchEndpoint,
            config["AzureAISearch:Indexes:Labs"] ?? "lab-references-index",
            searchApiKey),
        "LabSearch");

    kernel.ImportPluginFromObject(
        new MedicalLiteratureSearchPlugin(
            kernel,
            searchEndpoint,
            config["AzureAISearch:Indexes:Literature"] ?? "medical-literature-index",
            searchApiKey),
        "LiteratureSearch");

    //
    // Agent Plugins
    //
    kernel.ImportPluginFromObject(new SymptomsAnalyzerAgent(kernel), "SymptomsAgent");
    kernel.ImportPluginFromObject(new LabInterpreterAgent(kernel), "LabAgent");
    kernel.ImportPluginFromObject(new TreatmentPlannerAgent(kernel), "TreatmentAgent");
    kernel.ImportPluginFromObject(new DrugInteractionCheckerAgent(kernel), "DrugAgent");

    return kernel;
});

//
// ───────────────────────────────────────────────
// 4. Register Orchestrator (UNCHANGED LOGIC)
// ───────────────────────────────────────────────
//
builder.Services.AddSingleton<ClinicalOrchestrator>();
builder.Services.AddSingleton<CosmosSessionService>();

builder.Services.AddHttpClient<GuardrailService>(client =>
{
    client.BaseAddress = new Uri(config["GuardrailsApiUrl"] ?? "http://localhost:7071/");
});


//
// ───────────────────────────────────────────────
// 5. Build web application
// ───────────────────────────────────────────────
//
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var cosmosService = scope.ServiceProvider.GetRequiredService<CosmosSessionService>();
    await cosmosService.InitializeDatabaseAsync();
}

//
// ───────────────────────────────────────────────
// 6. Middleware & Endpoints
// ───────────────────────────────────────────────
//
// app.UseHttpsRedirection();
app.UseCors("AllowUI");
app.UseAuthorization();

app.MapControllers();
app.MapHub<ClinicalAssessmentHub>("/clinicalhub");

//
// ───────────────────────────────────────────────
// 7. Start server
// ───────────────────────────────────────────────
//
app.Run();