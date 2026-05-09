using Microsoft.Azure.Cosmos;
using System.Text.Json;

namespace ClinicalDecisionSupport.Services
{
    public class CosmosSessionService
    {
        private readonly CosmosClient _cosmosClient;
        private readonly Database _database;
        private readonly Container _sessionsContainer;
        private readonly Container _agentAssessmentsContainer;
        private readonly Container _guardrailLogsContainer;

        public CosmosSessionService(IConfiguration configuration)
        {
            var endpoint = configuration["CosmosDB:Endpoint"];
            var key = configuration["CosmosDB:Key"];
            
            // Allow this to fail gracefully if no real key is provided yet
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || key.Contains("..."))
            {
                Console.WriteLine("[CosmosDB] Warning: Invalid or mock connection string provided. Cosmos integration will run in mock mode.");
                return;
            }

            _cosmosClient = new CosmosClient(endpoint, key, new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });

            // In a real app, Database and Containers should be created during startup or CI/CD
            _database = _cosmosClient.GetDatabase("ClinicalDSS");
            _sessionsContainer = _database.GetContainer("Sessions");
            _agentAssessmentsContainer = _database.GetContainer("AgentAssessments");
            _guardrailLogsContainer = _database.GetContainer("GuardrailLogs");
        }

        public async Task InitializeDatabaseAsync()
        {
            if (_cosmosClient == null) return;

            try
            {
                var dbResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync("ClinicalDSS");
                var db = dbResponse.Database;

                await db.CreateContainerIfNotExistsAsync("Sessions", "/sessionId");
                await db.CreateContainerIfNotExistsAsync("AgentAssessments", "/sessionId");
                await db.CreateContainerIfNotExistsAsync("GuardrailLogs", "/sessionId");

                Console.WriteLine("[CosmosDB] Database and containers initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CosmosDB Error] Failed to initialize: {ex.Message}");
            }
        }

        public async Task CreateSessionAsync(string sessionId, string patientId, string clinicianName, string clinicianRole)
        {
            if (_cosmosClient == null) return;

            try
            {
                var session = new
                {
                    id = Guid.NewGuid().ToString(),
                    sessionId = sessionId,
                    patientId = patientId,
                    clinicianName = clinicianName,
                    clinicianRole = clinicianRole,
                    startTime = DateTime.UtcNow,
                    status = "InProgress"
                };

                await _sessionsContainer.CreateItemAsync(session, new PartitionKey(sessionId));
                Console.WriteLine($"[CosmosDB] Session created for {sessionId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CosmosDB Error] CreateSession: {ex.Message}");
            }
        }

        public async Task LogAgentAssessmentAsync(string sessionId, string agentName, string structuredJson)
        {
            if (_cosmosClient == null) return;

            try
            {
                // Parse the string to ensure it's valid JSON, then store as a dynamic object
                var data = JsonSerializer.Deserialize<dynamic>(structuredJson);
                
                var assessment = new
                {
                    id = Guid.NewGuid().ToString(),
                    sessionId = sessionId,
                    agentName = agentName,
                    timestamp = DateTime.UtcNow,
                    data = data
                };

                await _agentAssessmentsContainer.CreateItemAsync(assessment, new PartitionKey(sessionId));
                Console.WriteLine($"[CosmosDB] Agent assessment logged for {agentName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CosmosDB Error] LogAgentAssessment: {ex.Message}");
            }
        }

        public async Task LogGuardrailSampleAsync(string sessionId, string checkType, string status, string message)
        {
            if (_cosmosClient == null) return;

            try
            {
                var log = new
                {
                    id = Guid.NewGuid().ToString(),
                    sessionId = sessionId,
                    checkType = checkType,
                    status = status, // "ALLOW", "WARN", "BLOCK"
                    message = message,
                    timestamp = DateTime.UtcNow
                };

                await _guardrailLogsContainer.CreateItemAsync(log, new PartitionKey(sessionId));
                Console.WriteLine($"[CosmosDB] Guardrail log created: {status} - {checkType}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CosmosDB Error] LogGuardrailSample: {ex.Message}");
            }
        }
    }
}
