using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Brain
{
    internal static class LMStudioClient
    {
        internal const string ModelsPath = "api/v1/models";
        internal const string CaptionPath = "v1/chat/completions";
        internal const string EmbeddingsPath = "v1/embeddings";

        internal static string NormalizeServiceUrl(string serviceUrl) =>
            string.IsNullOrWhiteSpace(serviceUrl) ? Intelligence.LMSTUDIO_SERVICE_URL : serviceUrl.Trim().TrimEnd('/');

        internal static string BuildEndpoint(string serviceUrl, string path) =>
            NormalizeServiceUrl(serviceUrl) + "/" + path.TrimStart('/');

        internal static async Task<List<LMStudioModel>> GetModelsAsync(HttpClient client, string serviceUrl, CancellationToken cancellationToken = default)
        {
            using (HttpResponseMessage response = await client.GetAsync(BuildEndpoint(serviceUrl, ModelsPath), cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseModels(json);
            }
        }

        internal static List<LMStudioModel> ParseModels(string json)
        {
            JObject response = JObject.Parse(json);
            if (!(response["models"] is JArray entries))
                throw new JsonSerializationException("LM Studio REST v1 response must contain a models array.");
            List<LMStudioModel> models = new List<LMStudioModel>(entries.Count);
            foreach (JToken entry in entries)
            {
                if (!(entry is JObject model) || model["key"]?.Type != JTokenType.String) continue;
                NativeModel native = model.ToObject<NativeModel>();
                if (native == null || string.IsNullOrWhiteSpace(native.key)) continue;
                models.Add(new LMStudioModel
                {
                    id = native.key,
                    type = native.type,
                    state = native.loaded_instances != null && native.loaded_instances.Count > 0 ? "loaded" : "not-loaded",
                    quantization = native.quantization?.name,
                    max_context_length = native.max_context_length,
                    HasVisionCapability = native.capabilities?.vision == true
                });
            }
            return models;
        }

        sealed class NativeModel
        {
            public string key;
            public string type;
            public List<object> loaded_instances;
            public NativeQuantization quantization;
            public int? max_context_length;
            public NativeCapabilities capabilities;
        }

        sealed class NativeQuantization
        {
            public string name;
        }

        sealed class NativeCapabilities
        {
            public bool vision;
        }
    }
}
