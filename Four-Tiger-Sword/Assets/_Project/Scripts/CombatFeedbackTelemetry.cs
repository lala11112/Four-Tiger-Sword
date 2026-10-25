using System;
using UnityEngine;

[Serializable]
public sealed class CombatFeedbackTelemetryEvent
{
    public int sequence, unityFrameCount, ownerId, targetId, hitIndex;
    public double realTime;
    public float gameTime, value, fov;
    public string kind, detail;
    public Vector3 position, rotation;
}

/// <summary>Observations only; subscriptions cannot change combat execution.</summary>
public static class CombatFeedbackTelemetry
{
    private static int _sequence;
    public static event Action<CombatFeedbackTelemetryEvent> Recorded;
    public static bool HasListeners => Recorded != null;

    public static CombatFeedbackTelemetryEvent Emit(string kind, GameObject owner,
        GameObject target = null, int hitIndex = -1, string detail = null, float value = 0,
        Vector3 position = default, Vector3 rotation = default, float fov = 0)
    {
        var item = new CombatFeedbackTelemetryEvent { sequence = ++_sequence, unityFrameCount = Time.frameCount,
            realTime = Time.realtimeSinceStartupAsDouble, gameTime = Time.time, kind = kind,
            ownerId = owner != null ? owner.GetInstanceID() : 0, targetId = target != null ? target.GetInstanceID() : 0,
            hitIndex = hitIndex, detail = detail ?? string.Empty, value = value, position = position, rotation = rotation, fov = fov };
        // Report consumers must never interrupt a hit or prevent state cleanup.
        var handlers = Recorded;
        if (handlers != null)
            foreach (Action<CombatFeedbackTelemetryEvent> handler in handlers.GetInvocationList())
                try { handler(item); } catch (Exception exception) { Debug.LogException(exception); }
        return item;
    }
}
