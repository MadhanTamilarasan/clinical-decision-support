using Microsoft.Extensions.AI;

namespace ClinicalDecisionSupport.Services
{
    public class AzureEmbeddingService
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

        public AzureEmbeddingService(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            _embeddingGenerator = embeddingGenerator;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var embedding = await _embeddingGenerator.GenerateAsync(text);
            return embedding.Vector.ToArray();
        }
    }
}
