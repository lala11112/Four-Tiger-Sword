using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Brain
{
    internal enum EmbeddingProvider
    {
        Ollama = 1,
        LMStudio = 2
    }

    [Serializable]
    internal sealed class EmbeddingResult
    {
        public string text;
        public float[] embedding;
        public string error;
    }

    internal static class EmbeddingEngine
    {
        public const string DefaultOllamaEmbeddingModel = "embeddinggemma";

        public static async Task<List<EmbeddingResult>> EmbedTexts(
            List<string> texts,
            string modelName,
            EmbeddingProvider provider = EmbeddingProvider.Ollama,
            string serviceUrl = null,
            int timeoutSeconds = 0,
            CancellationToken cancellationToken = default)
        {
            if (texts == null || texts.Count == 0) return new List<EmbeddingResult>();
            if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("Embedding model name is required.", nameof(modelName));

            switch (provider)
            {
                case EmbeddingProvider.LMStudio:
                    return await EmbedWithLMStudio(texts, modelName, serviceUrl, timeoutSeconds, cancellationToken).ConfigureAwait(false);

                case EmbeddingProvider.Ollama:
                default:
                    return await EmbedWithOllama(texts, modelName, serviceUrl, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<List<EmbeddingResult>> EmbedWithOllama(
            List<string> texts,
            string modelName,
            string serviceUrl,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = CreateTimeout(timeoutSeconds))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            using (OllamaClient client = new OllamaClient(OllamaClient.CreateHttpClient(string.IsNullOrWhiteSpace(serviceUrl) ? Intelligence.OllamaServiceUrl : serviceUrl), true))
            {
                OllamaEmbedRequest request = new OllamaEmbedRequest
                {
                    Model = modelName,
                    Input = texts.ToArray(),
                    Truncate = true
                };

                OllamaEmbedResponse response = await client.EmbedAsync(request, linked.Token).ConfigureAwait(false);
                return MapResults(texts, response?.Embeddings);
            }
        }

        private static async Task<List<EmbeddingResult>> EmbedWithLMStudio(
            List<string> texts,
            string modelName,
            string serviceUrl,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            string baseUrl = string.IsNullOrWhiteSpace(serviceUrl) ? Intelligence.LMStudioServiceUrl : serviceUrl;
            using (CancellationTokenSource timeout = CreateTimeout(timeoutSeconds))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            using (HttpClient client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                LMStudioEmbeddingRequest request = new LMStudioEmbeddingRequest
                {
                    model = modelName,
                    input = texts
                };

                string body = JsonConvert.SerializeObject(request);
                using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await client.PostAsync(LMStudioClient.BuildEndpoint(baseUrl, LMStudioClient.EmbeddingsPath), content, linked.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LMStudioEmbeddingResponse parsed = JsonConvert.DeserializeObject<LMStudioEmbeddingResponse>(json);
                    List<float[]> embeddings = parsed?.data?
                        .OrderBy(d => d.index)
                        .Select(d => d.embedding)
                        .ToList();
                    return MapResults(texts, embeddings);
                }
            }
        }

        private static CancellationTokenSource CreateTimeout(int timeoutSeconds)
        {
            CancellationTokenSource timeout = new CancellationTokenSource();
            if (timeoutSeconds > 0) timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            return timeout;
        }

        private static List<EmbeddingResult> MapResults(List<string> texts, List<float[]> embeddings)
        {
            List<EmbeddingResult> results = new List<EmbeddingResult>(texts.Count);
            for (int i = 0; i < texts.Count; i++)
            {
                float[] vector = embeddings != null && i < embeddings.Count ? embeddings[i] : null;
                results.Add(new EmbeddingResult
                {
                    text = texts[i],
                    embedding = vector,
                    error = vector == null || vector.Length == 0 ? "Embedding response did not contain a vector." : null
                });
            }
            return results;
        }

        [Serializable]
        private sealed class LMStudioEmbeddingRequest
        {
            public string model;
            public List<string> input;
        }

        [Serializable]
        private sealed class LMStudioEmbeddingResponse
        {
            public List<LMStudioEmbeddingData> data;
        }

        [Serializable]
        private sealed class LMStudioEmbeddingData
        {
            public int index;
            public float[] embedding;
        }
    }
}
