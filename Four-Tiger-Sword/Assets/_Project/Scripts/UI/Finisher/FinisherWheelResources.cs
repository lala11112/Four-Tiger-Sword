using TMPro;
using UnityEngine;

/// <summary>The replaceable wheel prefab and Korean font, reachable in player builds.</summary>
public sealed class FinisherWheelResources : ScriptableObject
{
    public const string ResourceName = "FinisherWheelResources";
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("Use a prefab or variant here to replace the wheel design.")]
    [SerializeField] private FinisherWheelView prefab;

    public TMP_FontAsset Font => font;
    public FinisherWheelView Prefab => prefab;

    public void Configure(TMP_FontAsset fontAsset) => font = fontAsset;
    public void ConfigurePrefab(FinisherWheelView prefabAsset) => prefab = prefabAsset;
}
