using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;

public class FhirPatientDataPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public FhirPatientDataPlugin(string fhirBaseUrl, string accessToken)
    {
        _baseUrl = fhirBaseUrl.TrimEnd('/');

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/fhir+json"));
    }


    [KernelFunction("get_patient_context")]
    [Description("Fetches and normalizes patient clinical context from FHIR")]
    public async Task<string> GetPatientContextAsync(string patientId)
    {
        string logicalId = patientId;

        // Strategy: Try direct ID lookup first (works for old pre-loaded records like cdss-patient-2).
        // If that fails, try identifier search (works for new intake records with UUID logical IDs).
        if (patientId.Contains("cdss-patient-"))
        {
            // First try: direct lookup (old records where cdss-patient-X IS the logical ID)
            var directCheck = await _httpClient.GetAsync($"{_baseUrl}/Patient/{patientId}");
            if (directCheck.IsSuccessStatusCode)
            {
                Console.WriteLine($"[FHIR] Direct lookup succeeded for {patientId}");
                logicalId = patientId;
            }
            else
            {
                // Second try: identifier search (new records where cdss-patient-X is an identifier)
                Console.WriteLine($"[FHIR] Direct lookup failed for {patientId}, trying identifier search...");
                logicalId = await ResolveIdentifierToIdAsync(patientId);
                if (string.IsNullOrEmpty(logicalId)) 
                    throw new Exception($"Could not resolve patient identifier: {patientId}");
            }
        }

        var patient = await GetAsync($"Patient/{logicalId}");
        var conditions = await GetAsync($"Condition?patient={logicalId}");
        var observations = await GetAsync($"Observation?patient={logicalId}");
        var medications = await GetAsync($"MedicationStatement?patient={logicalId}");
        var allergies = await GetAsync($"AllergyIntolerance?patient={logicalId}");

        var (vitals, labs) = ExtractVitalsAndLabs(observations);

        var context = new
        {
            patientId = logicalId,
            identifier = patientId,
            demographics = ExtractDemographics(patient),
            conditions = ExtractConditions(conditions),
            observations = vitals,
            labs = labs,
            medications = ExtractMedications(medications),
            allergies = ExtractAllergies(allergies)
        };

        return JsonSerializer.Serialize(context, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private (Dictionary<string, string> vitals, List<object> labs) ExtractVitalsAndLabs(JsonDocument doc)
    {
        var vitals = new Dictionary<string, string>();
        var labs = new List<object>();

        if (doc.RootElement.TryGetProperty("entry", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var resource = entry.GetProperty("resource");
                string display = resource.GetProperty("code").GetProperty("text").GetString() ?? "Unknown";
                
                string valueStr;
                if (resource.TryGetProperty("valueQuantity", out var vq))
                {
                    var val = vq.GetProperty("value").ToString();
                    var unit = vq.TryGetProperty("unit", out var u) ? u.GetString() : "";
                    valueStr = $"{val} {unit}".Trim();
                }
                else if (resource.TryGetProperty("valueString", out var vs))
                {
                    valueStr = vs.GetString() ?? "N/A";
                }
                else
                {
                    valueStr = "N/A";
                }

                // Check category
                bool isLab = false;
                if (resource.TryGetProperty("category", out var cat))
                {
                    foreach (var c in cat.EnumerateArray())
                    {
                        if (c.TryGetProperty("coding", out var coding))
                        {
                            foreach (var cod in coding.EnumerateArray())
                            {
                                if (cod.TryGetProperty("code", out var codeProp) && codeProp.GetString() == "laboratory") 
                                    isLab = true;
                            }
                        }
                    }
                }

                if (isLab)
                {
                    labs.Add(new {
                        testName = display,
                        value = resource.TryGetProperty("valueQuantity", out var vqLab) ? vqLab.GetProperty("value").ToString() : valueStr,
                        unit = resource.TryGetProperty("valueQuantity", out var vqLab2) ? (vqLab2.TryGetProperty("unit", out var uLab) ? uLab.GetString() : "") : ""
                    });
                }
                else
                {
                    vitals[display] = valueStr;
                }
            }
        }
        return (vitals, labs);
    }

    private async Task<string> ResolveIdentifierToIdAsync(string identifier)
    {
        try
        {
            var searchUrl = $"Patient?identifier=urn:clinical-ds:patient-id|{identifier}";
            Console.WriteLine($"[FHIR] Resolving identifier: {searchUrl}");
            var response = await _httpClient.GetAsync($"{_baseUrl}/{searchUrl}");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[FHIR] Identifier search failed with {response.StatusCode}");
                return "";
            }

            var json = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[FHIR] Identifier search response: {json.Substring(0, Math.Min(json.Length, 500))}");
            
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("entry", out var entries) && entries.GetArrayLength() > 0)
            {
                var firstEntry = entries.EnumerateArray().First();
                var id = firstEntry.GetProperty("resource").GetProperty("id").GetString() ?? "";
                Console.WriteLine($"[FHIR] Resolved '{identifier}' to logical ID: '{id}'");
                return id;
            }
            
            Console.WriteLine($"[FHIR] No patient found for identifier: {identifier}");
            return "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FHIR] Error resolving identifier '{identifier}': {ex.Message}");
            return "";
        }
    }

    // -------------------- HTTP --------------------

    private async Task<JsonDocument> GetAsync(string relativeUrl)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/{relativeUrl}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"FHIR Error [{response.StatusCode}] for {relativeUrl}: {errorBody.Substring(0, Math.Min(errorBody.Length, 300))}");
                
                // For search queries, return an empty bundle instead of throwing
                if (relativeUrl.Contains("?"))
                    return JsonDocument.Parse("{\"resourceType\":\"Bundle\",\"total\":0}");
                    
                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FHIR GetAsync failed for {relativeUrl}: {ex.Message}");
            // Return empty document for search queries to prevent cascading failures
            return JsonDocument.Parse("{\"resourceType\":\"Bundle\",\"total\":0}");
        }
    }

    private async Task<string> PostAsync(string relativeUrl, object resource)
    {
        var json = JsonSerializer.Serialize(resource);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/fhir+json");
        var response = await _httpClient.PostAsync($"{_baseUrl}/{relativeUrl}", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"FHIR Post Error [{response.StatusCode}] for {relativeUrl}:");
            Console.WriteLine(errorBody);
            response.EnsureSuccessStatusCode();
        }

        var resultJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resultJson);
        return doc.RootElement.GetProperty("id").GetString() ?? "";
    }

    [KernelFunction("create_patient")]
    [Description("Creates a new patient record in FHIR with a unique identifier")]
    public async Task<string> CreatePatientAsync(string firstName, string lastName, string gender, string birthDate, string? customId = null)
    {
        var patient = new
        {
            resourceType = "Patient",
            identifier = customId != null ? new[] { 
                new { 
                    system = "urn:clinical-ds:patient-id", 
                    value = customId 
                } 
            } : null,
            name = new[] { new { family = lastName, given = new[] { firstName } } },
            gender = gender.ToLower(),
            birthDate = birthDate
        };

        return await PostAsync("Patient", patient);
    }

    [KernelFunction("create_observation")]
    [Description("Creates a new observation (e.g., vitals, labs) in FHIR")]
    public async Task<string> CreateObservationAsync(string patientId, string code, string display, double value, string unit, string category = "vital-signs")
    {
        var observation = new
        {
            resourceType = "Observation",
            status = "final",
            category = new[] { 
                new { 
                    coding = new[] { 
                        new { 
                            system = "http://terminology.hl7.org/CodeSystem/observation-category", 
                            code = category,
                            display = category == "vital-signs" ? "Vital Signs" : "Laboratory"
                        } 
                    } 
                } 
            },
            code = new { coding = new[] { new { system = "http://loinc.org", code = code, display = display } }, text = display },
            subject = new { reference = $"Patient/{patientId}" },
            effectiveDateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            // If value is 0 but we have a display string that looks like a value (e.g. BP "120/80"), use valueString
            valueQuantity = value > 0 ? new { value = value, unit = unit, system = "http://unitsofmeasure.org", code = unit } : null,
            valueString = value <= 0 ? unit : null 
        };

        return await PostAsync("Observation", observation);
    }

    [KernelFunction("create_medication_statement")]
    [Description("Creates a new MedicationStatement in FHIR")]
    public async Task<string> CreateMedicationStatementAsync(string patientId, string medicationName, string dosage)
    {
        var statement = new
        {
            resourceType = "MedicationStatement",
            status = "active", // Fixed: 'recorded' is not a valid FHIR R4 status
            subject = new { reference = $"Patient/{patientId}" },
            dateAsserted = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            medicationCodeableConcept = new { text = medicationName },
            dosage = new[] { new { text = dosage } }
        };

        return await PostAsync("MedicationStatement", statement);
    }

    [KernelFunction("create_allergy")]
    [Description("Creates a new AllergyIntolerance in FHIR")]
    public async Task<string> CreateAllergyIntoleranceAsync(string patientId, string allergyName)
    {
        var allergy = new
        {
            resourceType = "AllergyIntolerance",
            clinicalStatus = new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical", code = "active" } } },
            verificationStatus = new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/allergyintolerance-verification", code = "confirmed" } } },
            code = new { text = allergyName },
            patient = new { reference = $"Patient/{patientId}" }
        };

        return await PostAsync("AllergyIntolerance", allergy);
    }

    // -------------------- NORMALIZATION --------------------
    private object ExtractDemographics(JsonDocument patient)
    {
        var root = patient.RootElement;

        return new
        {
            gender = root.TryGetProperty("gender", out var g) ? g.GetString() : "unknown",
            birthDate = root.TryGetProperty("birthDate", out var b) ? b.GetString() : "unknown"
        };
    }

    private List<string> ExtractConditions(JsonDocument bundle)
    {
        if (!bundle.RootElement.TryGetProperty("entry", out var entries))
            return new();

        return entries.EnumerateArray()
            .Select(e =>
                e.GetProperty("resource")
                 .GetProperty("code")
                 .GetProperty("text")
                 .GetString())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }

    private List<string> ExtractMedications(JsonDocument bundle)
    {
        if (!bundle.RootElement.TryGetProperty("entry", out var entries))
            return new();

        return entries.EnumerateArray()
            .Select(e =>
                e.GetProperty("resource")
                 .GetProperty("medicationCodeableConcept")
                 .GetProperty("text")
                 .GetString())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }

    private List<string> ExtractAllergies(JsonDocument bundle)
    {
        if (!bundle.RootElement.TryGetProperty("entry", out var entries))
            return new();

        return entries.EnumerateArray()
            .Select(e =>
                e.GetProperty("resource")
                 .GetProperty("code")
                 .GetProperty("text")
                 .GetString())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }
}