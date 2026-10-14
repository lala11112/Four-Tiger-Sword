using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StudioBaekho.AutoHitTimer
{
    public interface IAutoHitTimerAdapter
    {
        string Id { get; }
        JObject Targets(string actorId);
        JObject PreviewChange(JObject request);
        JObject Apply(JObject request);
        JObject SaveStatus(JObject request);
    }

    public static class AutoHitTimerAdapters
    {
        static readonly Dictionary<string, IAutoHitTimerAdapter> Adapters = new Dictionary<string, IAutoHitTimerAdapter>(StringComparer.Ordinal);

        public static void Register(IAutoHitTimerAdapter adapter)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(adapter.Id)) throw new ArgumentException("Adapter ID is required.");
            Adapters[adapter.Id] = adapter;
        }

        public static IAutoHitTimerAdapter Resolve(string id = null)
        {
            id = id ?? "four-tiger-sword";
            if (!Adapters.TryGetValue(id, out var adapter)) throw new AutoHitTimerException("ADAPTER_MISSING", "프로젝트 어댑터가 설치되지 않았습니다: " + id);
            return adapter;
        }
    }

    public sealed class AutoHitTimerException : Exception
    {
        public string Code { get; }
        public AutoHitTimerException(string code, string message) : base(message) { Code = code; }
    }
}
