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
    public class LabReferenceSearchPlugin
    {
        private readonly SearchClient _searchClient;
        private readonly AzureEmbeddingService _embeddingService;

        public LabReferenceSearchPlugin(
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

        [KernelFunction("search_lab_references")]
        [Description("Searches laboratory reference ranges and critical values")]
        public async Task<string> SearchLabReferencesAsync(string query, int top = 5)
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