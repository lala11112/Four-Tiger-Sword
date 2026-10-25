using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Marks spawned world effects so changing stages can remove them immediately.</summary>
[DisallowMultipleComponent]
public sealed class StageTransientObject : MonoBehaviour
{
    private static readonly HashSet<StageTransientObject> Instances = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Instances.Clear();

    public static GameObject Track(GameObject instance)
    {
        if (instance == null) return null;
        if (!instance.TryGetComponent<StageTransientObject>(out var marker))
            marker = instance.AddComponent<StageTransientObject>();
        Instances.Add(marker);
        return instance;
    }

    public static void ClearScene(Scene scene)
    {
        foreach (var marker in new List<StageTransientObject>(Instances))
        {
            if (marker == null) { Instances.Remove(marker); continue; }
            if (marker.gameObject.scene != scene) continue;
            Instances.Remove(marker);
            marker.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(marker.gameObject);
            else DestroyImmediate(marker.gameObject);
        }
    }

    private void OnEnable() => Instances.Add(this);
    private void OnDestroy() => Instances.Remove(this);
}
