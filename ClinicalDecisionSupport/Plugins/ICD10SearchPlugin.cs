using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using ClinicalDecisionSupport.Services;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;

namespace ClinicalDecisionSupport.Plugins
{
    public class ICD10SearchPlugin
    {
        private readonly SearchClient _searchClient;
        private readonly AzureEmbeddingService _embeddingService;

        public ICD10SearchPlugin(
            Kernel kernel,
            string searchEndpoint,
            string indexName,
            string apiKey)
        {
            _searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(apiKey));

            var embeddingGenerator =
                kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            _embeddingService = new AzureEmbeddingService(embeddingGenerator);
        }

        [KernelFunction("search_icd10")]
        [Description("Searches ICD-10 codes related to clinical conditions")]
        public async Task<string> SearchIcd10Async(string query, int top = 5)
        {
            var vector = await _embeddingService.GenerateEmbeddingAsync(query);

            var options = new SearchOptions
            {
                Size = top,
                VectorSearch = new()
                {
                    Queries =
                    {
                        new VectorizedQuery(vector)
                        {
                            Fields = { "text_vector" },
                            KNearestNeighborsCount = top
                        }
                    }
                }
            };

            var response = await _searchClient.SearchAsync<SearchDocument>(query, options);

            var sb = new StringBuilder();
            await foreach (var result in response.Value.GetResultsAsync())
            {
                sb.AppendLine("- " + result.Document["chunk"]);
            }

            return sb.ToString();
        }
    }
}