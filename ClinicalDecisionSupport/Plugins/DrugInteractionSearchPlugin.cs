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
    public class DrugInteractionSearchPlugin
    {
        private readonly SearchClient _searchClient;
        private readonly AzureEmbeddingService _embeddingService;

        public DrugInteractionSearchPlugin(
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

        [KernelFunction("search_drug_interactions")]
        public async Task<string> SearchDrugInteractionsAsync(string query, int top = 5)
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

            var response = await _searchClient.SearchAsync<SearchDocument>(null, options);

            var sb = new StringBuilder();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                sb.AppendLine("- " + result.Document["chunk"]);
            }

            return sb.ToString();
        }
    }
}

/*using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;

namespace ClinicalDecisionSupport.Plugins
{
    public class DrugInteractionSearchPlugin
    {
        private readonly SearchClient _searchClient;

        public DrugInteractionSearchPlugin(string searchEndpoint, string indexName, string apiKey)
        {
            _searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(apiKey)
            );
        }

        [KernelFunction("search_drug_interactions")]
        [Description("Searches drug interaction database for known interactions, contraindications, and allergy data")]
        public async Task<string> SearchDrugInteractionsAsync(string query, int top = 5)
        {
            var options = new SearchOptions
            {
                Size = top
            };

            var response = await _searchClient.SearchAsync<SearchDocument>(query, options);

            var sb = new StringBuilder();

            await foreach (var result in response.Value.GetResultsAsync())
            {
                if (result.Document.TryGetValue("content", out var content))
                {
                    sb.AppendLine("- " + content?.ToString());
                }
            }

            return sb.ToString();
        }
    }
}*/
