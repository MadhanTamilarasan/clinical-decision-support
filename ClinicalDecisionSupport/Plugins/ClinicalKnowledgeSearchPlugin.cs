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
    public class ClinicalKnowledgeSearchPlugin
    {
        private readonly SearchClient _searchClient;
        private readonly AzureEmbeddingService _embeddingService;

        public ClinicalKnowledgeSearchPlugin(
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

        [KernelFunction("search_conditions")]
        [Description("Semantic search over clinical conditions")]
        public async Task<string> SearchConditionsAsync(string query, int top = 5)
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
    public class ClinicalKnowledgeSearchPlugin
    {
        private readonly SearchClient _searchClient;

        public ClinicalKnowledgeSearchPlugin(string searchEndpoint, string indexName, string apiKey)
        {
            _searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(apiKey)
            );
        }

        [KernelFunction("search_conditions")]
        [Description("Searches clinical conditions knowledge base using symptoms or complaints")]
        public async Task<string> SearchConditionsAsync(string query, int top = 5)
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