using AssetInventory.Automation;
using Newtonsoft.Json;

namespace AssetInventory.Integration.UnityPipeline
{
    public sealed class AssetInventoryCommandResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorCode { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }

    internal static class PipelineResultAdapter
    {
        internal static AssetInventoryCommandResult Convert(AutomationResponse response)
        {
            return new AssetInventoryCommandResult
            {
                Success = response.Succeeded,
                Message = response.Message,
                ErrorCode = response.ErrorCode,
                Data = response.Data
            };
        }
    }
}
